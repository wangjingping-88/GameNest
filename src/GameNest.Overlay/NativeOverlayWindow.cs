using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using GameNest.Domain;
using GameNest.Telemetry;

namespace GameNest.Overlay;

internal sealed class NativeOverlayWindow : IDisposable
{
    private const string WindowClassName = "GameNest.Overlay.Window";
    private const uint WsPopup = 0x80000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolwindow = 0x00000080;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExNoactivate = 0x08000000;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNozorder = 0x0004;
    private const uint SwpShowwindow = 0x0040;
    private const uint LwaAlpha = 0x00000002;
    private const int SwHide = 0;
    private const int HtClient = 1;
    private const int MaNoactivate = 3;
    private const uint WmDestroy = 0x0002;
    private const uint WmPaint = 0x000F;
    private const uint WmClose = 0x0010;
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmNchittest = 0x0084;
    private const uint WmMouseactivate = 0x0021;
    private const uint WmMousemove = 0x0200;
    private const uint WmLbuttondown = 0x0201;
    private const uint WmLbuttonup = 0x0202;
    private const uint WmCapturechanged = 0x0215;
    private const uint WmHotkey = 0x0312;
    private const uint WmAppMessage = 0x8001;
    private const int HotkeyId = 0x474E;
    private const int TransparentBackgroundMode = 1;
    private const uint DtCenter = 0x00000001;
    private const uint DtVcenter = 0x00000004;
    private const uint DtSingleline = 0x00000020;
    private const uint DtEndEllipsis = 0x00008000;
    private static readonly WndProcDelegate WindowProcedureDelegate = WindowProcedure;
    private static readonly ConcurrentDictionary<nint, NativeOverlayWindow> Windows = new();
    private readonly Action<OverlayWireStatus> _statusCallback;
    private readonly ConcurrentQueue<OverlayPipeMessage> _commands = new();
    private readonly object _frameSync = new();
    private nint _window;
    private OverlayWireFrame? _frame;
    private OverlayPipeMessage? _latestFrame;
    private OverlayHotkey? _hotkey;
    private bool _hotkeySuppressed;
    private bool _hotkeyRegistered;
    private bool _disposed;
    private bool _twoRows;
    private int _windowWidth;
    private int _windowHeight;
    private int _windowLeft;
    private int _windowTop;
    private bool _hasDraggedPosition;
    private bool _isDragging;
    private Point _dragStartCursor;
    private int _dragStartLeft;
    private int _dragStartTop;

    public NativeOverlayWindow(Action<OverlayWireStatus> statusCallback)
    {
        _statusCallback = statusCallback;
        _ = SetProcessDpiAwarenessContext(new nint(-4));
        RegisterWindowClass();
        _window = CreateWindowExW(
            WsExTopmost | WsExToolwindow | WsExLayered | WsExNoactivate,
            WindowClassName,
            "GameNest Overlay",
            WsPopup,
            0,
            0,
            1,
            1,
            nint.Zero,
            nint.Zero,
            GetModuleHandleW(null),
            nint.Zero);
        if (_window == nint.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        Windows[_window] = this;
    }

    public static int RunMessageLoop()
    {
        Message message;
        while (GetMessageW(out message, nint.Zero, 0, 0) > 0)
        {
            _ = TranslateMessage(ref message);
            _ = DispatchMessageW(ref message);
        }

        return checked((int)message.WParam);
    }

    public void Post(OverlayPipeMessage message)
    {
        if (message.Type == OverlayMessageTypes.Frame)
        {
            lock (_frameSync)
            {
                _latestFrame = message;
            }
        }
        else
        {
            _commands.Enqueue(message);
        }

        _ = PostMessageW(_window, WmAppMessage, nint.Zero, nint.Zero);
    }

    public void RequestClose() => _ = PostMessageW(_window, WmClose, nint.Zero, nint.Zero);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hotkeyRegistered)
        {
            _ = UnregisterHotKey(_window, HotkeyId);
        }

        if (_window != nint.Zero)
        {
            Windows.TryRemove(_window, out _);
            if (IsWindow(_window))
            {
                _ = DestroyWindow(_window);
            }

            _window = nint.Zero;
        }
    }

    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        if (!Windows.TryGetValue(window, out var instance))
        {
            return DefWindowProcW(window, message, wParam, lParam);
        }

        return instance.HandleMessage(message, wParam, lParam);
    }

    private nint HandleMessage(uint message, nuint wParam, nint lParam)
    {
        _ = lParam;
        switch (message)
        {
            case WmAppMessage:
                DrainMessages();
                return nint.Zero;
            case WmHotkey when checked((int)wParam) == HotkeyId:
                _hotkeySuppressed = !_hotkeySuppressed;
                ApplyVisibility();
                return nint.Zero;
            case WmPaint:
                Paint();
                return nint.Zero;
            case WmEraseBkgnd:
                return new nint(1);
            case WmNchittest:
                return new nint(HtClient);
            case WmMouseactivate:
                return new nint(MaNoactivate);
            case WmLbuttondown:
                BeginDrag();
                return nint.Zero;
            case WmMousemove:
                UpdateDrag();
                return nint.Zero;
            case WmLbuttonup:
            case WmCapturechanged:
                EndDrag();
                return nint.Zero;
            case WmClose:
                _ = DestroyWindow(_window);
                return nint.Zero;
            case WmDestroy:
                Windows.TryRemove(_window, out _);
                PostQuitMessage(0);
                return nint.Zero;
            default:
                return DefWindowProcW(_window, message, wParam, lParam);
        }
    }

    private void DrainMessages()
    {
        while (_commands.TryDequeue(out var command))
        {
            switch (command.Type)
            {
                case OverlayMessageTypes.Hide:
                    _ = ShowWindow(_window, SwHide);
                    break;
                case OverlayMessageTypes.Shutdown:
                    RequestClose();
                    return;
            }
        }

        OverlayPipeMessage? frame;
        lock (_frameSync)
        {
            frame = _latestFrame;
            _latestFrame = null;
        }

        if (frame?.Frame is not null)
        {
            ApplyFrame(frame.Frame);
        }
    }

    private void ApplyFrame(OverlayWireFrame frame)
    {
        _frame = frame;
        ConfigureHotkey(frame.ToggleHotkey);
        var metricCount = Math.Max(
            1,
            (frame.ShowFps ? 1 : 0) +
            (frame.ShowCpu ? 1 : 0) +
            (frame.ShowGpu ? 1 : 0) +
            (frame.ShowRam ? 1 : 0));
        var scale = Math.Max(0.5, frame.Dpi / 96d * frame.ScalePercent / 100d);
        var baseWidth = 28 + metricCount * 92;
        _twoRows = frame.Width < (baseWidth + 32) * scale;
        var columns = _twoRows ? Math.Min(2, metricCount) : metricCount;
        var rows = _twoRows ? checked((int)Math.Ceiling(metricCount / 2d)) : 1;
        _windowWidth = checked((int)Math.Round((28 + columns * 92) * scale));
        _windowHeight = checked((int)Math.Round((rows == 1 ? 44 : 78) * scale));
        var margin = checked((int)Math.Round(16 * scale));
        var position = Enum.TryParse<OverlayPosition>(frame.Position, true, out var parsed)
            ? parsed
            : OverlayPosition.TopRight;
        var left = position is OverlayPosition.TopLeft or OverlayPosition.BottomLeft
            ? frame.Left + margin
            : frame.Left + frame.Width - _windowWidth - margin;
        var top = position is OverlayPosition.TopLeft or OverlayPosition.TopRight
            ? frame.Top + margin
            : frame.Top + frame.Height - _windowHeight - margin;
        if (_hasDraggedPosition)
        {
            left = _windowLeft;
            top = _windowTop;
        }

        ClampToGameBounds(frame, ref left, ref top);
        _windowLeft = left;
        _windowTop = top;

        _ = SetLayeredWindowAttributes(
            _window,
            0,
            checked((byte)Math.Round(frame.BackgroundOpacityPercent / 100d * 255)),
            LwaAlpha);
        var radius = checked((int)Math.Round(10 * scale));
        var region = CreateRoundRectRgn(0, 0, _windowWidth + 1, _windowHeight + 1, radius, radius);
        _ = SetWindowRgn(_window, region, true);
        _ = SetWindowPos(
            _window,
            new nint(-1),
            left,
            top,
            _windowWidth,
            _windowHeight,
            SwpNoactivate | SwpShowwindow);
        _ = InvalidateRect(_window, nint.Zero, false);
        ApplyVisibility();
    }

    private void BeginDrag()
    {
        if (_frame?.IsVisible != true || !GetCursorPos(out _dragStartCursor))
        {
            return;
        }

        _isDragging = true;
        _dragStartLeft = _windowLeft;
        _dragStartTop = _windowTop;
        _ = SetCapture(_window);
    }

    private void UpdateDrag()
    {
        if (!_isDragging || _frame is null || !GetCursorPos(out var cursor))
        {
            return;
        }

        var left = _dragStartLeft + cursor.X - _dragStartCursor.X;
        var top = _dragStartTop + cursor.Y - _dragStartCursor.Y;
        ClampToGameBounds(_frame, ref left, ref top);
        _windowLeft = left;
        _windowTop = top;
        _hasDraggedPosition = true;
        _ = SetWindowPos(
            _window,
            nint.Zero,
            left,
            top,
            0,
            0,
            SwpNoactivate | SwpNosize | SwpNozorder);
    }

    private void EndDrag()
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        _ = ReleaseCapture();
    }

    private void ClampToGameBounds(OverlayWireFrame frame, ref int left, ref int top)
    {
        left = Math.Clamp(left, frame.Left, Math.Max(frame.Left, frame.Left + frame.Width - _windowWidth));
        top = Math.Clamp(top, frame.Top, Math.Max(frame.Top, frame.Top + frame.Height - _windowHeight));
    }

    private void ConfigureHotkey(string value)
    {
        var next = OverlayHotkey.Parse(value);
        if (_hotkey?.DisplayText.Equals(next.DisplayText, StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        if (_hotkeyRegistered)
        {
            _ = UnregisterHotKey(_window, HotkeyId);
            _hotkeyRegistered = false;
        }

        _hotkey = next;
        if (WindowsHotkeyMapper.TryMap(next, out var modifiers, out var virtualKey))
        {
            _hotkeyRegistered = RegisterHotKey(_window, HotkeyId, modifiers, virtualKey);
        }

        _statusCallback(
            new OverlayWireStatus(
                "Ready",
                _hotkeyRegistered,
                _hotkeyRegistered
                    ? $"快捷键 {next.DisplayText} 已注册。"
                    : $"快捷键 {next.DisplayText} 已被其他程序占用；覆盖层仍可随游戏生命周期显示。"));
    }

    private void ApplyVisibility()
    {
        if (_frame?.IsVisible == true && !_hotkeySuppressed)
        {
            _ = SetWindowPos(
                _window,
                new nint(-1),
                0,
                0,
                0,
                0,
                SwpNoactivate | SwpShowwindow | 0x0001 | 0x0002);
        }
        else
        {
            _ = ShowWindow(_window, SwHide);
        }
    }

    private void Paint()
    {
        var deviceContext = BeginPaint(_window, out var paint);
        if (deviceContext == nint.Zero)
        {
            return;
        }

        try
        {
            var bounds = new Rect { Left = 0, Top = 0, Right = _windowWidth, Bottom = _windowHeight };
            var background = CreateSolidBrush(ToColorRef(23, 26, 31));
            _ = FillRect(deviceContext, ref bounds, background);
            _ = DeleteObject(background);
            _ = SetBkMode(deviceContext, TransparentBackgroundMode);
            DrawMetrics(deviceContext);
        }
        finally
        {
            _ = EndPaint(_window, ref paint);
        }
    }

    private void DrawMetrics(nint deviceContext)
    {
        if (_frame is null)
        {
            return;
        }

        var metrics = new List<(string Text, OverlayWireMetric Metric, uint Color)>();
        if (_frame.ShowFps)
        {
            metrics.Add(($"FPS {FormatNumber(_frame.Fps, string.Empty)}", _frame.Fps, ToColorRef(98, 201, 129)));
        }

        if (_frame.ShowCpu)
        {
            metrics.Add(($"CPU {FormatNumber(_frame.Cpu, "%")}", _frame.Cpu, ToColorRef(84, 158, 255)));
        }

        if (_frame.ShowGpu)
        {
            metrics.Add(($"GPU {FormatNumber(_frame.Gpu, "%")}", _frame.Gpu, ToColorRef(244, 166, 86)));
        }

        if (_frame.ShowRam)
        {
            metrics.Add(($"RAM {FormatRam(_frame.Ram)}", _frame.Ram, ToColorRef(187, 134, 252)));
        }

        if (metrics.Count == 0)
        {
            metrics.Add(("GameNest", new OverlayWireMetric(null, Application.TelemetryMetricStatus.Unavailable, null), ToColorRef(210, 215, 222)));
        }

        var rows = _twoRows ? checked((int)Math.Ceiling(metrics.Count / 2d)) : 1;
        var columns = _twoRows ? Math.Min(2, metrics.Count) : metrics.Count;
        var cellWidth = _windowWidth / columns;
        var cellHeight = _windowHeight / rows;
        var fontHeight = -Math.Max(12, checked((int)Math.Round(14 * _frame.Dpi / 96d * _frame.ScalePercent / 100d)));
        var font = CreateFontW(
            fontHeight,
            0,
            0,
            0,
            600,
            0,
            0,
            0,
            1,
            0,
            0,
            5,
            0,
            "Cascadia Mono");
        var oldFont = SelectObject(deviceContext, font);
        try
        {
            for (var index = 0; index < metrics.Count; index++)
            {
                var row = _twoRows ? index / 2 : 0;
                var column = _twoRows ? index % 2 : index;
                var rect = new Rect
                {
                    Left = column * cellWidth,
                    Top = row * cellHeight,
                    Right = Math.Min(_windowWidth, (column + 1) * cellWidth),
                    Bottom = Math.Min(_windowHeight, (row + 1) * cellHeight),
                };
                var color = metrics[index].Metric.Status == Application.TelemetryMetricStatus.Available
                    ? metrics[index].Color
                    : ToColorRef(157, 164, 174);
                _ = SetTextColor(deviceContext, color);
                _ = DrawTextW(
                    deviceContext,
                    metrics[index].Text,
                    metrics[index].Text.Length,
                    ref rect,
                    DtCenter | DtVcenter | DtSingleline | DtEndEllipsis);
            }
        }
        finally
        {
            _ = SelectObject(deviceContext, oldFont);
            _ = DeleteObject(font);
        }
    }

    private static string FormatNumber(OverlayWireMetric metric, string suffix) =>
        metric.Status == Application.TelemetryMetricStatus.Available && metric.Value is not null
            ? $"{metric.Value.Value:0}{suffix}"
            : "--";

    private static string FormatRam(OverlayWireMetric metric)
    {
        if (metric.Status != Application.TelemetryMetricStatus.Available || metric.Value is null)
        {
            return "--";
        }

        const double gigabyte = 1024d * 1024d * 1024d;
        const double megabyte = 1024d * 1024d;
        return metric.Value.Value >= gigabyte
            ? $"{metric.Value.Value / gigabyte:0.0} GB"
            : $"{metric.Value.Value / megabyte:0} MB";
    }

    private static uint ToColorRef(byte red, byte green, byte blue) =>
        red | ((uint)green << 8) | ((uint)blue << 16);

    private static void RegisterWindowClass()
    {
        var windowClass = new WindowClass
        {
            Size = checked((uint)Marshal.SizeOf<WindowClass>()),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate),
            Instance = GetModuleHandleW(null),
            ClassName = WindowClassName,
            Cursor = LoadCursorW(nint.Zero, new nint(32512)),
        };
        var atom = RegisterClassExW(ref windowClass);
        if (atom == 0 && Marshal.GetLastWin32Error() != 1410)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private delegate nint WndProcDelegate(nint window, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Value;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public nint DeviceContext;
        public int Erase;
        public Rect Paint;
        public int Restore;
        public int Update;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Reserved;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClass windowClass);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessageW(out Message message, nint window, uint minimum, uint maximum);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DispatchMessageW(ref Message message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int ellipseWidth,
        int ellipseHeight);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint window, out PaintStruct paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(nint window, ref PaintStruct paint);

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(uint color);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint deviceContext, ref Rect rect, nint brush);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(nint deviceContext, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(nint deviceContext, uint color);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint deviceContext, nint value);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateFontW(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint charSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string faceName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(
        nint deviceContext,
        string text,
        int count,
        ref Rect rect,
        uint format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(
        nint window,
        nint rect,
        [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadCursorW(nint instance, nint cursorName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(nint value);
}

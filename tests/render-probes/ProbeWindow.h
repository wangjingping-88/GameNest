#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cwchar>

inline LRESULT CALLBACK ProbeWindowProcedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam)
{
    switch (message)
    {
    case WM_CLOSE:
        DestroyWindow(window);
        return 0;
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    default:
        return DefWindowProcW(window, message, wparam, lparam);
    }
}

inline HWND CreateProbeWindow(const wchar_t* title)
{
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    const HINSTANCE instance = GetModuleHandleW(nullptr);
    WNDCLASSEXW window_class{};
    window_class.cbSize = sizeof(window_class);
    window_class.hInstance = instance;
    window_class.lpfnWndProc = ProbeWindowProcedure;
    window_class.lpszClassName = L"GameNest.RenderProbe";
    window_class.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    RegisterClassExW(&window_class);

    const bool borderless = std::wcsstr(GetCommandLineW(), L"--borderless") != nullptr;
    const DWORD style = borderless ? WS_POPUP : WS_OVERLAPPEDWINDOW;
    RECT bounds{0, 0, borderless ? GetSystemMetrics(SM_CXSCREEN) : 960,
                      borderless ? GetSystemMetrics(SM_CYSCREEN) : 540};
    if (!borderless)
    {
        AdjustWindowRect(&bounds, style, FALSE);
    }

    HWND window = CreateWindowExW(
        0,
        window_class.lpszClassName,
        title,
        style | WS_VISIBLE,
        borderless ? 0 : CW_USEDEFAULT,
        borderless ? 0 : CW_USEDEFAULT,
        bounds.right - bounds.left,
        bounds.bottom - bounds.top,
        nullptr,
        nullptr,
        instance,
        nullptr);
    return window;
}

inline void GetProbeClientSize(HWND window, UINT& width, UINT& height)
{
    RECT bounds{};
    GetClientRect(window, &bounds);
    width = static_cast<UINT>(bounds.right - bounds.left);
    height = static_cast<UINT>(bounds.bottom - bounds.top);
}

inline bool PumpProbeMessages()
{
    MSG message{};
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE))
    {
        if (message.message == WM_QUIT)
        {
            return false;
        }

        TranslateMessage(&message);
        DispatchMessageW(&message);
    }

    return true;
}

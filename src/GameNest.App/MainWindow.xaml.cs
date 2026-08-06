using GameNest.App.ViewModels;
using GameNest.Application;
using GameNest.Domain;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.Graphics;

namespace GameNest.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private static readonly SizeInt32 DefaultWindowSize = new(1680, 1000);
    private readonly CancellationTokenSource _windowLifetime = new();
    private bool _isPickerOpen;

    public MainWindow(MainWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ContentRoot.DataContext = ViewModel;

        Title = "GameNest";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "GameNest.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = DefaultWindowSize.Width;
            presenter.PreferredMinimumHeight = DefaultWindowSize.Height;
        }

        AppWindow.Resize(DefaultWindowSize);
        Closed += HandleClosed;
    }

    public MainWindowViewModel ViewModel { get; }

    private async void AddGame_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.IsBusy || _isPickerOpen)
        {
            return;
        }

        _isPickerOpen = true;
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.Desktop,
                ViewMode = PickerViewMode.List,
                Title = "选择游戏主程序或快捷方式",
                CommitButtonText = "添加",
                SettingsIdentifier = "GameNest.AddGame",
            };
            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add(".lnk");

            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                await ViewModel.AddGameAsync(file.Path, _windowLifetime.Token);
            }
        }
        finally
        {
            _isPickerOpen = false;
        }
    }

    private async void AddScanRoot_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.Scan.IsScanning || _isPickerOpen)
        {
            return;
        }

        _isPickerOpen = true;
        try
        {
            var picker = new FolderPicker(AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
                ViewMode = PickerViewMode.List,
                Title = "选择扫描目录",
                CommitButtonText = "添加目录",
                SettingsIdentifier = "GameNest.AddScanRoot",
            };

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                await ViewModel.Scan.AddRootAsync(folder.Path, ScanMode.Quick, _windowLifetime.Token);
            }
        }
        finally
        {
            _isPickerOpen = false;
        }
    }

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.IsMaintenanceBusy || _isPickerOpen)
        {
            return;
        }

        _isPickerOpen = true;
        try
        {
            var picker = new FolderPicker(AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List,
                Title = "选择脱敏诊断包的保存目录",
                CommitButtonText = "导出到此处",
                SettingsIdentifier = "GameNest.ExportDiagnostics",
            };

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                await ViewModel.ExportDiagnosticsAsync(folder.Path, _windowLifetime.Token);
            }
        }
        finally
        {
            _isPickerOpen = false;
        }
    }

    private async void EditGame_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var game = ViewModel.SelectedGame;
        if (game is null || ViewModel.IsBusy)
        {
            return;
        }

        var titleBox = new TextBox
        {
            Header = "游戏名称",
            Text = game.Title,
            MaxLength = 160,
        };
        var descriptionBox = new TextBox
        {
            Header = "本地简介",
            Text = game.Model.Description ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 92,
        };
        var argumentsBox = new TextBox
        {
            Header = "启动参数",
            Text = game.Model.LaunchProfile.Arguments ?? string.Empty,
        };
        var workingDirectoryBox = new TextBox
        {
            Header = "工作目录",
            Text = game.WorkingDirectory,
        };
        var pathBox = new TextBox
        {
            Header = "主程序（如需更换，请移除后重新添加）",
            Text = game.ExecutablePath,
            IsReadOnly = true,
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(titleBox);
        content.Children.Add(descriptionBox);
        content.Children.Add(pathBox);
        content.Children.Add(argumentsBox);
        content.Children.Add(workingDirectoryBox);

        var dialog = new ContentDialog
        {
            XamlRoot = ContentRoot.XamlRoot,
            Title = "编辑本地游戏信息",
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.UpdateGameAsync(
                game,
                new GameEditorInput(
                    titleBox.Text,
                    descriptionBox.Text,
                    argumentsBox.Text,
                    workingDirectoryBox.Text),
                _windowLifetime.Token);
        }
    }

    private async void ChangeCover_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var game = ViewModel.SelectedGame;
        if (game is null || ViewModel.IsBusy || _isPickerOpen)
        {
            return;
        }

        _isPickerOpen = true;
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                ViewMode = PickerViewMode.Thumbnail,
                Title = "选择本地游戏封面",
                CommitButtonText = "使用此封面",
                SettingsIdentifier = "GameNest.ChangeCover",
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".webp");

            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                await ViewModel.ImportCoverAsync(game, file.Path, _windowLifetime.Token);
            }
        }
        finally
        {
            _isPickerOpen = false;
        }
    }

    private async void RemoveCover_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var game = ViewModel.SelectedGame;
        if (game is null || !game.HasCover || ViewModel.IsBusy)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = ContentRoot.XamlRoot,
            Title = $"移除“{game.Title}”的封面？",
            Content = "只会移除 GameNest 中的封面关联，不会删除原始图片。之后仍可重新选择封面。",
            PrimaryButtonText = "移除封面",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RemoveCoverAsync(game, _windowLifetime.Token);
        }
    }

    private async void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var game = ViewModel.SelectedGame;
        if (game is null || ViewModel.IsBusy)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = ContentRoot.XamlRoot,
            Title = $"从游戏库移除“{game.Title}”？",
            Content = "只会删除 GameNest 中的本地记录，不会删除原始游戏文件；缓存图标会在后续清理中回收。",
            PrimaryButtonText = "移除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RemoveGameAsync(game, _windowLifetime.Token);
        }
    }

    private async void GameOverlaySettings_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var game = ViewModel.SelectedGame;
        if (game is null || ViewModel.IsBusy)
        {
            return;
        }

        var global = await ViewModel.GetGlobalOverlayProfileAsync(_windowLifetime.Token);
        var existing = await ViewModel.GetGameOverlayProfileAsync(game.Id, _windowLifetime.Token);
        var effective = existing ?? global;
        var useOverride = new ToggleSwitch
        {
            Header = "独立设置",
            IsOn = existing is not null,
            OnContent = "该游戏使用独立设置",
            OffContent = "跟随全局设置",
        };
        var enabled = new ToggleSwitch
        {
            Header = "覆盖层",
            IsOn = effective.IsEnabled,
            OnContent = "启用",
            OffContent = "关闭",
        };
        var position = new ComboBox
        {
            Header = "位置",
            ItemsSource = ViewModel.OverlayPositionOptions,
            SelectedItem = ViewModel.OverlayPositionOptions.First(option => option.Value == effective.Position),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var scale = new ComboBox
        {
            Header = "缩放",
            ItemsSource = ViewModel.OverlayScaleOptions,
            SelectedItem = effective.ScalePercent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var opacityValue = new TextBlock
        {
            Text = $"{effective.BackgroundOpacityPercent}%",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var opacity = new Slider
        {
            Minimum = 50,
            Maximum = 95,
            StepFrequency = 1,
            Value = effective.BackgroundOpacityPercent,
        };
        opacity.ValueChanged += (_, args) => opacityValue.Text = $"{Math.Round(args.NewValue):0}%";
        var showFps = new CheckBox { Content = "FPS", IsChecked = effective.ShowFps };
        var showCpu = new CheckBox { Content = "CPU", IsChecked = effective.ShowCpu };
        var showGpu = new CheckBox { Content = "GPU", IsChecked = effective.ShowGpu };
        var showRam = new CheckBox { Content = "RAM", IsChecked = effective.ShowRam };
        var metrics = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        metrics.Children.Add(showFps);
        metrics.Children.Add(showCpu);
        metrics.Children.Add(showGpu);
        metrics.Children.Add(showRam);
        var hotkey = new TextBox
        {
            Header = "显示 / 隐藏快捷键",
            Text = effective.ToggleHotkey,
        };
        var hideInBackground = new ToggleSwitch
        {
            Header = "游戏不在前台时",
            IsOn = effective.HideWhenGameNotForeground,
            OnContent = "自动隐藏",
            OffContent = "仍然显示",
        };
        var editor = new StackPanel { Spacing = 12 };
        editor.Children.Add(enabled);
        editor.Children.Add(new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(),
                new ColumnDefinition { Width = new GridLength(12) },
                new ColumnDefinition(),
            },
            Children =
            {
                position,
                scale,
            },
        });
        Grid.SetColumn(scale, 2);
        var opacityHeader = new Grid();
        opacityHeader.ColumnDefinitions.Add(new ColumnDefinition());
        opacityHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        opacityHeader.Children.Add(new TextBlock { Text = "背景不透明度" });
        opacityHeader.Children.Add(opacityValue);
        Grid.SetColumn(opacityValue, 1);
        editor.Children.Add(opacityHeader);
        editor.Children.Add(opacity);
        editor.Children.Add(metrics);
        editor.Children.Add(hotkey);
        editor.Children.Add(hideInBackground);
        editor.IsHitTestVisible = useOverride.IsOn;
        editor.Opacity = useOverride.IsOn ? 1 : 0.55;
        useOverride.Toggled += (_, _) =>
        {
            editor.IsHitTestVisible = useOverride.IsOn;
            editor.Opacity = useOverride.IsOn ? 1 : 0.55;
        };

        var content = new StackPanel { Spacing = 16, MinWidth = 460 };
        content.Children.Add(useOverride);
        content.Children.Add(editor);
        var dialog = new ContentDialog
        {
            XamlRoot = ContentRoot.XamlRoot,
            Title = $"“{game.Title}”的覆盖层设置",
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var selectedPosition = (ViewModels.OverlayPositionOption?)position.SelectedItem
                               ?? ViewModel.OverlayPositionOptions[1];
        var selectedScale = scale.SelectedItem is int value ? value : 100;
        await ViewModel.SaveGameOverlayProfileAsync(
            game.Id,
            useOverride.IsOn,
            new OverlayProfileEditorInput(
                enabled.IsOn,
                selectedPosition.Value,
                selectedScale,
                checked((int)Math.Round(opacity.Value)),
                showFps.IsChecked == true,
                showCpu.IsChecked == true,
                showGpu.IsChecked == true,
                showRam.IsChecked == true,
                hotkey.Text,
                hideInBackground.IsOn),
            _windowLifetime.Token);
    }

    private async void StopGame_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var game = ViewModel.SelectedGame;
        if (game is null || !game.CanStop)
        {
            return;
        }

        var result = await ViewModel.StopGameAsync(game, force: false, _windowLifetime.Token);
        if (result?.Outcome != GameStopOutcome.ConfirmationRequired)
        {
            return;
        }

        var processText = result.RemainingProcessIds.Count == 0
            ? "已确认的游戏进程仍在运行。"
            : $"仍在运行的已确认 PID：{string.Join("、", result.RemainingProcessIds)}。";
        var dialog = new ContentDialog
        {
            XamlRoot = ContentRoot.XamlRoot,
            Title = $"强制结束“{game.Title}”？",
            Content = $"{result.Message}\n\n{processText}\n\n强制结束可能导致未保存进度丢失。",
            PrimaryButtonText = "强制结束",
            CloseButtonText = "继续运行",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.StopGameAsync(game, force: true, _windowLifetime.Token);
        }
    }

    private void HandleClosed(object sender, WindowEventArgs args)
    {
        _ = sender;
        _ = args;
        _windowLifetime.Cancel();
        Dispose();
    }

    public void Dispose()
    {
        _windowLifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}

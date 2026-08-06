using GameNest.App.ViewModels;
using GameNest.Application;
using GameNest.Domain;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using System.Numerics;

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
        ViewModel.UpdateInstallerStarted += HandleUpdateInstallerStarted;
        ViewModel.OpenUpdatePageRequested += HandleOpenUpdatePageRequested;
        ViewModel.FocusGameRequested += HandleFocusGameRequested;
        ContentRoot.ActualThemeChanged += HandleActualThemeChanged;
        HomeCoverBackground.Loaded += HandleHomeCoverLoaded;

        Title = "GameNest";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "GameNest.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = DefaultWindowSize.Width;
            presenter.PreferredMinimumHeight = DefaultWindowSize.Height;
        }

        AppWindow.Resize(GetInitialWindowSize());
        ApplyTitleBarTheme();
        Closed += HandleClosed;
    }

    public MainWindowViewModel ViewModel { get; }

    private SizeInt32 GetInitialWindowSize()
    {
        var workArea = DisplayArea
            .GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary)
            .WorkArea;
        var width = Math.Min(
            workArea.Width,
            Math.Max(DefaultWindowSize.Width, (int)Math.Round(workArea.Width * 0.66)));
        var height = Math.Min(
            workArea.Height,
            Math.Max(DefaultWindowSize.Height, (int)Math.Round(workArea.Height * 0.70)));
        return new SizeInt32(width, height);
    }

    private async void AddGame_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!ViewModel.IsHomePage && !ViewModel.IsLibraryPage)
        {
            ViewModel.OpenLibraryCommand.Execute(null);
            return;
        }

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

    private async void SearchOnlineCover_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var game = ViewModel.SelectedGame;
        if (game is null || ViewModel.IsBusy)
        {
            return;
        }

        var candidates = await ViewModel.SearchOnlineCoversAsync(game, _windowLifetime.Token);
        if (candidates.Count == 0)
        {
            ViewModel.StatusMessage = $"没有找到“{game.Title}”的在线封面，可继续使用本地自定义封面。";
            return;
        }

        var selector = new ComboBox
        {
            Header = "Steam 商店候选",
            ItemsSource = candidates,
            SelectedIndex = 0,
            DisplayMemberPath = nameof(GameCoverCandidate.Title),
            MinWidth = 360,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = ContentRoot.XamlRoot,
            Title = $"为“{game.Title}”选择在线封面",
            Content = selector,
            PrimaryButtonText = "使用封面",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && selector.SelectedItem is GameCoverCandidate candidate)
        {
            await ViewModel.ApplyOnlineCoverAsync(game, candidate, _windowLifetime.Token);
        }
    }

    private async void FetchAllCovers_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.IsBusy)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = ContentRoot.XamlRoot,
            Title = "获取全部游戏封面？",
            Content = "将为缺少封面的游戏查询 Steam 商店。已有封面和你手动移除的封面都不会被覆盖。",
            PrimaryButtonText = "开始获取",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.FetchAllMissingCoversAsync(_windowLifetime.Token);
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

    private async void RemoveSelectedGames_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!ViewModel.HasSelectedGames || ViewModel.IsBusy)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = ContentRoot.XamlRoot,
            Title = "从游戏库移除已选择项目？",
            Content = "只会删除 GameNest 中的本地记录，不会删除原始游戏文件。",
            PrimaryButtonText = "移除已选择项目",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RemoveSelectedGamesAsync(_windowLifetime.Token);
        }
    }

    private void GameGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = e;
        if (sender is not GridView grid)
        {
            return;
        }

        ViewModel.UpdateGameSelection(grid.SelectedItems.OfType<GameCardViewModel>());
    }

    private void GameCard_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not GameCardViewModel game)
        {
            return;
        }

        var grid = FindAncestor<GridView>(element);
        if (grid is null)
        {
            return;
        }

        if (!grid.SelectedItems.Contains(game))
        {
            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(game);
        }

        ViewModel.SelectedGame = game;
        ViewModel.UpdateGameSelection(grid.SelectedItems.OfType<GameCardViewModel>());
        var selectedCount = grid.SelectedItems.Count;
        var menu = new MenuFlyout();
        if (selectedCount == 1)
        {
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = "启动游戏",
                Command = game.LaunchCommand,
            });
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = game.FavoriteLabel,
                Command = game.ToggleFavoriteCommand,
            });
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        var removeItem = new MenuFlyoutItem
        {
            Text = selectedCount == 1 ? "从游戏库移除" : $"从游戏库移除已选（{selectedCount}）",
        };
        removeItem.Click += RemoveSelectedGames_Click;
        menu.Items.Add(removeItem);
        menu.ShowAt(element, e.GetPosition(element));
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
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
        ViewModel.UpdateInstallerStarted -= HandleUpdateInstallerStarted;
        ViewModel.OpenUpdatePageRequested -= HandleOpenUpdatePageRequested;
        ViewModel.FocusGameRequested -= HandleFocusGameRequested;
        ContentRoot.ActualThemeChanged -= HandleActualThemeChanged;
        HomeCoverBackground.Loaded -= HandleHomeCoverLoaded;
        _windowLifetime.Cancel();
        Dispose();
    }

    private void HandleUpdateInstallerStarted() => Close();

    private void HandleFocusGameRequested(GameCardViewModel game)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            GameGrid.SelectedItem = game;
            GameGrid.ScrollIntoView(game);
        });
    }

    private void HandleActualThemeChanged(FrameworkElement sender, object args)
    {
        _ = sender;
        _ = args;
        DispatcherQueue.TryEnqueue(ApplyTitleBarTheme);
    }

    private void HandleHomeCoverLoaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var visual = ElementCompositionPreview.GetElementVisual(HomeCoverBackground);
        visual.CenterPoint = new Vector3(
            (float)(HomeCoverBackground.ActualWidth / 2),
            (float)(HomeCoverBackground.ActualHeight / 2),
            0);
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0f, Vector3.One);
        animation.InsertKeyFrame(0.5f, new Vector3(1.035f, 1.035f, 1f));
        animation.InsertKeyFrame(1f, Vector3.One);
        animation.Duration = TimeSpan.FromSeconds(18);
        animation.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(visual.Scale), animation);
    }

    private void ApplyTitleBarTheme()
    {
        var isDark = ContentRoot.ActualTheme == ElementTheme.Dark;
        var titleBar = AppWindow.TitleBar;
        var foreground = isDark ? Color.FromArgb(255, 244, 247, 251) : Color.FromArgb(255, 24, 33, 47);
        var hoverBackground = isDark ? Color.FromArgb(255, 46, 57, 71) : Color.FromArgb(255, 223, 233, 245);
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = isDark ? Color.FromArgb(255, 166, 176, 191) : Color.FromArgb(255, 102, 112, 133);
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = isDark ? Color.FromArgb(255, 39, 71, 102) : Color.FromArgb(255, 213, 233, 255);
    }

    private async void HandleOpenUpdatePageRequested(Uri releasePageUri)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(releasePageUri);
        }
        catch (Exception)
        {
            ViewModel.StatusMessage = "无法打开 GitHub 下载页，请稍后重试。";
        }
    }

    public void Dispose()
    {
        _windowLifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}

using GameNest.App.ViewModels;
using GameNest.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace GameNest.App;

public partial class App : Microsoft.UI.Xaml.Application, IDisposable
{
    private static readonly Action<ILogger, Exception?> UnhandledUiException =
        LoggerMessage.Define(
            LogLevel.Critical,
            new EventId(2000, nameof(UnhandledUiException)),
            "主界面发生未处理异常。");

    private readonly CancellationTokenSource _applicationLifetime = new();
    private readonly ServiceProvider _services;
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        _services = BuildServices();
        UnhandledException += HandleUnhandledException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _ = args;

        var viewModel = _services.GetRequiredService<MainWindowViewModel>();
        _window = new MainWindow(viewModel);
        _window.Closed += HandleWindowClosed;
        _window.Activate();

        await viewModel.InitializeAsync(_applicationLifetime.Token);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(static builder => builder.SetMinimumLevel(LogLevel.Information));
        services.AddGameNestInfrastructure();
        services.AddSingleton<ScanPageViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
    }

    private void HandleUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        _ = sender;
        UnhandledUiException(_services.GetRequiredService<ILogger<App>>(), args.Exception);
    }

    private async void HandleWindowClosed(object sender, WindowEventArgs args)
    {
        _ = sender;
        _ = args;

        _applicationLifetime.Cancel();
        await _services.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        _applicationLifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}

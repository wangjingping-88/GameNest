using GameNest.App.ViewModels;
using GameNest.Application;
using GameNest.Infrastructure;
using GameNest.Infrastructure.Updates;
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
    private static readonly Action<ILogger, string, Exception?> UpdateModeFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2001, nameof(UpdateModeFailed)),
            "应用更新模式失败：{Mode}。");

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
        var commandLine = Environment.GetCommandLineArgs();
        if (TryGetOptionValue(commandLine, "--apply-update", out var updatePlan))
        {
            await RunUpdateApplierAsync(updatePlan);
            return;
        }

        var viewModel = _services.GetRequiredService<MainWindowViewModel>();
        _window = new MainWindow(
            viewModel,
            _services.GetRequiredService<IGameWindowLocator>());
        _window.Closed += HandleWindowClosed;
        if (TryGetOptionValue(commandLine, "--complete-update", out var completionPlan))
        {
            await CompleteUpdateAsync(viewModel, completionPlan);
        }
        else
        {
            _window.Activate();
            await viewModel.InitializeAsync(_applicationLifetime.Token);
        }
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

    private async Task RunUpdateApplierAsync(string planFile)
    {
        var exitCode = 20;
        try
        {
            exitCode = await _services
                .GetRequiredService<IPortableUpdateApplier>()
                .ApplyAsync(planFile, _applicationLifetime.Token);
        }
        catch (Exception exception)
        {
            UpdateModeFailed(_services.GetRequiredService<ILogger<App>>(), "apply-update", exception);
        }

        Environment.ExitCode = exitCode;
        await ShutdownWithoutWindowAsync();
    }

    private async Task CompleteUpdateAsync(MainWindowViewModel viewModel, string planFile)
    {
        PortableUpdatePlan? plan = null;
        try
        {
            plan = await PortableUpdatePlanStore.ReadAsync(planFile, _applicationLifetime.Token);
            var currentVersion = _services.GetRequiredService<IApplicationUpdateService>().CurrentVersion;
            if (!ApplicationVersion.TryParseStable(plan.ExpectedVersion, out var expectedVersion) ||
                expectedVersion != currentVersion)
            {
                throw new InvalidDataException("启动版本与升级计划不一致。");
            }

            await viewModel.InitializeAsync(_applicationLifetime.Token);
            if (viewModel.HasLibraryError)
            {
                throw new InvalidOperationException("新版初始化未能正常打开本地数据库。");
            }

            await File.WriteAllTextAsync(
                plan.HealthFile,
                $"GameNest {ApplicationVersion.Format(currentVersion)} healthy {DateTimeOffset.UtcNow:O}",
                _applicationLifetime.Token);
            _window?.Activate();
        }
        catch (Exception exception)
        {
            UpdateModeFailed(_services.GetRequiredService<ILogger<App>>(), "complete-update", exception);
            if (plan is not null)
            {
                try
                {
                    await File.WriteAllTextAsync(plan.FailureFile, exception.Message, CancellationToken.None);
                }
                catch (Exception writeException)
                {
                    UpdateModeFailed(_services.GetRequiredService<ILogger<App>>(), "write-update-failure", writeException);
                }
            }

            Environment.ExitCode = 21;
            _window?.Close();
        }
    }

    private async Task ShutdownWithoutWindowAsync()
    {
        _applicationLifetime.Cancel();
        await _services.DisposeAsync();
        Dispose();
        Exit();
    }

    private static bool TryGetOptionValue(
        string[] arguments,
        string option,
        out string value)
    {
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], option, StringComparison.Ordinal))
            {
                value = arguments[index + 1];
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        value = string.Empty;
        return false;
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

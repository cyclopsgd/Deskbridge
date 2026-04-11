using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Deskbridge;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplicationThemeManager.Apply(
            ApplicationTheme.Dark,
            WindowBackdropType.Mica,
            updateAccent: true
        );

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IConnectionPipeline, ConnectionPipeline>();
        services.AddSingleton<IDisconnectPipeline, DisconnectPipeline>();
        services.AddSingleton<IConnectionQuery, ConnectionQueryService>();

        // Views
        services.AddTransient<MainWindow>();
        services.AddTransient<ViewModels.MainWindowViewModel>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}

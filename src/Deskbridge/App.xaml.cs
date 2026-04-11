using System.Windows.Media;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Services;
using Wpf.Ui;
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

        // Override system accent with Deskbridge brand colour #007ACC (per SHEL-08)
        ApplicationAccentColorManager.Apply(
            Color.FromArgb(0xFF, 0x00, 0x7A, 0xCC),
            ApplicationTheme.Dark
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

        // Connection persistence and credentials
        services.AddSingleton<IConnectionStore>(sp =>
        {
            var store = new JsonConnectionStore();
            store.Load();
            return store;
        });
        services.AddSingleton<ICredentialService, WindowsCredentialService>();
        services.AddSingleton<IConnectionQuery>(sp =>
            new ConnectionQueryService(sp.GetRequiredService<IConnectionStore>()));

        // WPF-UI services (for Phases 3+ and 6)
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();

        // ViewModels
        services.AddTransient<ViewModels.MainWindowViewModel>();
        services.AddTransient<ViewModels.ConnectionTreeViewModel>();

        // Views
        services.AddTransient<Views.ConnectionTreeControl>();
        services.AddTransient<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}

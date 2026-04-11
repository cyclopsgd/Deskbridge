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
        // Views
        services.AddTransient<MainWindow>();
        services.AddTransient<ViewModels.MainWindowViewModel>();

        // Core services will be registered in Plan 02
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}

using Velopack;

namespace Deskbridge;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        // LOG-04 Pattern 4 — install global exception hooks BEFORE constructing App.
        // A crash inside the App ctor or InitializeComponent must still hit the logger;
        // the Dispatcher hook is added later in App.OnStartup once Application.Current
        // is valid.
        CrashHandler.Install();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}

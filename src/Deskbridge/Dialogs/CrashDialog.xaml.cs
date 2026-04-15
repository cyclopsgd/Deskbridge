using System.Diagnostics;
using System.Text;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Deskbridge.Dialogs;

/// <summary>
/// Phase 6 Plan 06-04 (LOG-04, D-11, UI-SPEC §Crash Dialog) completion of the
/// Plan 06-01 <c>TryShowCrashDialog</c> stub. Displays a modal ContentDialog
/// with Copy Details + Restart actions per UI-SPEC.
///
/// <para><b>Copy Details</b> flow — sets the dialog clipboard via
/// <see cref="Clipboard.SetText(string)"/> then transforms the button label to
/// "Copied" for 2 seconds before reverting. The <see cref="ContentDialog.Closing"/>
/// handler cancels the close when the Primary button was the trigger so the
/// user can actually paste somewhere (WPF-UI 4.2's <c>ButtonClicked</c> args
/// have no <c>Cancel</c>; <c>Closing</c> args do).</para>
///
/// <para><b>Restart</b> flow (UI-SPEC A11) — <c>Process.Start(MainModule.FileName)</c>
/// spawns a fresh copy of the exe then <c>Application.Current.Shutdown()</c>
/// exits the current process. Velopack's <c>UpdateManager.RestartApp()</c> is
/// NOT used here — we're not applying an update, just relaunching, and the
/// MainModule-based approach avoids an Update.exe-wait race.</para>
///
/// <para><b>Details content</b> — exception type, message, and stack trace for
/// the exception chain (inner exceptions walked). Plan 06-01's
/// <c>RedactSensitivePolicy</c> scrubs any <c>Password</c>/<c>Secret</c>-type
/// properties from log output; this dialog prints the raw exception so we
/// rely on the exception authors not to interpolate secrets into messages
/// (they don't in any of the pipeline code).</para>
/// </summary>
public partial class CrashDialog : ContentDialog
{
    private readonly Exception _exception;

    public CrashDialog(Exception exception, IContentDialogService service)
        : base(service.GetDialogHostEx())
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(service);

        _exception = exception;
        InitializeComponent();

        // WPF-UI 4.2.0: ContentDialogButtonClickEventArgs has no Cancel property.
        // We use the Closing event (which DOES have Cancel) to suppress the close
        // when the Primary button was pressed. ButtonClicked performs the side-
        // effect (clipboard copy + label transform for Primary, restart for Close).
        ButtonClicked += OnButtonClicked;
        Closing += OnClosing;
    }

    private async void OnButtonClicked(ContentDialog sender, ContentDialogButtonClickEventArgs e)
    {
        if (e.Button == ContentDialogButton.Primary)
        {
            // Copy Details — clipboard + transform label for 2s. The Closing
            // handler below cancels the natural close for Primary so the
            // dialog stays open (user can click Restart after pasting).
            try
            {
                Clipboard.SetText(BuildDetails(_exception));
            }
            catch (Exception copyEx)
            {
                Serilog.Log.Warning(copyEx, "Failed to copy crash details to clipboard");
                return;
            }

            var original = PrimaryButtonText;
            PrimaryButtonText = "Copied";
            try
            {
                await Task.Delay(2000);
            }
            finally
            {
                PrimaryButtonText = original;
            }
        }
    }

    /// <summary>
    /// WPF-UI 4.2.0 doesn't expose <c>Cancel</c> on the ButtonClicked event
    /// args but DOES expose it on the Closing event args. Cancel the close
    /// when Primary was the trigger (Copy Details must leave the dialog open);
    /// allow the close for Close (Restart — we shut down ourselves afterwards).
    /// </summary>
    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs e)
    {
        if (e.Result == ContentDialogResult.Primary)
        {
            e.Cancel = true;
            return;
        }

        // Restart — spawn a fresh process from the current MainModule then
        // shutdown. A11 rationale: Velopack's RestartApp expects an Update.exe
        // wait-for-exit pattern we don't need here.
        try
        {
            var fileName = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(fileName))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                });
            }
        }
        catch (Exception restartEx)
        {
            Serilog.Log.Warning(restartEx, "Failed to launch restart process — shutting down anyway");
        }

        try
        {
            System.Windows.Application.Current?.Shutdown();
        }
        catch (Exception shutdownEx)
        {
            Serilog.Log.Warning(shutdownEx, "Application.Current.Shutdown threw — process will exit via OS");
        }
    }

    /// <summary>
    /// Builds the clipboard payload: exception chain (type + message + stack trace).
    /// Walks <see cref="Exception.InnerException"/> so AggregateExceptions and
    /// rethrown-with-new-ex chains all land in the paste.
    /// </summary>
    internal static string BuildDetails(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var sb = new StringBuilder();
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            sb.Append(cur.GetType().FullName);
            sb.Append(": ");
            sb.AppendLine(cur.Message);
            if (!string.IsNullOrEmpty(cur.StackTrace))
            {
                sb.AppendLine(cur.StackTrace);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

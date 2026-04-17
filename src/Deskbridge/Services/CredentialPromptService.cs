using System.Net;
using System.Windows.Threading;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Dialogs;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Deskbridge.Services;

/// <summary>
/// UI-layer implementation of <see cref="ICredentialPromptService"/>. Shows a
/// <see cref="CredentialPromptDialog"/> (ContentDialog) when <c>CredentialMode.Prompt</c>
/// connections need credentials. Credentials entered here are one-time and are NOT
/// persisted to Windows Credential Manager.
///
/// <para>Must run on the STA dispatcher thread. The connection pipeline already runs
/// on the dispatcher (marshalled by <c>ConnectionCoordinator</c>), so the
/// <c>ContentDialog.ShowAsync</c> call awaits naturally within the pipeline's
/// <c>ResolveCredentialsStage.ExecuteAsync</c>.</para>
/// </summary>
public sealed class CredentialPromptService : ICredentialPromptService
{
    private readonly IContentDialogService _contentDialogService;

    public CredentialPromptService(IContentDialogService contentDialogService)
    {
        _contentDialogService = contentDialogService;
    }

    public async Task<NetworkCredential?> PromptAsync(ConnectionModel connection)
    {
        // Ensure we're on the UI thread — ContentDialog requires STA dispatcher.
        var dispatcher = Dispatcher.CurrentDispatcher;
        if (!dispatcher.CheckAccess())
        {
            return await dispatcher.InvokeAsync(() => PromptAsync(connection)).Task.Unwrap();
        }

        var dialogHost = _contentDialogService.GetDialogHostEx();
        if (dialogHost is null)
        {
            Serilog.Log.Error("ContentDialogHost is null; cannot show credential prompt");
            return null;
        }

        var dialog = new CredentialPromptDialog(
            dialogHost,
            connection.Hostname,
            connection.Username,
            connection.Domain);

        var result = await dialog.ShowAsync();

        if (result != ContentDialogResult.Primary)
            return null;

        // Return one-time credentials — NOT saved to Credential Manager
        return new NetworkCredential(
            dialog.EnteredUsername,
            dialog.EnteredPassword,
            dialog.EnteredDomain);
    }
}

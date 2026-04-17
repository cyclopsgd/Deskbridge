using System.Net;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Interfaces;

/// <summary>
/// Abstracts the UI credential prompt so <see cref="Pipeline.Stages.ResolveCredentialsStage"/>
/// can await user input without referencing WPF types. The UI implementation shows a
/// <c>ContentDialog</c> with hostname, username, domain, and password fields.
/// Returns <c>null</c> when the user cancels — the pipeline stage treats this as an abort.
/// Credentials from Prompt mode are one-time and are NOT saved to Credential Manager.
/// </summary>
public interface ICredentialPromptService
{
    /// <summary>
    /// Shows a credential prompt dialog for the given connection.
    /// </summary>
    /// <param name="connection">The connection being opened (hostname displayed as header,
    /// username/domain pre-filled if available on the model).</param>
    /// <returns>A <see cref="NetworkCredential"/> with the entered values, or <c>null</c>
    /// if the user cancelled.</returns>
    Task<NetworkCredential?> PromptAsync(ConnectionModel connection);
}

using System.Net;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Microsoft.Extensions.Logging;

namespace Deskbridge.Core.Pipeline.Stages;

/// <summary>
/// Resolves credentials per <see cref="ConnectionModel.CredentialMode"/>: Own walks
/// <c>ICredentialService.GetForConnection</c>; Inherit walks the group chain via
/// <c>ICredentialService.ResolveInherited</c>; Prompt awaits
/// <see cref="ICredentialPromptService.PromptAsync"/> for user-entered one-time
/// credentials. On resolution, username + domain flow into
/// <see cref="ConnectionContext.Connection"/> and password into
/// <see cref="ConnectionContext.ResolvedPassword"/> — never logged.
/// </summary>
public sealed class ResolveCredentialsStage(
    ICredentialService creds,
    IConnectionStore store,
    IEventBus bus,
    ILogger<ResolveCredentialsStage> log,
    ICredentialPromptService? promptService = null) : IConnectionPipelineStage
{
    public string Name => "ResolveCredentials";
    public int Order => 100;

    public async Task<PipelineResult> ExecuteAsync(ConnectionContext ctx)
    {
        var c = ctx.Connection;
        switch (c.CredentialMode)
        {
            case CredentialMode.Own:
            {
                var cred = creds.GetForConnection(c);
                if (cred is null)
                {
                    log.LogInformation("No own credential for {Hostname} — prompting", c.Hostname);
                    bus.Publish(new CredentialRequestedEvent(c));
                    return new PipelineResult(false, "Credentials not found (own)");
                }
                ApplyCredential(ctx, cred);
                log.LogInformation("Credentials resolved for {Hostname}", c.Hostname);
                return new PipelineResult(true);
            }
            case CredentialMode.Inherit:
            {
                var cred = creds.ResolveInherited(c, store);
                if (cred is not null)
                {
                    ApplyCredential(ctx, cred);
                    log.LogInformation("Inherited credentials resolved for {Hostname}", c.Hostname);
                    return new PipelineResult(true);
                }

                // RELY-03: inheritance chain exhausted -- fall back to connection's own
                // stored credential before prompting. This covers the common case where
                // a connection sits at the root level (no parent group) or all ancestor
                // groups lack credentials, but the user has stored credentials directly
                // on the connection itself.
                var ownCred = creds.GetForConnection(c);
                if (ownCred is not null)
                {
                    ApplyCredential(ctx, ownCred);
                    log.LogInformation(
                        "Inherited credential not found for {Hostname} -- fell back to own credential",
                        c.Hostname);
                    return new PipelineResult(true);
                }

                log.LogInformation("No inherited or own credential for {Hostname} -- prompting", c.Hostname);
                return await PromptForCredentialsAsync(ctx);
            }
            case CredentialMode.Prompt:
            default:
                return await PromptForCredentialsAsync(ctx);
        }
    }

    private async Task<PipelineResult> PromptForCredentialsAsync(ConnectionContext ctx)
    {
        if (promptService is null)
        {
            log.LogWarning(
                "CredentialMode.Prompt for {Hostname} but no ICredentialPromptService registered",
                ctx.Connection.Hostname);
            bus.Publish(new CredentialRequestedEvent(ctx.Connection));
            return new PipelineResult(false, "Credential prompt service not available");
        }

        log.LogInformation("Prompting user for credentials for {Hostname}", ctx.Connection.Hostname);
        var cred = await promptService.PromptAsync(ctx.Connection);

        if (cred is null)
        {
            log.LogInformation("User cancelled credential prompt for {Hostname}", ctx.Connection.Hostname);
            return new PipelineResult(false, "User cancelled credential prompt");
        }

        ApplyCredential(ctx, cred);
        log.LogInformation("Credentials provided via prompt for {Hostname}", ctx.Connection.Hostname);
        return new PipelineResult(true);
    }

    private static void ApplyCredential(ConnectionContext ctx, NetworkCredential cred)
    {
        ctx.Connection.Username = cred.UserName;
        ctx.Connection.Domain = cred.Domain;
        ctx.ResolvedPassword = cred.Password;  // Do NOT log
    }
}

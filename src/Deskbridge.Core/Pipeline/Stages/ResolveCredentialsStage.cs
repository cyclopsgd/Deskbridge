using System.Net;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Microsoft.Extensions.Logging;

namespace Deskbridge.Core.Pipeline.Stages;

/// <summary>
/// Resolves credentials per <see cref="ConnectionModel.CredentialMode"/>: Own walks
/// <c>ICredentialService.GetForConnection</c>; Inherit walks the group chain via
/// <c>ICredentialService.ResolveInherited</c>; Prompt publishes
/// <see cref="CredentialRequestedEvent"/> and fails. On resolution, username + domain
/// flow into <see cref="ConnectionContext.Connection"/> and password into
/// <see cref="ConnectionContext.ResolvedPassword"/> — never logged.
/// </summary>
public sealed class ResolveCredentialsStage(
    ICredentialService creds,
    IConnectionStore store,
    IEventBus bus,
    ILogger<ResolveCredentialsStage> log) : IConnectionPipelineStage
{
    public string Name => "ResolveCredentials";
    public int Order => 100;

    public Task<PipelineResult> ExecuteAsync(ConnectionContext ctx)
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
                    return Task.FromResult(new PipelineResult(false, "Credentials not found (own)"));
                }
                ApplyCredential(ctx, cred);
                log.LogInformation("Credentials resolved for {Hostname}", c.Hostname);
                return Task.FromResult(new PipelineResult(true));
            }
            case CredentialMode.Inherit:
            {
                var cred = creds.ResolveInherited(c, store);
                if (cred is null)
                {
                    log.LogInformation("No inherited credential for {Hostname} — prompting", c.Hostname);
                    bus.Publish(new CredentialRequestedEvent(c));
                    return Task.FromResult(new PipelineResult(false, "No inherited credential found"));
                }
                ApplyCredential(ctx, cred);
                log.LogInformation("Inherited credentials resolved for {Hostname}", c.Hostname);
                return Task.FromResult(new PipelineResult(true));
            }
            case CredentialMode.Prompt:
            default:
                bus.Publish(new CredentialRequestedEvent(c));
                return Task.FromResult(new PipelineResult(false, "Credentials require prompt"));
        }
    }

    private static void ApplyCredential(ConnectionContext ctx, NetworkCredential cred)
    {
        ctx.Connection.Username = cred.UserName;
        ctx.Connection.Domain = cred.Domain;
        ctx.ResolvedPassword = cred.Password;  // Do NOT log
    }
}

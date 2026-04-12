using System.Text.Json.Serialization;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Pipeline;

public class ConnectionContext
{
    public required ConnectionModel Connection { get; set; }

    /// <summary>
    /// Resolved credential password. Do not log or serialize this value.
    /// <see cref="JsonIgnoreAttribute"/> guards against accidental <c>System.Text.Json</c>
    /// serialization (T-04-JSN regression test <c>PasswordLeakTests.NotInJson</c>).
    /// </summary>
    [JsonIgnore]
    public string? ResolvedPassword { get; set; }

    [JsonIgnore]
    public IProtocolHost? Host { get; set; }

    public Dictionary<string, object> Properties { get; } = new();

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}

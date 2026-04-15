using System.Text.Json.Serialization;

namespace Deskbridge.Core.Models;

/// <summary>
/// D-10 audit schema. Serialised one record per line to
/// <c>%AppData%/Deskbridge/audit-YYYY-MM.jsonl</c>. <see cref="ErrorCode"/> is
/// null-omitted (<see cref="JsonIgnoreCondition.WhenWritingNull"/>) so success-path
/// records do not carry an empty field.
/// </summary>
/// <param name="Ts">ISO-8601 UTC timestamp (e.g. <c>DateTime.UtcNow.ToString("O")</c>).</param>
/// <param name="Type"><see cref="AuditAction"/> name, round-tripped via <c>ToString()</c>.</param>
/// <param name="ConnectionId">Connection GUID, or <c>null</c> for app-scope events (AppStarted, AppLocked, etc.).</param>
/// <param name="User"><see cref="System.Environment.UserName"/> at the time of the event.</param>
/// <param name="Outcome">Free-text outcome — convention is <c>"success"</c> or <c>"fail"</c>.</param>
/// <param name="ErrorCode">Optional structured error code; omitted from JSON when <c>null</c>.</param>
public sealed record AuditRecord(
    string Ts,
    string Type,
    Guid? ConnectionId,
    string User,
    string Outcome,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ErrorCode = null);

/// <summary>
/// Source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> for
/// <see cref="AuditRecord"/>. Uses camelCase + null-omission to keep the jsonl tight and
/// machine-friendly for downstream tooling (jq, Splunk, etc.).
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AuditRecord))]
internal sealed partial class AuditJsonContext : JsonSerializerContext
{
}

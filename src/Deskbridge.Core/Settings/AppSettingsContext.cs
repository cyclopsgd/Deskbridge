using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deskbridge.Core.Settings;

/// <summary>
/// Phase 6 Plan 06-02 (NOTF-04): source-generated <see cref="JsonSerializerContext"/>
/// for <see cref="AppSettings"/>. AOT-safe and avoids reflection-based serialisation —
/// same approach used by <see cref="Deskbridge.Core.Models.AuditJsonContext"/> in Plan 06-01.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(BulkOperationsRecord))]
[JsonSerializable(typeof(UninstallRecord))]
internal partial class AppSettingsContext : JsonSerializerContext { }

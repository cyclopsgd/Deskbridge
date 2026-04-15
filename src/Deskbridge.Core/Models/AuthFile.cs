using System.Text.Json.Serialization;

namespace Deskbridge.Core.Models;

/// <summary>
/// Phase 6 Plan 06-04 (SEC-01, D-16): the only contents of
/// <c>%AppData%/Deskbridge/auth.json</c>. Stores the PBKDF2-hashed master password
/// in the <c>v1.&lt;base64salt&gt;.&lt;base64key&gt;</c> envelope produced by
/// <see cref="Services.MasterPasswordService.HashNewPassword"/>. No recovery
/// field — D-16 forbids any recovery mechanism because any such channel weakens
/// PBKDF2 to the recovery channel's strength. To reset, delete this file.
/// </summary>
/// <param name="PasswordHash">v1-envelope hash string; never a plaintext password.</param>
/// <param name="SchemaVersion">Reserves the shape for a future algorithm bump (e.g. Argon2id).</param>
public sealed record AuthFile(string PasswordHash, int SchemaVersion = 1);

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for <see cref="AuthFile"/>.
/// CamelCase + indented output — auth.json is written rarely and inspected by humans
/// during recovery, so the few extra bytes are worth the readability.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AuthFile))]
internal sealed partial class AuthJsonContext : JsonSerializerContext
{
}

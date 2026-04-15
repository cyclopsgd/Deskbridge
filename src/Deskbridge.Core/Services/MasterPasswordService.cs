using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Serilog;

namespace Deskbridge.Core.Services;

/// <summary>
/// Phase 6 Plan 06-04 (SEC-01, Pattern 6, T-06-03) implementation of
/// <see cref="IMasterPasswordService"/>.
///
/// <para>Algorithm: <see cref="Rfc2898DeriveBytes.Pbkdf2(string, byte[], int, HashAlgorithmName, int)"/>
/// with <c>Iterations = 600_000</c> (OWASP 2023 guidance for PBKDF2-HMAC-SHA256),
/// <c>SaltBytes = 32</c>, <c>KeyBytes = 32</c>, <see cref="HashAlgorithmName.SHA256"/>.
/// Storage envelope: <c>v1.&lt;base64 salt&gt;.&lt;base64 key&gt;</c>. The version prefix
/// reserves shape for a future algorithm bump (e.g. Argon2id).</para>
///
/// <para>Verification uses <see cref="CryptographicOperations.FixedTimeEquals"/> —
/// any non-constant-time comparison would leak the PBKDF2 output one byte at a time
/// through a timing side channel (T-06-03).</para>
///
/// <para>Project constraint: SecureString is banned (CLAUDE.md — DE0001). Passwords
/// live as plain <see cref="string"/> for the duration of the hash/verify call and
/// are released as soon as the method returns. LockOverlayViewModel scrubs its
/// in-memory reference immediately after a successful verify (T-06-05 best effort).</para>
/// </summary>
public sealed class MasterPasswordService : IMasterPasswordService
{
    // OWASP 2023 for PBKDF2-HMAC-SHA256. Test 10 (Stopwatch ≥100ms floor) enforces
    // the iteration count isn't silently dropped by a refactor — 600_000 on a typical
    // laptop takes 200-500ms, well above the 100ms floor.
    private const int Iterations = 600_000;

    // 256-bit salt, 256-bit derived key. SaltBytes=32 exceeds the 16-byte minimum in
    // NIST SP 800-132 §5.1. Larger than strictly necessary but cheap — salt is
    // per-password so rainbow-table resistance is the main concern.
    private const int SaltBytes = 32;
    private const int KeyBytes = 32;

    private const int CurrentSchemaVersion = 1;
    private static readonly HashAlgorithmName Alg = HashAlgorithmName.SHA256;
    private static readonly string VersionPrefix = $"v{CurrentSchemaVersion}";

    private readonly string _directory;
    private readonly string _filePath;

    /// <summary>Production ctor — resolves <c>%AppData%/Deskbridge/auth.json</c>.</summary>
    public MasterPasswordService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Deskbridge"))
    {
    }

    /// <summary>
    /// Test / non-roaming ctor. <paramref name="directory"/> must be the DIRECTORY
    /// (e.g. a <c>TempDirScope</c> path); <c>auth.json</c> is appended automatically.
    /// </summary>
    public MasterPasswordService(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        _directory = directory;
        _filePath = Path.Combine(_directory, "auth.json");
        try
        {
            Directory.CreateDirectory(_directory);
        }
        catch (Exception ex)
        {
            // Mirror AuditLogger's pattern — don't throw from the ctor. First
            // SetMasterPassword will surface the IO failure when it tries to write.
            Log.Error(ex, "MasterPasswordService could not pre-create directory {Directory}", _directory);
        }
    }

    /// <summary>
    /// Pure-algorithm helper. Exposed as <c>internal</c> so MasterPasswordServiceTests
    /// can drive the KDF without mutating <c>auth.json</c>. Not on <see cref="IMasterPasswordService"/>
    /// because production callers should always go through <see cref="SetMasterPassword"/> /
    /// <see cref="VerifyMasterPassword"/>.
    /// </summary>
    internal string HashNewPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Alg, KeyBytes);
        return $"{VersionPrefix}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    /// <summary>
    /// Pure-algorithm helper — verify <paramref name="password"/> against a raw
    /// envelope string. Used by both the production API and by unit tests that need
    /// to inject a known salt without round-tripping through disk.
    /// </summary>
    internal static bool Verify(string password, string stored)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (string.IsNullOrEmpty(stored)) return false;

        var parts = stored.Split('.');
        if (parts.Length != 3) return false;
        if (!string.Equals(parts[0], VersionPrefix, StringComparison.Ordinal)) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length != SaltBytes || expected.Length != KeyBytes) return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Alg, KeyBytes);

        // T-06-03: FixedTimeEquals — non-constant-time compare leaks the derived
        // key byte-by-byte through the timing side channel.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool IsMasterPasswordSet()
    {
        if (!File.Exists(_filePath)) return false;
        try
        {
            var auth = ReadAuthFile();
            return auth is not null && !string.IsNullOrEmpty(auth.PasswordHash);
        }
        catch (Exception ex)
        {
            // Corrupt file on disk is NOT "master password set" — the user should
            // be shown the first-run setup path. Log for diagnosis.
            Log.Warning(ex, "auth.json present but unreadable - treating as unset");
            return false;
        }
    }

    public void SetMasterPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var hash = HashNewPassword(password);
        var auth = new AuthFile(hash, SchemaVersion: CurrentSchemaVersion);
        WriteAuthFileAtomically(auth);
    }

    public bool VerifyMasterPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        try
        {
            var auth = ReadAuthFile();
            if (auth is null) return false;
            if (auth.SchemaVersion != CurrentSchemaVersion)
            {
                Log.Warning("auth.json has unknown SchemaVersion={Version} - verify rejected", auth.SchemaVersion);
                return false;
            }
            return Verify(password, auth.PasswordHash);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "VerifyMasterPassword failed to read auth.json");
            return false;
        }
    }

    private AuthFile? ReadAuthFile()
    {
        if (!File.Exists(_filePath)) return null;
        var json = File.ReadAllText(_filePath, Encoding.UTF8);
        return JsonSerializer.Deserialize(json, AuthJsonContext.Default.AuthFile);
    }

    private void WriteAuthFileAtomically(AuthFile auth)
    {
        var json = JsonSerializer.Serialize(auth, AuthJsonContext.Default.AuthFile);
        var bomless = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var tmp = _filePath + ".tmp";

        // Same atomic tmp-rename pattern as JsonConnectionStore / WindowStateService:
        // a kill-9 between WriteAllText and Move leaves the destination untouched.
        File.WriteAllText(tmp, json, bomless);
        File.Move(tmp, _filePath, overwrite: true);
    }
}

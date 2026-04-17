namespace Deskbridge.Core.Interfaces;

/// <summary>
/// Phase 6 Plan 06-04 (SEC-01, T-06-03): master-password gate for the Deskbridge UI.
/// Backed by PBKDF2-HMAC-SHA256 at 600,000 iterations with a 32-byte random salt,
/// persisted to <c>%AppData%/Deskbridge/auth.json</c>. Per D-16 there is NO recovery
/// path — a forgotten password resets by deleting auth.json.
/// </summary>
/// <remarks>
/// <para>D-17 clarifies that this gate protects Deskbridge UI state (connection
/// metadata, settings, palette) rather than individual credential secrets.
/// Connection passwords live in Windows Credential Manager and follow the Windows
/// user session's lifecycle.</para>
/// </remarks>
public interface IMasterPasswordService
{
    /// <summary>
    /// Returns <c>true</c> when <c>auth.json</c> exists AND contains a parseable
    /// hash. First-run code paths key off this to decide whether to show the
    /// lock overlay in setup mode vs unlock mode.
    /// </summary>
    bool IsMasterPasswordSet();

    /// <summary>
    /// Computes a fresh PBKDF2 hash for <paramref name="password"/> and writes
    /// <c>auth.json</c> atomically (tmp-rename). Overwrites any existing hash.
    /// Uses <c>"password"</c> as the default auth mode.
    /// </summary>
    void SetMasterPassword(string password);

    /// <summary>
    /// Computes a fresh PBKDF2 hash for <paramref name="password"/> and writes
    /// <c>auth.json</c> atomically (tmp-rename) with the specified <paramref name="authMode"/>.
    /// </summary>
    /// <param name="password">The plaintext password or PIN.</param>
    /// <param name="authMode"><c>"password"</c> or <c>"pin"</c>.</param>
    void SetMasterPassword(string password, string authMode);

    /// <summary>
    /// Returns the stored authMode (<c>"password"</c> or <c>"pin"</c>).
    /// Returns <c>"password"</c> if no auth.json exists or the field is absent
    /// (backward compatibility with pre-PIN installs).
    /// </summary>
    string GetAuthMode();

    /// <summary>
    /// Deletes <c>auth.json</c> entirely. Used when disabling master password.
    /// No-op if the file does not exist.
    /// </summary>
    void DeleteAuthFile();

    /// <summary>
    /// Loads <c>auth.json</c> and verifies <paramref name="password"/> against the
    /// stored hash with <see cref="System.Security.Cryptography.CryptographicOperations.FixedTimeEquals"/>.
    /// Returns <c>false</c> for any failure (missing file, malformed hash, wrong
    /// password) — callers do NOT get a reason to avoid leaking the failure mode.
    /// </summary>
    bool VerifyMasterPassword(string password);
}

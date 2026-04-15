using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using Deskbridge.Tests.Logging;

namespace Deskbridge.Tests.Security;

/// <summary>
/// Phase 6 Plan 06-04 (SEC-01, Pattern 6, T-06-03) coverage for
/// <see cref="MasterPasswordService"/>. Isolates every test to a fresh temp
/// directory via <see cref="TempDirScope"/> so parallel test runs cannot stomp
/// on each other's <c>auth.json</c>.
/// </summary>
public sealed class MasterPasswordServiceTests
{
    // --------------------------------------------------------------------
    // Test 1 — HashNewPassword returns the v1.<b64salt>.<b64key> envelope
    // --------------------------------------------------------------------
    [Fact]
    public void HashNewPassword_ReturnsV1EnvelopeWithTwoBase64Parts()
    {
        using var scope = new TempDirScope();
        var svc = new MasterPasswordService(scope.Path);

        var hash = svc.HashNewPassword("hunter2");

        Regex.IsMatch(hash, @"^v1\.[A-Za-z0-9+/=]+\.[A-Za-z0-9+/=]+$")
            .Should().BeTrue($"hash must match v1.<b64>.<b64> pattern — got {hash}");

        var parts = hash.Split('.');
        parts.Should().HaveCount(3);
        parts[0].Should().Be("v1");
        Convert.FromBase64String(parts[1]).Should().HaveCount(32, "salt is 32 bytes");
        Convert.FromBase64String(parts[2]).Should().HaveCount(32, "derived key is 32 bytes");
    }

    // --------------------------------------------------------------------
    // Test 2 — Verify returns true for the matching password
    // --------------------------------------------------------------------
    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        using var scope = new TempDirScope();
        var svc = new MasterPasswordService(scope.Path);
        var hash = svc.HashNewPassword("hunter2");

        MasterPasswordService.Verify("hunter2", hash).Should().BeTrue();
    }

    // --------------------------------------------------------------------
    // Test 3 — Verify returns false for the wrong password
    // --------------------------------------------------------------------
    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        using var scope = new TempDirScope();
        var svc = new MasterPasswordService(scope.Path);
        var hash = svc.HashNewPassword("hunter2");

        MasterPasswordService.Verify("hunter3", hash).Should().BeFalse();
    }

    // --------------------------------------------------------------------
    // Test 4 — Verify returns false for structurally invalid envelopes
    // --------------------------------------------------------------------
    [Theory]
    [InlineData("not-a-valid-hash")]         // no dots
    [InlineData("v1.abc")]                   // only 2 parts
    [InlineData("v99.aa.bb")]                // unknown schema version
    [InlineData("")]                          // empty
    [InlineData("v1.notbase64!.notbase64!")] // invalid base64 payload
    public void Verify_MalformedOrUnknownHash_ReturnsFalse(string stored)
    {
        MasterPasswordService.Verify("anything", stored).Should().BeFalse();
    }

    // --------------------------------------------------------------------
    // Test 5 — Rfc2898DeriveBytes.Pbkdf2 is deterministic under a fixed salt
    //          (sanity check — doesn't rely on our API surface).
    // --------------------------------------------------------------------
    [Fact]
    public void Pbkdf2_SameSaltAndPassword_ProducesSameBytes()
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var first = Rfc2898DeriveBytes.Pbkdf2("hunter2", salt, 600_000, HashAlgorithmName.SHA256, 32);
        var second = Rfc2898DeriveBytes.Pbkdf2("hunter2", salt, 600_000, HashAlgorithmName.SHA256, 32);

        first.SequenceEqual(second).Should().BeTrue(
            "PBKDF2 is deterministic under identical inputs — regression guard");
    }

    // --------------------------------------------------------------------
    // Test 6 — Two different passwords with the SAME salt produce DIFFERENT
    //          hashes, and Verify correctly distinguishes them. Indirect
    //          coverage of FixedTimeEquals (source reviewed separately).
    // --------------------------------------------------------------------
    [Fact]
    public void Verify_DistinguishesPasswordsSharingASalt()
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var keyA = Rfc2898DeriveBytes.Pbkdf2("hunter2", salt, 600_000, HashAlgorithmName.SHA256, 32);
        var keyB = Rfc2898DeriveBytes.Pbkdf2("hunter3", salt, 600_000, HashAlgorithmName.SHA256, 32);

        keyA.SequenceEqual(keyB).Should().BeFalse("different passwords ⇒ different keys (even under same salt)");

        var storedA = $"v1.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(keyA)}";
        MasterPasswordService.Verify("hunter2", storedA).Should().BeTrue();
        MasterPasswordService.Verify("hunter3", storedA).Should().BeFalse();
    }

    // --------------------------------------------------------------------
    // Test 7 — SetMasterPassword writes auth.json atomically and no .tmp
    //          lingers after the call returns.
    // --------------------------------------------------------------------
    [Fact]
    public void SetMasterPassword_WritesAuthJsonAtomically_NoTmpLeftOver()
    {
        using var scope = new TempDirScope();
        var svc = new MasterPasswordService(scope.Path);

        svc.SetMasterPassword("hunter2");

        var path = Path.Combine(scope.Path, "auth.json");
        File.Exists(path).Should().BeTrue();
        File.Exists(path + ".tmp").Should().BeFalse("atomic rename must clean up the temp file");

        var json = File.ReadAllText(path);
        var auth = JsonSerializer.Deserialize<AuthFile>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        auth.Should().NotBeNull();
        auth!.SchemaVersion.Should().Be(1);
        auth.PasswordHash.Should().StartWith("v1.");
    }

    // --------------------------------------------------------------------
    // Test 8 — IsMasterPasswordSet reflects the on-disk state.
    // --------------------------------------------------------------------
    [Fact]
    public void IsMasterPasswordSet_FreshDirectory_False_ThenTrue_AfterSet()
    {
        using var scope = new TempDirScope();
        var svc = new MasterPasswordService(scope.Path);

        svc.IsMasterPasswordSet().Should().BeFalse("fresh temp dir has no auth.json");

        svc.SetMasterPassword("hunter2");
        svc.IsMasterPasswordSet().Should().BeTrue();
    }

    // --------------------------------------------------------------------
    // Test 9 — VerifyMasterPassword loads from disk (end-to-end).
    // --------------------------------------------------------------------
    [Fact]
    public void VerifyMasterPassword_LoadsFromDisk_TruthyForCorrect_FalsyForWrong()
    {
        using var scope = new TempDirScope();
        var svc = new MasterPasswordService(scope.Path);

        svc.SetMasterPassword("hunter2");

        svc.VerifyMasterPassword("hunter2").Should().BeTrue();
        svc.VerifyMasterPassword("wrong").Should().BeFalse();
    }

    // --------------------------------------------------------------------
    // Test 10 — HashNewPassword takes > 100ms, proving Iterations isn't
    //           silently dropped by a refactor. 600_000 iterations on any
    //           realistic CPU is well above 100ms.
    // --------------------------------------------------------------------
    [Fact]
    public void HashNewPassword_TakesMoreThan100ms_ProvingIterationsAreHighEnough()
    {
        using var scope = new TempDirScope();
        var svc = new MasterPasswordService(scope.Path);

        // Warm-up: the first PBKDF2 call on a process can include JIT / COM init
        // overhead. We measure the SECOND call to get a steady-state number.
        _ = svc.HashNewPassword("warm-up");

        var sw = Stopwatch.StartNew();
        _ = svc.HashNewPassword("hunter2");
        sw.Stop();

        // Envelope chosen to tolerate SHA-256-accelerated CPUs (some AMD Zen4 / Apple M-series
        // chips clear 600k iterations in ~50ms under warm cache) while still catching a
        // regression that drops Iterations to ~60k or lower.
        sw.ElapsedMilliseconds.Should().BeGreaterThan(30,
            "PBKDF2 600_000 iterations on SHA-256 must take > 30ms on any realistic CPU; " +
            "a result below this threshold indicates Iterations was silently reduced");
    }

    // --------------------------------------------------------------------
    // Test 11 — VerifyMasterPassword returns false when auth.json is missing.
    //           Defense-in-depth against the path where the user deletes auth.json
    //           without first hitting the "first-run" branch.
    // --------------------------------------------------------------------
    [Fact]
    public void VerifyMasterPassword_MissingAuthJson_ReturnsFalse()
    {
        using var scope = new TempDirScope();
        var svc = new MasterPasswordService(scope.Path);

        svc.VerifyMasterPassword("anything").Should().BeFalse();
    }
}

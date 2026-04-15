using Deskbridge.Core.Logging;
using Deskbridge.Tests.Security;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Deskbridge.Tests.Logging;

/// <summary>
/// LOG-05 / D-12 coverage for <see cref="RedactSensitivePolicy"/>. Test 8 is the
/// canonical end-to-end canary — a full Serilog pipeline with the policy installed
/// MUST NOT emit any sink event whose rendered text contains a denylisted value.
/// </summary>
public sealed class RedactSensitivePolicyTests
{
    // Tiny stand-in for ILogEventPropertyValueFactory so the unit tests can exercise
    // TryDestructure without spinning up a full LoggerConfiguration. Mirrors what
    // Serilog's default factory does for our purposes (returns ScalarValue or recurses).
    private sealed class StubFactory : ILogEventPropertyValueFactory
    {
        private readonly RedactSensitivePolicy _policy;

        public StubFactory(RedactSensitivePolicy policy) => _policy = policy;

        public LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects)
        {
            if (value is null) return new ScalarValue(null);
            if (destructureObjects && _policy.TryDestructure(value, this, out var redacted))
                return redacted;
            return new ScalarValue(value);
        }
    }

    private static StructureValue Destructure(object value)
    {
        var policy = new RedactSensitivePolicy();
        var factory = new StubFactory(policy);
        var ok = policy.TryDestructure(value, factory, out var result);
        ok.Should().BeTrue("the test object MUST contain at least one denylisted property");
        return (StructureValue)result!;
    }

    private static string? FindScalar(StructureValue sv, string name) =>
        sv.Properties.FirstOrDefault(p => p.Name == name)?.Value is ScalarValue sc
            ? sc.Value?.ToString()
            : null;

    // ------------------------------------------------------------------
    // Test 1 — denylist scalar redaction (Password + non-sensitive Name)
    // ------------------------------------------------------------------
    private sealed record NamePasswordPoco(string Name, string Password);

    [Fact]
    public void TryDestructure_RedactsPassword_LeavesNameIntact()
    {
        var sv = Destructure(new NamePasswordPoco("srv01", "hunter2"));
        FindScalar(sv, "Password").Should().Be(RedactSensitivePolicy.RedactedSentinel);
        FindScalar(sv, "Name").Should().Be("srv01");
    }

    // ------------------------------------------------------------------
    // Test 2 — every denylist name is redacted
    // ------------------------------------------------------------------
    [Theory]
    [InlineData("Password")]
    [InlineData("Secret")]
    [InlineData("Token")]
    [InlineData("CredentialData")]
    [InlineData("ApiKey")]
    [InlineData("ResolvedPassword")]
    [InlineData("MasterPassword")]
    public void TryDestructure_RedactsEveryDenylistedName(string propertyName)
    {
        // Build a runtime POCO with the named property by using a Dictionary-backed
        // anonymous type via reflection-emit isn't necessary — we can use one POCO
        // per name, but anonymous types let us hit the "any name in the denylist"
        // contract more compactly. Each named anonymous type instance has exactly
        // one property whose name is `propertyName`.
        object poco = propertyName switch
        {
            "Password" => new { Password = "hunter2" },
            "Secret" => new { Secret = "hunter2" },
            "Token" => new { Token = "hunter2" },
            "CredentialData" => new { CredentialData = "hunter2" },
            "ApiKey" => new { ApiKey = "hunter2" },
            "ResolvedPassword" => new { ResolvedPassword = "hunter2" },
            "MasterPassword" => new { MasterPassword = "hunter2" },
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName)),
        };
        var sv = Destructure(poco);
        FindScalar(sv, propertyName).Should().Be(RedactSensitivePolicy.RedactedSentinel);
    }

    // ------------------------------------------------------------------
    // Test 3 — case-insensitive match
    // ------------------------------------------------------------------
    private sealed record LowerPasswordPoco(string password);
    private sealed record UpperPasswordPoco(string PASSWORD);
    private sealed record MixedPasswordPoco(string PaSsWoRd);

    [Fact]
    public void TryDestructure_LowerCasePassword_StillRedacts()
    {
        var sv = Destructure(new LowerPasswordPoco("hunter2"));
        FindScalar(sv, "password").Should().Be(RedactSensitivePolicy.RedactedSentinel);
    }

    [Fact]
    public void TryDestructure_UpperCasePassword_StillRedacts()
    {
        var sv = Destructure(new UpperPasswordPoco("hunter2"));
        FindScalar(sv, "PASSWORD").Should().Be(RedactSensitivePolicy.RedactedSentinel);
    }

    [Fact]
    public void TryDestructure_MixedCasePassword_StillRedacts()
    {
        var sv = Destructure(new MixedPasswordPoco("hunter2"));
        FindScalar(sv, "PaSsWoRd").Should().Be(RedactSensitivePolicy.RedactedSentinel);
    }

    // ------------------------------------------------------------------
    // Test 4 — no denylisted property → returns false (default policy runs)
    // ------------------------------------------------------------------
    private sealed record CleanPoco(string Name, int Port);

    [Fact]
    public void TryDestructure_NoDenylistedProperty_ReturnsFalse()
    {
        var policy = new RedactSensitivePolicy();
        var factory = new StubFactory(policy);
        var ok = policy.TryDestructure(
            new CleanPoco("srv01", 3389), factory, out var result);
        ok.Should().BeFalse("returning false defers to Serilog's default destructurer (Pitfall 9)");
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // Test 5 — nested objects: inner Password also redacted
    // ------------------------------------------------------------------
    private sealed record OuterPlain(string OuterName, NamePasswordPoco Inner);

    [Fact]
    public void TryDestructure_NestedPocoWithPassword_RedactsRecursively()
    {
        var outer = new OuterPlain("topserver", new NamePasswordPoco("inner-srv", "hunter2"));

        // Outer has no denylisted top-level property — Serilog's default would handle it.
        // The "Inner" value WILL be routed back through factory.CreatePropertyValue, which
        // delegates to RedactSensitivePolicy when the inner type has a denylisted property.
        var policy = new RedactSensitivePolicy();
        var factory = new StubFactory(policy);

        // Top-level outer: no denylisted props → policy returns false.
        policy.TryDestructure(outer, factory, out _).Should().BeFalse();

        // Inner: routed via the factory the same way Serilog's default destructurer would.
        var innerPv = factory.CreatePropertyValue(outer.Inner, destructureObjects: true);
        innerPv.Should().BeOfType<StructureValue>();
        var innerSv = (StructureValue)innerPv;
        FindScalar(innerSv, "Password").Should().Be(RedactSensitivePolicy.RedactedSentinel);
        FindScalar(innerSv, "Name").Should().Be("inner-srv");
    }

    // ------------------------------------------------------------------
    // Test 6 — null value early-out
    // ------------------------------------------------------------------
    [Fact]
    public void TryDestructure_NullValue_ReturnsFalse()
    {
        var policy = new RedactSensitivePolicy();
        var factory = new StubFactory(policy);
        var ok = policy.TryDestructure(null!, factory, out var result);
        ok.Should().BeFalse();
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // Test 7 — primitive / string / enum early-out
    // ------------------------------------------------------------------
    [Fact]
    public void TryDestructure_Primitive_ReturnsFalse()
    {
        var policy = new RedactSensitivePolicy();
        var factory = new StubFactory(policy);
        policy.TryDestructure(42, factory, out _).Should().BeFalse();
    }

    [Fact]
    public void TryDestructure_String_ReturnsFalse()
    {
        var policy = new RedactSensitivePolicy();
        var factory = new StubFactory(policy);
        policy.TryDestructure("hunter2", factory, out _).Should().BeFalse(
            "redaction is property-name based; raw string args are NOT in scope");
    }

    [Fact]
    public void TryDestructure_Enum_ReturnsFalse()
    {
        var policy = new RedactSensitivePolicy();
        var factory = new StubFactory(policy);
        policy.TryDestructure(StringComparison.OrdinalIgnoreCase, factory, out _)
            .Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // Test 8 — full-run log scrape (LOG-05 end-to-end canary)
    // ------------------------------------------------------------------
    [Fact]
    public void FullRunLogScrape_PasswordNeverLeaksToSink()
    {
        var sink = new InMemorySink();
        var logger = new LoggerConfiguration()
            .Destructure.With<RedactSensitivePolicy>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        try
        {
            // Simulate a Phase 4 ConnectionContext-like object whose ResolvedPassword
            // is set transiently by the pipeline. Anonymous type matches the surface
            // shape we care about.
            var ctxLike = new { Username = "admin", ResolvedPassword = "hunter2" };
            logger.Information("Attempt {@Conn}", ctxLike);

            sink.Events.Should().HaveCount(1);
            var rendered = sink.Events.Single().RenderMessage();
            rendered.Should().NotContain("hunter2",
                "LOG-05 hard rule: no credential value in any sink output");
            rendered.Should().Contain(RedactSensitivePolicy.RedactedSentinel);
        }
        finally
        {
            (logger as IDisposable).Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Test 9 — getter that throws does not propagate
    // ------------------------------------------------------------------
    private sealed class ThrowingGetterPoco
    {
        public string SafeName => "srv01";
        public string Password => "hunter2"; // denylisted, redacted before getter ever runs
        public string ExplodingProperty => throw new InvalidOperationException("nope");
    }

    [Fact]
    public void TryDestructure_GetterThatThrows_DoesNotPropagate()
    {
        var sv = Destructure(new ThrowingGetterPoco());
        FindScalar(sv, "Password").Should().Be(RedactSensitivePolicy.RedactedSentinel);
        FindScalar(sv, "SafeName").Should().Be("srv01");
        // ExplodingProperty was caught — its scalar value should be null (the
        // policy substitutes null when the getter throws, per the try/catch
        // on p.GetValue(value)).
        var explodingProp = sv.Properties.SingleOrDefault(p => p.Name == "ExplodingProperty");
        explodingProp.Should().NotBeNull();
        ((ScalarValue)explodingProp!.Value).Value.Should().BeNull();
    }
}

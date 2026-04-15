using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace Deskbridge.Core.Logging;

/// <summary>
/// LOG-05 / D-12: denylist-based property-name redaction applied to every type Serilog
/// destructures. Any property whose NAME matches the denylist is replaced with the
/// sentinel <c>"***REDACTED***"</c> before the value reaches any sink. Non-denylisted
/// properties pass through the default destructuring pipeline via
/// <see cref="ILogEventPropertyValueFactory.CreatePropertyValue"/>.
/// </summary>
/// <remarks>
/// <para>
/// Chosen over <c>Destructure.ByTransforming&lt;T&gt;</c> because this protects every
/// type — including types added in future phases — without requiring explicit per-type
/// registration. A future developer cannot accidentally leak a credential by logging a
/// new POCO containing a "Password" field.
/// </para>
/// <para>
/// Pitfall 9 (recursive ByTransforming → stack overflow) avoided: returning <c>false</c>
/// when no denylisted property matched lets Serilog's default policy run, and we never
/// return a value of the input type — only a freshly-built <see cref="StructureValue"/>.
/// </para>
/// </remarks>
public sealed class RedactSensitivePolicy : IDestructuringPolicy
{
    /// <summary>
    /// Property names that MUST be redacted whenever Serilog destructures an object.
    /// Case-insensitive (PascalCase / camelCase / SCREAM_CASE all match). Sourced from
    /// CONTEXT D-12 plus the transient pipeline properties added in Phase 4.
    /// </summary>
    internal static readonly HashSet<string> Denylist = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password",
        "Secret",
        "Token",
        "CredentialData",
        "ApiKey",
        "ResolvedPassword",
        "MasterPassword",
    };

    /// <summary>The sentinel value substituted for any denylisted property.</summary>
    public const string RedactedSentinel = "***REDACTED***";

    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        result = null;
        if (value is null) return false;

        var type = value.GetType();

        // Fast-path opt-out for primitives, strings, and enums — these never have
        // properties we'd want to redact, and intervening would defeat Serilog's
        // built-in scalar handling.
        if (type.IsPrimitive || type == typeof(string) || type.IsEnum) return false;

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                        .ToArray();

        var values = new List<LogEventProperty>(props.Length);
        var touched = false;
        foreach (var p in props)
        {
            if (Denylist.Contains(p.Name))
            {
                values.Add(new LogEventProperty(p.Name, new ScalarValue(RedactedSentinel)));
                touched = true;
            }
            else
            {
                object? raw;
                try { raw = p.GetValue(value); }
                catch { raw = null; }
                values.Add(new LogEventProperty(
                    p.Name,
                    propertyValueFactory.CreatePropertyValue(raw, destructureObjects: true)));
            }
        }

        // If NOTHING was redacted, defer to Serilog's default destructuring pipeline.
        // This avoids ever returning a StructureValue for types that don't need our
        // intervention (and avoids Pitfall 9 same-type recursion entirely).
        if (!touched) return false;

        result = new StructureValue(values);
        return true;
    }
}

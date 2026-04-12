using Xunit;

namespace Deskbridge.Tests.Fixtures;

/// <summary>
/// Lightweight shim that mirrors the classic <c>Xunit.SkippableFact</c>
/// <c>Skip.If / Skip.IfNot</c> API against xUnit v3's native <see cref="Assert.Skip(string)"/>.
///
/// Why this exists: <c>Xunit.SkippableFact 1.5.23</c> targets xUnit v2 and collides with the
/// xUnit v3 types already in this project (CS0433 on <c>FactAttribute</c>). The plan
/// (04-01-PLAN.md Task 0.1 step 1) explicitly permits "implement inline via <c>Assert.Skip</c>
/// — planner to pick". This shim is that pick. Tests decorated with <c>[Fact]</c> call
/// <c>Skip.If(...)</c> at the top of the body; xUnit v3 reports the test as Skipped instead
/// of Passed/Failed.
/// </summary>
public static class Skip
{
    /// <summary>Skips the current test if <paramref name="condition"/> is true.</summary>
    public static void If(bool condition, string reason)
    {
        if (condition)
        {
            Assert.Skip(reason);
        }
    }

    /// <summary>Skips the current test if <paramref name="condition"/> is false.</summary>
    public static void IfNot(bool condition, string reason)
    {
        if (!condition)
        {
            Assert.Skip(reason);
        }
    }
}

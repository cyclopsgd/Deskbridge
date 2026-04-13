using Deskbridge.Core.Services;

namespace Deskbridge.Tests.Rdp;

/// <summary>
/// Table-driven tests for the full <see cref="DisconnectReasonClassifier"/> (Plan 04-03
/// Task 1.1 — expands the Plan 04-02 stub). Verifies every documented disconnect-reason
/// code maps to the correct <see cref="DisconnectCategory"/> and that <c>ShouldAutoRetry</c>
/// honours the D-06 skip list (Authentication + Licensing + UserInitiated go to manual
/// overlay; everything else auto-retries).
///
/// <para>Source for codes: RDP-ACTIVEX-PITFALLS §7 and 04-CONTEXT §D-06.</para>
/// </summary>
public sealed class DisconnectReasonClassifierTests
{
    // --- Classify: table-driven over the 19 documented codes ---

    [Theory]
    [InlineData(1, DisconnectCategory.UserInitiated)]
    [InlineData(2, DisconnectCategory.UserInitiated)]
    [InlineData(3, DisconnectCategory.ServerInitiated)]
    [InlineData(264, DisconnectCategory.NetworkLost)]
    [InlineData(516, DisconnectCategory.NetworkLost)]
    [InlineData(772, DisconnectCategory.NetworkLost)]
    [InlineData(1028, DisconnectCategory.NetworkLost)]
    [InlineData(2308, DisconnectCategory.NetworkLost)]
    [InlineData(260, DisconnectCategory.DnsFailure)]
    [InlineData(520, DisconnectCategory.DnsFailure)]
    [InlineData(2055, DisconnectCategory.Authentication)]
    [InlineData(2567, DisconnectCategory.Authentication)]
    [InlineData(2823, DisconnectCategory.Authentication)]
    [InlineData(3335, DisconnectCategory.Authentication)]
    [InlineData(3591, DisconnectCategory.Authentication)]
    [InlineData(3847, DisconnectCategory.Authentication)]
    [InlineData(2056, DisconnectCategory.Licensing)]
    [InlineData(2312, DisconnectCategory.Licensing)]
    [InlineData(3334, DisconnectCategory.Protocol)]
    [InlineData(999, DisconnectCategory.Unknown)]
    public void ClassifyMapsCodeToCategory(int discReason, DisconnectCategory expected)
    {
        DisconnectReasonClassifier.Classify(discReason).Should().Be(expected);
    }

    // --- ShouldAutoRetry: positive + negative list ---

    [Fact]
    public void ShouldAutoRetry_Retries_For_ServerInitiated_NetworkLost_DnsFailure_Protocol_Unknown()
    {
        DisconnectReasonClassifier.ShouldAutoRetry(DisconnectCategory.ServerInitiated).Should().BeTrue();
        DisconnectReasonClassifier.ShouldAutoRetry(DisconnectCategory.NetworkLost).Should().BeTrue();
        DisconnectReasonClassifier.ShouldAutoRetry(DisconnectCategory.DnsFailure).Should().BeTrue();
        DisconnectReasonClassifier.ShouldAutoRetry(DisconnectCategory.Protocol).Should().BeTrue();
        DisconnectReasonClassifier.ShouldAutoRetry(DisconnectCategory.Unknown).Should().BeTrue();
    }

    [Fact]
    public void ShouldAutoRetry_SkipsRetry_For_UserInitiated_Authentication_Licensing()
    {
        DisconnectReasonClassifier.ShouldAutoRetry(DisconnectCategory.UserInitiated).Should().BeFalse();
        DisconnectReasonClassifier.ShouldAutoRetry(DisconnectCategory.Authentication).Should().BeFalse();
        DisconnectReasonClassifier.ShouldAutoRetry(DisconnectCategory.Licensing).Should().BeFalse();
    }

    // --- Describe: signature retained from Plan 04-02 stub ---

    [Fact]
    public void Describe_WithGetErrorDescriptionFunc_ReturnsFuncResult_IfNoThrow()
    {
        Func<uint, uint, string> getter = (d, e) => $"rdp says: {d}/{e}";
        DisconnectReasonClassifier.Describe(264, 0, getter).Should().Be("rdp says: 264/0");
    }

    [Fact]
    public void Describe_WhenFuncThrows_ReturnsFallbackString_ContainingDiscReason()
    {
        Func<uint, uint, string> thrower = (_, _) => throw new InvalidOperationException("boom");
        var result = DisconnectReasonClassifier.Describe(1800, 5, thrower);
        result.Should().Contain("1800").And.Contain("5");
    }

    [Fact]
    public void Describe_WhenFuncIsNull_ReturnsFallbackString()
    {
        DisconnectReasonClassifier.Describe(2055, 0, null)
            .Should().Contain("2055");
    }
}

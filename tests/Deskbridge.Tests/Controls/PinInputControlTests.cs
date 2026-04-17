using Deskbridge.Controls;

namespace Deskbridge.Tests.Controls;

/// <summary>
/// Pure logic tests for <see cref="PinInputControl"/> static helper methods.
/// No STA thread or UI required -- these test digit filtering, paste distribution,
/// and pin assembly logic only.
/// </summary>
public class PinInputControlTests
{
    // ---- IsDigit ----

    [Theory]
    [InlineData('0', true)]
    [InlineData('1', true)]
    [InlineData('5', true)]
    [InlineData('9', true)]
    [InlineData('a', false)]
    [InlineData('Z', false)]
    [InlineData(' ', false)]
    [InlineData('!', false)]
    [InlineData('.', false)]
    [InlineData('\t', false)]
    public void IsDigit_ClassifiesCorrectly(char c, bool expected)
    {
        PinInputControl.IsDigit(c).Should().Be(expected);
    }

    // ---- DistributeDigits ----

    [Fact]
    public void DistributeDigits_FullSixDigitString_ReturnsSixElements()
    {
        var result = PinInputControl.DistributeDigits("123456");

        result.Should().HaveCount(6);
        result.Should().Equal("1", "2", "3", "4", "5", "6");
    }

    [Fact]
    public void DistributeDigits_PartialInput_PadsWithEmpty()
    {
        var result = PinInputControl.DistributeDigits("12");

        result.Should().HaveCount(6);
        result.Should().Equal("1", "2", "", "", "", "");
    }

    [Fact]
    public void DistributeDigits_NonDigitPaste_ReturnsNull()
    {
        var result = PinInputControl.DistributeDigits("abc123");

        result.Should().BeNull();
    }

    [Fact]
    public void DistributeDigits_MoreThanSixDigits_TruncatesToSix()
    {
        var result = PinInputControl.DistributeDigits("1234567890");

        result.Should().HaveCount(6);
        result.Should().Equal("1", "2", "3", "4", "5", "6");
    }

    [Fact]
    public void DistributeDigits_EmptyString_ReturnsAllEmpty()
    {
        var result = PinInputControl.DistributeDigits("");

        result.Should().HaveCount(6);
        result.Should().Equal("", "", "", "", "", "");
    }

    [Fact]
    public void DistributeDigits_MixedDigitsAndLetters_ReturnsNull()
    {
        var result = PinInputControl.DistributeDigits("12ab56");

        result.Should().BeNull();
    }

    // ---- AssemblePin ----

    [Fact]
    public void AssemblePin_AllFilled_ReturnsConcatenated()
    {
        var cells = new[] { "1", "2", "3", "4", "5", "6" };

        PinInputControl.AssemblePin(cells).Should().Be("123456");
    }

    [Fact]
    public void AssemblePin_PartiallyFilled_ReturnsPartial()
    {
        var cells = new[] { "1", "2", "", "", "", "" };

        PinInputControl.AssemblePin(cells).Should().Be("12");
    }

    [Fact]
    public void AssemblePin_AllEmpty_ReturnsEmpty()
    {
        var cells = new[] { "", "", "", "", "", "" };

        PinInputControl.AssemblePin(cells).Should().Be("");
    }
}

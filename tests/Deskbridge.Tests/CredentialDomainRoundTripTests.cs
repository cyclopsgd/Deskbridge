using System.Net;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests;

public sealed class CredentialDomainRoundTripTests
{
    [Fact]
    public void NormalizeCredential_NullInput_ReturnsNull()
    {
        var result = WindowsCredentialService.NormalizeCredential(null);
        result.Should().BeNull();
    }

    [Fact]
    public void NormalizeCredential_DotBackslashUsername_SplitsDomainAndUsername()
    {
        // Windows CredMan stores ".\cyclopsgd" in UserName when domain is "."
        var cred = new NetworkCredential(@".\cyclopsgd", "secret");
        var result = WindowsCredentialService.NormalizeCredential(cred);

        result.Should().NotBeNull();
        result!.Domain.Should().Be(".");
        result.UserName.Should().Be("cyclopsgd");
        result.Password.Should().Be("secret");
    }

    [Fact]
    public void NormalizeCredential_CorpBackslashAdmin_SplitsDomainAndUsername()
    {
        var cred = new NetworkCredential(@"CORP\admin", "pass123");
        var result = WindowsCredentialService.NormalizeCredential(cred);

        result.Should().NotBeNull();
        result!.Domain.Should().Be("CORP");
        result.UserName.Should().Be("admin");
        result.Password.Should().Be("pass123");
    }

    [Fact]
    public void NormalizeCredential_NoBackslash_LeavesAsIs()
    {
        var cred = new NetworkCredential("admin", "pass");
        var result = WindowsCredentialService.NormalizeCredential(cred);

        result.Should().NotBeNull();
        result!.Domain.Should().BeEmpty();
        result.UserName.Should().Be("admin");
    }

    [Fact]
    public void NormalizeCredential_EmptyUsername_ReturnsEmptyFields()
    {
        var cred = new NetworkCredential(string.Empty, "pass");
        var result = WindowsCredentialService.NormalizeCredential(cred);

        result.Should().NotBeNull();
        result!.Domain.Should().BeEmpty();
        result.UserName.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeCredential_UpnFormat_LeavesAsIs()
    {
        // UPN format (user@domain.com) has no backslash -- should not be split
        var cred = new NetworkCredential("user@domain.com", "pass");
        var result = WindowsCredentialService.NormalizeCredential(cred);

        result.Should().NotBeNull();
        result!.Domain.Should().BeEmpty();
        result.UserName.Should().Be("user@domain.com");
    }

    [Fact]
    public void NormalizeCredential_MultipleBackslashes_SplitsOnFirstOnly()
    {
        // Only split on the FIRST backslash
        var cred = new NetworkCredential(@"DOMAIN\sub\user", "pass");
        var result = WindowsCredentialService.NormalizeCredential(cred);

        result.Should().NotBeNull();
        result!.Domain.Should().Be("DOMAIN");
        result.UserName.Should().Be(@"sub\user");
    }

    [Fact]
    public void NormalizeCredential_DomainAlreadySet_LeavesAsIs()
    {
        // If Domain is already populated, CredMan split it correctly -- don't re-split
        var cred = new NetworkCredential("admin", "pass", "CORP");
        var result = WindowsCredentialService.NormalizeCredential(cred);

        result.Should().NotBeNull();
        result!.Domain.Should().Be("CORP");
        result.UserName.Should().Be("admin");
    }

    [Fact]
    public void NormalizeCredential_DomainAlreadySet_WithBackslashInUsername_LeavesAsIs()
    {
        // Edge case: domain already set AND username has backslash -- trust the domain
        var cred = new NetworkCredential(@"sub\user", "pass", "CORP");
        var result = WindowsCredentialService.NormalizeCredential(cred);

        result.Should().NotBeNull();
        result!.Domain.Should().Be("CORP");
        result.UserName.Should().Be(@"sub\user");
    }

    [Fact]
    public void NormalizeCredential_PreservesPassword()
    {
        var cred = new NetworkCredential(@"CORP\admin", "my-secret-password");
        var result = WindowsCredentialService.NormalizeCredential(cred);

        result.Should().NotBeNull();
        result!.Password.Should().Be("my-secret-password");
    }

    [Theory]
    [InlineData(@".\cyclopsgd", "", ".", "cyclopsgd")]
    [InlineData(@"CORP\admin", "", "CORP", "admin")]
    [InlineData("admin", "", "", "admin")]
    [InlineData(@"DOMAIN\sub\user", "", "DOMAIN", @"sub\user")]
    [InlineData("admin", "CORP", "CORP", "admin")]
    [InlineData(@"user@domain.com", "", "", "user@domain.com")]
    public void NormalizeCredential_TheoryTable(
        string inputUsername, string inputDomain,
        string expectedDomain, string expectedUsername)
    {
        var cred = new NetworkCredential(inputUsername, "pass", inputDomain);
        var result = WindowsCredentialService.NormalizeCredential(cred);

        result.Should().NotBeNull();
        result!.Domain.Should().Be(expectedDomain);
        result.UserName.Should().Be(expectedUsername);
    }
}

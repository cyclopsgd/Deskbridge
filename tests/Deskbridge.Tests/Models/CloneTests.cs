using Deskbridge.Core.Models;

namespace Deskbridge.Tests.Models;

/// <summary>
/// Pins <see cref="ConnectionModel.Clone"/> as a true deep copy of the mutable reference members
/// (audit W1): transactional bulk edit applies edits to clones, so a clone MUST share no mutable
/// state with the original — otherwise mutating one would leak into the store's live backing object.
/// </summary>
public sealed class CloneTests
{
    [Fact]
    public void Clone_MutatingOriginalTags_DoesNotAffectClone()
    {
        var original = new ConnectionModel
        {
            Name = "Server",
            Tags = ["prod", "eu-west"],
        };

        var clone = original.Clone();

        // Mutate the ORIGINAL's Tags list AFTER cloning.
        original.Tags.Add("added-after-clone");
        original.Tags[0] = "mutated";

        clone.Tags.Should().Equal("prod", "eu-west");
        clone.Tags.Should().NotBeSameAs(original.Tags);
    }

    [Fact]
    public void Clone_MutatingOriginalDisplaySettings_DoesNotAffectClone()
    {
        var original = new ConnectionModel
        {
            Name = "Server",
            DisplaySettings = new DisplaySettings { Width = 1920, Height = 1080, SmartSizing = true },
        };

        var clone = original.Clone();

        // Mutate the ORIGINAL's DisplaySettings AFTER cloning.
        original.DisplaySettings!.Width = 800;
        original.DisplaySettings.Height = 600;
        original.DisplaySettings.SmartSizing = false;

        clone.DisplaySettings.Should().NotBeNull();
        clone.DisplaySettings.Should().NotBeSameAs(original.DisplaySettings);
        clone.DisplaySettings!.Width.Should().Be(1920);
        clone.DisplaySettings.Height.Should().Be(1080);
        clone.DisplaySettings.SmartSizing.Should().BeTrue();
    }

    [Fact]
    public void Clone_NullDisplaySettings_ClonesAsNull()
    {
        var original = new ConnectionModel { Name = "Server", DisplaySettings = null };

        var clone = original.Clone();

        clone.DisplaySettings.Should().BeNull();
    }

    [Fact]
    public void Clone_CopiesScalarAndStringProperties()
    {
        var original = new ConnectionModel
        {
            Name = "Server",
            Hostname = "alpha.local",
            Port = 3390,
            Username = "admin",
            Domain = "CORP",
            CredentialMode = CredentialMode.Own,
        };

        var clone = original.Clone();

        clone.Id.Should().Be(original.Id);
        clone.Name.Should().Be("Server");
        clone.Hostname.Should().Be("alpha.local");
        clone.Port.Should().Be(3390);
        clone.Username.Should().Be("admin");
        clone.Domain.Should().Be("CORP");
        clone.CredentialMode.Should().Be(CredentialMode.Own);
    }
}

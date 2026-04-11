using Deskbridge.ViewModels;

namespace Deskbridge.Tests.ViewModels;

public class TabItemViewModelTests
{
    [Fact]
    public void Title_DefaultsToEmptyString()
    {
        var sut = new TabItemViewModel();
        sut.Title.Should().Be(string.Empty);
    }

    [Fact]
    public void Title_IsObservable()
    {
        var sut = new TabItemViewModel();
        var changed = false;
        sut.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TabItemViewModel.Title))
                changed = true;
        };

        sut.Title = "New Title";

        changed.Should().BeTrue();
        sut.Title.Should().Be("New Title");
    }

    [Fact]
    public void IsActive_DefaultsToFalse()
    {
        var sut = new TabItemViewModel();
        sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_IsObservable()
    {
        var sut = new TabItemViewModel();
        var changed = false;
        sut.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TabItemViewModel.IsActive))
                changed = true;
        };

        sut.IsActive = true;

        changed.Should().BeTrue();
        sut.IsActive.Should().BeTrue();
    }

    [Fact]
    public void ConnectionId_CanBeInitialized()
    {
        var id = Guid.NewGuid();
        var sut = new TabItemViewModel { ConnectionId = id };

        sut.ConnectionId.Should().Be(id);
    }

    [Fact]
    public void ConnectionId_DefaultsToEmptyGuid()
    {
        var sut = new TabItemViewModel();
        sut.ConnectionId.Should().Be(Guid.Empty);
    }
}

using Deskbridge.Tests.Fixtures;

namespace Deskbridge.Tests.Tabs;

/// <summary>
/// Wave 0 skip-scaffolds for TabHostManager. Each method declares the behavior under
/// test in a [Fact] so the Validation map can see it exists; the real assertions land
/// in Plan 05-01 Task 2 when ITabHostManager + TabHostManager are implemented.
/// Runs on STA because TabHostManager.HostMounted propagation asserts dispatcher-thread
/// marshalling (mirrors ConnectionCoordinatorTests).
/// </summary>
[Collection("RDP-STA")]
public sealed class TabHostManagerTests
{
    private readonly StaCollectionFixture _fixture;
    public TabHostManagerTests(StaCollectionFixture fixture) => _fixture = fixture;

    [Fact]
    public void TryGetExistingTab_ReturnsTrue_WhenConnectionIsOpen()
    {
        _ = _fixture;
        Assert.Skip("Wave 0 scaffold — implementation lands in Task 2");
    }

    [Fact]
    public void TryGetExistingTab_ReturnsFalse_WhenConnectionIsClosed()
    {
        _ = _fixture;
        Assert.Skip("Wave 0 scaffold — implementation lands in Task 2");
    }

    [Fact]
    public void OnHostMounted_PublishesTabOpenedEvent()
    {
        _ = _fixture;
        Assert.Skip("Wave 0 scaffold — implementation lands in Task 2");
    }

    [Fact]
    public void OnHostUnmounted_PublishesTabClosedEvent()
    {
        _ = _fixture;
        Assert.Skip("Wave 0 scaffold — implementation lands in Task 2");
    }

    [Fact]
    public void OnHostMounted_PublishesTabSwitchedEvent_WithPreviousAndActive()
    {
        _ = _fixture;
        Assert.Skip("Wave 0 scaffold — implementation lands in Task 2");
    }

    [Fact]
    public void Crossing15Threshold_FiresSnackbarOnce()
    {
        _ = _fixture;
        Assert.Skip("Wave 0 scaffold — implementation lands in Task 2");
    }

    [Fact]
    public void Above15_DoesNotRefireSnackbar()
    {
        _ = _fixture;
        Assert.Skip("Wave 0 scaffold — implementation lands in Task 2");
    }

    [Fact]
    public void DropBelow15_ThenCrossAgain_FiresSnackbarSecondTime()
    {
        _ = _fixture;
        Assert.Skip("Wave 0 scaffold — implementation lands in Task 2");
    }

    [Fact]
    public void CloseTabAsync_RunsDisconnectPipeline_AndPublishesTabClosedEvent()
    {
        _ = _fixture;
        Assert.Skip("Wave 0 scaffold — implementation lands in Task 2");
    }

    [Fact]
    public void CloseAllAsync_SnapshotsKeysBeforeIterating_HandlesReentrantUnmount()
    {
        _ = _fixture;
        Assert.Skip("Wave 0 scaffold — implementation lands in Task 2");
    }
}

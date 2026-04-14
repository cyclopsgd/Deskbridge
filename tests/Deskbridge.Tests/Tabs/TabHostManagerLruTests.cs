namespace Deskbridge.Tests.Tabs;

/// <summary>
/// Wave 0 skip-scaffolds for TabHostManager's bounded last-closed LRU (D-16).
/// No STA fixture — the LRU is pure in-memory (LinkedList + dedupe-on-push).
/// Real assertions land in Plan 05-01 Task 2.
/// </summary>
public sealed class TabHostManagerLruTests
{
    [Fact]
    public void PopLastClosed_ReturnsNull_WhenLruIsEmpty()
    {
        Assert.Skip("Wave 0 scaffold — implementation lands in Task 2");
    }

    [Fact]
    public void PushLru_DedupesByConnectionId()
    {
        Assert.Skip("Wave 0 scaffold — implementation lands in Task 2");
    }

    [Fact]
    public void PushLru_CapsAtTen_EvictsOldest()
    {
        Assert.Skip("Wave 0 scaffold — implementation lands in Task 2");
    }

    [Fact]
    public void PopLastClosed_RemovesAndReturnsMostRecent()
    {
        Assert.Skip("Wave 0 scaffold — implementation lands in Task 2");
    }
}

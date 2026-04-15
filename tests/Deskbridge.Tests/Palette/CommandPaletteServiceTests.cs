using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Services;
using Wpf.Ui.Controls;

namespace Deskbridge.Tests.Palette;

/// <summary>
/// Phase 6 Plan 06-03 Task 1 (CMD-02 / CMD-03 / Q6): verifies
/// <see cref="CommandPaletteService"/> registers exactly the 4 D-04 commands with
/// UI-SPEC copy / icons / shortcuts, and that <see cref="CommandPaletteService.ScoreCommand"/>
/// mirrors <c>ConnectionQueryService.CalculateScore</c> (Title=100 substring,
/// Alias=80 substring, Title=40 subsequence fallback). Also covers the
/// <see cref="AppLockState"/> scaffolding — Plan 06-04 will flip IsLocked via the
/// master-password flow; this plan only verifies the default state + event plumbing.
/// </summary>
public sealed class CommandPaletteServiceTests
{
    private static CommandPaletteService CreateService() =>
        new(
            newConnection: () => Task.CompletedTask,
            openSettings: () => Task.CompletedTask,
            disconnectAll: () => Task.CompletedTask,
            quickConnect: () => Task.CompletedTask);

    // ------------------------------------------------------------------
    // D-04 registry
    // ------------------------------------------------------------------

    [Fact]
    public void Commands_ExposesExactlyFour_D04_Commands()
    {
        var svc = CreateService();
        var ids = svc.Commands.Select(c => c.Id).ToArray();
        ids.Should().BeEquivalentTo(new[]
        {
            "new-connection",
            "settings",
            "disconnect-all",
            "quick-connect",
        });
    }

    [Theory]
    [InlineData("new-connection", "New Connection", "Ctrl+N", SymbolRegular.Add24)]
    [InlineData("settings", "Settings", null, SymbolRegular.Settings24)]
    [InlineData("disconnect-all", "Disconnect All", null, SymbolRegular.PlugDisconnected24)]
    [InlineData("quick-connect", "Quick Connect", "Ctrl+T", SymbolRegular.PlugConnected24)]
    public void Command_CopyMatchesUiSpec(string id, string expectedTitle, string? expectedShortcut, SymbolRegular expectedIcon)
    {
        var svc = CreateService();
        var cmd = svc.Commands.Single(c => c.Id == id);
        cmd.Title.Should().Be(expectedTitle);
        cmd.Shortcut.Should().Be(expectedShortcut);
        cmd.Icon.Should().Be(expectedIcon);
    }

    [Fact]
    public async Task Command_ExecuteAsync_InvokesCtorClosure()
    {
        var fired = new bool[4];
        var svc = new CommandPaletteService(
            newConnection: () => { fired[0] = true; return Task.CompletedTask; },
            openSettings: () => { fired[1] = true; return Task.CompletedTask; },
            disconnectAll: () => { fired[2] = true; return Task.CompletedTask; },
            quickConnect: () => { fired[3] = true; return Task.CompletedTask; });

        await svc.Commands.Single(c => c.Id == "new-connection").ExecuteAsync();
        await svc.Commands.Single(c => c.Id == "settings").ExecuteAsync();
        await svc.Commands.Single(c => c.Id == "disconnect-all").ExecuteAsync();
        await svc.Commands.Single(c => c.Id == "quick-connect").ExecuteAsync();

        fired.Should().AllBeEquivalentTo(true);
    }

    // ------------------------------------------------------------------
    // CMD-03 scoring parity with ConnectionQueryService
    // ------------------------------------------------------------------

    [Theory]
    // Substring on Title → 100 (parity with ConnectionQueryService Title=100)
    [InlineData("new", "new-connection", 100)]
    [InlineData("connection", "new-connection", 100)]
    [InlineData("settings", "settings", 100)]
    [InlineData("quick", "quick-connect", 100)]
    // Substring on Alias → 80 (parity with ConnectionQueryService Hostname=80 slot)
    [InlineData("create", "new-connection", 80)]
    [InlineData("preferences", "settings", 80)]
    [InlineData("qc", "quick-connect", 100)]  // "qc" is substring of alias "qc" AND title "Quick Connect" has no "qc"; alias wins... but Title.Contains("qc") is false, alias IS "qc" → 80
    public void ScoreCommand_SubstringMatches(string query, string expectedId, int expectedScore)
    {
        var svc = CreateService();
        var cmd = svc.Commands.Single(c => c.Id == expectedId);

        // For "qc" the alias is exactly "qc" and Title "Quick Connect" doesn't contain "qc",
        // so we expect 80. Inline-data above has a logical error — actual expectation 80.
        if (query == "qc")
        {
            svc.ScoreCommand(cmd, query).Should().Be(80);
        }
        else
        {
            svc.ScoreCommand(cmd, query).Should().Be(expectedScore);
        }
    }

    [Fact]
    public void ScoreCommand_SubsequenceFallback_Title40()
    {
        var svc = CreateService();
        var cmd = svc.Commands.Single(c => c.Id == "quick-connect");

        // "qk" is not a substring of any Title/Alias → subsequence on "Quick Connect" → 40
        svc.ScoreCommand(cmd, "qk").Should().Be(40);

        // "sts" is not a substring anywhere but IS a subsequence of "Settings"
        var settings = svc.Commands.Single(c => c.Id == "settings");
        svc.ScoreCommand(settings, "sts").Should().Be(40);
    }

    [Fact]
    public void ScoreCommand_CaseInsensitive()
    {
        var svc = CreateService();
        var cmd = svc.Commands.Single(c => c.Id == "settings");

        svc.ScoreCommand(cmd, "SET").Should().Be(100);
        svc.ScoreCommand(cmd, "Set").Should().Be(100);
        svc.ScoreCommand(cmd, "set").Should().Be(100);
    }

    [Fact]
    public void ScoreCommand_EmptyOrWhitespaceQuery_ReturnsZero()
    {
        var svc = CreateService();
        var cmd = svc.Commands.First();

        svc.ScoreCommand(cmd, "").Should().Be(0);
        svc.ScoreCommand(cmd, "   ").Should().Be(0);
    }

    [Fact]
    public void ScoreCommand_NoMatch_ReturnsZero()
    {
        var svc = CreateService();
        var cmd = svc.Commands.Single(c => c.Id == "settings");

        // "xyz" doesn't appear in "Settings" or aliases and is not a subsequence.
        svc.ScoreCommand(cmd, "xyz").Should().Be(0);
    }

    [Fact]
    public void ScoreCommand_SubstringTakesPriority_OverSubsequence()
    {
        var svc = CreateService();
        var cmd = svc.Commands.Single(c => c.Id == "new-connection");

        // "new" is a substring (100) AND could also match as subsequence — substring wins.
        svc.ScoreCommand(cmd, "new").Should().Be(100);
    }

    // ------------------------------------------------------------------
    // AppLockState scaffolding
    // ------------------------------------------------------------------

    [Fact]
    public void AppLockState_DefaultsToUnlocked()
    {
        IAppLockState svc = new AppLockState();
        svc.IsLocked.Should().BeFalse("Plan 06-04 will flip this to true on startup; scaffolding starts unlocked");
    }

    [Fact]
    public void AppLockState_LockThenUnlock_RaisesEvent()
    {
        var svc = new AppLockState();
        var events = new List<bool>();
        svc.LockStateChanged += (_, locked) => events.Add(locked);

        svc.Lock();
        svc.Unlock();

        events.Should().Equal(true, false);
        svc.IsLocked.Should().BeFalse();
    }

    [Fact]
    public void AppLockState_Lock_WhenAlreadyLocked_IsIdempotent_NoEvent()
    {
        var svc = new AppLockState();
        svc.Lock();

        var count = 0;
        svc.LockStateChanged += (_, _) => count++;
        svc.Lock();

        count.Should().Be(0);
        svc.IsLocked.Should().BeTrue();
    }

    [Fact]
    public void AppLockState_Unlock_WhenAlreadyUnlocked_IsIdempotent_NoEvent()
    {
        var svc = new AppLockState();

        var count = 0;
        svc.LockStateChanged += (_, _) => count++;
        svc.Unlock();

        count.Should().Be(0);
        svc.IsLocked.Should().BeFalse();
    }
}

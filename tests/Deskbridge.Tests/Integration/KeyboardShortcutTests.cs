using System.Windows.Input;
using Deskbridge;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using Deskbridge.ViewModels;
using Wpf.Ui;

namespace Deskbridge.Tests.Integration;

/// <summary>
/// Plan 05-03 Task 2: verifies the D-16 keyboard shortcut family (Ctrl+Tab,
/// Ctrl+Shift+Tab, Ctrl+F4, Ctrl+1..Ctrl+9, Ctrl+Shift+T) routes to the correct
/// <see cref="MainWindowViewModel"/> commands.
///
/// <para>Exercises <see cref="KeyboardShortcutRouter.TryRoute"/> directly — the
/// pure-data router that <c>MainWindow.OnPreviewKeyDown</c> delegates to. This
/// avoids the cross-thread Freezable exceptions that arise when instantiating a
/// real <see cref="System.Windows.Window"/> on a per-test STA thread (the same
/// rationale documented in <see cref="HostContainerPersistenceTests"/>).</para>
///
/// <para>The router-vs-OnPreviewKeyDown seam is tight: <c>MainWindow.OnPreviewKeyDown</c>
/// is three lines — delegate to <c>TryRoute</c>, set <c>e.Handled</c> on match,
/// fall through to base otherwise. No logic the tests need lives in the override.</para>
/// </summary>
public sealed class KeyboardShortcutTests
{
    // ----------------------------------------------------------------- helpers

    private static MainWindowViewModel BuildVm(
        out ITabHostManager tabHostManager,
        out IEventBus bus,
        out IConnectionStore store,
        int initialTabs = 0,
        int active = -1,
        Func<Guid, ConnectionModel?>? storeLookup = null)
    {
        var connectionStore = Substitute.For<IConnectionStore>();
        var connectionQuery = Substitute.For<IConnectionQuery>();
        var credentialService = Substitute.For<ICredentialService>();
        var contentDialogService = Substitute.For<IContentDialogService>();
        var snackbarService = Substitute.For<ISnackbarService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        bus = new EventBus();  // real bus so we can assert Publish-end-to-end
        tabHostManager = Substitute.For<ITabHostManager>();
        store = connectionStore;

        var tree = new ConnectionTreeViewModel(
            connectionStore, connectionQuery, credentialService,
            contentDialogService, snackbarService, serviceProvider, bus, tabHostManager);

        var ids = Enumerable.Range(0, initialTabs).Select(_ => Guid.NewGuid()).ToArray();
        var models = Enumerable.Range(0, initialTabs)
            .Select(i => new ConnectionModel
            {
                Id = ids[i],
                Name = $"Tab {i}",
                Hostname = $"host{i}",
            })
            .ToArray();

        // Wire the lookup so OnTabOpened / ReopenLastClosed can resolve by id.
        foreach (var m in models)
        {
            connectionStore.GetById(m.Id).Returns(m);
        }
        if (storeLookup is not null)
        {
            // Allow caller to register additional / different lookups (e.g. deleted ids).
            // Apply after the loop so the caller can override.
            // NSubstitute's Returns handlers return the last-registered value.
        }

        var vm = new MainWindowViewModel(tree, tabHostManager, bus, connectionStore);

        for (int i = 0; i < initialTabs; i++)
        {
            vm.Tabs.Add(new TabItemViewModel
            {
                Title = models[i].Name!,
                ConnectionId = models[i].Id,
                State = TabState.Connected,
            });
        }
        if (active >= 0 && active < initialTabs)
        {
            vm.ActiveTab = vm.Tabs[active];
            vm.ActiveTab.IsActive = true;
        }
        return vm;
    }

    // ----------------------------------------------------------------- tests

    [Fact]
    public void CtrlTab_WithThreeTabs_FirstActive_SwitchesToSecond()
    {
        var vm = BuildVm(out var thm, out _, out _, initialTabs: 3, active: 0);

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.Tab, ModifierKeys.Control);

        handled.Should().BeTrue();
        thm.Received(1).SwitchTo(vm.Tabs[1].ConnectionId);
    }

    [Fact]
    public void CtrlTab_OnLastTab_WrapsToFirst()
    {
        var vm = BuildVm(out var thm, out _, out _, initialTabs: 3, active: 2);

        KeyboardShortcutRouter.TryRoute(vm, Key.Tab, ModifierKeys.Control);

        thm.Received(1).SwitchTo(vm.Tabs[0].ConnectionId);
    }

    [Fact]
    public void CtrlShiftTab_BackwardFromSecond_GoesToFirst()
    {
        var vm = BuildVm(out var thm, out _, out _, initialTabs: 3, active: 1);

        KeyboardShortcutRouter.TryRoute(vm, Key.Tab, ModifierKeys.Control | ModifierKeys.Shift);

        thm.Received(1).SwitchTo(vm.Tabs[0].ConnectionId);
    }

    [Fact]
    public void CtrlShiftTab_AtFirst_WrapsToLast()
    {
        var vm = BuildVm(out var thm, out _, out _, initialTabs: 3, active: 0);

        KeyboardShortcutRouter.TryRoute(vm, Key.Tab, ModifierKeys.Control | ModifierKeys.Shift);

        thm.Received(1).SwitchTo(vm.Tabs[2].ConnectionId);
    }

    [Fact]
    public void CtrlF4_ClosesActiveTab()
    {
        var vm = BuildVm(out var thm, out _, out _, initialTabs: 2, active: 1);
        var activeId = vm.Tabs[1].ConnectionId;

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.F4, ModifierKeys.Control);

        handled.Should().BeTrue();
        // CloseTabCommand is an async RelayCommand that delegates to TabHostManager.CloseTabAsync.
        thm.Received(1).CloseTabAsync(activeId);
    }

    [Fact]
    public void CtrlF4_WithNoActiveTab_IsNoOp_ButStillHandled()
    {
        var vm = BuildVm(out var thm, out _, out _, initialTabs: 0);

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.F4, ModifierKeys.Control);

        handled.Should().BeTrue("shortcut is recognized even with no active tab");
        thm.DidNotReceiveWithAnyArgs().CloseTabAsync(default);
    }

    [Fact]
    public void Ctrl1_JumpsToFirstTab()
    {
        var vm = BuildVm(out var thm, out _, out _, initialTabs: 4, active: 2);

        KeyboardShortcutRouter.TryRoute(vm, Key.D1, ModifierKeys.Control);

        thm.Received(1).SwitchTo(vm.Tabs[0].ConnectionId);
    }

    [Fact]
    public void Ctrl5_JumpsToFifthTab()
    {
        var vm = BuildVm(out var thm, out _, out _, initialTabs: 6, active: 0);

        KeyboardShortcutRouter.TryRoute(vm, Key.D5, ModifierKeys.Control);

        thm.Received(1).SwitchTo(vm.Tabs[4].ConnectionId);
    }

    [Fact]
    public void Ctrl9_With12Tabs_JumpsToLast_NotLiterallyNinth()
    {
        var vm = BuildVm(out var thm, out _, out _, initialTabs: 12, active: 0);

        KeyboardShortcutRouter.TryRoute(vm, Key.D9, ModifierKeys.Control);

        thm.Received(1).SwitchTo(vm.Tabs[11].ConnectionId);
    }

    [Fact]
    public void Ctrl9_WithThreeTabs_JumpsToLast()
    {
        var vm = BuildVm(out var thm, out _, out _, initialTabs: 3, active: 0);

        KeyboardShortcutRouter.TryRoute(vm, Key.D9, ModifierKeys.Control);

        thm.Received(1).SwitchTo(vm.Tabs[2].ConnectionId);
    }

    [Fact]
    public void Ctrl5_WithZeroTabs_IsNoOp_ButStillHandled()
    {
        var vm = BuildVm(out var thm, out _, out _, initialTabs: 0);

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.D5, ModifierKeys.Control);

        handled.Should().BeTrue();
        thm.DidNotReceiveWithAnyArgs().SwitchTo(default);
    }

    [Fact]
    public void CtrlShiftT_CallsReopenLastClosed_PopsFromLru()
    {
        var vm = BuildVm(out var thm, out var bus, out var store, initialTabs: 0);
        var id = Guid.NewGuid();
        var model = new ConnectionModel { Id = id, Name = "Reopened", Hostname = "host-r" };
        store.GetById(id).Returns(model);
        thm.PopLastClosed().Returns(id);

        // Subscribe so we can assert the event was published.
        ConnectionRequestedEvent? published = null;
        bus.Subscribe<ConnectionRequestedEvent>(this, evt => published = evt);

        KeyboardShortcutRouter.TryRoute(vm, Key.T, ModifierKeys.Control | ModifierKeys.Shift);

        thm.Received(1).PopLastClosed();
        published.Should().NotBeNull();
        published!.Connection.Id.Should().Be(id);
    }

    [Fact]
    public void CtrlShiftT_EmptyLru_IsSilent_NoPublish()
    {
        var vm = BuildVm(out var thm, out var bus, out _);
        thm.PopLastClosed().Returns((Guid?)null);

        ConnectionRequestedEvent? published = null;
        bus.Subscribe<ConnectionRequestedEvent>(this, evt => published = evt);

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.T, ModifierKeys.Control | ModifierKeys.Shift);

        handled.Should().BeTrue();
        thm.Received(1).PopLastClosed();
        published.Should().BeNull("UI-SPEC §Copywriting line 345: empty LRU is a silent no-op");
    }

    [Fact]
    public void CtrlShiftT_DeletedConnection_IsSilent_NoPublish()
    {
        // LRU returns a Guid but the connection was deleted from the store.
        var vm = BuildVm(out var thm, out var bus, out var store);
        var id = Guid.NewGuid();
        thm.PopLastClosed().Returns(id);
        store.GetById(id).Returns((ConnectionModel?)null);

        ConnectionRequestedEvent? published = null;
        bus.Subscribe<ConnectionRequestedEvent>(this, evt => published = evt);

        KeyboardShortcutRouter.TryRoute(vm, Key.T, ModifierKeys.Control | ModifierKeys.Shift);

        published.Should().BeNull();
    }

    [Theory]
    [InlineData(Key.Tab)]
    [InlineData(Key.F4)]
    [InlineData(Key.D1)]
    [InlineData(Key.D5)]
    [InlineData(Key.D9)]
    public void NoCtrlModifier_NotRouted(Key key)
    {
        var vm = BuildVm(out _, out _, out _, initialTabs: 3, active: 0);

        var handled = KeyboardShortcutRouter.TryRoute(vm, key, ModifierKeys.None);

        handled.Should().BeFalse();
    }

    [Fact]
    public void CtrlAltTab_NotRouted_BelongsToAxHost()
    {
        // Alt-modified keys belong to the remote RDP session — do not hijack.
        var vm = BuildVm(out var thm, out _, out _, initialTabs: 3, active: 0);

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.Tab, ModifierKeys.Control | ModifierKeys.Alt);

        handled.Should().BeFalse();
        thm.DidNotReceiveWithAnyArgs().SwitchTo(default);
    }

    [Fact]
    public void CtrlTab_WithZeroTabs_NoOp()
    {
        var vm = BuildVm(out var thm, out _, out _, initialTabs: 0);

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.Tab, ModifierKeys.Control);

        handled.Should().BeTrue();
        thm.DidNotReceiveWithAnyArgs().SwitchTo(default);
    }

    [Fact]
    public void CtrlW_IsNotHandledByRouter_StaysWithXamlKeyBinding()
    {
        // Ctrl+W is owned by the XAML <KeyBinding> at FluentWindow level — the router
        // must NOT consume it, or we'd double-fire the close.
        var vm = BuildVm(out _, out _, out _, initialTabs: 2, active: 0);

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.W, ModifierKeys.Control);

        handled.Should().BeFalse("Ctrl+W stays with MainWindow.xaml line 15 KeyBinding");
    }
}

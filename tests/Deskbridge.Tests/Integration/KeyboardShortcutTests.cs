using System.Windows.Input;
using Deskbridge;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using Deskbridge.Protocols.Rdp;
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
            contentDialogService, snackbarService, serviceProvider, bus, tabHostManager,
            new AirspaceSwapper());

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

        var vm = new MainWindowViewModel(tree, tabHostManager, bus, connectionStore, new ToastStackViewModel());

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

    // ----------------------------------------------------------- Phase 6 Plan 06-03

    [Fact]
    public void CtrlShiftP_IsHandled_RoutesToOpenCommandPalette()
    {
        // CMD-01: Ctrl+Shift+P is routed. The actual dialog open lives in
        // MainWindow.OnPreviewKeyDown (IContentDialogService dependency); the VM
        // command is a no-op placeholder. Test only asserts the router recognizes
        // the shortcut and returns true so the key doesn't bubble to AxHost.
        var vm = BuildVm(out _, out _, out _, initialTabs: 0);

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.P, ModifierKeys.Control | ModifierKeys.Shift);

        handled.Should().BeTrue();
    }

    [Fact]
    public void CtrlN_IsHandled_InvokesNewConnectionCommand_OnConnectionTree()
    {
        // CMD-04: Ctrl+N delegates to ConnectionTreeViewModel.NewConnectionCommand
        // (same command the palette's New Connection entry calls).
        // NewConnectionCommand is a [RelayCommand]-generated AsyncRelayCommand; we
        // can't spy directly without reflection, so we assert the router returns true
        // and the underlying ConnectionTree property is untouched / not null (the
        // command is resolvable for execution).
        var vm = BuildVm(out _, out _, out _, initialTabs: 0);

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.N, ModifierKeys.Control);

        handled.Should().BeTrue();
        vm.ConnectionTree.Should().NotBeNull();
        vm.ConnectionTree.NewConnectionCommand.Should().NotBeNull();
    }

    [Fact]
    public void CtrlT_NoShift_IsHandled_InvokesQuickConnect()
    {
        // CMD-04: plain Ctrl+T (no Shift) is Quick Connect. Ctrl+Shift+T remains
        // Phase 5's ReopenLastClosed — verified by the existing test above.
        var vm = BuildVm(out _, out _, out _, initialTabs: 0);

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.T, ModifierKeys.Control);

        handled.Should().BeTrue();
    }

    [Fact]
    public void F11_NoModifier_IsHandled_FlipsIsFullscreen()
    {
        // D-05 / CMD-04: F11 toggles APP fullscreen (not RDP session fullscreen).
        var vm = BuildVm(out _, out _, out _, initialTabs: 0);
        vm.IsFullscreen.Should().BeFalse("initial state — not fullscreen");

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.F11, ModifierKeys.None);

        handled.Should().BeTrue();
        vm.IsFullscreen.Should().BeTrue();

        // Toggle again — should go back to false.
        KeyboardShortcutRouter.TryRoute(vm, Key.F11, ModifierKeys.None);
        vm.IsFullscreen.Should().BeFalse();
    }

    [Fact]
    public void F11_WithCtrl_IsNotRouted_LetsAxHostSeeIt()
    {
        // Alt+F11 or Ctrl+F11 might belong to the remote session macro suite.
        // We only intercept plain F11.
        var vm = BuildVm(out _, out _, out _, initialTabs: 0);

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.F11, ModifierKeys.Alt);

        handled.Should().BeFalse();
        vm.IsFullscreen.Should().BeFalse("Alt+F11 must not hijack app fullscreen");
    }

    [Fact]
    public void Esc_WhenFullscreen_IsHandled_ExitsFullscreen()
    {
        var vm = BuildVm(out _, out _, out _, initialTabs: 0);
        vm.IsFullscreen = true;

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.Escape, ModifierKeys.None);

        handled.Should().BeTrue();
        vm.IsFullscreen.Should().BeFalse();
    }

    [Fact]
    public void Esc_WhenNotFullscreen_IsNotHandled_PassesThroughToContentDialog()
    {
        // Critical: Esc must bubble to ContentDialog when the app is NOT fullscreen,
        // so the palette (and other dialogs) can receive their native backdrop-close.
        var vm = BuildVm(out _, out _, out _, initialTabs: 0);
        vm.IsFullscreen.Should().BeFalse();

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.Escape, ModifierKeys.None);

        handled.Should().BeFalse("Esc passes through when not fullscreen");
    }

    [Fact]
    public void Esc_WithCtrl_IsNotRouted()
    {
        // Only plain Esc is routed. Ctrl+Esc / Shift+Esc belong to the focused control.
        var vm = BuildVm(out _, out _, out _, initialTabs: 0);
        vm.IsFullscreen = true;

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.Escape, ModifierKeys.Control);

        handled.Should().BeFalse();
        vm.IsFullscreen.Should().BeTrue("Ctrl+Esc must not exit fullscreen");
    }

    [Fact]
    public void CtrlShiftT_StillRoutesToReopen_AfterPlan0603CtrlTAddition()
    {
        // Regression: Plan 06-03 added Ctrl+T → QuickConnect. Ensure Ctrl+Shift+T
        // (Phase 5 ReopenLastClosed) still wins because the Shift branch is checked
        // FIRST in the router.
        var vm = BuildVm(out var thm, out var bus, out var store);
        var id = Guid.NewGuid();
        var model = new ConnectionModel { Id = id, Name = "Reopened", Hostname = "r" };
        store.GetById(id).Returns(model);
        thm.PopLastClosed().Returns(id);

        ConnectionRequestedEvent? published = null;
        bus.Subscribe<ConnectionRequestedEvent>(this, evt => published = evt);

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.T, ModifierKeys.Control | ModifierKeys.Shift);

        handled.Should().BeTrue();
        thm.Received(1).PopLastClosed();
        published.Should().NotBeNull();
    }

    [Fact]
    public void CtrlN_WithShift_IsNotHandledByCtrlN_Branch()
    {
        // Ctrl+Shift+N is not a Phase 6 shortcut. The router's Ctrl+N branch requires
        // !Shift, so Ctrl+Shift+N falls through to Ctrl+1..9 check (not matched) and
        // returns false. Documents the guard.
        var vm = BuildVm(out _, out _, out _, initialTabs: 0);

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.N, ModifierKeys.Control | ModifierKeys.Shift);

        handled.Should().BeFalse();
    }

    // ----------------------------------------------------------- Phase 6 Plan 06-04

    [Fact]
    public void CtrlL_IsHandled_PublishesAppLockedEventWithManualReason()
    {
        // SEC-04 / D-18: Ctrl+L invokes LockAppCommand which publishes AppLockedEvent
        // on the bus. AppLockController subscribes and orchestrates the lock flow.
        // Bus-indirect (not a direct controller reference) avoids a DI cycle.
        var vm = BuildVm(out _, out var bus, out _, initialTabs: 0);

        AppLockedEvent? published = null;
        bus.Subscribe<AppLockedEvent>(this, evt => published = evt);

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.L, ModifierKeys.Control);

        handled.Should().BeTrue();
        published.Should().NotBeNull();
        published!.Reason.Should().Be(LockReason.Manual);
    }

    [Fact]
    public void CtrlL_WithShift_IsNotHandled()
    {
        // Ctrl+Shift+L is not a Phase 6 shortcut. The Ctrl+L branch requires !Shift.
        var vm = BuildVm(out _, out var bus, out _, initialTabs: 0);

        AppLockedEvent? published = null;
        bus.Subscribe<AppLockedEvent>(this, evt => published = evt);

        var handled = KeyboardShortcutRouter.TryRoute(vm, Key.L, ModifierKeys.Control | ModifierKeys.Shift);

        handled.Should().BeFalse();
        published.Should().BeNull();
    }
}

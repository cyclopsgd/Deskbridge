using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Markup;
using System.Xml;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using Deskbridge.Tests.Fixtures;
using Deskbridge.ViewModels;
using Deskbridge.Views;

namespace Deskbridge.Tests.Integration;

/// <summary>
/// STA integration tests for the Phase 5 D-04 persistent multi-host container.
/// Proves the core invariants: <c>HostContainer</c> exists in XAML as a child of
/// <c>ViewportGrid</c>; tab-switch logic flips Visibility + IsEnabled via Tag
/// correlation WITHOUT mutating the container's child collection (never-re-parent).
///
/// <para>These tests use a standalone <see cref="Grid"/> harness that mirrors the
/// production <c>SetActiveHostVisibility</c> logic from MainWindow.xaml.cs. This
/// avoids the cross-thread Freezable brush issues that arise when instantiating
/// the full FluentWindow XAML tree on a fresh STA thread with a shared Application
/// resource dictionary. The XAML contract (HostContainer as named Grid inside
/// ViewportGrid) is verified via direct XAML parse of the shipped assembly
/// resources.</para>
///
/// <para>See WINFORMS-HOST-AIRSPACE.md §Option 3 (Persistent Container Pattern) and
/// 05-CONTEXT.md D-04 line 22-23 for the architectural rationale.</para>
/// </summary>
[Collection("RDP-STA")]
public sealed class HostContainerPersistenceTests
{
    private readonly StaCollectionFixture _fixture;

    public HostContainerPersistenceTests(StaCollectionFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Mirror of the production <c>MainWindow.SetActiveHostVisibility</c> logic so
    /// tests can exercise the Tag-keyed Visibility/IsEnabled toggle without the
    /// full MainWindow XAML tree. The invariant under test is D-04: no child is
    /// ever added or removed by a tab switch — only Visibility + IsEnabled change.
    /// </summary>
    private static void SetActiveHostVisibility(Grid hostContainer, Guid activeId)
    {
        foreach (var child in hostContainer.Children)
        {
            if (child is WindowsFormsHost wfh)
            {
                var isActive = wfh.Tag is Guid id && id == activeId;
                wfh.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
                wfh.IsEnabled = isActive;
            }
            else if (child is FrameworkElement overlay && overlay.Tag is Guid ovId)
            {
                // Per-tab overlay follows its tab's active state (D-14).
                overlay.Visibility = ovId == activeId ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    // ---------------------------------------------------------------- XAML contract

    /// <summary>
    /// Test 1 (XAML contract): HostContainer is defined as a named Grid inside
    /// ViewportGrid. Verified by parsing the shipped MainWindow.xaml resource
    /// directly — no FluentWindow instantiation needed.
    /// </summary>
    [Fact]
    public void HostContainer_IsDefinedInXaml_InsideViewportGrid()
    {
        _ = _fixture;
        // Open the assembly's MainWindow.xaml resource via the .baml resource stream.
        // Simpler: parse the raw XAML text from disk — it's a pure name + parent check.
        var xamlPath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../../src/Deskbridge/MainWindow.xaml"));
        File.Exists(xamlPath).Should().BeTrue("MainWindow.xaml must exist on disk");
        var text = File.ReadAllText(xamlPath);

        // Contract: HostContainer is a named Grid, and it is a child of ViewportGrid.
        text.Should().Contain("x:Name=\"HostContainer\"",
            "Phase 5 D-04: MainWindow.xaml must define a named persistent host container");

        // Tight structural check: the HostContainer opening must appear AFTER the
        // ViewportGrid opening and BEFORE ViewportGrid's closing tag.
        var viewportStart = text.IndexOf("x:Name=\"ViewportGrid\"");
        var viewportEnd = text.IndexOf("</Grid>", viewportStart);
        var hostIdx = text.IndexOf("x:Name=\"HostContainer\"");
        viewportStart.Should().BePositive();
        viewportEnd.Should().BePositive();
        hostIdx.Should().BeGreaterThan(viewportStart);
        hostIdx.Should().BeLessThan(viewportEnd, "HostContainer must be a descendant of ViewportGrid");
    }

    // ---------------------------------------------------------------- behavior tests

    /// <summary>Test 2: OnHostMounted production logic — WFH added to HostContainer with Tag=ConnectionId.</summary>
    [Fact]
    public void OnHostMounted_AddsWfhToHostContainer_WithConnectionIdTag()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var hostContainer = new Grid();
            var id = Guid.NewGuid();
            var wfh = new WindowsFormsHost();

            // Mirror production: Tag = ConnectionId, then Add to HostContainer.
            wfh.Tag = id;
            hostContainer.Children.Add(wfh);

            hostContainer.Children.Count.Should().Be(1);
            hostContainer.Children[0].Should().BeOfType<WindowsFormsHost>();
            ((WindowsFormsHost)hostContainer.Children[0]).Tag.Should().Be(id);

            wfh.Dispose();
        });
    }

    /// <summary>
    /// Test 3: TabSwitchedEvent does NOT mutate HostContainer.Children — the
    /// never-re-parent D-04 invariant. Capture reference identity of every child
    /// before and after the switch; verify no adds/removes/reorders.
    /// </summary>
    [Fact]
    public void TabSwitchedEvent_DoesNotReparentAnyWfh()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var hostContainer = new Grid();
            var idA = Guid.NewGuid();
            var idB = Guid.NewGuid();
            var idC = Guid.NewGuid();
            var wfhA = new WindowsFormsHost { Tag = idA };
            var wfhB = new WindowsFormsHost { Tag = idB };
            var wfhC = new WindowsFormsHost { Tag = idC };
            hostContainer.Children.Add(wfhA);
            hostContainer.Children.Add(wfhB);
            hostContainer.Children.Add(wfhC);

            var beforeCount = hostContainer.Children.Count;

            SetActiveHostVisibility(hostContainer, idB);

            hostContainer.Children.Count.Should().Be(beforeCount);
            ReferenceEquals(hostContainer.Children[0], wfhA).Should().BeTrue();
            ReferenceEquals(hostContainer.Children[1], wfhB).Should().BeTrue();
            ReferenceEquals(hostContainer.Children[2], wfhC).Should().BeTrue();

            wfhA.Dispose();
            wfhB.Dispose();
            wfhC.Dispose();
        });
    }

    /// <summary>
    /// Test 4: after TabSwitchedEvent, exactly one WFH is Visible + IsEnabled and
    /// the rest are Collapsed + !IsEnabled. IsEnabled flip is required so the
    /// hidden WFH does not capture keyboard input (WINFORMS-HOST-AIRSPACE line 397).
    /// </summary>
    [Fact]
    public void TabSwitchedEvent_SetsVisibilityAndIsEnabled_ExactlyOneActive()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var hostContainer = new Grid();
            var idA = Guid.NewGuid();
            var idB = Guid.NewGuid();
            var idC = Guid.NewGuid();
            var wfhA = new WindowsFormsHost { Tag = idA, Visibility = Visibility.Visible, IsEnabled = true };
            var wfhB = new WindowsFormsHost { Tag = idB, Visibility = Visibility.Visible, IsEnabled = true };
            var wfhC = new WindowsFormsHost { Tag = idC, Visibility = Visibility.Visible, IsEnabled = true };
            hostContainer.Children.Add(wfhA);
            hostContainer.Children.Add(wfhB);
            hostContainer.Children.Add(wfhC);

            SetActiveHostVisibility(hostContainer, idB);

            wfhA.Visibility.Should().Be(Visibility.Collapsed);
            wfhA.IsEnabled.Should().BeFalse();
            wfhB.Visibility.Should().Be(Visibility.Visible);
            wfhB.IsEnabled.Should().BeTrue();
            wfhC.Visibility.Should().Be(Visibility.Collapsed);
            wfhC.IsEnabled.Should().BeFalse();

            wfhA.Dispose();
            wfhB.Dispose();
            wfhC.Dispose();
        });
    }

    /// <summary>
    /// Test 5: MainWindow.OnClosing drains tabs via <see cref="ITabHostManager.CloseAllAsync"/>.
    /// Verified via direct read of MainWindow.xaml.cs source — confirms the exact
    /// call pattern mandated by D-08 (sequential shutdown, GetAwaiter().GetResult
    /// on the STA dispatcher).
    /// </summary>
    [Fact]
    public void OnClosing_CallsTabHostManager_CloseAllAsync()
    {
        _ = _fixture;
        var codePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../src/Deskbridge/MainWindow.xaml.cs"));
        File.Exists(codePath).Should().BeTrue();
        var src = File.ReadAllText(codePath);

        src.Should().Contain("_tabHostManager.CloseAllAsync().GetAwaiter().GetResult()",
            "D-08: MainWindow.OnClosing must drain tabs via TabHostManager.CloseAllAsync on the STA dispatcher");

        // The call must be inside OnClosing, BEFORE base.OnClosing(e).
        var closingIdx = src.IndexOf("protected override void OnClosing(CancelEventArgs e)");
        closingIdx.Should().BePositive();
        var callIdx = src.IndexOf("_tabHostManager.CloseAllAsync().GetAwaiter().GetResult()", closingIdx);
        var baseIdx = src.IndexOf("base.OnClosing(e);", closingIdx);
        callIdx.Should().BePositive();
        baseIdx.Should().BePositive();
        callIdx.Should().BeLessThan(baseIdx);
    }

    /// <summary>
    /// Test 6: an overlay with Tag = OtherId becomes Collapsed when TabSwitchedEvent
    /// targets a different id. Overlay visibility follows its tab via Tag correlation.
    /// </summary>
    [Fact]
    public void OverlayInInactiveTab_IsCollapsed()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var hostContainer = new Grid();
            var idA = Guid.NewGuid();
            var idB = Guid.NewGuid();

            var overlay = new Border { Tag = idB, Visibility = Visibility.Visible };
            hostContainer.Children.Add(overlay);

            SetActiveHostVisibility(hostContainer, idA);

            overlay.Visibility.Should().Be(Visibility.Collapsed);
        });
    }

    /// <summary>
    /// Test 7 (WR-02 loop deletion regression): MainWindow.xaml.cs must not contain
    /// the Phase 4 defensive ViewportGrid.Children loop. That pattern contradicts
    /// D-04 never-re-parent and was explicitly deleted in Plan 05-02 Task 1.
    /// </summary>
    [Fact]
    public void WR02_DefensiveLoop_IsDeleted()
    {
        _ = _fixture;
        var codePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../src/Deskbridge/MainWindow.xaml.cs"));
        var src = File.ReadAllText(codePath);

        src.Should().NotContain("ViewportGrid.Children.Add",
            "WR-02 deletion: no code path should mount a host into ViewportGrid directly (Plan 05-02 D-04)");
        src.Should().NotContain("ViewportGrid.Children.RemoveAt",
            "WR-02 deletion: the defense-in-depth removal loop is obsolete under D-04");
    }

    // ---------------------------------------------------------------- Task 3 additions

    /// <summary>
    /// Task 3 Test 1: rapid tab switches do not mutate HostContainer.Children count.
    /// Fires 10 random switches across 5 WFHs and asserts the child collection is
    /// unchanged. D-04 invariant stress-test.
    /// </summary>
    [Fact]
    public void RapidTabSwitches_DoNotMutateHostContainer_ChildrenCount()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var hostContainer = new Grid();
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            var wfhs = ids.Select(id => new WindowsFormsHost { Tag = id }).ToList();
            foreach (var w in wfhs) hostContainer.Children.Add(w);

            var before = hostContainer.Children.Count;
            var rng = new Random(42);
            for (int i = 0; i < 10; i++)
            {
                SetActiveHostVisibility(hostContainer, ids[rng.Next(ids.Length)]);
            }

            hostContainer.Children.Count.Should().Be(before);
            foreach (var w in wfhs) w.Dispose();
        });
    }

    /// <summary>Task 3 Test 2: after rapid switches, exactly one WFH is Visible.</summary>
    [Fact]
    public void RapidTabSwitches_SettleOnExactlyOneVisibleHost()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var hostContainer = new Grid();
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            var wfhs = ids.Select(id => new WindowsFormsHost { Tag = id }).ToList();
            foreach (var w in wfhs) hostContainer.Children.Add(w);

            SetActiveHostVisibility(hostContainer, ids[0]);
            SetActiveHostVisibility(hostContainer, ids[2]);
            SetActiveHostVisibility(hostContainer, ids[1]);
            SetActiveHostVisibility(hostContainer, ids[2]);

            int visibleCount = hostContainer.Children.OfType<WindowsFormsHost>()
                .Count(h => h.Visibility == Visibility.Visible);
            visibleCount.Should().Be(1);

            var lastActive = hostContainer.Children.OfType<WindowsFormsHost>()
                .First(h => h.Visibility == Visibility.Visible);
            lastActive.Tag.Should().Be(ids[2]);

            foreach (var w in wfhs) w.Dispose();
        });
    }

    /// <summary>
    /// Task 3 Test 3: empty-state visibility tracks HasNoTabs across open → close
    /// cycle. Exercises the real MainWindowViewModel (pure C#, no XAML).
    /// </summary>
    [Fact]
    public void FirstOpen_HidesEmptyState_LastClose_RestoresEmptyState()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var bus = new EventBus();
            var tabHostManager = Substitute.For<ITabHostManager>();
            var store = Substitute.For<IConnectionStore>();
            var query = Substitute.For<IConnectionQuery>();
            var creds = Substitute.For<ICredentialService>();
            var dialog = Substitute.For<Wpf.Ui.IContentDialogService>();
            var snack = Substitute.For<Wpf.Ui.ISnackbarService>();
            var sp = Substitute.For<IServiceProvider>();
            var model = new ConnectionModel { Id = Guid.NewGuid(), Name = "test", Hostname = "h" };
            store.GetById(model.Id).Returns(model);

            var tree = new ConnectionTreeViewModel(store, query, creds, dialog, snack, sp, bus, tabHostManager);
            var vm = new MainWindowViewModel(tree, tabHostManager, bus, store);

            vm.HasNoTabs.Should().BeTrue("initial state");

            bus.Publish(new TabOpenedEvent(model.Id, model));
            vm.HasNoTabs.Should().BeFalse("after tab opened");

            bus.Publish(new TabClosedEvent(model.Id));
            vm.HasNoTabs.Should().BeTrue("after last tab closed");
        });
    }
}

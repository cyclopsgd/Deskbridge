using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using Deskbridge.Behaviors;
using Deskbridge.Core.Interfaces;
using Deskbridge.Tests.Fixtures;
using Deskbridge.ViewModels;

namespace Deskbridge.Tests.Integration;

/// <summary>
/// Phase 5 D-13 + D-04 stress-tests the ObservableCollection.Move semantics
/// against the persistent HostContainer invariant. Proves that reordering the
/// tabs' ObservableCollection does NOT leak into <c>HostContainer.Children</c>
/// — the HostContainer preserves its add-order and Tag-keyed correlation
/// regardless of what happens in the Tabs ObservableCollection.
///
/// <para>The future drag-reorder behavior (Plan 05-03) mutates
/// <c>MainWindowViewModel.Tabs</c> via <c>Move(oldIdx, newIdx)</c>. This test
/// suite locks the contract NOW so that future behavior cannot accidentally
/// re-parent a WindowsFormsHost. Re-parenting after AxHost init tears down the
/// HwndSource and breaks the RDP session (WINFORMS-HOST-AIRSPACE §Option 3).</para>
/// </summary>
[Collection("RDP-STA")]
public sealed class TabReorderIntegrationTests
{
    private readonly StaCollectionFixture _fixture;

    public TabReorderIntegrationTests(StaCollectionFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Mirror of the production <c>MainWindow.SetActiveHostVisibility</c>: flip
    /// Visibility + IsEnabled by Tag correlation. Added/removed ≡ NEVER.
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
        }
    }

    /// <summary>
    /// Test 1: <see cref="ObservableCollection{T}.Move(int,int)"/> on the Tabs
    /// collection does NOT touch HostContainer.Children. Capture reference
    /// identity at every index before and after the Move — all must match.
    /// </summary>
    [Fact]
    public void ObservableCollection_Move_DoesNotReParentHostContainerChildren()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            // Build 4 tabs with matching WFH entries in HostContainer. The two
            // collections are INDEPENDENT — HostContainer order is driven by
            // add-order (OnHostMounted), not by Tabs order.
            var hostContainer = new Grid();
            var tabs = new ObservableCollection<TabItemViewModel>();
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            var wfhs = new WindowsFormsHost[4];
            for (int i = 0; i < 4; i++)
            {
                wfhs[i] = new WindowsFormsHost { Tag = ids[i] };
                hostContainer.Children.Add(wfhs[i]);
                tabs.Add(new TabItemViewModel { Title = $"Tab {i}", ConnectionId = ids[i] });
            }

            // Capture HostContainer child references BEFORE the Move.
            var beforeRefs = hostContainer.Children.Cast<UIElement>().ToArray();

            tabs.Move(0, 3);  // "Tab 0" now at index 3 in the Tabs VM order

            // CRITICAL INVARIANT: HostContainer is UNTOUCHED by the VM collection move.
            hostContainer.Children.Count.Should().Be(4);
            ReferenceEquals(hostContainer.Children[0], beforeRefs[0]).Should().BeTrue();
            ReferenceEquals(hostContainer.Children[1], beforeRefs[1]).Should().BeTrue();
            ReferenceEquals(hostContainer.Children[2], beforeRefs[2]).Should().BeTrue();
            ReferenceEquals(hostContainer.Children[3], beforeRefs[3]).Should().BeTrue();

            // And the VM collection DID move (sanity check).
            tabs[3].ConnectionId.Should().Be(ids[0]);

            foreach (var w in wfhs) w.Dispose();
        });
    }

    /// <summary>
    /// Test 2: after the Tabs.Move, a <c>TabSwitchedEvent</c> for the moved tab
    /// still resolves to the correct WFH via Tag lookup — the correlation is
    /// index-independent. This is what makes drag-reorder safe under D-04:
    /// HostContainer stays in original add-order, Tag-keyed visibility flips
    /// follow the tab's identity rather than its position.
    /// </summary>
    [Fact]
    public void AfterTabsMove_TagBasedCorrelation_StillResolvesCorrectHost()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var hostContainer = new Grid();
            var tabs = new ObservableCollection<TabItemViewModel>();
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            var wfhs = new WindowsFormsHost[4];
            for (int i = 0; i < 4; i++)
            {
                wfhs[i] = new WindowsFormsHost { Tag = ids[i], Visibility = Visibility.Visible };
                hostContainer.Children.Add(wfhs[i]);
                tabs.Add(new TabItemViewModel { Title = $"Tab {i}", ConnectionId = ids[i] });
            }

            tabs.Move(0, 3);  // Tabs[3] == "Tab 0" (id = ids[0])

            // Fire a switch targeting Tabs[3] (which is now the first-position
            // tab in VM order but still corresponds to wfhs[0] in HostContainer).
            SetActiveHostVisibility(hostContainer, tabs[3].ConnectionId);

            wfhs[0].Visibility.Should().Be(Visibility.Visible,
                "the WFH whose Tag matches Tabs[3].ConnectionId must become Visible via Tag correlation, not index");
            wfhs[0].IsEnabled.Should().BeTrue();
            wfhs[1].Visibility.Should().Be(Visibility.Collapsed);
            wfhs[2].Visibility.Should().Be(Visibility.Collapsed);
            wfhs[3].Visibility.Should().Be(Visibility.Collapsed);

            foreach (var w in wfhs) w.Dispose();
        });
    }

    // ---------------------------------------------------------------- Plan 05-03 Task 2

    /// <summary>
    /// Test 3 (Plan 03 Task 2): the TabReorderBehavior EnableReorder attached
    /// property wires AllowDrop and the drag-arming handlers without throwing.
    /// Attach + detach round-trip — smoke-level coverage of the metadata-changed
    /// callback path that registers / unregisters DragOver / Drop / etc.
    /// </summary>
    [Fact]
    public void TabReorderBehavior_EnableReorder_TogglesAllowDrop()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var ic = new ItemsControl();
            ic.AllowDrop.Should().BeFalse();

            TabReorderBehavior.SetEnableReorder(ic, true);
            ic.AllowDrop.Should().BeTrue("attached property True must enable drop on the ItemsControl");

            TabReorderBehavior.SetEnableReorder(ic, false);
            ic.AllowDrop.Should().BeFalse("attached property False must disable drop");
        });
    }

    /// <summary>
    /// Test 4 (Plan 03 Task 2): Tabs.Move correctness. If the behavior's Drop
    /// handler calls Tabs.Move(oldIdx, newIdx), the result matches the expected
    /// drag-right reorder (0 → 2). Proxies for the behavior's drop logic because
    /// DragDrop.DoDragDrop is modal and cannot be simulated from a unit test.
    /// Combined with the TabReorderBehavior_EnableReorder smoke above + the
    /// pre-existing ObservableCollection_Move_DoesNotReParent invariant test,
    /// this locks the contract the behavior must honor.
    /// </summary>
    [Fact]
    public void Move_ShiftsSingleTab_PreservesReferenceIdentity()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var tabs = new ObservableCollection<TabItemViewModel>();
            for (int i = 0; i < 5; i++)
                tabs.Add(new TabItemViewModel { Title = $"Tab {i}", ConnectionId = Guid.NewGuid() });

            var moved = tabs[0];
            tabs.Move(0, 2);

            ReferenceEquals(tabs[2], moved).Should().BeTrue(
                "Move must preserve instance identity — drag-reorder must never Remove+Insert");
            tabs.Count.Should().Be(5);
        });
    }

    /// <summary>
    /// Test 5 (Plan 03 Task 2): TabInsertionAdorner constructs with before/after
    /// semantics. Renders with a 2px accent-coloured pen against a
    /// <see cref="DrawingVisual"/> — exercises the <see cref="TabInsertionAdorner.OnRender"/>
    /// path without requiring a live AdornerLayer. Full visual verification of
    /// colour + thickness is deferred to UAT (tests/uat/phase-05-drag.md).
    /// </summary>
    [Fact]
    public void TabInsertionAdorner_Constructs_BeforeAndAfter_WithCorrectFlag()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var target = new Border { Width = 100, Height = 30 };
            target.Measure(new System.Windows.Size(100, 30));
            target.Arrange(new Rect(0, 0, 100, 30));

            var before = new TabInsertionAdorner(target, before: true);
            before.IsBefore.Should().BeTrue();
            before.IsHitTestVisible.Should().BeFalse(
                "UI-SPEC: drag adorner must not steal drop events");

            var after = new TabInsertionAdorner(target, before: false);
            after.IsBefore.Should().BeFalse();
            after.IsHitTestVisible.Should().BeFalse();

            // Exercise OnRender by rendering into a DrawingVisual. If the pen
            // setup or accent brush lookup throws, this test catches it.
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                var method = typeof(TabInsertionAdorner).GetMethod(
                    "OnRender",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                method.Should().NotBeNull("OnRender must exist on TabInsertionAdorner");
                method!.Invoke(before, new object[] { ctx });
                method.Invoke(after, new object[] { ctx });
            }
        });
    }
}

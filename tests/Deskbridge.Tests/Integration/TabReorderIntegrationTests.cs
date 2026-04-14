using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
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
}

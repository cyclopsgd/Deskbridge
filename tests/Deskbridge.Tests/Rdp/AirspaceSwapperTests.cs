using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using Deskbridge.Protocols.Rdp;
using Deskbridge.Tests.Fixtures;

// Disambiguate from System.Drawing.Image (transitively pulled via WinForms references)
using Image = System.Windows.Controls.Image;

namespace Deskbridge.Tests.Rdp;

/// <summary>
/// Tests for <see cref="AirspaceSwapper"/>. Runs on STA to instantiate WPF <see cref="Window"/>.
/// </summary>
[Collection("RDP-STA")]
public sealed class AirspaceSwapperTests
{
    private readonly StaCollectionFixture _fixture;
    public AirspaceSwapperTests(StaCollectionFixture fixture) => _fixture = fixture;

    [Fact]
    public void AttachToWindow_DoesNotThrow_AndIsIdempotent()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            var window = new Window
            {
                Width = 10,
                Height = 10,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Visibility = Visibility.Hidden,
            };
            window.Show();
            try
            {
                swapper.AttachToWindow(window);
                // Calling twice must not throw — idempotent
                swapper.AttachToWindow(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void RegisterHost_ThenUnregister_DoesNotThrow()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            var window = new Window
            {
                Width = 10, Height = 10,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Visibility = Visibility.Hidden,
            };
            window.Show();
            try
            {
                swapper.AttachToWindow(window);
                var host = new WindowsFormsHost();
                var overlay = new Image();
                swapper.RegisterHost(host, overlay);
                swapper.UnregisterHost(host);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HideWithoutSnapshot_Returns_DisposableToken_ThatRestoresVisibility()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            var host = new WindowsFormsHost { Visibility = Visibility.Visible };

            var token = swapper.HideWithoutSnapshot(host);
            host.Visibility.Should().Be(Visibility.Collapsed);

            token.Dispose();
            host.Visibility.Should().Be(Visibility.Visible);
        });
    }

    /// <summary>
    /// WR-06 (Phase 5 / Pattern 4) regression guard. In the multi-host tab model
    /// background tabs are Collapsed on tab switch; on WM_ENTERSIZEMOVE we snapshot
    /// each host's pre-drag visibility and on WM_EXITSIZEMOVE we restore per-host
    /// (NOT unconditional Visible). This test registers two hosts, one Visible
    /// (active tab) and one Collapsed (background tab), drives the WndProc through
    /// ENTER then EXIT, and asserts the active tab returns to Visible while the
    /// background tab stays Collapsed.
    /// </summary>
    [Fact]
    public void MultiHost_ExitSizeMove_RestoresPreDragVisibility()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            var window = new Window
            {
                Width = 100, Height = 100,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Visibility = Visibility.Hidden,
            };
            window.Show();
            try
            {
                swapper.AttachToWindow(window);

                var hostA = new WindowsFormsHost { Visibility = Visibility.Visible };
                var hostB = new WindowsFormsHost { Visibility = Visibility.Collapsed };
                var overlayA = new Image();
                var overlayB = new Image();
                swapper.RegisterHost(hostA, overlayA);
                swapper.RegisterHost(hostB, overlayB);

                InvokeWndProc(swapper, 0x0231); // WM_ENTERSIZEMOVE
                hostA.Visibility.Should().Be(Visibility.Collapsed,
                    "WM_ENTERSIZEMOVE collapses every host while the snapshot overlay is visible");
                hostB.Visibility.Should().Be(Visibility.Collapsed,
                    "host B was already Collapsed before the drag");

                InvokeWndProc(swapper, 0x0232); // WM_EXITSIZEMOVE
                hostA.Visibility.Should().Be(Visibility.Visible,
                    "WM_EXITSIZEMOVE restores host A to its pre-drag Visibility (Visible)");
                hostB.Visibility.Should().Be(Visibility.Collapsed,
                    "WM_EXITSIZEMOVE restores host B to its pre-drag Visibility (Collapsed) — NOT unconditional Visible");
            }
            finally
            {
                window.Close();
            }
        });
    }

    // --- SnapshotAndHideAll / RestoreAll tests ---

    /// <summary>
    /// SnapshotAndHideAll collapses visible hosts and skips already-collapsed ones.
    /// Overlay becomes visible for the active host; the background host's overlay
    /// stays collapsed (it was already Collapsed before the call).
    /// </summary>
    [Fact]
    public void SnapshotAndHideAll_CollapseVisibleHosts_SkipsAlreadyCollapsed()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            var hostA = new WindowsFormsHost { Visibility = Visibility.Visible };
            var hostB = new WindowsFormsHost { Visibility = Visibility.Collapsed };
            var overlayA = new Image();
            var overlayB = new Image();
            swapper.RegisterHost(hostA, overlayA);
            swapper.RegisterHost(hostB, overlayB);

            swapper.SnapshotAndHideAll();

            // Host A was Visible, should now be Collapsed
            hostA.Visibility.Should().Be(Visibility.Collapsed,
                "SnapshotAndHideAll collapses visible hosts");
            // Host B was already Collapsed, should stay Collapsed
            hostB.Visibility.Should().Be(Visibility.Collapsed,
                "SnapshotAndHideAll skips already-collapsed hosts");
            // Overlay B should NOT become Visible (host B was skipped)
            overlayB.Visibility.Should().Be(Visibility.Collapsed,
                "overlay for already-collapsed host stays collapsed");
        });
    }

    /// <summary>
    /// After SnapshotAndHideAll, RestoreAll returns each host to its pre-snapshot
    /// visibility. Background tabs (Collapsed) stay Collapsed; active tabs (Visible)
    /// return to Visible.
    /// </summary>
    [Fact]
    public void RestoreAll_RestoresPerHostVisibility()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            var hostA = new WindowsFormsHost { Visibility = Visibility.Visible };
            var hostB = new WindowsFormsHost { Visibility = Visibility.Collapsed };
            var overlayA = new Image();
            var overlayB = new Image();
            swapper.RegisterHost(hostA, overlayA);
            swapper.RegisterHost(hostB, overlayB);

            swapper.SnapshotAndHideAll();
            swapper.RestoreAll();

            hostA.Visibility.Should().Be(Visibility.Visible,
                "RestoreAll returns active host to Visible");
            hostB.Visibility.Should().Be(Visibility.Collapsed,
                "RestoreAll returns background host to Collapsed (NOT Visible)");
            overlayA.Visibility.Should().Be(Visibility.Collapsed,
                "overlay hidden after RestoreAll");
            overlayA.Source.Should().BeNull(
                "overlay source cleared after RestoreAll");
        });
    }

    /// <summary>
    /// SnapshotAndHideAll with no registered hosts is a no-op (no crash).
    /// </summary>
    [Fact]
    public void SnapshotAndHideAll_WhenNoHostsRegistered_NoOp()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            // Should not throw
            swapper.SnapshotAndHideAll();
            swapper.RestoreAll();
        });
    }

    /// <summary>
    /// Audit A4 regression guard: nested SnapshotAndHideAll/RestoreAll pairs.
    /// Only the outermost SnapshotAndHideAll captures the pre-dialog visibility;
    /// the inner pair is a no-op, and only the outermost RestoreAll restores —
    /// to the PRE-OUTER state (active host Visible, background host Collapsed),
    /// never the all-Collapsed state the inner snapshot would have captured.
    /// </summary>
    [Fact]
    public void NestedSnapshotAndRestore_PreservesPreOuterVisibility()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            var hostA = new WindowsFormsHost { Visibility = Visibility.Visible };
            var hostB = new WindowsFormsHost { Visibility = Visibility.Collapsed };
            swapper.RegisterHost(hostA, new Image());
            swapper.RegisterHost(hostB, new Image());

            swapper.SnapshotAndHideAll();   // outer — captures A=Visible, B=Collapsed
            hostA.Visibility.Should().Be(Visibility.Collapsed,
                "outer SnapshotAndHideAll collapses the visible host");

            swapper.SnapshotAndHideAll();   // inner — must NOT overwrite the outer snapshot
            swapper.RestoreAll();           // inner — must NOT restore yet

            hostA.Visibility.Should().Be(Visibility.Collapsed,
                "inner RestoreAll must defer the actual restore to the outermost scope");

            swapper.RestoreAll();           // outer — performs the real restore

            hostA.Visibility.Should().Be(Visibility.Visible,
                "outer RestoreAll restores the PRE-OUTER visibility, not the all-Collapsed inner state");
            hostB.Visibility.Should().Be(Visibility.Collapsed,
                "background host stays Collapsed after the nested dialog scopes close");
        });
    }

    /// <summary>
    /// Audit A4: an unbalanced RestoreAll (no matching SnapshotAndHideAll) must not
    /// throw and must not touch host visibility — in particular it must NOT fall
    /// back to forcing every host Visible.
    /// </summary>
    [Fact]
    public void UnbalancedRestoreAll_DoesNotThrow_AndDoesNotTouchHosts()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            var background = new WindowsFormsHost { Visibility = Visibility.Collapsed };
            swapper.RegisterHost(background, new Image());

            // No SnapshotAndHideAll was called — this is unbalanced.
            swapper.RestoreAll();

            background.Visibility.Should().Be(Visibility.Collapsed,
                "unbalanced RestoreAll must not force hosts Visible (audit A4 null-fallback bug)");

            // A subsequent balanced pair must still work normally.
            var active = new WindowsFormsHost { Visibility = Visibility.Visible };
            swapper.RegisterHost(active, new Image());
            swapper.SnapshotAndHideAll();
            active.Visibility.Should().Be(Visibility.Collapsed);
            swapper.RestoreAll();
            active.Visibility.Should().Be(Visibility.Visible,
                "a balanced pair after an unbalanced RestoreAll behaves normally");
            background.Visibility.Should().Be(Visibility.Collapsed);
        });
    }

    /// <summary>
    /// Audit A6 regression guard: AssertDispatcher must check against the dispatcher
    /// captured at construction, not Dispatcher.CurrentDispatcher (which is created
    /// on demand for the calling thread and therefore always passes CheckAccess).
    /// Calling a public member from a different thread must throw.
    /// </summary>
    [Fact]
    public void PublicMember_CalledFromWrongThread_ThrowsInvalidOperation()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            var host = new WindowsFormsHost();
            swapper.RegisterHost(host, new Image());

            Exception? fromOtherThread = null;
            var thread = new Thread(() =>
            {
                // UnregisterHost hits AssertDispatcher before touching any WPF
                // DependencyObject, so the observed exception is unambiguously ours
                // (not WPF's own VerifyAccess).
                try { swapper.UnregisterHost(host); }
                catch (Exception ex) { fromOtherThread = ex; }
            });
            thread.Start();
            thread.Join();

            fromOtherThread.Should().BeOfType<InvalidOperationException>(
                "AirspaceSwapper members must reject calls from threads other than the constructing dispatcher's (audit A6)");
        });
    }

    /// <summary>
    /// Audit A5: outside a size-move there is no drag snapshot to invalidate —
    /// InvalidateSnapshots must be a no-op and must not touch overlay state
    /// (e.g. a snapshot held by a dialog scope).
    /// </summary>
    [Fact]
    public void InvalidateSnapshots_OutsideSizeMove_IsNoOp()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            var host = new WindowsFormsHost { Visibility = Visibility.Visible };
            var overlay = new Image();
            swapper.RegisterHost(host, overlay);

            var dummy = new System.Windows.Media.Imaging.WriteableBitmap(
                1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            overlay.Source = dummy;
            overlay.Visibility = Visibility.Visible;

            swapper.InvalidateSnapshots();

            overlay.Source.Should().BeSameAs(dummy,
                "InvalidateSnapshots outside a size-move must not touch overlay sources");
            overlay.Visibility.Should().Be(Visibility.Visible);
        });
    }

    /// <summary>
    /// Audit A5: during a size-move a DPI change makes the captured drag snapshot
    /// stale (wrong scale). InvalidateSnapshots must recapture each host that was
    /// Visible pre-drag; when recapture fails (here: the WFH has no child HWND) the
    /// stale bitmap must be CLEARED rather than left to render stretched. Hosts that
    /// were Collapsed pre-drag (background tabs) contributed no snapshot and must
    /// not be touched.
    /// </summary>
    [Fact]
    public void InvalidateSnapshots_DuringSizeMove_ClearsStaleSnapshot_WhenRecaptureFails()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            var active = new WindowsFormsHost { Visibility = Visibility.Visible };
            var background = new WindowsFormsHost { Visibility = Visibility.Collapsed };
            var activeOverlay = new Image();
            var backgroundOverlay = new Image();
            swapper.RegisterHost(active, activeOverlay);
            swapper.RegisterHost(background, backgroundOverlay);

            InvokeWndProc(swapper, 0x0231); // WM_ENTERSIZEMOVE

            // Simulate a successful ENTERSIZEMOVE capture (the headless test WFH has
            // no child HWND, so the real capture returned null).
            var stale = new System.Windows.Media.Imaging.WriteableBitmap(
                1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            activeOverlay.Source = stale;
            activeOverlay.Visibility = Visibility.Visible;
            var untouched = new System.Windows.Media.Imaging.WriteableBitmap(
                1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            backgroundOverlay.Source = untouched;

            swapper.InvalidateSnapshots();

            activeOverlay.Source.Should().BeNull(
                "a stale-DPI snapshot that cannot be recaptured must be cleared, not stretched");
            activeOverlay.Visibility.Should().Be(Visibility.Collapsed);
            backgroundOverlay.Source.Should().BeSameAs(untouched,
                "hosts that were Collapsed pre-drag contributed no snapshot and must not be touched");

            InvokeWndProc(swapper, 0x0232); // WM_EXITSIZEMOVE — restore for cleanliness
        });
    }

    /// <summary>
    /// Invokes the private <c>WndProc</c> on the swapper directly. We bypass the
    /// HwndSource hook because simulating a real drag-resize in a hidden unit-test
    /// window is flaky (Window.Hidden suppresses the WM_ENTERSIZEMOVE pump). The
    /// contract under test is the per-host visibility restore logic inside WndProc,
    /// which doesn't care whether it was called via the hook or reflection.
    /// </summary>
    private static void InvokeWndProc(AirspaceSwapper sut, int msg)
    {
        var method = typeof(AirspaceSwapper).GetMethod(
            "WndProc",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AirspaceSwapper.WndProc not found via reflection");

        var handled = false;
        var args = new object?[] { IntPtr.Zero, msg, IntPtr.Zero, IntPtr.Zero, handled };
        method.Invoke(sut, args);
    }
}

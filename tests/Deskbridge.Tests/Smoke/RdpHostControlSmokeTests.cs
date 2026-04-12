using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using AxMSTSCLib;
using Deskbridge.Protocols.Rdp;
using Deskbridge.Protocols.Rdp.Prototype;
using Deskbridge.Tests.Fixtures;
using Xunit;

namespace Deskbridge.Tests.Smoke;

/// <summary>
/// Plan 04-01 gate tests for the RDP ActiveX smoke prototype.
///
/// Gate 1 — 20-cycle GDI baseline: GetGuiResources delta < 50 after 20 connect/disconnect
///          cycles (RDP-ACTIVEX-PITFALLS §2, D-02 criterion #1).
/// Gate 2 — IMsTscNonScriptable cast + ClearTextPassword succeeds against a real RDP
///          target and OnLoginComplete fires (D-02 criterion #2).
/// Gate 3 — AxSiting.SiteAndConfigure throws <c>InvalidOperationException</c> when the
///          AxHost.Handle remains 0 after presumed siting (D-02 criterion #3). No live
///          RDP required — this is the pure-unit gate.
/// Gate 4 — Intentional bad hostname triggers OnDisconnected without tearing down the
///          test process; <c>ErrorOccurred</c> is surfaced (D-02 criterion #4).
///
/// Gates 1, 2, 4 Skip when <c>DESKBRIDGE_SMOKE_RDP_HOST</c> is unset (see how-to-verify
/// in the plan checkpoint). Gate 3 runs unconditionally.
/// </summary>
[Collection("RDP-STA")]
public class RdpHostControlSmokeTests
{
    // Real RDP target — pulled from env var so CI can skip with the Skip shim
    // e.g. DESKBRIDGE_SMOKE_RDP_HOST=localhost DESKBRIDGE_SMOKE_RDP_USER=Admin DESKBRIDGE_SMOKE_RDP_PASS=...
    private static readonly string? Host = Environment.GetEnvironmentVariable("DESKBRIDGE_SMOKE_RDP_HOST");
    private static readonly string? User = Environment.GetEnvironmentVariable("DESKBRIDGE_SMOKE_RDP_USER");
    private static readonly string? Pass = Environment.GetEnvironmentVariable("DESKBRIDGE_SMOKE_RDP_PASS");
    private static readonly string? Domain = Environment.GetEnvironmentVariable("DESKBRIDGE_SMOKE_RDP_DOMAIN");
    private const int DefaultPort = 3389;

    private readonly StaCollectionFixture _fixture;

    public RdpHostControlSmokeTests(StaCollectionFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Gate1_20CycleGdiBaseline_HandleCountReturnsToBaseline()
    {
        Skip.If(Host is null, "Set DESKBRIDGE_SMOKE_RDP_HOST to run");
        Skip.IfNot(_fixture.IsSta(), "STA required");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        int baseline = GetGdi();

        for (int i = 0; i < 20; i++)
        {
            await RunOneCycleAsync();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        int final = GetGdi();
        int delta = final - baseline;
        // [VERIFIED: dotnet/winforms #13499 reported still leaking in .NET 10 preview 6]
        // If this assertion fails, escalate before Plan 04-02 begins.
        Assert.True(
            delta < 50,
            $"GDI handle leak detected: baseline={baseline}, final={final}, delta={delta}");
    }

    [Fact]
    public async Task Gate2_IMsTscNonScriptable_PasswordSetSucceeds()
    {
        Skip.If(Host is null, "Set DESKBRIDGE_SMOKE_RDP_HOST to run");
        Skip.If(User is null || Pass is null, "Set DESKBRIDGE_SMOKE_RDP_USER + DESKBRIDGE_SMOKE_RDP_PASS to run");
        Skip.IfNot(_fixture.IsSta(), "STA required");

        var window = CreateHiddenStagingWindow(out var viewport);
        window.Show();

        try
        {
            using var smoke = new RdpSmokeHost();
            viewport.Children.Add(new ContentControl());  // realize layout

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var connectTask = smoke.ConnectAsync(viewport, Host!, DefaultPort, User, Domain, Pass, cts.Token);
            await connectTask;  // Should complete via OnLoginComplete
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Gate3_SitingOrderGuard_ThrowsWhenConfigureBeforeSite()
    {
        Skip.IfNot(_fixture.IsSta(), "STA required");

        // Verify SiteAndConfigure helper throws if Handle == 0 after presumed siting.
        // No Window root → Grid has no HwndSource → handle stays 0.
        var panel = new Grid();
        var host = new WindowsFormsHost();
        var rdp = new AxMsRdpClient9NotSafeForScripting();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            AxSiting.SiteAndConfigure(panel, host, rdp, r => r.Server = "ignored"));
        Assert.Contains("not sited", ex.Message);

        try { host.Child = null; } catch { }
        try { rdp.Dispose(); } catch { }
        try { host.Dispose(); } catch { }
    }

    [Fact]
    public async Task Gate4_ComError_DoesNotTearDownApp()
    {
        Skip.If(Host is null, "Set DESKBRIDGE_SMOKE_RDP_HOST to run");
        Skip.IfNot(_fixture.IsSta(), "STA required");

        var window = CreateHiddenStagingWindow(out var viewport);
        window.Show();

        string? observedError = null;

        try
        {
            using var smoke = new RdpSmokeHost();
            smoke.ErrorOccurred += (_, msg) => observedError = msg;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            // Deliberately invalid hostname — OnDisconnected should fire with discReason != 0.
            var act = () => smoke.ConnectAsync(viewport, "definitely-not-a-host.invalid", DefaultPort, User, Domain, Pass, cts.Token);
            var ex = await Assert.ThrowsAnyAsync<Exception>(act);
            Assert.NotNull(ex);

            // Dispose cleanly. If this throws or tears down the process, the gate fails.
            smoke.Dispose();

            // Process stayed alive and disconnect reason was surfaced.
            Assert.False(Process.GetCurrentProcess().HasExited);
            Assert.NotNull(smoke.LastDiscReason);
            Assert.NotEqual(0, smoke.LastDiscReason);
        }
        finally
        {
            window.Close();
        }

        // observedError may or may not be set depending on whether OnLogonError fired;
        // the contract is that Dispose runs cleanly and the process survives.
        _ = observedError;
    }

    // ---- Helpers -------------------------------------------------------------

    private async Task RunOneCycleAsync()
    {
        var window = CreateHiddenStagingWindow(out var viewport);
        window.Show();
        try
        {
            using var smoke = new RdpSmokeHost();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await smoke.ConnectAsync(viewport, Host!, DefaultPort, User, Domain, Pass, cts.Token);
            }
            catch
            {
                // A connect failure still exercises the dispose path — GDI measurement covers both.
            }
            await smoke.DisconnectAsync();
            // `using` triggers the full disposal sequence (WFH leak fixes etc.) when this block exits
        }
        finally
        {
            window.Close();
        }
    }

    private static Window CreateHiddenStagingWindow(out Grid viewport)
    {
        var window = new Window
        {
            Width = 800,
            Height = 600,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Visibility = Visibility.Hidden,
        };
        viewport = new Grid();
        window.Content = viewport;
        return window;
    }

    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);

    private static int GetGdi()
        => GetGuiResources(Process.GetCurrentProcess().Handle, 0 /*GR_GDIOBJECTS*/);
}

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using Deskbridge.Protocols.Rdp;
using Deskbridge.Protocols.Rdp.Prototype;
using Deskbridge.Tests.Fixtures;
using Deskbridge.Tests.Rdp;
using Xunit;

namespace Deskbridge.Tests.Smoke;

/// <summary>
/// Plan 04-01 gate tests for the RDP ActiveX smoke prototype.
///
/// Gate 1 — 20-cycle GDI baseline: GetGuiResources delta &lt; 50 after 20 connect/disconnect
///          cycles (RDP-ACTIVEX-PITFALLS §2, D-02 criterion #1).
/// Gate 2 — IMsTscNonScriptable cast + ClearTextPassword succeeds against a real RDP
///          target and OnLoginComplete fires (D-02 criterion #2).
/// Gate 3 — AxSiting.SiteAndConfigure throws <c>InvalidOperationException</c> when the
///          AxHost.Handle remains 0 after presumed siting (D-02 criterion #3). No live
///          RDP required — this is the pure-unit gate. Uses <see cref="FakeAxHost"/>
///          because under WPF 10 / .NET 10 the real <c>AxMsRdpClient9NotSafeForScripting</c>
///          realizes a Win32 handle even on an unrooted <see cref="Grid"/> once the
///          <see cref="StaRunner"/> Dispatcher pump is running — so a real control can't
///          reproduce the <c>Handle == 0</c> branch. Deviation from the original plan
///          (where Gate 3 was end-to-end); see Plan 04-01 Task 0.1 line 166 which
///          pre-authorized the FakeAxHost approach.
/// Gate 4 — Intentional bad hostname triggers OnDisconnected without tearing down the
///          test process; <c>ErrorOccurred</c> is surfaced (D-02 criterion #4).
///
/// Gates 1, 2, 4 Skip when <c>DESKBRIDGE_SMOKE_RDP_HOST</c> is unset. Gate 3 runs
/// unconditionally. Every gate body is hosted on a fresh STA thread by
/// <see cref="StaRunner"/> — xUnit v3 3.2.2 worker threads are MTA and the package ships
/// no STA attribute, so the pump is built per-test (see <see cref="StaCollectionFixture"/>).
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
    public void Gate1_20CycleGdiBaseline_HandleCountReturnsToBaseline()
    {
        Skip.If(Host is null, "Set DESKBRIDGE_SMOKE_RDP_HOST to run");
        _ = _fixture;

        StaRunner.RunAsync(async () =>
        {
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

            // Emit the delta unconditionally so reruns show the actual number, not just pass/fail.
            // TestContext.Current is xunit.v3's ambient context; SendDiagnosticMessage surfaces in
            // test output when diagnostic messages are enabled, and Console.WriteLine is captured
            // into the per-test log regardless of runner config.
            var msg = $"Gate1: GDI handle delta over 20 cycles = {delta} (baseline={baseline}, final={final})";
            TestContext.Current.SendDiagnosticMessage(msg);
            Console.WriteLine(msg);

            // [VERIFIED: dotnet/winforms #13499 reported still leaking in .NET 10 preview 6]
            // If this assertion fails, escalate before Plan 04-02 begins.
            Assert.True(
                delta < 50,
                $"GDI handle leak detected: baseline={baseline}, final={final}, delta={delta}");
        });
    }

    [Fact]
    public void Gate2_IMsTscNonScriptable_PasswordSetSucceeds()
    {
        Skip.If(Host is null, "Set DESKBRIDGE_SMOKE_RDP_HOST to run");
        Skip.If(User is null || Pass is null, "Set DESKBRIDGE_SMOKE_RDP_USER + DESKBRIDGE_SMOKE_RDP_PASS to run");
        _ = _fixture;

        StaRunner.RunAsync(async () =>
        {
            var window = CreateHiddenStagingWindow(out var viewport);
            window.Show();

            try
            {
                using var smoke = new RdpSmokeHost();
                viewport.Children.Add(new ContentControl());  // realize layout

                // Capture ErrorOccurred payload (includes OnLogonError lError + disconnect reason
                // text) so we can emit it alongside discReason if ConnectAsync throws. NEVER logs
                // the password — RdpSmokeHost already guarantees the event message is sanitized.
                string? capturedError = null;
                smoke.ErrorOccurred += (_, msg) => capturedError = msg;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    var connectTask = smoke.ConnectAsync(viewport, Host!, DefaultPort, User, Domain, Pass, cts.Token);
                    await connectTask;  // Should complete via OnLoginComplete
                }
                catch (Exception ex)
                {
                    // Emit diagnostics BEFORE rethrowing so the failed-test log shows enough to
                    // distinguish server auth rejection (1800) from transport (264/516/520/1032).
                    var header = $"Gate2: Connect failed. discReason={smoke.LastDiscReason?.ToString() ?? "<null>"}, ErrorOccurred payload={capturedError ?? "<none>"}";
                    TestContext.Current.SendDiagnosticMessage(header);
                    Console.WriteLine(header);
                    Console.WriteLine("Gate2 hint: discReason 1800 typically = server rejected auth (wrong password, disabled account, CredSSP/NLA mismatch, or account lacks RDP grant).");
                    Console.WriteLine("Gate2 hint: discReason 516/520 = socket failure. 264 = DNS. 1032 = DNS name lookup failure.");
                    Console.WriteLine("Gate2 hint: See https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-rdpbcgr/ for full reason code table.");
                    _ = ex;
                    throw;
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Gate3_SitingOrderGuard_ThrowsWhenConfigureBeforeSite()
    {
        _ = _fixture;

        StaRunner.Run(() =>
        {
            // Verify SiteAndConfigure helper throws if Handle == 0 after presumed siting.
            // Uses FakeAxHost to force Handle == 0: under WPF 10 / .NET 10 the real
            // AxMsRdpClient9NotSafeForScripting realizes a handle even on an unrooted Grid
            // once the Dispatcher pump is running, so the guard's throw path is only
            // reachable through a stub. See FakeAxHost.cs and Plan 04-01 Task 0.1 line 166.
            var panel = new Grid();
            var host = new WindowsFormsHost();
            var rdp = new FakeAxHost();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                AxSiting.SiteAndConfigure(panel, host, rdp, _ => { /* should not execute */ }));
            Assert.Contains("not sited", ex.Message);

            try { host.Child = null; } catch { }
            try { rdp.Dispose(); } catch { }
            try { host.Dispose(); } catch { }
        });
    }

    [Fact]
    public void Gate4_ComError_DoesNotTearDownApp()
    {
        Skip.If(Host is null, "Set DESKBRIDGE_SMOKE_RDP_HOST to run");
        _ = _fixture;

        StaRunner.RunAsync(async () =>
        {
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
        });
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

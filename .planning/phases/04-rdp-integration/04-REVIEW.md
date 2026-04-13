---
phase: 04-rdp-integration
reviewed: 2026-04-12T00:00:00Z
depth: standard
files_reviewed: 49
files_reviewed_list:
  - src/Deskbridge.Core/Events/ConnectionEvents.cs
  - src/Deskbridge.Core/Exceptions/RdpConnectFailedException.cs
  - src/Deskbridge.Core/Interfaces/IConnectionCoordinator.cs
  - src/Deskbridge.Core/Interfaces/IProtocolHost.cs
  - src/Deskbridge.Core/Interfaces/IProtocolHostFactory.cs
  - src/Deskbridge.Core/Pipeline/Stages/ConnectStage.cs
  - src/Deskbridge.Core/Pipeline/Stages/CreateHostStage.cs
  - src/Deskbridge.Core/Pipeline/Stages/DisconnectStage.cs
  - src/Deskbridge.Core/Pipeline/Stages/DisposeStage.cs
  - src/Deskbridge.Core/Pipeline/Stages/PublishClosedEventStage.cs
  - src/Deskbridge.Core/Pipeline/Stages/ResolveCredentialsStage.cs
  - src/Deskbridge.Core/Pipeline/Stages/UpdateRecentsStage.cs
  - src/Deskbridge.Core/Services/ConnectionCoordinator.cs
  - src/Deskbridge.Core/Services/DisconnectReasonClassifier.cs
  - src/Deskbridge.Core/Services/RdpReconnectCoordinator.cs
  - src/Deskbridge.Core/Services/ReconnectUiRequest.cs
  - src/Deskbridge.Core/Services/WindowsCredentialService.cs
  - src/Deskbridge.Core/Models/ConnectionModel.cs
  - src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs
  - src/Deskbridge.Protocols.Rdp/AxSiting.cs
  - src/Deskbridge.Protocols.Rdp/Prototype/RdpSmokeHost.cs
  - src/Deskbridge.Protocols.Rdp/RdpConnectionConfigurator.cs
  - src/Deskbridge.Protocols.Rdp/RdpHostControl.cs
  - src/Deskbridge.Protocols.Rdp/RdpProtocolHostFactory.cs
  - src/Deskbridge/App.xaml.cs
  - src/Deskbridge/Converters/EnumToVisibilityConverter.cs
  - src/Deskbridge/MainWindow.xaml
  - src/Deskbridge/MainWindow.xaml.cs
  - src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs
  - src/Deskbridge/ViewModels/ReconnectOverlayViewModel.cs
  - src/Deskbridge/Views/ReconnectOverlay.xaml
  - src/Deskbridge/Views/ReconnectOverlay.xaml.cs
  - src/Deskbridge/Views/ConnectionTreeControl.xaml.cs
  - tests/Deskbridge.Tests/Coordinator/ConnectionCoordinatorTests.cs
  - tests/Deskbridge.Tests/Pipeline/ConnectStageTests.cs
  - tests/Deskbridge.Tests/Pipeline/ConnectionPipelineIntegrationTests.cs
  - tests/Deskbridge.Tests/Pipeline/CreateHostStageTests.cs
  - tests/Deskbridge.Tests/Pipeline/DisconnectStageTests.cs
  - tests/Deskbridge.Tests/Pipeline/DisposeStageTests.cs
  - tests/Deskbridge.Tests/Pipeline/PublishClosedEventStageTests.cs
  - tests/Deskbridge.Tests/Pipeline/ResolveCredentialsStageTests.cs
  - tests/Deskbridge.Tests/Pipeline/UpdateRecentsStageTests.cs
  - tests/Deskbridge.Tests/Fixtures/Skip.cs
  - tests/Deskbridge.Tests/Fixtures/StaCollectionFixture.cs
  - tests/Deskbridge.Tests/Rdp/AirspaceSwapperTests.cs
  - tests/Deskbridge.Tests/Rdp/DisconnectReasonClassifierTests.cs
  - tests/Deskbridge.Tests/Rdp/ErrorIsolationTests.cs
  - tests/Deskbridge.Tests/Rdp/FakeAxHost.cs
  - tests/Deskbridge.Tests/Rdp/RdpHostControlShapeTests.cs
  - tests/Deskbridge.Tests/Rdp/RdpReconnectCoordinatorTests.cs
  - tests/Deskbridge.Tests/Rdp/SitingGuardTests.cs
  - tests/Deskbridge.Tests/Security/ErrorSanitizationTests.cs
  - tests/Deskbridge.Tests/Security/InMemorySink.cs
  - tests/Deskbridge.Tests/Security/PasswordLeakTests.cs
  - tests/Deskbridge.Tests/Smoke/RdpHostControlSmokeTests.cs
  - tests/Deskbridge.Tests/ViewModels/ReconnectOverlayViewModelTests.cs
findings:
  critical: 0
  warning: 5
  info: 8
  total: 13
status: issues_found
---

# Phase 4: Code Review Report

**Reviewed:** 2026-04-12
**Depth:** standard
**Files Reviewed:** 49 (production + test)
**Status:** issues_found

## Summary

Phase 4 ("RDP Integration") delivers the RDP pipeline + COM/ActiveX lifecycle per REFERENCE.md and the RDP-ACTIVEX-PITFALLS doc. Overall quality is high: the strict disposal sequence is in place with both reflection-based WFH leak fixes, COM releases use `FinalReleaseComObject` on `GetOcx()` only (never `ReleaseComObject`), STA assertions guard every dangerous entry point, and the password surface is strongly sanitized (JsonIgnore + defensive `context.ResolvedPassword = null` after write + regression tests that fail if "Hunter2" appears in any sink). Mandatory project constraints were all respected: no `SecureString`, no `[GeneratedComInterface]`, no `<LangVersion>preview</LangVersion>`, `CredentialType.Windows` on TERMSRV/* with legacy Generic fallback.

No Critical findings. Five Warnings cluster around two themes: (1) a latent race between the replacement-disconnect and the new `HostCreatedEvent` that can leave the old WFH mounted in `ViewportGrid` and (2) small robustness gaps in disposal/subscription handoffs. The Info items are mostly cleanup opportunities (dead field `_suppressedHost`, inconsistent exception sanitization between ConnectStage and `RunConnectSafely`, `RdpSmokeHost.ErrorOccurred` embedding `ex.Message` from COMException which may contravene the project-wide T-04-EXC rule even in a prototype).

All rollback fixes from the live-verification run (commits `6abbdb1`, `936ffd9`, `3941153`, `406c97d`, `a48b263`, `ea03486`) are correctly applied and I looked specifically at each. The `[diag]` Info log in `RdpHostControl.ConnectAsync` is flagged at Info severity only as previously agreed.

## Warnings

### WR-01: Replace-active-host race can leave stale WindowsFormsHost mounted

**File:** `src/Deskbridge.Core/Services/ConnectionCoordinator.cs:93-107`, with companion at `:292-309`
**Issue:**
When the user switches from connection A (active) to connection B, `OnConnectionRequested` fires `RunDisconnectSafely(A)` and `RunConnectSafely(B)` concurrently (both are fire-and-forget). Two interleavings exist:

1. **A's disconnect pipeline completes first** → `OnConnectionClosed(A)` runs while `_active` still points to A → `HostUnmounted(A)` fires → MainWindow removes A's WFH → B's `HostCreatedEvent` then sets `_active = B` and mounts B. Correct.
2. **B's `HostCreatedEvent` fires first** (typical, because `CreateHostStage` is synchronous while A's disconnect awaits `DisconnectAsync`) → `_active` is overwritten to B → `HostMounted(B)` mounts B's WFH alongside A's still-live WFH → later, A's disconnect pipeline publishes `ConnectionClosedEvent(A)` → `OnConnectionClosed` sees `active.Model.Id != A.Id` and **no-ops**. A's host is disposed by `DisposeStage` but its WFH stays parented in `ViewportGrid`, covering / overlapping B's viewport until the Window closes.

This also orphans the `DisconnectedAfterConnect += OnDisconnectedAfterConnect` subscription that `OnHostCreated` wired on A: `_active` is overwritten without first unsubscribing from A, so if A fires a late post-login `OnDisconnected` it will be rejected by the `ReferenceEquals(active.Host, host)` guard at line 328 but the event delivery path is still live until A.Dispose() actually tears down the ActiveX.

**Fix:**
In `OnHostCreated`, when `_active` is non-null and its host is not the incoming `evt.Host`, unmount + unsubscribe the old host explicitly before overwriting. Alternatively, track unmount per-host rather than per-active-slot:

```csharp
private void OnHostCreated(HostCreatedEvent evt)
{
    if (_disposed) return;
    if (!_dispatcher.CheckAccess())
    {
        _dispatcher.Invoke(() => OnHostCreated(evt));
        return;
    }

    // If a previous host is still tracked, hand it off cleanly before replacing.
    if (_active is { } previous && !ReferenceEquals(previous.Host, evt.Host))
    {
        try { previous.Host.DisconnectedAfterConnect -= OnDisconnectedAfterConnect; }
        catch { /* disposed host may throw */ }
        HostUnmounted?.Invoke(this, previous.Host);  // MainWindow removes WFH
    }

    _active = (evt.Host, evt.Connection);
    evt.Host.DisconnectedAfterConnect += OnDisconnectedAfterConnect;
    HostMounted?.Invoke(this, evt.Host);
}
```

Alternatively, have the replacement path in `OnConnectionRequested` `await RunDisconnectSafely` before calling `_ = RunConnectSafely`. Awaiting requires making the handler async-void or promoting it to `async Task` and queueing from the dispatcher. The non-awaiting fix above is simpler and preserves the fire-and-forget semantics.

---

### WR-02: MainWindow.OnHostMounted/OnHostUnmounted does not guard viewport ordering on rapid swap

**File:** `src/Deskbridge/MainWindow.xaml.cs:78-102`
**Issue:**
`OnHostMounted` unconditionally calls `ViewportGrid.Children.Add(rdp.Host)`. If WR-01 fires (stale WFH still in the tree), two WFHs are parented. `OnHostUnmounted` only removes the specific host, so the stale one lingers. Compounds WR-01's effect.

**Fix:**
Defensive removal at the top of `OnHostMounted`: walk any existing `WindowsFormsHost` children and remove them before adding the new host. This also avoids the recoverable case where a disposed host's WFH (post-OnConnectionFailed) is still in the tree when a new connect is requested.

```csharp
private void OnHostMounted(object? sender, IProtocolHost host)
{
    if (host is not RdpHostControl rdp) return;
    Dispatcher.Invoke(() =>
    {
        CloseOverlay();

        // Defense-in-depth: remove any stale WFH children before adding the new one.
        // Guards against the replacement-race in ConnectionCoordinator.OnHostCreated.
        for (int i = ViewportGrid.Children.Count - 1; i >= 0; i--)
        {
            if (ViewportGrid.Children[i] is System.Windows.Forms.Integration.WindowsFormsHost stale)
            {
                ViewportGrid.Children.RemoveAt(i);
            }
        }

        ViewportGrid.Children.Add(rdp.Host);
        ViewportGrid.UpdateLayout();
        _airspace.RegisterHost(rdp.Host, ViewportSnapshot);
        _activeRdpHost = rdp;
    });
}
```

---

### WR-03: AirspaceSwapper.HideWithoutSnapshot RestoreVisibilityToken promotes Collapsed-before to Visible-after

**File:** `src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs:200-216`
**Issue:**
The token's `Dispose` restores with:
```csharp
_host.Visibility = _previous == Visibility.Collapsed ? Visibility.Visible : _previous;
```
If the caller already had `_host.Visibility == Collapsed` for a legitimate reason (e.g., the drag/resize handler had just collapsed it via `WM_ENTERSIZEMOVE`, or the D-12 single-host policy had collapsed it prior to reconnect), dispose will flip it back to `Visible`. That subtly races with the reconnect overlay path: if the reconnect overlay closes while a drag is in progress, the WFH becomes `Visible` before the drag handler's snapshot overlay is hidden, and the user briefly sees the live RDP surface on top of the snapshot. Edge case but observable during rapid user interaction.

**Fix:**
Restore to the actual captured previous value, not a forced `Visible`:
```csharp
public void Dispose()
{
    if (_disposed) return;
    _disposed = true;
    _host.Visibility = _previous;  // Preserve whatever the caller had before HideWithoutSnapshot
}
```
If the intent was to handle the case where the WFH was `Collapsed` at capture time (so Dispose should force it back up), then the caller should assert preconditions — but collapsing a collapsed WFH and then "restoring" to Visible violates the principle of least surprise. The existing code comment claims "previous" semantics; the implementation doesn't match.

---

### WR-04: RdpSmokeHost.ErrorOccurred payloads embed COM exception message

**File:** `src/Deskbridge.Protocols.Rdp/Prototype/RdpSmokeHost.cs:132, 138, 144, 159, 164`
**Issue:**
Five `ErrorOccurred?.Invoke(this, "...: " + ex.Message)` call sites. T-04-EXC requires COM exception logs to show type + HResult only because some COM implementations have been observed to embed credentials in `ex.Message`. Gate 2 of the smoke tests (`RdpHostControlSmokeTests`) subscribes to `ErrorOccurred` and routes the payload through `TestContext.SendDiagnosticMessage` + `Console.WriteLine` — the test output is a sink. The prototype's class-level comment claims "Message NEVER contains the password" but that guarantee isn't enforced; it depends on whatever the underlying COM layer happens to write.

Also, this is documented as a "Phase 4-only throwaway prototype" — but while it's checked in, it's one of the surfaces PasswordLeakTests wouldn't cover if someone pastes a leaking ex.Message through ErrorOccurred.

**Fix:**
Mirror the sanitization `RdpHostControl.ConnectAsync` uses (line 134):
```csharp
catch (COMException ex)
{
    ErrorOccurred?.Invoke(this, $"ClearTextPassword failed: {ex.GetType().Name} HResult=0x{ex.HResult:X8}");
    _loginTcs.TrySetException(ex);
    return _loginTcs.Task;
}
```
Apply the same pattern to lines 132, 138, 144, 159, 164. Lines 308-309 also embed discReason — that value is safe by design — but change the wording for consistency ("OnDisconnected during connect" is fine; no ex.Message).

---

### WR-05: ConnectionCoordinator.RunConnectSafely / RunDisconnectSafely include ex.Message for non-sensitive exceptions

**File:** `src/Deskbridge.Core/Services/ConnectionCoordinator.cs:123-135, 147-155`
**Issue:**
The new sanitization helper:
```csharp
var safeMessage = ex is COMException or ExternalException
                    or AuthenticationException or WebException
    ? "<redacted: sensitive exception type>"
    : ex.Message;
_logger.LogError(..., "... Message={Message}", ..., safeMessage);
```
The allow-list is too narrow. `SocketException`, `SqlException` (unlikely here but architecturally possible), `HttpRequestException`, `SecurityException`, and most importantly **`InvalidOperationException`** (thrown by RdpHostControl with messages containing RDP-specific state text) are not on the sensitive list and so get their `ex.Message` logged. The RDP-ACTIVEX-PITFALLS §3 contract is "type + HResult only for *any* COM-family exception," but the allow-list here excludes anything that inherits from `Exception` but isn't an `ExternalException` subclass. In particular `AxHost.InvalidActiveXStateException` derives from `InvalidOperationException` — its message can contain ActiveX state detail that is not credential material but also isn't documented as sanitized.

This is narrower than the ConnectStage catch filter (`TimeoutException or COMException or OperationCanceledException`) which never logs ex.Message.

**Fix:**
Invert to a narrow allow-list of exception types whose messages are known-safe, and default-redact everything else:
```csharp
var safeMessage = ex is TimeoutException or ArgumentException or OperationCanceledException
    ? ex.Message
    : $"<redacted: {ex.GetType().Name}>";
```
Or unconditionally log type + HResult only (matching ConnectStage). The "safe message" branch is a defense-in-depth net, not primary signal.

---

## Info

### IN-01: Dead field `_suppressedHost` in ConnectionCoordinator

**File:** `src/Deskbridge.Core/Services/ConnectionCoordinator.cs:29, 339, 430, 454, 496`
**Issue:**
`_suppressedHost` is written at four call sites but never read. It looks like a leftover from an earlier iteration where the coordinator was going to suppress a specific host's `HostUnmounted` during reconnect episodes (the comment at line 336 hints at this intent). The current design uses `_active = null` as the sentinel instead, which makes `_suppressedHost` dead state.

**Fix:**
Remove the field and the four assignments. If the original intent is needed later (e.g., overlay needs to distinguish "host X is dead, don't emit Unmounted" from "host X is live"), reintroduce with an actual reader.

---

### IN-02: RdpHostControl.Dispose re-unsubscribes an already-torn-down handler

**File:** `src/Deskbridge.Protocols.Rdp/RdpHostControl.cs:223-225`
**Issue:**
```csharp
_rdp.OnLoginComplete -= OnLoginComplete;
_rdp.OnDisconnected -= OnDisconnectedDuringConnect;
_rdp.OnDisconnected -= OnDisconnectedAfterConnectHandler;
```
Line 319-320 already subscribes `OnDisconnectedAfterConnectHandler` and unsubscribes `OnDisconnectedDuringConnect` on `OnLoginComplete`. The Dispose tries both unsubscribes for safety (correct) — but if the COM object is mid-teardown the three remove calls are three separate risk surfaces. They're wrapped in a single try/catch, so one throw skips the rest. Consider three separate try/catch blocks so a partial failure doesn't skip downstream unsubscribes.

**Fix (optional):**
```csharp
try { _rdp.OnLoginComplete -= OnLoginComplete; } catch { }
try { _rdp.OnDisconnected -= OnDisconnectedDuringConnect; } catch { }
try { _rdp.OnDisconnected -= OnDisconnectedAfterConnectHandler; } catch { }
try { _rdp.OnLogonError -= OnLogonError; } catch { }
```
This matches the per-call idiom used in the KeyboardInputSite reflection fix section below.

---

### IN-03: `[diag]` Info-level log line in RdpHostControl should be Debug

**File:** `src/Deskbridge.Protocols.Rdp/RdpHostControl.cs:110-118`
**Issue:**
Per the phase-4 instructions, `[diag]` at Info is acceptable during the debugging phase but should be demoted to Debug before shipping v1. Same pattern may exist in AirspaceSwapper (line 137, 148) but those are already LogDebug — good.

**Fix:**
Demote to `_logger.LogDebug` once the diagnostic value has been captured, or gate behind a config flag. Left as Info pending the T-04 verification pass per instructions.

---

### IN-04: DisconnectReasonClassifier.Describe swallows all exceptions silently

**File:** `src/Deskbridge.Core/Services/DisconnectReasonClassifier.cs:91-102`
**Issue:**
```csharp
catch
{
    // COM call may throw mid-teardown. Fall through to fallback text.
}
```
Empty catch — standard for a defensive describer. Consider logging at Debug level so "why did my reason-text go generic?" is traceable in troubleshooting scenarios. Not security-relevant (the describer never sees credentials).

**Fix:**
Pass an optional ILogger or accept that this is intentional and document the decision.

---

### IN-05: WindowsCredentialService.DeleteForConnection swallows all exceptions silently

**File:** `src/Deskbridge.Core/Services/WindowsCredentialService.cs:62-73`
**Issue:**
Two empty catch blocks around RemoveCredentials. The "credential may not exist" case is expected but "credential manager API returned an unexpected HResult" is also swallowed. If the user is deleting a connection and expects the saved password to be gone, a failure here is invisible.

**Fix:**
Distinguish "not found" from "other error" — AdysTech.CredentialManager throws `CredentialAPIException` with specific HResults. Log the non-not-found cases at Warning:
```csharp
try { CredentialManager.RemoveCredentials(target, RdpTargetType); }
catch (Exception ex) when (IsNotFound(ex)) { /* expected */ }
catch (Exception ex)
{
    Log.Warning(ex, "Failed to delete RDP credentials for {Target}", target);
}
```

---

### IN-06: ConnectionCoordinator never unsubscribes from `ReconnectOverlayHandle.CancelRequested`

**File:** `src/Deskbridge.Core/Services/ConnectionCoordinator.cs:365-368`
**Issue:**
```csharp
handle.CancelRequested += (_, _) => { try { _reconnectCts?.Cancel(); } catch { } };
```
The anonymous handler captures `_reconnectCts`. If the same `ReconnectOverlayHandle` were reused across episodes (it isn't today — a fresh handle is made per episode), the handler would stack up. Not a bug today but brittle if the design evolves.

**Fix:**
Either document that handles are single-use (add a comment), or assign `CancelRequested` to a private method so unsubscribe is clean if needed later. Also `WireManualHandlers` at line 434-455 attaches handlers to the handle's events without any unsubscribe path — same story, the handle is disposed-of by garbage collection. Acceptable per current design.

---

### IN-07: EnumToVisibilityConverter.ConvertBack throws NotSupportedException

**File:** `src/Deskbridge/Converters/EnumToVisibilityConverter.cs:22-23`
**Issue:**
`ConvertBack` throwing is correct for one-way bindings (the converter is used in `ReconnectOverlay.xaml` for read-only visibility). WPF may call `ConvertBack` on `TwoWay` bindings silently — if someone adds a TwoWay binding later, the throw kills the UI thread's binding update. Consider returning `DependencyProperty.UnsetValue` instead:

**Fix:**
```csharp
public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    => DependencyProperty.UnsetValue;
```

---

### IN-08: RdpReconnectCoordinator.RunAsync lacks `await notifyAttempt` error handling

**File:** `src/Deskbridge.Core/Services/RdpReconnectCoordinator.cs:89`
**Issue:**
```csharp
await notifyAttempt(attempt, delay);
```
If the overlay ViewModel's UpdateAttempt throws (e.g., the VM has been disposed, or a subsequent `OnPropertyChanged` binding has an exception), the entire reconnect loop aborts and propagates through `RunAutoReconnectAsync` → `ConnectionClosedEvent(Error)`. Probably desirable (unknown UI state shouldn't hide failure from user), but worth documenting. Consider try/catch around the notify call and continue-with-retry — the loop's job is to drive reconnects, not to crash on notify failures.

**Fix (optional):**
```csharp
try { await notifyAttempt(attempt, delay); }
catch (Exception ex)
{
    // Don't let notifier failures kill the retry loop.
    // Caller can wrap notifyAttempt with its own logging if needed.
}
```

---

_Reviewed: 2026-04-12_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_

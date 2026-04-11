---
phase: 01-foundation
reviewed: 2026-04-11T00:00:00Z
depth: standard
files_reviewed: 34
files_reviewed_list:
  - src/Deskbridge.Core/Interfaces/IEventBus.cs
  - src/Deskbridge.Core/Interfaces/IConnectionPipeline.cs
  - src/Deskbridge.Core/Interfaces/IDisconnectPipeline.cs
  - src/Deskbridge.Core/Interfaces/IConnectionQuery.cs
  - src/Deskbridge.Core/Interfaces/INotificationService.cs
  - src/Deskbridge.Core/Interfaces/IProtocolHost.cs
  - src/Deskbridge.Core/Interfaces/IConnectionStore.cs
  - src/Deskbridge.Core/Models/ConnectionModel.cs
  - src/Deskbridge.Core/Models/ConnectionGroup.cs
  - src/Deskbridge.Core/Models/ConnectionFilter.cs
  - src/Deskbridge.Core/Models/Enums.cs
  - src/Deskbridge.Core/Events/ConnectionEvents.cs
  - src/Deskbridge.Core/Events/AppEvents.cs
  - src/Deskbridge.Core/Pipeline/ConnectionContext.cs
  - src/Deskbridge.Core/Pipeline/DisconnectContext.cs
  - src/Deskbridge.Core/Pipeline/PipelineResult.cs
  - src/Deskbridge.Core/Pipeline/ConnectionPipeline.cs
  - src/Deskbridge.Core/Pipeline/DisconnectPipeline.cs
  - src/Deskbridge.Core/Services/EventBus.cs
  - src/Deskbridge.Core/Services/NotificationService.cs
  - src/Deskbridge.Core/Services/ConnectionQueryService.cs
  - src/Deskbridge/App.xaml.cs
  - src/Deskbridge/Program.cs
  - src/Deskbridge/MainWindow.xaml.cs
  - src/Deskbridge/ViewModels/MainWindowViewModel.cs
  - tests/Deskbridge.Tests/EventBusTests.cs
  - tests/Deskbridge.Tests/ConnectionPipelineTests.cs
  - tests/Deskbridge.Tests/DisconnectPipelineTests.cs
  - tests/Deskbridge.Tests/ConnectionQueryTests.cs
  - tests/Deskbridge.Tests/NotificationServiceTests.cs
  - tests/Deskbridge.Tests/DiCompositionTests.cs
  - Directory.Build.props
  - Directory.Packages.props
  - .editorconfig
findings:
  critical: 2
  warning: 4
  info: 4
  total: 10
status: issues_found
---

# Phase 01: Code Review Report

**Reviewed:** 2026-04-11T00:00:00Z
**Depth:** standard
**Files Reviewed:** 34
**Status:** issues_found

## Summary

The foundation layer is well-structured. Interfaces are clean, the pipeline pattern is sound, and the DI wiring in `App.xaml.cs` mirrors what the `DiCompositionTests` verify — good practice. Nullable reference types are enabled project-wide and treated as errors, which eliminates a whole class of null-safety bugs.

Two critical issues need to be resolved before the foundation is considered stable. `ConnectionQueryService` is registered as a singleton but holds a mutable `List<T>` with no thread-safety, and — more importantly — it is always constructed via the parameterless constructor at runtime, meaning it is permanently empty and every `IConnectionQuery` call returns nothing. Additionally, `DisconnectPipeline` has an abort-on-first-failure behaviour that is semantically dangerous for cleanup pipelines and will silently skip remaining teardown stages (e.g., event publishing, resource release) if any earlier stage fails.

---

## Critical Issues

### CR-01: `ConnectionQueryService` is always empty at runtime (no data source)

**File:** `src/Deskbridge.Core/Services/ConnectionQueryService.cs:11-13`

**Issue:** The parameterless constructor initialises `_connections` to an empty list and there is no mechanism to populate it after construction. The DI registration in `App.xaml.cs` uses `AddSingleton<IConnectionQuery, ConnectionQueryService>()`, which resolves via the parameterless constructor. Every call to `GetAll()`, `Search()`, `GetByGroup()`, etc. will always return an empty list. The seeded constructor (`ConnectionQueryService(IEnumerable<ConnectionModel>)`) is never reachable through DI.

There is also no public method to refresh or set the connection list, so even after `IConnectionStore` is implemented, `ConnectionQueryService` will have no way to receive data unless redesigned.

**Fix:** Inject `IConnectionStore` (or a `Func<IReadOnlyList<ConnectionModel>>` delegate) so the query service can read live data:

```csharp
public sealed class ConnectionQueryService : IConnectionQuery
{
    private readonly IConnectionStore _store;

    public ConnectionQueryService(IConnectionStore store)
    {
        _store = store;
    }

    public IReadOnlyList<ConnectionModel> GetAll() => _store.GetAll();

    public IReadOnlyList<ConnectionModel> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GetAll();

        var normalizedQuery = query.Trim().ToLowerInvariant();
        return _store.GetAll()
            .Select(c => new { Connection = c, Score = CalculateScore(c, normalizedQuery) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Connection)
            .ToList();
    }

    // ... remaining methods delegate to _store.GetAll() similarly
}
```

Also register `IConnectionStore` in `App.xaml.cs` once the implementation exists.

---

### CR-02: `DisconnectPipeline` aborts on first stage failure — cleanup stages are silently skipped

**File:** `src/Deskbridge.Core/Pipeline/DisconnectPipeline.cs:14-23`

**Issue:** The disconnect pipeline short-circuits identically to the connect pipeline: if any stage returns `Success = false`, all subsequent stages are skipped. This is correct for a connect pipeline (don't proceed past a failed validation/auth stage), but it is wrong for a disconnect/cleanup pipeline. Stages registered after the failing one — such as an event-publishing stage, an audit-logging stage, or a COM resource release stage — will never run. This will cause silent resource leaks and missing audit events in production.

```csharp
// Current — cleanup stages 3+ are skipped if stage 2 fails
foreach (var stage in _stages.OrderBy(s => s.Order))
{
    var result = await stage.ExecuteAsync(context);
    if (!result.Success)
        return result;   // <-- stages 3, 4, 5 never run
}
```

**Fix:** Run all stages regardless of individual failures; collect failures and return an aggregate result:

```csharp
public async Task<PipelineResult> DisconnectAsync(DisconnectContext context)
{
    List<string>? failures = null;

    foreach (var stage in _stages.OrderBy(s => s.Order))
    {
        var result = await stage.ExecuteAsync(context);
        if (!result.Success)
        {
            failures ??= [];
            failures.Add($"[{stage.Name}] {result.FailureReason}");
        }
    }

    return failures is null
        ? new PipelineResult(true)
        : new PipelineResult(false, string.Join("; ", failures));
}
```

Update `DisconnectPipelineTests` to assert that all stages run even when an intermediate one fails.

---

## Warnings

### WR-01: `INotificationService` declares non-nullable event; implementation is nullable

**File:** `src/Deskbridge.Core/Interfaces/INotificationService.cs:12` and `src/Deskbridge.Core/Services/NotificationService.cs:22`

**Issue:** The interface declares `event EventHandler<Notification> NotificationRaised` (non-nullable), but the implementation declares `event EventHandler<Notification>? NotificationRaised` (nullable). With `Nullable` enabled and nullable diagnostics set to error in `.editorconfig`, this mismatch may produce a CS8633 warning. More practically, the interface contract implies the event is always raise-able without a null check, but the implementation requires one. All interface consumers that omit the null check when raising the event through the interface would be incorrect.

**Fix:** Add the nullable annotation to the interface declaration to match the implementation:

```csharp
public interface INotificationService
{
    void Show(string title, string message, NotificationLevel level = NotificationLevel.Info, TimeSpan? duration = null);
    void ShowError(string title, string message, Exception? exception = null);
    IReadOnlyList<Notification> Recent { get; }
    event EventHandler<Notification>? NotificationRaised;   // add ?
}
```

---

### WR-02: `ConnectionFilter.IsConnected` is silently ignored in `GetByFilter`

**File:** `src/Deskbridge.Core/Services/ConnectionQueryService.cs:43-59` and `src/Deskbridge.Core/Models/ConnectionFilter.cs:9`

**Issue:** `ConnectionFilter` exposes an `IsConnected` property, but `GetByFilter` never applies it. A caller constructing `new ConnectionFilter { IsConnected = true }` will receive all connections regardless of their connection state. There is no compiler error, no runtime error, and no test for this — it is a silent correctness hole. Filtering by `IsConnected` requires knowledge of active sessions, so the correct fix depends on architecture decisions, but the dead property as-is is misleading.

**Fix (option A):** Remove `IsConnected` from `ConnectionFilter` until active session state is available from a session manager. This is the simplest fix that avoids misleading callers.

**Fix (option B):** Apply the filter using a session registry:

```csharp
// In GetByFilter, after other filters:
if (filter.IsConnected is not null)
{
    var activeSessions = _sessionRegistry.GetActiveConnectionIds(); // injected
    results = filter.IsConnected.Value
        ? results.Where(c => activeSessions.Contains(c.Id))
        : results.Where(c => !activeSessions.Contains(c.Id));
}
```

---

### WR-03: `EventBus` uses `WeakReferenceMessenger.Default` (global static) — test isolation is broken

**File:** `src/Deskbridge.Core/Services/EventBus.cs:7`

**Issue:** `_messenger` is assigned `WeakReferenceMessenger.Default`, which is a process-wide static singleton. Every `EventBus` instance constructed in tests shares the same underlying messenger. If a subscriber registered in one test is not unsubscribed before the next test runs, it will receive events from subsequent tests, causing interference. The `EventBusTests` class does clean up after each test, but any future test that neglects cleanup will experience intermittent failures.

**Fix:** Accept `IMessenger` via constructor injection so tests can supply an isolated instance:

```csharp
public sealed class EventBus : IEventBus
{
    private readonly IMessenger _messenger;

    public EventBus(IMessenger messenger)
    {
        _messenger = messenger;
    }

    // ... rest unchanged
}
```

Register in DI:

```csharp
services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
services.AddSingleton<IEventBus, EventBus>();
```

In tests, pass `new WeakReferenceMessenger()` for a fresh isolated instance per test.

---

### WR-04: `App.OnStartup` has no global exception handling

**File:** `src/Deskbridge/App.xaml.cs:13-29`

**Issue:** There are no handlers for `DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, or `TaskScheduler.UnobservedTaskException`. An unhandled exception on the UI thread will display a default Windows crash dialog with no logging or graceful shutdown. An unobserved faulted `Task` will be silently swallowed (or crash in older .NET) with no diagnostic output. For a desktop app that will manage active RDP sessions, silent crashes without logs make production diagnostics extremely difficult.

**Fix:** Add handlers in `OnStartup` (after Serilog is initialised):

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);

    DispatcherUnhandledException += (_, ex) =>
    {
        Log.Fatal(ex.Exception, "Unhandled dispatcher exception");
        ex.Handled = true;
        // Show error notification then shutdown
        Current.Shutdown(1);
    };

    AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        Log.Fatal(ex.ExceptionObject as Exception, "Unhandled AppDomain exception");

    TaskScheduler.UnobservedTaskException += (_, ex) =>
    {
        Log.Error(ex.Exception, "Unobserved task exception");
        ex.SetObserved();
    };

    // ... rest of startup
}
```

---

## Info

### IN-01: `NotificationLevel` and `Notification` types declared inside an interface file

**File:** `src/Deskbridge.Core/Interfaces/INotificationService.cs:3-5`

**Issue:** `NotificationLevel` (enum) and `Notification` (record) are defined in the same file as `INotificationService`. This is inconsistent with the rest of the codebase where domain types live in `Deskbridge.Core.Models`. Other parts of the codebase (`ConnectionEvents.cs`, etc.) already reference `NotificationLevel` from `INotificationService.cs`, creating an implicit coupling between the interface file and consumers who need the enum.

**Fix:** Move `NotificationLevel` and `Notification` to `src/Deskbridge.Core/Models/` (e.g., `NotificationModels.cs` or alongside `Enums.cs`).

---

### IN-02: Magic number `50` in `NotificationService.AddRecent` should be a constant

**File:** `src/Deskbridge.Core/Services/NotificationService.cs:43`

**Issue:** The history cap of 50 is a bare literal with no explanation of why that limit was chosen. It is referenced once so duplication is not a current concern, but it should be named.

**Fix:**

```csharp
private const int MaxRecentNotifications = 50;

private void AddRecent(Notification notification)
{
    lock (_lock)
    {
        _recent.Add(notification);
        if (_recent.Count > MaxRecentNotifications)
            _recent.RemoveAt(0);
    }
}
```

---

### IN-03: `ConnectionPipeline.ConnectAsync` discards `CancellationToken` from callers

**File:** `src/Deskbridge.Core/Pipeline/ConnectionPipeline.cs:15-25`

**Issue:** `IConnectionPipeline.ConnectAsync(ConnectionModel)` takes no `CancellationToken`, so the `ConnectionContext.CancellationToken` is always `CancellationToken.None`. Long-running stages (e.g., TCP handshake, credential resolution) cannot be cancelled by the UI. This limits the ability to implement responsive "Cancel" buttons and may cause the app to hang during shutdown if a pipeline is mid-flight.

**Fix:** Add `CancellationToken` to the interface and propagate it:

```csharp
public interface IConnectionPipeline
{
    void AddStage(IConnectionPipelineStage stage);
    Task<PipelineResult> ConnectAsync(ConnectionModel connection, CancellationToken cancellationToken = default);
}

// In ConnectionPipeline:
public async Task<PipelineResult> ConnectAsync(ConnectionModel connection, CancellationToken cancellationToken = default)
{
    var context = new ConnectionContext
    {
        Connection = connection,
        CancellationToken = cancellationToken
    };
    // ...
}
```

---

### IN-04: `ConnectionQueryService` internal list is not thread-safe for read+write scenarios

**File:** `src/Deskbridge.Core/Services/ConnectionQueryService.cs:8`

**Issue:** `_connections` is a plain `List<ConnectionModel>` with no synchronisation. If CR-01 is resolved by injecting `IConnectionStore` (which reads a fresh snapshot each call), this issue disappears. However, if the design retains an in-memory list that gets refreshed (e.g., via an `IEventBus` subscription), concurrent reads during writes will throw `InvalidOperationException`. This is noted here for awareness during the CR-01 fix.

**Fix:** If an in-memory list is retained after the CR-01 redesign, protect it with a `ReaderWriterLockSlim` or switch to `ImmutableList<T>` with atomic replacement via `Volatile.Write`/`Interlocked.Exchange`.

---

_Reviewed: 2026-04-11T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_

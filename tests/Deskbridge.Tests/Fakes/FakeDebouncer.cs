using Deskbridge.Core.Interfaces;

namespace Deskbridge.Tests.Fakes;

/// <summary>
/// Phase 21 (PERF-02): synchronous test double for <see cref="IDebouncer"/>.
/// Captures the most recent scheduled action without timing; tests trigger
/// the captured action manually via <see cref="Fire"/> to assert trailing-fire
/// debounce semantics deterministically (no Dispatcher pump required).
/// </summary>
internal sealed class FakeDebouncer : IDebouncer
{
    private Action? _pending;

    public int ScheduleCallCount { get; private set; }
    public int CancelCallCount { get; private set; }

    public bool HasPending => _pending is not null;

    public void Schedule(Action action)
    {
        ScheduleCallCount++;
        _pending = action;
    }

    public void Cancel()
    {
        CancelCallCount++;
        _pending = null;
    }

    public void Fire()
    {
        var a = _pending;
        _pending = null;
        a?.Invoke();
    }
}

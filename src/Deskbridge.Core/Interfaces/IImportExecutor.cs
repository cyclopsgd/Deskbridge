using Deskbridge.Core.Models;

namespace Deskbridge.Core.Interfaces;

/// <summary>
/// Phase 22 (D-03): runs the prepare loop on a thread-pool task and reports
/// per-row progress via <see cref="IProgress{T}"/>. The VM is responsible for
/// the single <c>IConnectionStore.SaveBatch</c> commit (D-02). Executor is
/// parser-agnostic — it consumes the already-parsed <see cref="ImportedNode"/>
/// tree from any <c>IConnectionImporter.ParseAsync</c>.
/// </summary>
public interface IImportExecutor
{
    /// <summary>
    /// Transforms an <see cref="ImportRequest"/> into an
    /// <see cref="ImportPrepareResult"/>. Per-row failures are captured in the
    /// result's <c>Failures</c> list rather than aborting the loop (D-07).
    /// The supplied <paramref name="ct"/> is honored at loop entry only (D-06):
    /// pre-cancellation throws <see cref="OperationCanceledException"/>; mid-loop
    /// cancellation is not supported in this phase.
    /// </summary>
    /// <param name="request">Already-parsed nodes plus the existing store snapshot.</param>
    /// <param name="progress">Optional per-row progress callback. Constructed on the UI
    /// thread by the VM so it auto-marshals via <c>SynchronizationContext</c>.</param>
    /// <param name="ct">Cancellation token (boundary cancel only — see D-06).</param>
    Task<ImportPrepareResult> PrepareAsync(
        ImportRequest request,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
}

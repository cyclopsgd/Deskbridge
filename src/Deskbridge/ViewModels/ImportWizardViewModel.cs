using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Events;
using Deskbridge.Core.Models;
using Wpf.Ui.Controls;

namespace Deskbridge.ViewModels;

/// <summary>
/// Phase 7 Plan 07-04 (MIG-02): Multi-step import wizard state management.
/// 4-step flow: source selection, file picker, tree preview with checkboxes, confirm.
///
/// Phase 22 Plan 22-02 (IMP-03 / D-02 / D-03 / D-06 / D-07 / D-15): refactored to
/// delegate the prepare loop to <see cref="IImportExecutor"/>. The VM now owns the
/// determinate-progress observable surface (<c>IsImportWriteInProgress</c>,
/// <c>TotalToImport</c>, <c>ImportedCount</c>), surfaces per-row failures via
/// <c>Failures</c>, and computes the Step 4 InfoBar Severity / Title.
/// </summary>
public partial class ImportWizardViewModel : ObservableObject
{
    private readonly IReadOnlyList<IConnectionImporter> _importers;
    private readonly IConnectionStore _store;
    private readonly IEventBus _bus;
    private readonly IAuditLogger _audit;
    private readonly IImportExecutor _executor;

    /// <summary>
    /// Delegate for opening a file dialog. Injected for testability --
    /// production wires Microsoft.Win32.OpenFileDialog, tests provide a stub.
    /// </summary>
    private readonly Func<string, string?>? _fileBrowser;

    /// <summary>
    /// Phase 22 (D-06 fix-forward): each parse run creates a fresh CTS so the
    /// importer receives a non-default CancellationToken. Re-issuing a parse
    /// cancels the previous CTS first.
    /// </summary>
    private CancellationTokenSource? _parseCts;

    public ImportWizardViewModel(
        IReadOnlyList<IConnectionImporter> importers,
        IConnectionStore store,
        IEventBus bus,
        IAuditLogger audit,
        IImportExecutor executor,
        Func<string, string?>? fileBrowser = null)
    {
        _importers = importers;
        _store = store;
        _bus = bus;
        _audit = audit;
        _executor = executor;
        _fileBrowser = fileBrowser;

        AvailableImporters = importers;
        if (importers.Count > 0)
            SelectedImporter = importers[0];

        // Phase 22 (UI-SPEC §"Severity Selection Rule"): keep the Step 4 InfoBar
        // computed properties in sync with bulk-loaded failures.
        Failures.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(FailedCount));
            OnPropertyChanged(nameof(ImportSeverity));
            OnPropertyChanged(nameof(ImportTitleText));
            OnPropertyChanged(nameof(ImportSummary));
        };
    }

    // ---------------------------------------------------------------- observable state

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonText))]
    [NotifyPropertyChangedFor(nameof(IsCloseButtonVisible))]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(IsStep4))]
    public partial int CurrentStep { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial IConnectionImporter? SelectedImporter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial string? FilePath { get; set; }

    [ObservableProperty]
    public partial ImportResult? ParseResult { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsProcessing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportSummary))]
    [NotifyPropertyChangedFor(nameof(ImportSeverity))]
    [NotifyPropertyChangedFor(nameof(ImportTitleText))]
    public partial int ImportedCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportSummary))]
    public partial int SkippedCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportSummary))]
    public partial int RenamedCount { get; set; }

    /// <summary>
    /// Phase 22 (IMP-03 / D-15): true while <see cref="IImportExecutor.PrepareAsync"/>
    /// is running on the thread pool. Drives the determinate progress overlay
    /// (vs. parse-phase ProgressRing) and the Cancel-button-disabled contract (D-05).
    /// </summary>
    [ObservableProperty]
    public partial bool IsImportWriteInProgress { get; set; }

    /// <summary>
    /// Phase 22 (IMP-03): denominator of the determinate ProgressBar. Set on the
    /// UI thread BEFORE <c>Task.Run</c> so the bar appears at full extent the
    /// instant the overlay flips on (RESEARCH P6).
    /// </summary>
    [ObservableProperty]
    public partial int TotalToImport { get; set; }

    // ---------------------------------------------------------------- computed

    public IReadOnlyList<IConnectionImporter> AvailableImporters { get; }

    public ObservableCollection<ImportTreeItemViewModel> PreviewItems { get; } = [];

    public ObservableCollection<DuplicateItemViewModel> DuplicateItems { get; } = [];

    /// <summary>
    /// Phase 22 (D-15): per-row import failures bulk-loaded from the executor.
    /// Bound to the failure-list ItemsControl on Step 4 (UI-SPEC §"Failure List").
    /// </summary>
    public ObservableCollection<FailedImport> Failures { get; } = [];

    public int FailedCount => Failures.Count;

    public bool CanGoNext => CurrentStep switch
    {
        1 => SelectedImporter is not null,
        2 => !string.IsNullOrEmpty(FilePath),
        3 => PreviewItems.Any(HasCheckedItem),
        _ => false,
    };

    public bool CanGoBack => CurrentStep > 1 && CurrentStep < 4;

    public string PrimaryButtonText => CurrentStep switch
    {
        1 or 2 => "Next",
        3 => "Import",
        _ => "Done",
    };

    public bool IsCloseButtonVisible => CurrentStep < 4;

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep4 => CurrentStep == 4;

    /// <summary>
    /// Phase 22 (UI-SPEC §"InfoBar copy"): three-branch summary text.
    ///   Success: "Imported N connection(s). S skipped, R renamed."
    ///   Partial failure: "Imported N connection(s). S skipped, R renamed, F failed."
    ///   Total failure: "Import failed. F of T connections could not be saved. See log for details."
    /// </summary>
    public string ImportSummary =>
        FailedCount == 0
            ? $"Imported {ImportedCount} connection(s). {SkippedCount} skipped, {RenamedCount} renamed."
            : ImportedCount == 0
                ? $"Import failed. {FailedCount} of {TotalToImport} connections could not be saved. See log for details."
                : $"Imported {ImportedCount} connection(s). {SkippedCount} skipped, {RenamedCount} renamed, {FailedCount} failed.";

    /// <summary>
    /// Phase 22 (UI-SPEC §"Severity Selection Rule"): deterministic InfoBar severity
    /// driven by FailedCount + ImportedCount. No user-controlled input feeds the rule.
    /// </summary>
    public InfoBarSeverity ImportSeverity =>
        Failures.Count == 0
            ? InfoBarSeverity.Success
            : ImportedCount == 0
                ? InfoBarSeverity.Error
                : InfoBarSeverity.Warning;

    public string ImportTitleText =>
        Failures.Count == 0
            ? "Import Complete"
            : ImportedCount == 0
                ? "Import Failed"
                : "Import Completed with Errors";

    // ---------------------------------------------------------------- commands

    [RelayCommand]
    private async Task NextStepAsync()
    {
        if (!CanGoNext || IsProcessing) return;

        switch (CurrentStep)
        {
            case 1:
                CurrentStep = 2;
                break;
            case 2:
                await ParseFileAsync();
                break;
            case 3:
                await ImportSelectedAsync();
                break;
        }
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CanGoBack) CurrentStep--;
    }

    [RelayCommand]
    private void BrowseFile()
    {
        if (SelectedImporter is null) return;

        if (_fileBrowser is not null)
        {
            var path = _fileBrowser(SelectedImporter.FileFilter);
            if (path is not null) FilePath = path;
            return;
        }

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = SelectedImporter.FileFilter,
            Title = $"Select {SelectedImporter.SourceName} configuration file"
        };
        if (dlg.ShowDialog() == true)
        {
            FilePath = dlg.FileName;
        }
    }

    // ---------------------------------------------------------------- parse

    internal async Task ParseFileAsync()
    {
        if (SelectedImporter is null || string.IsNullOrEmpty(FilePath) || IsProcessing) return;

        IsProcessing = true;
        ErrorMessage = null;

        // Phase 22 (D-06 fix-forward at line 181): cancel any prior parse and
        // thread a fresh CT into ParseAsync.
        _parseCts?.Cancel();
        _parseCts?.Dispose();
        _parseCts = new CancellationTokenSource();

        try
        {
            using var stream = File.OpenRead(FilePath);
            var result = await SelectedImporter.ParseAsync(stream, _parseCts.Token);
            ParseResult = result;
            BuildPreviewTree(result.RootNodes);
            CurrentStep = 3;
        }
        catch (ImportException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to read file: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Overload for testing: accepts a stream directly instead of reading from FilePath.
    /// </summary>
    internal async Task ParseFromStreamAsync(Stream stream)
    {
        if (SelectedImporter is null) return;

        IsProcessing = true;
        ErrorMessage = null;

        // Phase 22 (D-06 fix-forward at line 211): cancel any prior parse and
        // thread a fresh CT into ParseAsync.
        _parseCts?.Cancel();
        _parseCts?.Dispose();
        _parseCts = new CancellationTokenSource();

        try
        {
            var result = await SelectedImporter.ParseAsync(stream, _parseCts.Token);
            ParseResult = result;
            BuildPreviewTree(result.RootNodes);
            CurrentStep = 3;
        }
        catch (ImportException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to read file: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void BuildPreviewTree(IReadOnlyList<ImportedNode> nodes)
    {
        PreviewItems.Clear();
        foreach (var node in nodes)
        {
            PreviewItems.Add(BuildTreeItem(node));
        }
    }

    private static ImportTreeItemViewModel BuildTreeItem(ImportedNode node)
    {
        var item = new ImportTreeItemViewModel
        {
            Name = node.Name,
            Type = node.Type,
            Protocol = node.Protocol,
            Hostname = node.Hostname,
            Username = node.Username,
            Domain = node.Domain,
            Port = node.Port,
            IsChecked = node.Protocol == Protocol.Rdp || node.Type == ImportNodeType.Container,
        };
        foreach (var child in node.Children)
        {
            item.Children.Add(BuildTreeItem(child));
        }
        return item;
    }

    // ---------------------------------------------------------------- import

    /// <summary>
    /// Phase 22 Plan 22-02 (IMP-03 / D-02 / D-03 / D-06 / D-07): refactored to
    /// delegate the prepare loop to <see cref="IImportExecutor"/>. Pattern
    /// (RESEARCH P2 + P6 + P8 option c):
    ///
    /// 1. Build <see cref="ImportRequest"/> on the UI thread (cheap walk).
    /// 2. Set <c>TotalToImport</c> + flip <c>IsImportWriteInProgress</c> BEFORE Task.Run.
    /// 3. Construct <see cref="Progress{T}"/> on the UI thread to capture the
    ///    DispatcherSynchronizationContext (auto-marshals progress callbacks).
    /// 4. Run <see cref="IImportExecutor.PrepareAsync"/> on the thread pool
    ///    (D-06: prepare is non-cancellable in this phase).
    /// 5. Bulk-load failures on the UI thread.
    /// 6. <see cref="IConnectionStore.SaveBatch"/> exactly ONCE (D-02).
    /// 7. Fatal exception → SaveBatch NOT called; user stays on Step 3 (D-07).
    /// </summary>
    internal async Task ImportSelectedAsync()
    {
        if (SelectedImporter is null || IsProcessing) return;

        IsProcessing = true;
        ErrorMessage = null;

        try
        {
            // 1. Build request on UI thread (cheap)
            var checkedNodes = ToImportedNodes(PreviewItems);
            var existingConns = _store.GetAll();
            var existingGroups = _store.GetGroups();
            var resolutions = DuplicateItems
                .Select(d => new DuplicateResolution(d.Hostname, d.Action))
                .ToList();
            var request = new ImportRequest(
                checkedNodes,
                existingConns,
                existingGroups,
                resolutions);

            // 2. Set denominator + flip write flag BEFORE Task.Run
            TotalToImport = CountConnections(checkedNodes);
            IsImportWriteInProgress = true;
            ImportedCount = 0;
            SkippedCount = 0;
            RenamedCount = 0;
            Failures.Clear();

            // 3. Construct Progress<int> on UI thread (RESEARCH P6 — captures
            //    DispatcherSynchronizationContext so callbacks marshal automatically).
            //
            //    A flag captured by the lambda decides whether a callback is
            //    still allowed to mutate ImportedCount. Once we set this flag to
            //    false (after Task.Run returns), any callback that has been
            //    queued by Progress<int>.Report but not yet dequeued — possible
            //    when there's no DispatcherSynchronizationContext, e.g. parallel
            //    xUnit runs — short-circuits and cannot overwrite the final
            //    count with a stale processed-count.
            var progressActive = new[] { true };
            var progress = new Progress<int>(n =>
            {
                if (System.Threading.Volatile.Read(ref progressActive[0]))
                    ImportedCount = n;
            });

            // 4. Run prepare loop on thread pool (D-06: prepare is non-cancellable)
            var result = await Task.Run(
                () => _executor.PrepareAsync(request, progress, CancellationToken.None),
                CancellationToken.None);

            // 5a. Disable progress-callback writes BEFORE assigning final counts.
            //     Volatile.Write ensures the flag flip is visible to any worker
            //     thread that may dequeue a stale callback after this point.
            System.Threading.Volatile.Write(ref progressActive[0], false);

            // 5b. Bulk-load failures on UI thread (P8 option c)
            foreach (var f in result.Failures)
                Failures.Add(f);

            ImportedCount = result.ImportedCount;
            SkippedCount = result.SkippedCount;
            RenamedCount = result.RenamedCount;
            OnPropertyChanged(nameof(ImportSummary));

            // 6. Single SaveBatch (D-02)
            _store.SaveBatch(result.ConnectionsToSave, result.GroupsToSave);
            _bus.Publish(new ConnectionDataChangedEvent());
            _bus.Publish(new ConnectionImportedEvent(ImportedCount, SelectedImporter.SourceName));

            await _audit.LogAsync(new AuditRecord(
                Ts: DateTime.UtcNow.ToString("O"),
                Type: AuditAction.ConnectionsImported.ToString(),
                ConnectionId: null,
                User: Environment.UserName,
                Outcome: $"success: {ImportedCount} imported, {SkippedCount} skipped, {RenamedCount} renamed, {FailedCount} failed"));

            CurrentStep = 4;
        }
        catch (Exception ex)
        {
            // D-07: fatal throw → SaveBatch NOT called; user returns to prior step
            ErrorMessage = $"Import failed: {ex.Message}";
            // Do NOT advance CurrentStep — user stays on Step 3 with InfoBar.
        }
        finally
        {
            IsImportWriteInProgress = false;
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Counts CONNECTION-typed RDP-protocol leaves in the supplied tree. Used to
    /// set the determinate progress denominator (<c>TotalToImport</c>) — matches
    /// the executor's "non-RDP rows are filtered before processed++" behaviour.
    /// </summary>
    private static int CountConnections(IReadOnlyList<ImportedNode> nodes)
    {
        int n = 0;
        foreach (var node in nodes)
        {
            if (node.Type == ImportNodeType.Connection && node.Protocol == Protocol.Rdp) n++;
            n += CountConnections(node.Children);
        }
        return n;
    }

    /// <summary>
    /// Converts the checked subset of <see cref="PreviewItems"/> back into the
    /// <see cref="ImportedNode"/> tree the executor consumes. Skips unchecked
    /// nodes; preserves Container / Connection ordering.
    /// </summary>
    private static IReadOnlyList<ImportedNode> ToImportedNodes(IEnumerable<ImportTreeItemViewModel> items)
    {
        var list = new List<ImportedNode>();
        foreach (var item in items)
        {
            if (!item.IsChecked) continue;
            var children = ToImportedNodes(item.Children);
            list.Add(new ImportedNode(
                item.Name,
                item.Type,
                item.Hostname,
                item.Port,
                item.Username,
                item.Domain,
                item.Protocol,
                Description: null,
                Children: children));
        }
        return list;
    }

    // ---------------------------------------------------------------- helpers

    private static bool HasCheckedItem(ImportTreeItemViewModel item)
    {
        if (item.IsChecked && item.Type == ImportNodeType.Connection) return true;
        return item.Children.Any(HasCheckedItem);
    }
}

// ---------------------------------------------------------------- duplicate handling
//
// Phase 22 Plan 22-02: the view-tier `enum DuplicateAction` was removed —
// `DuplicateItemViewModel.Action` now binds directly to the Core enum at
// `Deskbridge.Core.Models.DuplicateAction`. Single source of truth.

public partial class DuplicateItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Hostname { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DuplicateAction Action { get; set; } = DuplicateAction.Rename;
}

// ---------------------------------------------------------------- tree item

public partial class ImportTreeItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsChecked { get; set; } = true;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    public ImportNodeType Type { get; init; }
    public Protocol Protocol { get; init; }
    public string? Hostname { get; init; }
    public string? Username { get; init; }
    public string? Domain { get; init; }
    public int Port { get; init; } = 3389;

    public bool IsSupported => Protocol == Protocol.Rdp || Type == ImportNodeType.Container;
    public ObservableCollection<ImportTreeItemViewModel> Children { get; } = [];

    /// <summary>
    /// Cascade check/uncheck to all children when a folder is toggled.
    /// </summary>
    partial void OnIsCheckedChanged(bool value)
    {
        if (Type != ImportNodeType.Container) return;
        foreach (var child in Children)
        {
            child.IsChecked = value;
        }
    }
}

// ---------------------------------------------------------------- extensions

internal static class ImportProtocolExtensions
{
    public static bool IsSupported(this Protocol protocol) => protocol == Protocol.Rdp;
    public static Protocol ToProtocol(this Protocol protocol) => protocol;
}

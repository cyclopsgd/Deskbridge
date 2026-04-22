using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Events;
using Deskbridge.Core.Models;

namespace Deskbridge.ViewModels;

/// <summary>
/// Phase 7 Plan 07-04 (MIG-02): Multi-step import wizard state management.
/// 4-step flow: source selection, file picker, tree preview with checkboxes, confirm.
/// </summary>
public partial class ImportWizardViewModel : ObservableObject
{
    private readonly IReadOnlyList<IConnectionImporter> _importers;
    private readonly IConnectionStore _store;
    private readonly IEventBus _bus;
    private readonly IAuditLogger _audit;

    /// <summary>
    /// Delegate for opening a file dialog. Injected for testability --
    /// production wires Microsoft.Win32.OpenFileDialog, tests provide a stub.
    /// </summary>
    private readonly Func<string, string?>? _fileBrowser;

    public ImportWizardViewModel(
        IReadOnlyList<IConnectionImporter> importers,
        IConnectionStore store,
        IEventBus bus,
        IAuditLogger audit,
        Func<string, string?>? fileBrowser = null)
    {
        _importers = importers;
        _store = store;
        _bus = bus;
        _audit = audit;
        _fileBrowser = fileBrowser;

        AvailableImporters = importers;
        if (importers.Count > 0)
            SelectedImporter = importers[0];
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
    public partial int ImportedCount { get; set; }

    [ObservableProperty]
    public partial int SkippedCount { get; set; }

    [ObservableProperty]
    public partial int RenamedCount { get; set; }

    // ---------------------------------------------------------------- computed

    public IReadOnlyList<IConnectionImporter> AvailableImporters { get; }

    public ObservableCollection<ImportTreeItemViewModel> PreviewItems { get; } = [];

    public ObservableCollection<DuplicateItemViewModel> DuplicateItems { get; } = [];

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

    public string ImportSummary =>
        $"Imported {ImportedCount} connection(s). {SkippedCount} skipped, {RenamedCount} renamed.";

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
        try
        {
            using var stream = File.OpenRead(FilePath);
            var result = await SelectedImporter.ParseAsync(stream);
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
        try
        {
            var result = await SelectedImporter.ParseAsync(stream);
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

    internal async Task ImportSelectedAsync()
    {
        if (SelectedImporter is null || IsProcessing) return;

        IsProcessing = true;
        var imported = 0;
        var skipped = 0;
        var renamed = 0;

        try
        {
            var existingConnections = _store.GetAll();
            var existingHostnames = new HashSet<string>(
                existingConnections
                    .Where(c => !string.IsNullOrEmpty(c.Hostname))
                    .Select(c => c.Hostname!),
                StringComparer.OrdinalIgnoreCase);

            // First pass: create groups to maintain hierarchy
            var groupMap = new Dictionary<string, Guid>(); // path -> groupId
            var existingGroups = _store.GetGroups();

            // Flatten checked items and import
            var flatItems = FlattenCheckedItems(PreviewItems, null, groupMap, existingGroups);

            // Process duplicate resolution
            DuplicateItems.Clear();

            foreach (var (item, groupId) in flatItems)
            {
                if (item.Type != ImportNodeType.Connection) continue;
                if (!item.Protocol.IsSupported()) continue;

                var hostname = item.Hostname;

                // Check for duplicates
                if (!string.IsNullOrEmpty(hostname) && existingHostnames.Contains(hostname))
                {
                    // Check if user has resolved this duplicate
                    var resolution = DuplicateItems.FirstOrDefault(d =>
                        string.Equals(d.Hostname, hostname, StringComparison.OrdinalIgnoreCase));

                    if (resolution is not null && resolution.Action == DuplicateAction.Skip)
                    {
                        skipped++;
                        continue;
                    }

                    if (resolution is not null && resolution.Action == DuplicateAction.Rename)
                    {
                        // Append suffix to avoid conflict
                        var conn = CreateConnectionModel(item, groupId);
                        conn.Name = $"{conn.Name} (imported)";
                        _store.Save(conn);
                        existingHostnames.Add(conn.Hostname!);
                        imported++;
                        renamed++;
                        continue;
                    }

                    if (resolution is not null && resolution.Action == DuplicateAction.Overwrite)
                    {
                        // Find and update existing
                        var existing = existingConnections.FirstOrDefault(c =>
                            string.Equals(c.Hostname, hostname, StringComparison.OrdinalIgnoreCase));
                        if (existing is not null)
                        {
                            existing.Name = item.Name;
                            existing.Port = item.Port;
                            existing.Username = item.Username;
                            existing.Domain = item.Domain;
                            existing.Protocol = item.Protocol.ToProtocol();
                            existing.GroupId = groupId;
                            existing.UpdatedAt = DateTime.UtcNow;
                            _store.Save(existing);
                            imported++;
                            continue;
                        }
                    }

                    // Auto-rename if no explicit resolution
                    var autoConn = CreateConnectionModel(item, groupId);
                    autoConn.Name = $"{autoConn.Name} (imported)";
                    _store.Save(autoConn);
                    existingHostnames.Add(autoConn.Hostname!);
                    imported++;
                    renamed++;
                    continue;
                }

                // No duplicate -- import directly
                var newConn = CreateConnectionModel(item, groupId);
                _store.Save(newConn);
                if (!string.IsNullOrEmpty(hostname))
                    existingHostnames.Add(hostname);
                imported++;
            }

            ImportedCount = imported;
            SkippedCount = skipped;
            RenamedCount = renamed;

            // Publish event and log audit
            _bus.Publish(new ConnectionImportedEvent(imported, SelectedImporter.SourceName));

            await _audit.LogAsync(new AuditRecord(
                Ts: DateTime.UtcNow.ToString("O"),
                Type: AuditAction.ConnectionsImported.ToString(),
                ConnectionId: null,
                User: Environment.UserName,
                Outcome: $"success: {imported} imported, {skipped} skipped, {renamed} renamed"));

            CurrentStep = 4;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private static ConnectionModel CreateConnectionModel(ImportTreeItemViewModel item, Guid? groupId)
    {
        return new ConnectionModel
        {
            Id = Guid.NewGuid(),
            Name = item.Name,
            Hostname = item.Hostname ?? string.Empty,
            Port = item.Port,
            Username = item.Username,
            Domain = item.Domain,
            Protocol = item.Protocol.ToProtocol(),
            GroupId = groupId,
            CredentialMode = CredentialMode.Prompt, // MIG-03: no passwords imported
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    private List<(ImportTreeItemViewModel Item, Guid? GroupId)> FlattenCheckedItems(
        IEnumerable<ImportTreeItemViewModel> items,
        Guid? parentGroupId,
        Dictionary<string, Guid> groupMap,
        IReadOnlyList<ConnectionGroup> existingGroups)
    {
        var result = new List<(ImportTreeItemViewModel, Guid?)>();

        foreach (var item in items)
        {
            if (!item.IsChecked) continue;

            if (item.Type == ImportNodeType.Container)
            {
                // Create or reuse group
                var groupId = EnsureGroup(item.Name, parentGroupId, groupMap, existingGroups);
                // Recurse into children
                result.AddRange(FlattenCheckedItems(item.Children, groupId, groupMap, existingGroups));
            }
            else
            {
                result.Add((item, parentGroupId));
            }
        }

        return result;
    }

    private Guid EnsureGroup(
        string name,
        Guid? parentGroupId,
        Dictionary<string, Guid> groupMap,
        IReadOnlyList<ConnectionGroup> existingGroups)
    {
        var key = $"{parentGroupId}|{name}";
        if (groupMap.TryGetValue(key, out var existingId))
            return existingId;

        // Check if a group with this name already exists under the same parent
        var existing = existingGroups.FirstOrDefault(g =>
            g.Name == name && g.ParentGroupId == parentGroupId);
        if (existing is not null)
        {
            groupMap[key] = existing.Id;
            return existing.Id;
        }

        var group = new ConnectionGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            ParentGroupId = parentGroupId,
        };
        _store.SaveGroup(group);
        groupMap[key] = group.Id;
        return group.Id;
    }

    // ---------------------------------------------------------------- helpers

    private static bool HasCheckedItem(ImportTreeItemViewModel item)
    {
        if (item.IsChecked && item.Type == ImportNodeType.Connection) return true;
        return item.Children.Any(HasCheckedItem);
    }
}

// ---------------------------------------------------------------- duplicate handling

public enum DuplicateAction { Skip, Overwrite, Rename }

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

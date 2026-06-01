using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Deskbridge.Core.Models;

namespace Deskbridge.ViewModels;

/// <summary>
/// One editable field in the bulk-edit dialog (BULK-03). Pairs a per-field enable flag
/// (the left-column checkbox) with the value the user wants to write to every selected
/// connection, and tracks whether the selection shares a common value for this field.
/// </summary>
/// <remarks>
/// <para>Shared field: every selected connection already has the same value — the field
/// pre-fills with that value and <see cref="Placeholder"/> is empty.</para>
/// <para>Divergent field: the selection has &gt;1 distinct value — the field stays blank
/// and <see cref="Placeholder"/> is "Multiple values".</para>
/// <para>The checkbox starts unchecked in both cases; nothing is written until the user
/// explicitly enables the field.</para>
/// </remarks>
public partial class BulkEditField<T> : ObservableObject
{
    private readonly Action _onEnabledChanged;

    internal BulkEditField(bool isShared, T value, Action onEnabledChanged)
    {
        IsShared = isShared;
        _onEnabledChanged = onEnabledChanged;
        Placeholder = isShared ? string.Empty : "Multiple values";
        Value = value;
    }

    /// <summary>True when every selected connection shares the same value for this field.</summary>
    public bool IsShared { get; }

    /// <summary>
    /// Tertiary-coloured hint shown on a divergent field ("Multiple values"); empty when shared.
    /// </summary>
    public string Placeholder { get; }

    /// <summary>The value to write to all selected connections when <see cref="IsEnabled"/> is true.</summary>
    [ObservableProperty]
    public partial T Value { get; set; }

    /// <summary>
    /// Per-field enable flag (the left-column checkbox). A field is written to the selection
    /// only when this is true. Raises <see cref="BulkEditViewModel.CanApply"/> recomputation.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    partial void OnIsEnabledChanged(bool value) => _onEnabledChanged();
}

/// <summary>
/// BULK-03 — Bulk Edit ViewModel. Intentionally dependency-light: it takes only the selected
/// <see cref="ConnectionModel"/>s (and an optional group list for the Group ComboBox) so its
/// field-diff / per-field-enable / apply / validate logic is unit-testable with zero mocks.
/// </summary>
/// <remarks>
/// Editable set is non-secret only: Hostname, Port, CredentialMode, Username, Domain, GroupId.
/// <para><b>Name is excluded</b> (each connection keeps its unique name — bulk-renaming is
/// nonsensical).</para>
/// <para><b>No password / credential secret is ever read or written here</b> (T-23-04). Credential
/// storage is owned by <c>ICredentialService</c> and is out of scope for bulk edit.</para>
/// Persistence (SaveBatch) is wired by plan 23-03; this VM only mutates the in-memory models.
/// </remarks>
public partial class BulkEditViewModel : ObservableObject
{
    private readonly IReadOnlyList<ConnectionModel> _selected;

    /// <param name="selected">The connections to edit in bulk (the multi-selection).</param>
    /// <param name="availableGroups">
    /// Optional flat group list for the Group ComboBox. Kept dependency-light: a plain
    /// (Id, DisplayName) list rather than an injected store, so apply/diff tests need no mocks.
    /// </param>
    public BulkEditViewModel(
        IReadOnlyList<ConnectionModel> selected,
        IReadOnlyList<GroupDisplayItem>? availableGroups = null)
    {
        _selected = selected ?? throw new ArgumentNullException(nameof(selected));

        AvailableGroups = availableGroups is null
            ? []
            : new ObservableCollection<GroupDisplayItem>(availableGroups);

        // Build each field by diffing the selection: shared (all equal) → pre-fill;
        // divergent (>1 distinct) → blank + "Multiple values" placeholder.
        HostnameField = BuildField(m => m.Hostname, string.Empty);
        PortField = BuildField(
            m => m.Port,
            0,
            sharedValue => sharedValue == 0 ? string.Empty : sharedValue.ToString(),
            string.Empty);
        CredentialModeField = BuildField(m => m.CredentialMode, CredentialMode.Inherit);
        UsernameField = BuildField(m => m.Username, (string?)null);
        DomainField = BuildField(m => m.Domain, (string?)null);
        GroupField = BuildField(m => m.GroupId, (Guid?)null);
    }

    public ObservableCollection<GroupDisplayItem> AvailableGroups { get; }

    /// <summary>Display-friendly options for the Credential mode ComboBox (avoids the "- - -" glyph).</summary>
    public IReadOnlyList<CredentialModeOption> CredentialModes { get; } = new[]
    {
        new CredentialModeOption(CredentialMode.Inherit, "Inherit from parent group"),
        new CredentialModeOption(CredentialMode.Own, "Use own credentials"),
        new CredentialModeOption(CredentialMode.Prompt, "Prompt at connection time"),
    };

    /// <summary>Dialog title, e.g. "Edit 4 connections" (singular: "Edit 1 connection").</summary>
    public string DialogTitle =>
        _selected.Count == 1 ? "Edit 1 connection" : $"Edit {_selected.Count} connections";

    /// <summary>Number of connections in the current multi-selection being edited.</summary>
    public int SelectedCount => _selected.Count;

    // --- Editable fields (Name EXCLUDED; no password) ---

    public BulkEditField<string> HostnameField { get; }
    public BulkEditField<string> PortField { get; }
    public BulkEditField<CredentialMode> CredentialModeField { get; }
    public BulkEditField<string?> UsernameField { get; }
    public BulkEditField<string?> DomainField { get; }
    public BulkEditField<Guid?> GroupField { get; }

    // --- Flat per-field enable proxies (two-way bound by the dialog checkboxes) ---
    // These forward to the field objects so the XAML can bind IsXxxEnabled directly and
    // the CanApply gate is driven by exactly one source of truth.

    public bool IsHostnameEnabled
    {
        get => HostnameField.IsEnabled;
        set => HostnameField.IsEnabled = value;
    }

    public bool IsPortEnabled
    {
        get => PortField.IsEnabled;
        set => PortField.IsEnabled = value;
    }

    public bool IsCredentialModeEnabled
    {
        get => CredentialModeField.IsEnabled;
        set => CredentialModeField.IsEnabled = value;
    }

    public bool IsUsernameEnabled
    {
        get => UsernameField.IsEnabled;
        set => UsernameField.IsEnabled = value;
    }

    public bool IsDomainEnabled
    {
        get => DomainField.IsEnabled;
        set => DomainField.IsEnabled = value;
    }

    public bool IsGroupEnabled
    {
        get => GroupField.IsEnabled;
        set => GroupField.IsEnabled = value;
    }

    /// <summary>
    /// True when at least one field is enabled. Drives the dialog's Apply button
    /// (IsPrimaryButtonEnabled). Recomputed whenever any per-field enable flag changes.
    /// </summary>
    public bool CanApply =>
        IsHostnameEnabled || IsPortEnabled || IsCredentialModeEnabled
        || IsUsernameEnabled || IsDomainEnabled || IsGroupEnabled;

    /// <summary>
    /// Pure, no-throw validation gate (T-23-03). Returns false when an ENABLED field has an
    /// invalid value: Port outside 1..65535, or whitespace/empty Hostname. Unchecked fields
    /// are ignored. Mirrors the SaveConnectionFromQuickEdit port-range shape.
    /// </summary>
    public bool Validate()
    {
        if (IsPortEnabled)
        {
            if (!int.TryParse(PortField.Value, out var port) || port < 1 || port > 65535)
                return false;
        }

        if (IsHostnameEnabled && string.IsNullOrWhiteSpace(HostnameField.Value))
            return false;

        return true;
    }

    /// <summary>
    /// Writes ONLY the enabled fields to every selected connection and returns the mutated list.
    /// Unchecked fields are left untouched (divergent values preserved per-model).
    /// <b>Never writes Name. Never reads or writes any password/credential secret.</b> (T-23-04)
    /// </summary>
    /// <param name="models">
    /// Optional explicit target list (used by tests). Defaults to the selection passed to the ctor.
    /// </param>
    public List<ConnectionModel> ApplyToModels(IReadOnlyList<ConnectionModel>? models = null)
    {
        var targets = models ?? _selected;

        foreach (var model in targets)
        {
            if (IsHostnameEnabled)
                model.Hostname = HostnameField.Value;

            if (IsPortEnabled && int.TryParse(PortField.Value, out var port))
                model.Port = port;

            if (IsCredentialModeEnabled)
                model.CredentialMode = CredentialModeField.Value;

            if (IsUsernameEnabled)
                model.Username = UsernameField.Value;

            if (IsDomainEnabled)
                model.Domain = DomainField.Value;

            if (IsGroupEnabled)
                model.GroupId = GroupField.Value;

            // Name is intentionally never written. No password/credential write occurs here.
            model.UpdatedAt = DateTime.UtcNow;
        }

        return targets.ToList();
    }

    // --- Field construction (diff helpers) ---

    private BulkEditField<TField> BuildField<TField>(
        Func<ConnectionModel, TField> selector,
        TField blankValue)
        => BuildField(selector, blankValue, shared => shared, blankValue);

    /// <summary>
    /// Builds a field by diffing the selection. <paramref name="project"/> maps the shared model
    /// value to the VM's value representation (e.g. int Port → string for the TextBox).
    /// </summary>
    private BulkEditField<TVm> BuildField<TModel, TVm>(
        Func<ConnectionModel, TModel> selector,
        TModel _,
        Func<TModel, TVm> project,
        TVm blankValue)
    {
        var distinct = _selected.Select(selector).Distinct().Count();
        var isShared = distinct == 1 && _selected.Count > 0;
        var value = isShared ? project(selector(_selected[0])) : blankValue;
        return new BulkEditField<TVm>(isShared, value, RaiseCanApplyChanged);
    }

    private void RaiseCanApplyChanged() => OnPropertyChanged(nameof(CanApply));
}

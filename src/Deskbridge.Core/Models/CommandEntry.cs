using Wpf.Ui.Controls;

namespace Deskbridge.Core.Models;

/// <summary>
/// Phase 6 Plan 06-03 (CMD-02): one palette command.
/// <para><see cref="Id"/> is stable across renames (survives UI-SPEC copy tweaks);
/// <see cref="Title"/> and <see cref="Aliases"/> drive the fuzzy match in
/// <c>ICommandPaletteService.ScoreCommand</c>.</para>
/// <para><see cref="ExecuteAsync"/> is the concrete action — the
/// <c>CommandPaletteService</c> registers these as closures that delegate to
/// <c>MainWindowViewModel</c> / <c>ConnectionTreeViewModel</c> commands so the
/// palette doesn't depend on the WPF exe project.</para>
/// </summary>
public sealed record CommandEntry(
    string Id,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Aliases,
    SymbolRegular Icon,
    string? Shortcut,
    Func<Task> ExecuteAsync);

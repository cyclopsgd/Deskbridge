using System.Collections.ObjectModel;
using Deskbridge.Core.Interfaces;

namespace Deskbridge.ViewModels;

/// <summary>
/// Phase 6 Plan 06-03 (CMD-01 / CMD-03 / D-02): the palette dialog's DataContext.
/// <para>Empty-state branch (<see cref="SearchText"/> is empty): top-5 recents from
/// <see cref="IConnectionQuery.GetRecent"/> FIRST, then the 4 D-04 commands from
/// <see cref="ICommandPaletteService.Commands"/> (D-02).</para>
/// <para>Ranked-merge branch: calls <see cref="IConnectionQuery.Search"/> exactly
/// once per SearchText change (CMD-03 — no duplication of the Phase 3 scorer) and
/// scores each <see cref="Deskbridge.Core.Models.CommandEntry"/> via
/// <see cref="ICommandPaletteService.ScoreCommand"/>. Rows sort by score desc
/// (commands first on ties), then alphabetical by Title.</para>
/// <para>Connection scores are approximated as <c>100 - index</c> from the
/// <see cref="IConnectionQuery.Search"/> result (already ranked). This preserves
/// the Phase 3 Search ordering of connection rows relative to each other while
/// letting commands interleave by their absolute scores. A full numeric-score
/// refactor of <see cref="IConnectionQuery"/> would risk the Phase 3 surface and
/// is deferred.</para>
/// </summary>
public partial class CommandPaletteViewModel : ObservableObject
{
    private readonly IConnectionQuery _query;
    private readonly ICommandPaletteService _palette;
    private readonly IEventBus _bus;

    public CommandPaletteViewModel(IConnectionQuery query, ICommandPaletteService palette, IEventBus bus)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(bus);

        _query = query;
        _palette = palette;
        _bus = bus;

        Refresh();  // Initial empty-state render — D-02 shows recents + commands.
    }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial CommandPaletteRowViewModel? SelectedItem { get; set; }

    /// <summary>
    /// Unified row collection. Cleared and rebuilt on every <see cref="SearchText"/>
    /// change. Ordered by the VM's ranked-merge (empty state: recents then commands).
    /// </summary>
    public ObservableCollection<CommandPaletteRowViewModel> Items { get; } = new();

    partial void OnSearchTextChanged(string value) => Refresh();

    private void Refresh()
    {
        Items.Clear();
        var q = SearchText?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(q))
        {
            // D-02 empty state: top 5 recents (connection rows) THEN the 4 commands.
            foreach (var c in _query.GetRecent(5))
            {
                Items.Add(new CommandPaletteRowViewModel(c, score: 0, _bus));
            }
            foreach (var cmd in _palette.Commands)
            {
                Items.Add(new CommandPaletteRowViewModel(cmd, score: 0));
            }
        }
        else
        {
            // CMD-03: IConnectionQuery.Search called EXACTLY ONCE per keystroke; no
            // reimplementation of the dual-score algorithm. Score proxy via 100-index.
            var searchResults = _query.Search(q);
            var connectionRows = new List<CommandPaletteRowViewModel>(searchResults.Count);
            for (int i = 0; i < searchResults.Count; i++)
            {
                connectionRows.Add(new CommandPaletteRowViewModel(searchResults[i], score: 100 - i, _bus));
            }

            var commandRows = new List<CommandPaletteRowViewModel>();
            foreach (var cmd in _palette.Commands)
            {
                var score = _palette.ScoreCommand(cmd, q);
                if (score > 0)
                {
                    commandRows.Add(new CommandPaletteRowViewModel(cmd, score));
                }
            }

            // Merge: Score desc → commands before connections on ties → alphabetical Title.
            // The IsCommand tiebreaker is explicit per RESEARCH Pattern 2.2 — when a command
            // and a connection score the same, the user almost always wants the command.
            var merged = commandRows
                .Concat(connectionRows)
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.IsCommand ? 0 : 1)
                .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase);

            foreach (var row in merged)
            {
                Items.Add(row);
            }
        }

        // UX: ListBox selection always anchored to the top of the refreshed list so
        // Enter executes the best match (D-02).
        SelectedItem = Items.FirstOrDefault();
    }

    [RelayCommand]
    public async Task ExecuteSelectedAsync()
    {
        if (SelectedItem is null) return;
        await SelectedItem.ExecuteAsync();
    }
}

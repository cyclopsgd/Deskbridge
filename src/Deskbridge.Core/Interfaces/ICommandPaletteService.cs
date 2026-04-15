using Deskbridge.Core.Models;

namespace Deskbridge.Core.Interfaces;

/// <summary>
/// Phase 6 Plan 06-03 (CMD-02 / CMD-03): registry of palette commands and the
/// command-side fuzzy scorer. CMD-03 mandates that the connection-side scoring
/// reuses <see cref="IConnectionQuery.Search"/> — <see cref="ScoreCommand"/>
/// applies the same rules (Title=100 substring, Aliases=80 substring, Title=40
/// subsequence fallback) so command results rank alongside connection results
/// without a second scoring algorithm.
/// </summary>
public interface ICommandPaletteService
{
    /// <summary>The 4 D-04 canonical commands: new-connection, settings, disconnect-all, quick-connect.</summary>
    IEnumerable<CommandEntry> Commands { get; }

    /// <summary>
    /// Score <paramref name="command"/> against <paramref name="query"/>.
    /// Parity with <c>ConnectionQueryService.CalculateScore</c>:
    /// <list type="bullet">
    /// <item>Substring on <see cref="CommandEntry.Title"/> → 100</item>
    /// <item>Substring on any <see cref="CommandEntry.Aliases"/> entry → 80 (standing in for Hostname's 80-slot)</item>
    /// <item>Subsequence on Title → 40 (only when no substring match landed — matches ConnectionQueryService's zero-score gate)</item>
    /// <item>Empty / whitespace query → 0 (caller is responsible for the empty-state branch)</item>
    /// </list>
    /// Case-insensitive via <see cref="StringComparison.OrdinalIgnoreCase"/>.
    /// </summary>
    int ScoreCommand(CommandEntry command, string query);
}

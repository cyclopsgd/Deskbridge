using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Wpf.Ui.Controls;

namespace Deskbridge.Core.Services;

/// <summary>
/// Phase 6 Plan 06-03 (CMD-02 / CMD-03): registry of the 4 D-04 palette commands +
/// the command-side fuzzy scorer. The 4 <see cref="Func{Task}"/> ctor parameters
/// are closures that delegate to the ViewModel-hosted commands — this keeps
/// <c>Deskbridge.Core</c> free of WPF exe dependencies while still letting the DI
/// factory in <c>App.ConfigureServices</c> wire <c>MainWindowViewModel</c> /
/// <c>ConnectionTreeViewModel</c> methods as the actual handlers.
/// </summary>
public sealed class CommandPaletteService : ICommandPaletteService
{
    private readonly List<CommandEntry> _commands;

    public CommandPaletteService(
        Func<Task> newConnection,
        Func<Task> openSettings,
        Func<Task> disconnectAll,
        Func<Task> quickConnect)
    {
        ArgumentNullException.ThrowIfNull(newConnection);
        ArgumentNullException.ThrowIfNull(openSettings);
        ArgumentNullException.ThrowIfNull(disconnectAll);
        ArgumentNullException.ThrowIfNull(quickConnect);

        // D-04: exactly these 4 commands, in this order. UI-SPEC §Command Palette
        // Copywriting (lines 340-365) pins Title / Shortcut / Icon verbatim.
        _commands = new List<CommandEntry>
        {
            new(
                Id: "new-connection",
                Title: "New Connection",
                Subtitle: null,
                Aliases: new[] { "create", "add" },
                Icon: SymbolRegular.Add24,
                Shortcut: "Ctrl+N",
                ExecuteAsync: newConnection),

            new(
                Id: "settings",
                Title: "Settings",
                Subtitle: null,
                Aliases: new[] { "preferences", "options" },
                Icon: SymbolRegular.Settings24,
                Shortcut: null,
                ExecuteAsync: openSettings),

            new(
                Id: "disconnect-all",
                Title: "Disconnect All",
                Subtitle: "Closes every open RDP tab",
                Aliases: new[] { "close all", "end all" },
                Icon: SymbolRegular.PlugDisconnected24,
                Shortcut: null,
                ExecuteAsync: disconnectAll),

            new(
                Id: "quick-connect",
                Title: "Quick Connect",
                Subtitle: "Connect by typing a hostname",
                Aliases: new[] { "qc", "connect to" },
                Icon: SymbolRegular.PlugConnected24,
                Shortcut: "Ctrl+T",
                ExecuteAsync: quickConnect),
        };
    }

    public IEnumerable<CommandEntry> Commands => _commands;

    public int ScoreCommand(CommandEntry command, string query)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(query)) return 0;
        var q = query.Trim();

        // Parity with ConnectionQueryService.CalculateScore:
        // - Substring Title = 100
        // - Substring Alias = 80 (standing in for Hostname's 80-slot in the connection scorer)
        if (command.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) return 100;
        foreach (var alias in command.Aliases)
        {
            if (alias.Contains(q, StringComparison.OrdinalIgnoreCase)) return 80;
        }

        // Subsequence fallback on Title = 40. Mirrors ConnectionQueryService's
        // `if (score == 0)` gate — subsequence is checked only when no substring matched.
        if (IsSubsequence(q, command.Title)) return 40;
        return 0;
    }

    private static bool IsSubsequence(string query, string target)
    {
        var q = query.ToLowerInvariant();
        var t = target.ToLowerInvariant();
        int qi = 0;
        for (int ti = 0; ti < t.Length && qi < q.Length; ti++)
        {
            if (q[qi] == t[ti]) qi++;
        }
        return qi == q.Length;
    }
}

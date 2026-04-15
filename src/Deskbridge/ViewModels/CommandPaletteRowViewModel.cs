using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Wpf.Ui.Controls;

namespace Deskbridge.ViewModels;

/// <summary>
/// Phase 6 Plan 06-03 (CMD-01 / CMD-02): unified palette row.
/// <see cref="IsCommand"/> distinguishes the two execution paths:
/// <list type="bullet">
/// <item><c>IsCommand=false</c>: publish <see cref="ConnectionRequestedEvent"/> for the wrapped <see cref="ConnectionModel"/>.</item>
/// <item><c>IsCommand=true</c>: invoke the wrapped <see cref="CommandEntry.ExecuteAsync"/> delegate.</item>
/// </list>
/// <see cref="Score"/> is populated by the VM's ranked-merge so the ListBox sort
/// is consistent between command and connection rows (CMD-03).
/// </summary>
public sealed class CommandPaletteRowViewModel
{
    private readonly ConnectionModel? _connection;
    private readonly CommandEntry? _command;
    private readonly IEventBus? _bus;

    /// <summary>Connection row ctor — wraps a <see cref="ConnectionModel"/> for publish via <paramref name="bus"/>.</summary>
    public CommandPaletteRowViewModel(ConnectionModel connection, int score, IEventBus bus)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(bus);

        _connection = connection;
        _bus = bus;
        Title = string.IsNullOrWhiteSpace(connection.Name) ? connection.Hostname : connection.Name;
        // UI-SPEC §Row anatomy: Subtitle hidden when Hostname equals Title (avoids
        // duplicate "host01 / host01" rendering). Ordinal equality — case-sensitive
        // host shouldn't collapse with differently-cased Name.
        Subtitle = string.Equals(Title, connection.Hostname, StringComparison.Ordinal)
            ? null
            : connection.Hostname;
        Icon = SymbolRegular.Desktop24;
        Shortcut = null;
        Score = score;
        IsCommand = false;
    }

    /// <summary>Command row ctor — wraps a <see cref="CommandEntry"/> for delegate invoke.</summary>
    public CommandPaletteRowViewModel(CommandEntry command, int score)
    {
        ArgumentNullException.ThrowIfNull(command);

        _command = command;
        Title = command.Title;
        Subtitle = command.Subtitle;
        Icon = command.Icon;
        Shortcut = command.Shortcut;
        Score = score;
        IsCommand = true;
    }

    public string Title { get; }
    public string? Subtitle { get; }
    public SymbolRegular Icon { get; }
    public string? Shortcut { get; }
    public int Score { get; }
    public bool IsCommand { get; }

    /// <summary>
    /// Underlying Guid for connection rows so the palette consumer can correlate
    /// the selected row back to the connection (tests + potential future auditing).
    /// Null for command rows.
    /// </summary>
    public Guid? ConnectionId => _connection?.Id;

    /// <summary>Underlying command id for command rows; null for connection rows.</summary>
    public string? CommandId => _command?.Id;

    /// <summary>
    /// Execute the row's action.
    /// <list type="bullet">
    /// <item>Connection row: publish <see cref="ConnectionRequestedEvent"/> — the existing Phase 4 pipeline takes over.</item>
    /// <item>Command row: invoke <see cref="CommandEntry.ExecuteAsync"/>.</item>
    /// </list>
    /// </summary>
    public Task ExecuteAsync()
    {
        if (IsCommand)
        {
            return _command!.ExecuteAsync();
        }

        _bus!.Publish(new ConnectionRequestedEvent(_connection!));
        return Task.CompletedTask;
    }
}

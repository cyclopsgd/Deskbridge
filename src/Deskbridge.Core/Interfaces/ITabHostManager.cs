namespace Deskbridge.Core.Interfaces;

/// <summary>
/// Multi-host tab state owner (D-01). Phase 5 replacement for the Phase 4 single-slot
/// <c>_active</c> field on <see cref="IConnectionCoordinator"/>. Subscribes to coordinator
/// <c>HostMounted</c> / <c>HostUnmounted</c> / <c>ReconnectOverlayRequested</c> and republishes
/// as <c>TabOpenedEvent</c> / <c>TabClosedEvent</c> / <c>TabSwitchedEvent</c> /
/// <c>TabStateChangedEvent</c>.
///
/// <para>Kept free of WPF types (D-10 Core boundary) so ViewModels consume the interface
/// without dragging in AxHost or WindowsFormsHost references.</para>
/// </summary>
public interface ITabHostManager
{
    /// <summary>Host for a given connection id, or null if no tab is open. D-03.</summary>
    IProtocolHost? GetHost(Guid connectionId);

    /// <summary>
    /// True if a tab exists for the connection. D-02 publisher-side switch-to-existing check —
    /// <c>ConnectionTreeViewModel.Connect</c> calls this BEFORE publishing
    /// <c>ConnectionRequestedEvent</c> to decide whether to fire the pipeline or just
    /// <see cref="SwitchTo"/> the existing tab.
    /// </summary>
    bool TryGetExistingTab(Guid connectionId, out IProtocolHost host);

    /// <summary>Current live tab count. Drives D-09 15+ threshold.</summary>
    int ActiveCount { get; }

    /// <summary>Currently active tab id (the one whose host has <c>Visibility.Visible</c>). Null when no tabs.</summary>
    Guid? ActiveId { get; }

    /// <summary>All known hosts. D-07 Close All / D-08 app shutdown.</summary>
    IReadOnlyCollection<IProtocolHost> AllHosts { get; }

    /// <summary>Switch focus to an existing tab. Publishes <c>TabSwitchedEvent</c>. No-op if connection is not open.</summary>
    void SwitchTo(Guid connectionId);

    /// <summary>Close one tab via <see cref="IDisconnectPipeline"/>. D-06.</summary>
    Task CloseTabAsync(Guid connectionId);

    /// <summary>Close all tabs except the specified one. D-07 context menu.</summary>
    Task CloseOthersAsync(Guid keepConnectionId);

    /// <summary>Close every tab sequentially. D-07 context menu + D-08 app shutdown.</summary>
    Task CloseAllAsync();

    /// <summary>Pop the most-recently-closed connection id. Null when LRU empty. D-16 Ctrl+Shift+T.</summary>
    Guid? PopLastClosed();
}

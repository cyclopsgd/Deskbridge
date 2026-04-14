namespace Deskbridge.Core.Models;

/// <summary>
/// Per-tab lifecycle state driving the D-12 visual indicators.
/// Mutually exclusive — at most one of ProgressRing / amber dot / red dot is visible.
/// Member order and names are load-bearing: Plan 05-03 XAML bindings reference these
/// names directly via value-converter bindings. Do not rename or reorder.
/// </summary>
public enum TabState
{
    /// <summary>
    /// ConnectionRequestedEvent fired -&gt; pipeline running.
    /// Renders a 12px WPF-UI ProgressRing to the left of the title.
    /// Default value (0).
    /// </summary>
    Connecting,

    /// <summary>
    /// ConnectionEstablishedEvent received. No indicator in the tab label;
    /// the active tab accent border communicates focus independently.
    /// </summary>
    Connected,

    /// <summary>
    /// RdpReconnectCoordinator backoff loop is running for this connection.
    /// Renders an 8px amber Ellipse (DeskbridgeWarningBrush #FFCC02) to the left of the title.
    /// Visible on both active and inactive tabs (D-12 background-tab visibility).
    /// </summary>
    Reconnecting,

    /// <summary>
    /// Auth failure or retry cap hit. Renders an 8px red Ellipse
    /// (DeskbridgeErrorBrush #F44747) to the left of the title. Cleared when the
    /// user actions the reconnect overlay or a subsequent reconnect succeeds.
    /// </summary>
    Error
}

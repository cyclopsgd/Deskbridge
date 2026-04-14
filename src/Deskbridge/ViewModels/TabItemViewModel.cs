using Deskbridge.Core.Models;

namespace Deskbridge.ViewModels;

public partial class TabItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>
    /// Phase 5 D-12: per-tab lifecycle state driving the mutually-exclusive
    /// ProgressRing / amber dot / red dot indicators (see UI-SPEC §Per-Tab State
    /// Visual Contract). Default <see cref="TabState.Connecting"/> on creation;
    /// <see cref="MainWindowViewModel.OnTabStateChanged"/> transitions on
    /// <c>TabStateChangedEvent</c>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnecting))]
    [NotifyPropertyChangedFor(nameof(IsReconnecting))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(TooltipText))]
    public partial TabState State { get; set; } = TabState.Connecting;

    /// <summary>
    /// Hostname (from ConnectionModel). Populated by MainWindowViewModel.OnTabOpened.
    /// Used by TooltipText. Never includes credential fields (T-05-01).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TooltipText))]
    public partial string Hostname { get; set; } = string.Empty;

    /// <summary>
    /// Optional session resolution for the tooltip. Populated by MainWindowViewModel
    /// after OnLoginComplete when DesktopWidth/Height become non-zero. Null/zero
    /// renders as em-dash (UI-SPEC fallback rule).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TooltipText))]
    public partial (int Width, int Height)? Resolution { get; set; }

    /// <summary>
    /// Current reconnect attempt number (1-20) when State == Reconnecting.
    /// Zero otherwise. Surfaces in the Reconnecting tooltip "{Hostname} · Reconnecting attempt {N}/20".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TooltipText))]
    public partial int ReconnectAttempt { get; set; }

    public Guid ConnectionId { get; init; }

    /// <summary>D-12: exactly one of these is true at a time. Connected -> all three false.</summary>
    public bool IsConnecting => State == TabState.Connecting;

    public bool IsReconnecting => State == TabState.Reconnecting;

    public bool IsError => State == TabState.Error;

    /// <summary>
    /// UI-SPEC §Copywriting Contract — tab tooltip per state.
    /// Punctuation: U+00B7 middle-dot, U+2026 ellipsis, U+00D7 multiplication, U+2014 em-dash.
    /// Never interpolates credentials or any secret (T-05-01).
    /// </summary>
    public string TooltipText => State switch
    {
        TabState.Connecting => $"{Hostname} \u00B7 Connecting\u2026",
        TabState.Connected => Resolution is ({ } w, { } h) && w > 0 && h > 0
            ? $"{Hostname} \u00B7 {w}\u00D7{h}"
            : $"{Hostname} \u00B7 \u2014",
        TabState.Reconnecting => $"{Hostname} \u00B7 Reconnecting attempt {ReconnectAttempt}/20",
        TabState.Error => $"{Hostname} \u00B7 Connection failed \u2014 click tab to reconnect",
        _ => Hostname,
    };
}

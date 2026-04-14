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
    public partial TabState State { get; set; } = TabState.Connecting;

    public Guid ConnectionId { get; init; }
}

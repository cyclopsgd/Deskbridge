using System.Collections.ObjectModel;
using Deskbridge.Models;

namespace Deskbridge.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(ConnectionTreeViewModel connectionTree)
    {
        ConnectionTree = connectionTree;
    }

    // Expose ConnectionTreeViewModel for Ctrl+N binding in MainWindow
    [ObservableProperty]
    public partial ConnectionTreeViewModel ConnectionTree { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "Deskbridge";

    // Panel state (per D-04 VS Code toggle pattern)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsConnectionsActive))]
    [NotifyPropertyChangedFor(nameof(IsSearchActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive))]
    public partial PanelMode ActivePanelMode { get; set; } = PanelMode.Connections;

    public bool IsPanelVisible => ActivePanelMode != PanelMode.None;
    public bool IsConnectionsActive => ActivePanelMode == PanelMode.Connections;
    public bool IsSearchActive => ActivePanelMode == PanelMode.Search;
    public bool IsSettingsActive => ActivePanelMode == PanelMode.Settings;

    // Tab state
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTabs))]
    public partial ObservableCollection<TabItemViewModel> Tabs { get; set; } = [];

    [ObservableProperty]
    public partial TabItemViewModel? ActiveTab { get; set; }

    public bool HasNoTabs => Tabs.Count == 0;

    // Status bar
    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty]
    public partial string StatusSecondary { get; set; } = "No active connection";

    // Commands
    [RelayCommand]
    private void TogglePanel(PanelMode mode)
    {
        ActivePanelMode = ActivePanelMode == mode ? PanelMode.None : mode;
    }

    [RelayCommand]
    private void CloseTab(TabItemViewModel tab)
    {
        Tabs.Remove(tab);
        if (ActiveTab == tab)
        {
            ActiveTab = Tabs.Count > 0 ? Tabs[^1] : null;
            if (ActiveTab is not null) ActiveTab.IsActive = true;
        }
        OnPropertyChanged(nameof(HasNoTabs));
    }

    [RelayCommand]
    private void SwitchTab(TabItemViewModel tab)
    {
        if (ActiveTab is not null) ActiveTab.IsActive = false;
        ActiveTab = tab;
        tab.IsActive = true;
    }
}

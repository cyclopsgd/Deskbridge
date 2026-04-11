using System.Collections.Specialized;
using Deskbridge.Core.Models;
using Deskbridge.ViewModels;

namespace Deskbridge.Views;

public partial class ConnectionTreeControl : UserControl
{
    private readonly ConnectionTreeViewModel _viewModel;

    public ConnectionTreeControl(ConnectionTreeViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Populate CredentialMode ComboBox with enum values
        CredentialModeCombo.ItemsSource = Enum.GetValues<CredentialMode>();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.LoadTree();
        UpdateEmptyState();

        // Track RootItems changes for empty state visibility
        _viewModel.RootItems.CollectionChanged += RootItems_CollectionChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionTreeViewModel.RootItems))
        {
            UpdateEmptyState();
            // Re-subscribe to the new collection
            _viewModel.RootItems.CollectionChanged += RootItems_CollectionChanged;
        }
    }

    private void RootItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyStateOverlay.Visibility = _viewModel.RootItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // Quick properties inline edit handlers — save on focus loss
    private void QuickProperty_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_viewModel.PrimarySelectedItem is ConnectionTreeItemViewModel connVm)
        {
            _viewModel.SaveConnectionFromQuickEdit(connVm);
        }
    }

    private void GroupQuickProperty_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_viewModel.PrimarySelectedItem is GroupTreeItemViewModel groupVm)
        {
            _viewModel.SaveGroupFromQuickEdit(groupVm);
        }
    }

    private void CredentialMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.PrimarySelectedItem is ConnectionTreeItemViewModel connVm)
        {
            _viewModel.SaveConnectionFromQuickEdit(connVm);
        }
    }
}

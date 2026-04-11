using System.Collections.Specialized;
using System.Windows.Input;
using System.Windows.Media;
using Deskbridge.Core.Models;
using Deskbridge.ViewModels;

namespace Deskbridge.Views;

public partial class ConnectionTreeControl : UserControl
{
    private readonly ConnectionTreeViewModel _viewModel;

    // Original name for Escape-cancel is stored on _viewModel.OriginalNameBeforeRename
    // (set by the RenameItem command before IsRenaming=true)

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
        UpdateQuickPropertiesRowHeight();

        // Track RootItems changes for empty state visibility
        _subscribedRootItems = _viewModel.RootItems;
        _subscribedRootItems.CollectionChanged += RootItems_CollectionChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    // Track the collection we're subscribed to, so we can unsubscribe when RootItems changes
    private System.Collections.ObjectModel.ObservableCollection<TreeItemViewModel>? _subscribedRootItems;

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionTreeViewModel.RootItems))
        {
            UpdateEmptyState();
            // Unsubscribe from the old collection before subscribing to the new one
            if (_subscribedRootItems is not null)
            {
                _subscribedRootItems.CollectionChanged -= RootItems_CollectionChanged;
            }
            _subscribedRootItems = _viewModel.RootItems;
            _subscribedRootItems.CollectionChanged += RootItems_CollectionChanged;
        }
        else if (e.PropertyName == nameof(ConnectionTreeViewModel.IsQuickPropertiesVisible))
        {
            UpdateQuickPropertiesRowHeight();
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

    private void UpdateQuickPropertiesRowHeight()
    {
        if (_viewModel.IsQuickPropertiesVisible)
        {
            QuickPropertiesRow.MinHeight = 80;
            QuickPropertiesRow.Height = new GridLength(120);
        }
        else
        {
            QuickPropertiesRow.MinHeight = 0;
            QuickPropertiesRow.Height = new GridLength(0);
        }
    }

    // --- Keyboard shortcuts in TreeView ---

    private void TreeView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F2:
                // F2: Start inline rename on PrimarySelectedItem
                if (_viewModel.PrimarySelectedItem is not null)
                {
                    _viewModel.RenameItemCommand.Execute(_viewModel.PrimarySelectedItem);
                    e.Handled = true;
                }
                break;

            case Key.Delete:
                // Delete: Delete selected items with confirmation
                if (_viewModel.SelectedItems.Count > 0)
                {
                    _viewModel.DeleteSelectedCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            case Key.Enter:
                // Enter on connection: Connect (stub). On group: toggle expand/collapse.
                if (_viewModel.PrimarySelectedItem is GroupTreeItemViewModel group)
                {
                    group.IsExpanded = !group.IsExpanded;
                    e.Handled = true;
                }
                else if (_viewModel.PrimarySelectedItem is ConnectionTreeItemViewModel)
                {
                    _viewModel.ConnectCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            case Key.C when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                // Ctrl+C: Copy hostname of PrimarySelectedItem
                if (_viewModel.PrimarySelectedItem is ConnectionTreeItemViewModel connForCopy)
                {
                    _viewModel.CopyHostnameCommand.Execute(connForCopy);
                    e.Handled = true;
                }
                break;

            case Key.Escape:
                // Escape: Deselect all
                foreach (var item in _viewModel.SelectedItems)
                {
                    item.IsSelected = false;
                }
                _viewModel.SelectedItems.Clear();
                _viewModel.PrimarySelectedItem = null;
                e.Handled = true;
                break;
        }
    }

    // --- Double-click: open editor dialog ---

    private void TreeView_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject dblClickSource) return;
        var treeViewItem = FindAncestor<TreeViewItem>(dblClickSource);
        if (treeViewItem is null) return;

        if (treeViewItem.DataContext is not TreeItemViewModel item) return;

        if (item is ConnectionTreeItemViewModel)
        {
            // Double-click connection: open editor
            _viewModel.EditItemCommand.Execute(item);
            e.Handled = true;
        }
        else if (item is GroupTreeItemViewModel group)
        {
            // Double-click group: toggle expand/collapse
            group.IsExpanded = !group.IsExpanded;
            e.Handled = true;
        }
    }

    // --- Right-click: assign context menu dynamically ---

    private void TreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject rightClickSource) return;
        var treeViewItem = FindAncestor<TreeViewItem>(rightClickSource);
        if (treeViewItem is null) return;

        if (treeViewItem.DataContext is not TreeItemViewModel item) return;

        // Select the item if not already in selection
        if (!item.IsSelected)
        {
            // Deselect others and select this one
            foreach (var sel in _viewModel.SelectedItems)
                sel.IsSelected = false;
            _viewModel.SelectedItems.Clear();

            item.IsSelected = true;
            _viewModel.SelectedItems.Add(item);
            _viewModel.PrimarySelectedItem = item;
        }

        // Determine which context menu to show
        if (_viewModel.SelectedItems.Count > 1)
        {
            // Multi-select context menu
            var menu = (ContextMenu)FindResource("MultiSelectContextMenu");
            // Update the header item with count
            if (menu.Items[0] is MenuItem header)
            {
                header.Header = $"{_viewModel.SelectedItems.Count} items selected";
            }
            PopulateMoveToSubmenu(menu);
            treeViewItem.ContextMenu = menu;
        }
        else if (item is ConnectionTreeItemViewModel)
        {
            var menu = (ContextMenu)FindResource("ConnectionContextMenu");
            PopulateMoveToSubmenu(menu);
            treeViewItem.ContextMenu = menu;
        }
        else if (item is GroupTreeItemViewModel)
        {
            var menu = (ContextMenu)FindResource("GroupContextMenu");
            PopulateMoveToSubmenu(menu);
            treeViewItem.ContextMenu = menu;
        }

        treeViewItem.Focus();
        e.Handled = false; // Let WPF show the context menu
    }

    /// <summary>
    /// Populate the "Move to..." submenu with available groups.
    /// </summary>
    private void PopulateMoveToSubmenu(ContextMenu menu)
    {
        // Find the "Move to..." MenuItem in the context menu
        MenuItem? moveToItem = null;
        foreach (var obj in menu.Items)
        {
            if (obj is MenuItem mi && mi.Header is string headerStr && headerStr == "Move to...")
            {
                moveToItem = mi;
                break;
            }
        }

        if (moveToItem is null) return;

        moveToItem.Items.Clear();

        // Add "(Root)" option
        var rootMenuItem = new MenuItem
        {
            Header = "(Root)",
            Command = _viewModel.MoveToGroupCommand,
            CommandParameter = (Guid?)null,
        };

        // Disable if all selected items are already at root
        bool allAtRoot = _viewModel.SelectedItems.All(i =>
            (i is ConnectionTreeItemViewModel c && c.GroupId is null) ||
            (i is GroupTreeItemViewModel g && g.ParentGroupId is null));
        rootMenuItem.IsEnabled = !allAtRoot;

        moveToItem.Items.Add(rootMenuItem);
        moveToItem.Items.Add(new Separator());

        // Add all groups with depth indentation
        var groups = _viewModel.GetAvailableGroupsForMove();
        foreach (var (id, displayName, depth) in groups)
        {
            var groupMenuItem = new MenuItem
            {
                Header = displayName,
                Padding = new Thickness(16 * depth, 0, 0, 0),
                Command = _viewModel.MoveToGroupCommand,
                CommandParameter = (Guid?)id,
            };

            // Disable the current parent group
            bool isCurrentParent = _viewModel.SelectedItems.All(i =>
                (i is ConnectionTreeItemViewModel c && c.GroupId == id) ||
                (i is GroupTreeItemViewModel g && g.ParentGroupId == id));
            if (isCurrentParent && _viewModel.SelectedItems.Count > 0)
            {
                groupMenuItem.IsEnabled = false;
            }

            moveToItem.Items.Add(groupMenuItem);
        }
    }

    // --- Rename TextBox handlers ---

    private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (textBox.DataContext is not TreeItemViewModel item) return;

        switch (e.Key)
        {
            case Key.Enter:
                // Commit rename
                CommitRename(item);
                e.Handled = true;
                break;

            case Key.Escape:
                // Cancel rename - restore original name
                if (_viewModel.OriginalNameBeforeRename is not null)
                {
                    item.Name = _viewModel.OriginalNameBeforeRename;
                }
                item.IsRenaming = false;
                _viewModel.OriginalNameBeforeRename = null;
                e.Handled = true;
                break;
        }
    }

    private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (textBox.DataContext is not TreeItemViewModel item || !item.IsRenaming) return;

        // Commit rename on focus loss
        CommitRename(item);
    }

    private void RenameTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        // When the rename TextBox appears, focus it and select all text
        if (sender is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private void CommitRename(TreeItemViewModel item)
    {
        item.IsRenaming = false;
        _viewModel.OriginalNameBeforeRename = null;

        var name = item.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name)) return;

        // Persist the renamed item
        if (item is ConnectionTreeItemViewModel connVm)
        {
            _viewModel.SaveConnectionFromQuickEdit(connVm);
        }
        else if (item is GroupTreeItemViewModel groupVm)
        {
            _viewModel.SaveGroupFromQuickEdit(groupVm);
        }
    }

    // Quick properties inline edit handlers -- save on focus loss
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

    // --- Visual tree helper ---

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T target) return target;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}

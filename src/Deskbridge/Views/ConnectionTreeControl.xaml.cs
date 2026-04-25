using System.Collections.Specialized;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Deskbridge.ViewModels;

namespace Deskbridge.Views;

public partial class ConnectionTreeControl : UserControl
{
    private readonly ConnectionTreeViewModel _viewModel;

    // Persist user-resized panel height across collapse/expand cycles. See
    // WPF-TREEVIEW-PATTERNS.md Section 3 — GridSplitter writes pixel values
    // with GridUnitType.Star (dotnet/wpf#4392), so we capture ActualHeight
    // in DragCompleted rather than binding RowDefinition.Height directly.
    //
    // Row 3 now contains BOTH the 28px header and the body, so heights below
    // include the header strip. Collapsed = HeaderHeight (28). Expanded min =
    // HeaderHeight + ~80px of body = 108.
    private const double HeaderHeight = 28;
    private const double MinExpandedPanelHeight = 108;
    private GridLength _savedPanelHeight = new(290, GridUnitType.Pixel);

    public ConnectionTreeControl(ConnectionTreeViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Unsubscribe to avoid leaking the control via the ViewModel's event list.
        // The ViewModel is DI-resolved and can outlive the control (transient vs singleton).
        if (_subscribedRootItems is not null)
        {
            _subscribedRootItems.CollectionChanged -= RootItems_CollectionChanged;
            _subscribedRootItems = null;
        }
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    // Track the collection we're subscribed to, so we can unsubscribe when RootItems changes
    private System.Collections.ObjectModel.ObservableCollection<TreeItemViewModel>? _subscribedRootItems;

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionTreeViewModel.RootItems))
        {
            UpdateEmptyState();
            if (_subscribedRootItems is not null)
            {
                _subscribedRootItems.CollectionChanged -= RootItems_CollectionChanged;
            }
            _subscribedRootItems = _viewModel.RootItems;
            _subscribedRootItems.CollectionChanged += RootItems_CollectionChanged;
        }
        else if (e.PropertyName == nameof(ConnectionTreeViewModel.IsQuickPropertiesExpanded))
        {
            UpdateQuickPropertiesRowHeight();
        }
        else if (e.PropertyName == nameof(ConnectionTreeViewModel.HasStoredCredential)
              || e.PropertyName == nameof(ConnectionTreeViewModel.PrimarySelectedItem))
        {
            UpdatePasswordFieldVisibility();
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

    /// <summary>
    /// Expand/collapse the combined quick-properties row. Row 3 holds both the
    /// 28px header (always visible) and the body (toggled via Visibility). On
    /// collapse we save the current height so re-expanding restores the user's
    /// preferred size, and shrink the row to HeaderHeight.
    /// </summary>
    private void UpdateQuickPropertiesRowHeight()
    {
        if (_viewModel.IsQuickPropertiesExpanded)
        {
            QuickPropertiesRow.MinHeight = MinExpandedPanelHeight;
            QuickPropertiesRow.Height = _savedPanelHeight;
        }
        else
        {
            // Save current height before collapsing (only if not already collapsed)
            double actual = QuickPropertiesRow.ActualHeight;
            if (actual >= MinExpandedPanelHeight)
                _savedPanelHeight = new GridLength(actual, GridUnitType.Pixel);

            QuickPropertiesRow.MinHeight = HeaderHeight;
            QuickPropertiesRow.Height = new GridLength(HeaderHeight, GridUnitType.Pixel);
        }
    }

    /// <summary>
    /// Persist user-resized panel height after a GridSplitter drag completes.
    /// Critical: read ActualHeight, not RowDefinition.Height — dotnet/wpf#4392
    /// causes GridSplitter to write GridUnitType.Star values that corrupt the
    /// stored GridLength.
    /// </summary>
    private void PanelSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        double actual = QuickPropertiesRow.ActualHeight;
        if (actual >= MinExpandedPanelHeight)
            _savedPanelHeight = new GridLength(actual, GridUnitType.Pixel);
    }

    // --- Keyboard shortcuts in TreeView ---

    private void TreeView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
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
                // Escape: Deselect all (snapshot SelectedItems before iterating — IsSelected
                // setters shouldn't mutate the collection today but defense-in-depth is cheap).
                {
                    var snapshot = _viewModel.SelectedItems.ToList();
                    foreach (var item in snapshot)
                    {
                        item.IsSelected = false;
                    }
                }
                _viewModel.SelectedItems.Clear();
                _viewModel.PrimarySelectedItem = null;
                e.Handled = true;
                break;
        }
    }

    // --- Double-click: connect (or toggle group expand) ---

    private void TreeView_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject dblClickSource) return;
        var treeViewItem = FindAncestor<TreeViewItem>(dblClickSource);
        if (treeViewItem is null) return;

        if (treeViewItem.DataContext is not TreeItemViewModel item) return;

        if (item is ConnectionTreeItemViewModel)
        {
            // Double-click connection: connect (Phase 4/5 will wire this to the RDP pipeline)
            _viewModel.ConnectCommand.Execute(item);
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
            // Deselect others (snapshot to avoid modification during enumeration) and select this one
            var snapshot = _viewModel.SelectedItems.ToList();
            foreach (var sel in snapshot)
                sel.IsSelected = false;
            _viewModel.SelectedItems.Clear();

            item.IsSelected = true;
            _viewModel.SelectedItems.Add(item);
            _viewModel.PrimarySelectedItem = item;
        }

        // Determine which context menu to show
        //
        // CONTEXT-MENU BINDING CONTRACT (see ConnectionTreeControl.xaml)
        // -- The menus are x:Shared="False" resources; each FindResource() call returns a
        //    FRESH ContextMenu instance.
        // -- We set menu.DataContext = _viewModel (the tree VM). MenuItem Command bindings
        //    resolve against the tree VM via inherited DataContext. CommandParameter bindings
        //    use PlacementTarget.DataContext (i.e. the TreeViewItem's item VM).
        // -- RelativeSource AncestorType=TreeView bindings DO NOT work here because the
        //    menu's logical tree is rooted in the UserControl.Resources, not the TreeView.
        if (_viewModel.SelectedItems.Count > 1)
        {
            // Multi-select context menu
            var menu = (ContextMenu)FindResource("MultiSelectContextMenu");
            menu.DataContext = _viewModel;
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
            menu.DataContext = _viewModel;
            PopulateMoveToSubmenu(menu);
            treeViewItem.ContextMenu = menu;
        }
        else if (item is GroupTreeItemViewModel)
        {
            var menu = (ContextMenu)FindResource("GroupContextMenu");
            menu.DataContext = _viewModel;
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

    // Quick properties inline edit handlers -- save on focus loss
    //
    // Defensive guard: exceptions from these handlers propagate back into the WPF
    // input pipeline (PasswordBox.OnLostFocus → InputManager → Dispatcher) and
    // terminate the process. A misconfigured credential store must not kill the
    // UI thread. Log and swallow — the ViewModel is responsible for state, and
    // the user can retry via the editor dialog.
    private void QuickProperty_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel.PrimarySelectedItem is ConnectionTreeItemViewModel connVm)
            {
                _viewModel.SaveConnectionFromQuickEdit(connVm);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "QuickProperty_LostFocus handler threw");
        }
    }

    private bool _suppressPasswordChanged;

    private void UpdatePasswordFieldVisibility()
    {
        _suppressPasswordChanged = true;
        QuickPasswordBox.Password = "";
        _suppressPasswordChanged = false;
        Serilog.Log.Debug(
            "QuickPassword state: IsQuickPasswordVisible={Visible} ShowButton={Button} ShowField={Field} HasStored={Stored}",
            _viewModel.IsQuickPasswordVisible,
            _viewModel.ShowQuickPasswordButton,
            _viewModel.ShowQuickPasswordField,
            _viewModel.HasStoredCredential);
    }

    private void QuickPassword_GotFocus(object sender, RoutedEventArgs e)
    {
    }

    private void QuickPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressPasswordChanged) return;
        // Save handled on LostFocus via QuickProperty_LostFocus or dedicated handler
    }

    private void QuickPassword_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            var pw = QuickPasswordBox.Password;
            if (!string.IsNullOrEmpty(pw))
            {
                _viewModel.SaveQuickPassword(pw);
                _viewModel.RefreshStoredCredentialState();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "QuickPassword_LostFocus handler threw");
        }
    }

    private void QuickPasswordChange_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StartQuickPasswordChange();
    }

    private void QuickPasswordClear_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearQuickPassword();
        QuickPasswordBox.Password = "";
    }

    private void QuickPasswordCancel_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CancelQuickPasswordChange();
        QuickPasswordBox.Password = "";
    }

    private void GroupQuickProperty_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel.PrimarySelectedItem is GroupTreeItemViewModel groupVm)
            {
                _viewModel.SaveGroupFromQuickEdit(groupVm);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "GroupQuickProperty_LostFocus handler threw");
        }
    }

    private void ComboBox_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        e.Handled = true;
        var parent = FindAncestor<System.Windows.Controls.ScrollViewer>((DependencyObject)sender);
        parent?.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        });
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

using System.Windows.Input;
using System.Windows.Media;
using Deskbridge.ViewModels;

namespace Deskbridge.Behaviors;

/// <summary>
/// Attached behavior enabling multi-select on WPF TreeView via Ctrl+Click (toggle) and
/// Shift+Click (range). Uses the pattern documented in WPF-TREEVIEW-PATTERNS.md Section 1:
///
/// - Subscribes with <c>AddHandler(MouseLeftButtonDownEvent, handler, handledEventsToo: true)</c>
///   so that WPF's internal TreeViewItem.OnMouseLeftButtonDown (which sets e.Handled = true)
///   doesn't prevent the behavior from seeing the click.
/// - Drives selection via a ViewModel IsSelected property -- the DataTemplate's
///   highlight DataTrigger binds to that, not to TreeViewItem.IsSelected.
/// - Suppresses the native TreeView single-select highlight after each click so that
///   WPF's forced ChangeSelection doesn't clear our parallel multi-selection state.
/// - Plain click clears all and selects the clicked item (single-select behavior).
/// </summary>
public static class TreeViewMultiSelectBehavior
{
    public static readonly DependencyProperty EnableMultiSelectProperty =
        DependencyProperty.RegisterAttached(
            "EnableMultiSelect",
            typeof(bool),
            typeof(TreeViewMultiSelectBehavior),
            new PropertyMetadata(false, OnEnableMultiSelectChanged));

    public static bool GetEnableMultiSelect(DependencyObject obj) =>
        (bool)obj.GetValue(EnableMultiSelectProperty);

    public static void SetEnableMultiSelect(DependencyObject obj, bool value) =>
        obj.SetValue(EnableMultiSelectProperty, value);

    // Shift-click anchor. Tracks the last "plain" or "ctrl" click target so that shift-click
    // extends a range from that anchor. Static because the behavior is static; there is at
    // most one multi-select TreeView in the app at a time.
    private static TreeItemViewModel? _anchorItem;

    // Deferred-collapse state. When a user plain-clicks an item that is already part of
    // a multi-selection, we don't collapse the selection to just that item immediately
    // (doing so would break drag-to-move of a whole selection group, because the drag
    // doesn't start until MouseMove crosses the threshold — see WPF-TREEVIEW-PATTERNS
    // §4.5). Instead we remember the pending item and collapse on MouseUp only if no
    // drag occurred.
    private static TreeItemViewModel? _pendingCollapseItem;

    private static void OnEnableMultiSelectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeView treeView) return;

        if ((bool)e.NewValue)
        {
            // CRITICAL: handledEventsToo: true. Without this the handler never fires,
            // because TreeViewItem.OnMouseLeftButtonDown marks the event Handled before
            // it bubbles to the TreeView. This is the single most common reason
            // TreeView multi-select "doesn't work".
            treeView.AddHandler(
                UIElement.MouseLeftButtonDownEvent,
                new MouseButtonEventHandler(TreeView_MouseLeftButtonDown),
                handledEventsToo: true);
            treeView.AddHandler(
                UIElement.MouseLeftButtonUpEvent,
                new MouseButtonEventHandler(TreeView_MouseLeftButtonUp),
                handledEventsToo: true);
        }
        else
        {
            treeView.RemoveHandler(
                UIElement.MouseLeftButtonDownEvent,
                new MouseButtonEventHandler(TreeView_MouseLeftButtonDown));
            treeView.RemoveHandler(
                UIElement.MouseLeftButtonUpEvent,
                new MouseButtonEventHandler(TreeView_MouseLeftButtonUp));
        }
    }

    private static void TreeView_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeView treeView) return;
        if (e.OriginalSource is not DependencyObject originalSource) return;

        var treeViewItem = FindAncestor<TreeViewItem>(originalSource);
        if (treeViewItem is null) return;

        // Ignore clicks on the expander chevron itself -- let WPF handle expand/collapse.
        if (IsOnExpander(originalSource)) return;

        if (treeViewItem.DataContext is not TreeItemViewModel clickedItem) return;
        if (treeView.DataContext is not ConnectionTreeViewModel viewModel) return;

        bool isCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool isShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (isCtrl)
        {
            ToggleItem(viewModel, clickedItem);
            _anchorItem = clickedItem;
            _pendingCollapseItem = null;
        }
        else if (isShift && _anchorItem is not null)
        {
            SelectRange(viewModel, _anchorItem, clickedItem);
            // Anchor stays the same for subsequent shift-clicks
            _pendingCollapseItem = null;
        }
        else if (clickedItem.IsSelected && viewModel.SelectedItems.Count > 1)
        {
            // Plain click on an already-selected item inside a multi-selection.
            // Preserve the full selection so drag-drop can carry all of them;
            // defer "collapse to single-select" to MouseUp if no drag started.
            // See WPF-TREEVIEW-PATTERNS §4.5 (deferred selection).
            _pendingCollapseItem = clickedItem;
            viewModel.PrimarySelectedItem = clickedItem;
            _anchorItem = clickedItem;
        }
        else
        {
            ClearAll(viewModel);
            SelectItem(viewModel, clickedItem);
            _anchorItem = clickedItem;
            _pendingCollapseItem = null;
        }

        // Suppress native TreeView single-select highlight: TreeView's internal
        // ChangeSelection runs AFTER this handler and will set a single TreeViewItem's
        // IsSelected = true, which (via our ItemContainerStyle TwoWay binding) used to
        // deselect every other VM. We now bind IsSelected one-way from the template's
        // DataTrigger against the VM, but we still clear the native selected container
        // so we don't get a dual highlight.
        treeView.Dispatcher.InvokeAsync(() =>
        {
            if (treeView.SelectedItem is TreeItemViewModel nativeSelected)
            {
                var container = FindContainerForItem(treeView, nativeSelected);
                if (container is not null && container.IsSelected)
                    container.IsSelected = false;
            }
        }, System.Windows.Threading.DispatcherPriority.Background);

        // Focus so keyboard navigation continues from this item
        treeViewItem.Focus();
    }

    private static void TreeView_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pendingCollapseItem is null) return;
        if (sender is not TreeView treeView) return;
        if (treeView.DataContext is not ConnectionTreeViewModel viewModel)
        {
            _pendingCollapseItem = null;
            return;
        }

        var toCollapse = _pendingCollapseItem;
        _pendingCollapseItem = null;

        // No drag happened; collapse the multi-selection to just the clicked item.
        ClearAll(viewModel);
        SelectItem(viewModel, toCollapse);
        _anchorItem = toCollapse;
    }

    /// <summary>
    /// Called by <see cref="TreeViewDragDropBehavior"/> when a drag has begun,
    /// so the deferred-collapse pending state is cleared (drag preserved the
    /// multi-selection instead of collapsing it on MouseUp).
    /// </summary>
    public static void NotifyDragStarted() => _pendingCollapseItem = null;

    private static void SelectItem(ConnectionTreeViewModel vm, TreeItemViewModel item)
    {
        item.IsSelected = true;
        if (!vm.SelectedItems.Contains(item))
            vm.SelectedItems.Add(item);
        vm.PrimarySelectedItem = item;
    }

    private static void ToggleItem(ConnectionTreeViewModel vm, TreeItemViewModel item)
    {
        if (item.IsSelected)
        {
            item.IsSelected = false;
            vm.SelectedItems.Remove(item);
            // PrimarySelectedItem falls back to another selected item, or null
            if (vm.PrimarySelectedItem == item)
                vm.PrimarySelectedItem = vm.SelectedItems.LastOrDefault();
        }
        else
        {
            item.IsSelected = true;
            if (!vm.SelectedItems.Contains(item))
                vm.SelectedItems.Add(item);
            vm.PrimarySelectedItem = item;
        }
    }

    private static void SelectRange(ConnectionTreeViewModel vm, TreeItemViewModel anchor, TreeItemViewModel target)
    {
        var flat = GetFlatVisibleItems(vm);
        int anchorIdx = flat.IndexOf(anchor);
        int targetIdx = flat.IndexOf(target);
        if (anchorIdx < 0 || targetIdx < 0)
        {
            ClearAll(vm);
            SelectItem(vm, target);
            return;
        }

        ClearAll(vm);
        int lo = Math.Min(anchorIdx, targetIdx);
        int hi = Math.Max(anchorIdx, targetIdx);
        for (int i = lo; i <= hi; i++)
        {
            flat[i].IsSelected = true;
            if (!vm.SelectedItems.Contains(flat[i]))
                vm.SelectedItems.Add(flat[i]);
        }
        vm.PrimarySelectedItem = target;
    }

    private static void ClearAll(ConnectionTreeViewModel vm)
    {
        var snapshot = vm.SelectedItems.ToList();
        foreach (var item in snapshot)
            item.IsSelected = false;
        vm.SelectedItems.Clear();
    }

    /// <summary>
    /// Walks the model tree in display order (respecting expanded/collapsed groups)
    /// to produce a flat list of visible TreeItemViewModels. Operates on the MODEL,
    /// not the visual tree, so virtualization can't cause null entries.
    /// </summary>
    public static List<TreeItemViewModel> GetFlatVisibleItems(ConnectionTreeViewModel viewModel)
    {
        var result = new List<TreeItemViewModel>();
        FlattenVisible(viewModel.RootItems, result);
        return result;
    }

    private static void FlattenVisible(IEnumerable<TreeItemViewModel> items, List<TreeItemViewModel> result)
    {
        foreach (var item in items)
        {
            result.Add(item);
            if (item is GroupTreeItemViewModel group && group.IsExpanded)
                FlattenVisible(group.Children, result);
        }
    }

    private static bool IsOnExpander(DependencyObject source)
    {
        // The expander is a ToggleButton with PART_Header-adjacent Path chevron.
        // Walk up until we find either a ToggleButton or a TreeViewItem -- if we
        // hit the ToggleButton first, the click was on the expander.
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ToggleButton) return true;
            if (source is TreeViewItem) return false;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T target) return target;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static TreeViewItem? FindContainerForItem(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem tvi)
            return tvi;

        foreach (object child in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(child) is not TreeViewItem childContainer)
                continue;
            var result = FindContainerForItem(childContainer, item);
            if (result is not null) return result;
        }
        return null;
    }
}

using System.Windows.Input;
using Deskbridge.ViewModels;

namespace Deskbridge.Behaviors;

/// <summary>
/// Attached behavior that adds Ctrl+Click (toggle) and Shift+Click (range) multi-select
/// to a standard WPF TreeView via the ViewModel's IsSelected property.
/// </summary>
public static class TreeViewMultiSelectBehavior
{
    // Attached property: EnableMultiSelect
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

    // Track last-clicked item for Shift+Click range selection
    private static TreeItemViewModel? _lastClickedItem;

    private static void OnEnableMultiSelectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeView treeView) return;

        if ((bool)e.NewValue)
        {
            treeView.PreviewMouseLeftButtonDown += TreeView_PreviewMouseLeftButtonDown;
        }
        else
        {
            treeView.PreviewMouseLeftButtonDown -= TreeView_PreviewMouseLeftButtonDown;
        }
    }

    private static void TreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeView treeView) return;

        // Find the TreeViewItem that was clicked
        if (e.OriginalSource is not DependencyObject originalSource) return;

        var treeViewItem = FindAncestor<TreeViewItem>(originalSource);
        if (treeViewItem is null) return;

        if (treeViewItem.DataContext is not TreeItemViewModel clickedItem) return;

        // Get the ViewModel
        if (treeView.DataContext is not ConnectionTreeViewModel viewModel) return;

        bool isCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool isShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (isCtrl)
        {
            // Ctrl+Click: Toggle selection on clicked item
            clickedItem.IsSelected = !clickedItem.IsSelected;
            if (clickedItem.IsSelected)
            {
                if (!viewModel.SelectedItems.Contains(clickedItem))
                    viewModel.SelectedItems.Add(clickedItem);
            }
            else
            {
                viewModel.SelectedItems.Remove(clickedItem);
            }

            viewModel.PrimarySelectedItem = clickedItem;
            _lastClickedItem = clickedItem;
        }
        else if (isShift && _lastClickedItem is not null)
        {
            // Shift+Click: Select range from _lastClickedItem to current item
            var flatItems = GetFlatVisibleItems(viewModel);
            int startIndex = flatItems.IndexOf(_lastClickedItem);
            int endIndex = flatItems.IndexOf(clickedItem);

            if (startIndex >= 0 && endIndex >= 0)
            {
                // Deselect all first
                DeselectAll(viewModel);

                int low = Math.Min(startIndex, endIndex);
                int high = Math.Max(startIndex, endIndex);

                for (int i = low; i <= high; i++)
                {
                    flatItems[i].IsSelected = true;
                    if (!viewModel.SelectedItems.Contains(flatItems[i]))
                        viewModel.SelectedItems.Add(flatItems[i]);
                }
            }

            viewModel.PrimarySelectedItem = clickedItem;
            // Do NOT update _lastClickedItem for shift-click (anchor stays)
        }
        else
        {
            // Plain click: Deselect all, select clicked item only
            DeselectAll(viewModel);

            clickedItem.IsSelected = true;
            viewModel.SelectedItems.Add(clickedItem);
            viewModel.PrimarySelectedItem = clickedItem;
            _lastClickedItem = clickedItem;
        }

        // Prevent standard TreeView single-select behavior
        e.Handled = true;

        // Manually focus the clicked TreeViewItem for keyboard navigation
        treeViewItem.Focus();
    }

    private static void DeselectAll(ConnectionTreeViewModel viewModel)
    {
        foreach (var item in viewModel.SelectedItems)
        {
            item.IsSelected = false;
        }
        viewModel.SelectedItems.Clear();
    }

    /// <summary>
    /// Walks the tree in display order (respecting expanded/collapsed groups)
    /// to produce a flat list of visible TreeItemViewModels.
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
            {
                FlattenVisible(group.Children, result);
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T target) return target;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}

using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Deskbridge.ViewModels;

namespace Deskbridge.Behaviors;

/// <summary>
/// Attached behavior that enables in-process drag-drop for tree items with visual indicators.
/// Uses DataObject.SetData(typeof(T), item) directly -- no BinaryFormatter.
/// </summary>
public static class TreeViewDragDropBehavior
{
    private const double DragThreshold = 4.0;
    private const string DragDataFormat = "DeskbridgeTreeItems";

    // Attached property: EnableDragDrop
    public static readonly DependencyProperty EnableDragDropProperty =
        DependencyProperty.RegisterAttached(
            "EnableDragDrop",
            typeof(bool),
            typeof(TreeViewDragDropBehavior),
            new PropertyMetadata(false, OnEnableDragDropChanged));

    public static bool GetEnableDragDrop(DependencyObject obj) =>
        (bool)obj.GetValue(EnableDragDropProperty);

    public static void SetEnableDragDrop(DependencyObject obj, bool value) =>
        obj.SetValue(EnableDragDropProperty, value);

    // State tracking
    private static Point _mouseStartPoint;
    private static bool _isDragPending;
    private static TreeViewItem? _currentDropTarget;
    private static DropInsertionAdorner? _currentAdorner;
    private static DropPosition _currentDropPosition = DropPosition.Into;

    private static void OnEnableDragDropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeView treeView) return;

        if ((bool)e.NewValue)
        {
            treeView.AllowDrop = true;
            // TreeViewMultiSelectBehavior marks PreviewMouseLeftButtonDown as handled
            // after it updates selection; we still need to see the event to arm a
            // pending drag, so register with handledEventsToo: true.
            treeView.AddHandler(
                UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(TreeView_PreviewMouseLeftButtonDown),
                handledEventsToo: true);
            treeView.PreviewMouseMove += TreeView_PreviewMouseMove;
            treeView.PreviewMouseLeftButtonUp += TreeView_PreviewMouseLeftButtonUp;
            treeView.DragOver += TreeView_DragOver;
            treeView.Drop += TreeView_Drop;
            treeView.DragLeave += TreeView_DragLeave;
            treeView.PreviewKeyDown += TreeView_PreviewKeyDown;
            treeView.GiveFeedback += TreeView_GiveFeedback;
        }
        else
        {
            treeView.AllowDrop = false;
            treeView.RemoveHandler(
                UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(TreeView_PreviewMouseLeftButtonDown));
            treeView.PreviewMouseMove -= TreeView_PreviewMouseMove;
            treeView.PreviewMouseLeftButtonUp -= TreeView_PreviewMouseLeftButtonUp;
            treeView.DragOver -= TreeView_DragOver;
            treeView.Drop -= TreeView_Drop;
            treeView.DragLeave -= TreeView_DragLeave;
            treeView.PreviewKeyDown -= TreeView_PreviewKeyDown;
            treeView.GiveFeedback -= TreeView_GiveFeedback;
        }
    }

    private static void TreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeView treeView) return;

        if (e.OriginalSource is not DependencyObject originalSource) return;
        var treeViewItem = FindAncestor<TreeViewItem>(originalSource);
        if (treeViewItem is null) return;

        if (treeViewItem.DataContext is not TreeItemViewModel itemVm || !itemVm.IsSelected) return;

        // Start pending drag -- record start position
        _mouseStartPoint = e.GetPosition(treeView);
        _isDragPending = true;
    }

    private static void TreeView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragPending) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _isDragPending = false;
            return;
        }

        if (sender is not TreeView treeView) return;

        var currentPos = e.GetPosition(treeView);
        var diff = currentPos - _mouseStartPoint;

        // Check drag threshold (4px)
        if (Math.Abs(diff.X) < DragThreshold && Math.Abs(diff.Y) < DragThreshold)
            return;

        _isDragPending = false;

        if (treeView.DataContext is not ConnectionTreeViewModel viewModel || viewModel.SelectedItems.Count == 0) return;

        // Create data object with selected items (in-process only, no BinaryFormatter)
        var dragData = new DataObject();
        var selectedItems = viewModel.SelectedItems.ToList();
        dragData.SetData(DragDataFormat, selectedItems);

        // Begin drag operation
        try
        {
            DragDrop.DoDragDrop(treeView, dragData, DragDropEffects.Move);
        }
        finally
        {
            ClearDropIndicators();
        }
    }

    private static void TreeView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragPending = false;
    }

    private static void TreeView_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (!e.Data.GetDataPresent(DragDataFormat)) return;

        if (e.OriginalSource is not DependencyObject dragOverSource
            || FindAncestor<TreeViewItem>(dragOverSource) is not TreeViewItem treeViewItem)
        {
            ClearDropIndicators();
            return;
        }

        if (treeViewItem.DataContext is not TreeItemViewModel targetVm) return;

        // Don't allow dropping on one of the dragged items themselves
        if (e.Data.GetData(DragDataFormat) is List<TreeItemViewModel> draggedItems
            && draggedItems.Contains(targetVm)) return;

        // Don't allow dropping a group onto one of its own descendants (circular reference)
        if (targetVm is GroupTreeItemViewModel targetGroup
            && e.Data.GetData(DragDataFormat) is List<TreeItemViewModel> draggedForDescendantCheck
            && draggedForDescendantCheck.OfType<GroupTreeItemViewModel>().Any(g => IsDescendantOf(targetGroup, g)))
            return;

        // Compute drop position within the target TreeViewItem header (not full content
        // area — content includes nested children and would make the "middle" cover the
        // whole subtree). The header bounds come from the TreeViewItem's first visual
        // child row; we fall back to ActualHeight when unavailable.
        var headerHeight = GetHeaderHeight(treeViewItem);
        var pos = e.GetPosition(treeViewItem);
        var third = headerHeight / 3.0;

        DropPosition dropPosition;
        if (targetVm is GroupTreeItemViewModel)
        {
            // Groups can accept all three positions.
            if (pos.Y < third) dropPosition = DropPosition.Before;
            else if (pos.Y > headerHeight - third) dropPosition = DropPosition.After;
            else dropPosition = DropPosition.Into;
        }
        else
        {
            // Connections: only top-half / bottom-half (no "into").
            dropPosition = pos.Y < headerHeight / 2.0 ? DropPosition.Before : DropPosition.After;
        }

        e.Effects = DragDropEffects.Move;
        ShowDropIndicator(treeViewItem, dropPosition);
        _currentDropPosition = dropPosition;

        e.Handled = true;
    }

    private static double GetHeaderHeight(TreeViewItem item)
    {
        // A TreeViewItem's visual tree is roughly: Grid > (ToggleButton, ContentPresenter, ItemsPresenter).
        // We want just the header row height so the "middle" band isn't stretched across
        // all expanded descendants. When we can't find it, fall back to a sensible min.
        if (VisualTreeHelper.GetChildrenCount(item) > 0
            && VisualTreeHelper.GetChild(item, 0) is FrameworkElement grid)
        {
            // Header is typically the first row; its children occupy rowSpan 0.
            // Simplest heuristic: use the content presenter's ActualHeight if present.
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(grid); i++)
            {
                if (VisualTreeHelper.GetChild(grid, i) is ContentPresenter cp && cp.ActualHeight > 0)
                    return cp.ActualHeight;
            }
        }
        // Fallback: capped to avoid misclassifying top/bottom thirds when the subtree
        // is expanded and ActualHeight balloons.
        return Math.Min(item.ActualHeight, 32.0);
    }

    private static void TreeView_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragDataFormat)) return;

        if (e.Data.GetData(DragDataFormat) is not List<TreeItemViewModel> draggedItems
            || draggedItems.Count == 0) return;

        var dropTarget = e.OriginalSource is DependencyObject ds ? FindAncestor<TreeViewItem>(ds) : null;
        if (dropTarget?.DataContext is not TreeItemViewModel targetVm) return;

        if (sender is not TreeView treeView) return;
        if (treeView.DataContext is not ConnectionTreeViewModel viewModel) return;

        // Prevent dropping a group onto / next to its own descendant (circular reference)
        if (targetVm is GroupTreeItemViewModel tg
            && draggedItems.OfType<GroupTreeItemViewModel>().Any(g => IsDescendantOf(tg, g)))
        {
            ClearDropIndicators();
            return;
        }

        viewModel.ReorderItems(draggedItems, targetVm, _currentDropPosition);

        ClearDropIndicators();
        e.Handled = true;
    }

    private static void TreeView_DragLeave(object sender, DragEventArgs e)
    {
        ClearDropIndicators();
    }

    private static void TreeView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Escape cancels drag (handled by the DragDrop system via QueryContinueDrag but
        // we also ensure it cleans up visual indicators)
        if (e.Key == Key.Escape)
        {
            ClearDropIndicators();
        }
    }

    private static void TreeView_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        // Swap the default Windows dotted-rectangle feedback for an OS cursor that
        // matches the operation. A cursor-following snapshot is still a stretch goal;
        // this at least ensures the user sees a clear Move/No-drop indicator.
        e.UseDefaultCursors = false;
        Mouse.SetCursor(e.Effects == DragDropEffects.None ? Cursors.No : Cursors.Hand);
        e.Handled = true;
    }

    // --- Visual indicators ---

    private static void ShowDropIndicator(TreeViewItem item, DropPosition position)
    {
        // Replace the adorner when the target changes OR the position changes on the
        // same target (e.g. user moves from middle to top third of the same row).
        if (_currentDropTarget == item && _currentAdorner?.Position == position) return;

        ClearDropIndicators();
        _currentDropTarget = item;

        var adornerLayer = AdornerLayer.GetAdornerLayer(item);
        if (adornerLayer is null) return;

        _currentAdorner = new DropInsertionAdorner(item, position, GetHeaderHeight(item));
        adornerLayer.Add(_currentAdorner);
    }

    private static void ClearDropIndicators()
    {
        if (_currentAdorner is not null && _currentDropTarget is not null)
        {
            var adornerLayer = AdornerLayer.GetAdornerLayer(_currentDropTarget);
            adornerLayer?.Remove(_currentAdorner);
        }

        _currentAdorner = null;
        _currentDropTarget = null;
    }

    /// <summary>
    /// Returns true if <paramref name="candidate"/> is a descendant of <paramref name="potentialAncestor"/>
    /// in the ViewModel tree. Used to prevent circular group nesting via drag-drop.
    /// </summary>
    private static bool IsDescendantOf(GroupTreeItemViewModel candidate, GroupTreeItemViewModel potentialAncestor)
    {
        foreach (var child in potentialAncestor.Children)
        {
            if (child == candidate) return true;
            if (child is GroupTreeItemViewModel childGroup && IsDescendantOf(candidate, childGroup))
                return true;
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
}

/// <summary>
/// Adorner that renders a drop indicator in one of three positions:
/// Before (line at top), Into (row highlight), After (line at bottom).
/// </summary>
internal sealed class DropInsertionAdorner : Adorner
{
    private readonly double _headerHeight;

    public DropPosition Position { get; }

    public DropInsertionAdorner(UIElement adornedElement, DropPosition position, double headerHeight)
        : base(adornedElement)
    {
        Position = position;
        _headerHeight = headerHeight;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = AdornedElement.RenderSize;
        var width = size.Width;

        if (Position == DropPosition.Into)
        {
            var brush = TryFindBrush("SubtleFillColorSecondaryBrush")
                ?? new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x7A, 0xCC));
            // Highlight just the header row, not the full (expanded) subtree.
            drawingContext.DrawRectangle(brush, null, new Rect(0, 0, width, _headerHeight));
        }
        else
        {
            var brush = TryFindBrush("SystemAccentColorPrimaryBrush")
                ?? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));

            var pen = new Pen(brush, 2);
            double y = Position == DropPosition.Before ? 0 : _headerHeight;
            drawingContext.DrawLine(pen, new Point(0, y), new Point(width, y));
        }
    }

    private Brush? TryFindBrush(string resourceKey)
    {
        try
        {
            return Application.Current?.TryFindResource(resourceKey) is Brush brush ? brush : null;
        }
        catch
        {
            return null;
        }
    }
}

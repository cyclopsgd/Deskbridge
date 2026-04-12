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
    // Drag threshold -- 3x the system default (12px) to avoid accidental drags
    // from simple clicks. Computed once on first access using
    // SystemParameters.MinimumHorizontalDragDistance / MinimumVerticalDragDistance
    // (typically 4px each) so the behavior scales with user accessibility
    // settings.
    private const double DragThresholdMultiplier = 3.0;
    private static readonly double DragThresholdX =
        SystemParameters.MinimumHorizontalDragDistance * DragThresholdMultiplier;
    private static readonly double DragThresholdY =
        SystemParameters.MinimumVerticalDragDistance * DragThresholdMultiplier;

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

        // Require movement past the 3x-system drag threshold before committing
        // to a drag-drop. This prevents a simple click from initiating drag on
        // the way to release.
        if (Math.Abs(diff.X) < DragThresholdX && Math.Abs(diff.Y) < DragThresholdY)
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
            // Dropping into empty tree area (past the last item) -> move to root.
            // Allow the operation but don't draw a per-item adorner; the Drop
            // handler routes this to MoveItemsToRoot.
            ClearDropIndicators();
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
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

        if (sender is not TreeView treeView) return;
        if (treeView.DataContext is not ConnectionTreeViewModel viewModel) return;

        var dropTarget = e.OriginalSource is DependencyObject ds ? FindAncestor<TreeViewItem>(ds) : null;

        // Drop into empty tree area (no TreeViewItem under the cursor) moves the
        // dragged items to the root level. This is the only way to move a
        // subfolder out of its parent when the user hasn't got another root-level
        // item to drop Before/After.
        if (dropTarget?.DataContext is not TreeItemViewModel targetVm)
        {
            viewModel.MoveItemsToRoot(draggedItems);
            ClearDropIndicators();
            e.Handled = true;
            return;
        }

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
/// Before (line at top), Into (row highlight + border), After (line at bottom).
/// Lines extend the full width of the host TreeView (not just the adorned row)
/// so the insertion point is obvious regardless of item indentation.
/// </summary>
internal sealed class DropInsertionAdorner : Adorner
{
    private readonly double _headerHeight;
    private readonly TreeView? _hostTree;

    public DropPosition Position { get; }

    public DropInsertionAdorner(UIElement adornedElement, DropPosition position, double headerHeight)
        : base(adornedElement)
    {
        Position = position;
        _headerHeight = headerHeight;
        IsHitTestVisible = false;
        _hostTree = FindHostTree(adornedElement);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = AdornedElement.RenderSize;
        // Extend lines across the full TreeView width so indented children still
        // show a clear insertion line; fall back to the row width when we can't
        // compute a wider extent.
        double width = Math.Max(size.Width, ComputeFullWidth());

        var accentBrush = TryFindBrush("SystemAccentColorPrimaryBrush")
            ?? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));

        if (Position == DropPosition.Into)
        {
            // Semi-transparent fill (30% of accent) + 2px accent border around header.
            var fill = TryFindBrush("SystemAccentColorSecondaryBrush")
                ?? new SolidColorBrush(Color.FromArgb(0x4D, 0x00, 0x7A, 0xCC));
            if (fill is SolidColorBrush solid && solid.Opacity >= 1.0)
            {
                fill = new SolidColorBrush(solid.Color) { Opacity = 0.3 };
            }

            var borderPen = new Pen(accentBrush, 2) { DashCap = PenLineCap.Flat };
            // Inset by 1px so the 2px stroke draws entirely inside the row bounds.
            var rect = new Rect(1, 1, Math.Max(0, width - 2), Math.Max(0, _headerHeight - 2));
            drawingContext.DrawRectangle(fill, borderPen, rect);
        }
        else
        {
            var pen = new Pen(accentBrush, 2);
            double y = Position == DropPosition.Before ? 1 : _headerHeight - 1;
            drawingContext.DrawLine(pen, new Point(0, y), new Point(width, y));
        }
    }

    /// <summary>
    /// Width of the host TreeView expressed in the adorned element's coordinate
    /// space. Lets the insertion line extend past the row's measured width so the
    /// target is obvious even when the adorned row is narrow (indented children).
    /// </summary>
    private double ComputeFullWidth()
    {
        if (_hostTree is null || !_hostTree.IsLoaded) return 0;
        try
        {
            // Transform the TreeView's right edge into adorned-element coordinates.
            var transform = _hostTree.TransformToVisual((Visual)AdornedElement);
            var leftInAdorned = transform.Transform(new Point(0, 0)).X;
            return _hostTree.ActualWidth - leftInAdorned;
        }
        catch
        {
            return 0;
        }
    }

    private static TreeView? FindHostTree(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is TreeView tv) return tv;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
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

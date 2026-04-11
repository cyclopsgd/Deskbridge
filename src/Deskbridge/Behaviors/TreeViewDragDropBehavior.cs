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

    private static void OnEnableDragDropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeView treeView) return;

        if ((bool)e.NewValue)
        {
            treeView.AllowDrop = true;
            treeView.PreviewMouseLeftButtonDown += TreeView_PreviewMouseLeftButtonDown;
            treeView.PreviewMouseMove += TreeView_PreviewMouseMove;
            treeView.PreviewMouseLeftButtonUp += TreeView_PreviewMouseLeftButtonUp;
            treeView.DragOver += TreeView_DragOver;
            treeView.Drop += TreeView_Drop;
            treeView.DragLeave += TreeView_DragLeave;
            treeView.PreviewKeyDown += TreeView_PreviewKeyDown;
        }
        else
        {
            treeView.AllowDrop = false;
            treeView.PreviewMouseLeftButtonDown -= TreeView_PreviewMouseLeftButtonDown;
            treeView.PreviewMouseMove -= TreeView_PreviewMouseMove;
            treeView.PreviewMouseLeftButtonUp -= TreeView_PreviewMouseLeftButtonUp;
            treeView.DragOver -= TreeView_DragOver;
            treeView.Drop -= TreeView_Drop;
            treeView.DragLeave -= TreeView_DragLeave;
            treeView.PreviewKeyDown -= TreeView_PreviewKeyDown;
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

        if (targetVm is GroupTreeItemViewModel)
        {
            // Drop ON group: highlight with SubtleFillColorSecondaryBrush via adorner
            e.Effects = DragDropEffects.Move;
            ShowGroupHighlight(treeViewItem);
        }
        else
        {
            // Drop near a connection: show insertion line
            e.Effects = DragDropEffects.Move;
            ShowInsertionLine(treeViewItem);
        }

        e.Handled = true;
    }

    private static void TreeView_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragDataFormat)) return;

        if (e.Data.GetData(DragDataFormat) is not List<TreeItemViewModel> draggedItems
            || draggedItems.Count == 0) return;

        var dropTarget = e.OriginalSource is DependencyObject ds ? FindAncestor<TreeViewItem>(ds) : null;
        Guid? targetGroupId = null;

        if (dropTarget?.DataContext is GroupTreeItemViewModel group)
        {
            // Prevent dropping a group onto its own descendant (circular reference)
            if (draggedItems.OfType<GroupTreeItemViewModel>().Any(g => IsDescendantOf(group, g)))
                return;
            targetGroupId = group.Id;
        }
        else if (dropTarget?.DataContext is ConnectionTreeItemViewModel conn)
        {
            // Drop on a connection moves to the same group as the target connection
            targetGroupId = conn.GroupId;
        }

        if (sender is not TreeView treeView) return;
        if (treeView.DataContext is not ConnectionTreeViewModel viewModel) return;

        // Execute the move via the ViewModel's MoveToGroup command
        viewModel.MoveToGroupCommand.Execute(targetGroupId);

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

    // --- Visual indicators ---

    private static void ShowGroupHighlight(TreeViewItem item)
    {
        if (_currentDropTarget == item) return;

        ClearDropIndicators();
        _currentDropTarget = item;

        var adornerLayer = AdornerLayer.GetAdornerLayer(item);
        if (adornerLayer is null) return;

        _currentAdorner = new DropInsertionAdorner(item, isGroupHighlight: true);
        adornerLayer.Add(_currentAdorner);
    }

    private static void ShowInsertionLine(TreeViewItem item)
    {
        if (_currentDropTarget == item) return;

        ClearDropIndicators();
        _currentDropTarget = item;

        var adornerLayer = AdornerLayer.GetAdornerLayer(item);
        if (adornerLayer is null) return;

        _currentAdorner = new DropInsertionAdorner(item, isGroupHighlight: false);
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
/// Adorner that renders either a group drop highlight or a 2px insertion line.
/// </summary>
internal sealed class DropInsertionAdorner : Adorner
{
    private readonly bool _isGroupHighlight;

    public DropInsertionAdorner(UIElement adornedElement, bool isGroupHighlight)
        : base(adornedElement)
    {
        _isGroupHighlight = isGroupHighlight;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = AdornedElement.RenderSize;

        if (_isGroupHighlight)
        {
            // Highlight entire group row with semi-transparent accent
            var brush = TryFindBrush("SubtleFillColorSecondaryBrush")
                ?? new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x7A, 0xCC));

            drawingContext.DrawRectangle(brush, null, new Rect(0, 0, size.Width, size.Height));
        }
        else
        {
            // 2px insertion line at bottom in SystemAccentColorPrimaryBrush
            var brush = TryFindBrush("SystemAccentColorPrimaryBrush")
                ?? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));

            var pen = new Pen(brush, 2);
            drawingContext.DrawLine(pen, new Point(0, size.Height), new Point(size.Width, size.Height));
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

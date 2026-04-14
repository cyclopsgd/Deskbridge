using System.Collections.ObjectModel;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Deskbridge.ViewModels;

namespace Deskbridge.Behaviors;

/// <summary>
/// Plan 05-03 D-13 attached behavior: enables drag-to-reorder on the tab bar
/// <see cref="ItemsControl"/>. Drop mutates the bound <c>ObservableCollection&lt;TabItemViewModel&gt;</c>
/// via <see cref="ObservableCollection{T}.Move(int,int)"/> — NEVER Remove+Insert —
/// so the D-04 persistent HostContainer invariant holds: tabs reorder in the VM
/// collection but <c>HostContainer.Children</c> is untouched.
///
/// <para>
/// <b>Do not</b> mutate <c>HostContainer</c> from here. Tag-keyed Visibility in
/// <c>MainWindow.SetActiveHostVisibility</c> is what follows a moved tab — index
/// is irrelevant to the WFH parent slot (WINFORMS-HOST-AIRSPACE §Option 3).
/// </para>
///
/// <para>
/// Uses a 3x system drag threshold (matches <see cref="TreeViewDragDropBehavior"/>)
/// so a simple click never initiates a drag. LeftClick / MiddleClick / ContextMenu
/// MouseBindings on the per-tab Border are NOT blocked — this behavior only arms
/// <c>_dragPending</c> in <c>PreviewMouseLeftButtonDown</c> and fires
/// <c>DragDrop.DoDragDrop</c> in <c>PreviewMouseMove</c>. If movement stays under
/// the threshold, the MouseBindings fire normally.
/// </para>
/// </summary>
public static class TabReorderBehavior
{
    private const double DragThresholdMultiplier = 3.0;
    private static readonly double DragThresholdX =
        SystemParameters.MinimumHorizontalDragDistance * DragThresholdMultiplier;
    private static readonly double DragThresholdY =
        SystemParameters.MinimumVerticalDragDistance * DragThresholdMultiplier;

    // Distinct from TreeView's "DeskbridgeTreeItems" — T-05-09 collision mitigation.
    private const string DragDataFormat = "DeskbridgeTabItem";

    public static readonly DependencyProperty EnableReorderProperty =
        DependencyProperty.RegisterAttached(
            "EnableReorder",
            typeof(bool),
            typeof(TabReorderBehavior),
            new PropertyMetadata(false, OnEnableReorderChanged));

    public static bool GetEnableReorder(DependencyObject obj) =>
        (bool)obj.GetValue(EnableReorderProperty);

    public static void SetEnableReorder(DependencyObject obj, bool value) =>
        obj.SetValue(EnableReorderProperty, value);

    // Static state. Only one drag can be in flight system-wide (WPF DragDrop.DoDragDrop
    // is modal-synchronous), so static state is safe here.
    private static Point _mouseStartPoint;
    private static bool _dragPending;
    private static TabItemViewModel? _draggedTab;
    private static TabInsertionAdorner? _currentAdorner;
    private static FrameworkElement? _currentAdornerTarget;
    private static InsertionSide _currentSide;

    private enum InsertionSide { Before, After }

    private static void OnEnableReorderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl ic) return;

        if ((bool)e.NewValue)
        {
            ic.AllowDrop = true;
            // handledEventsToo:true so MouseBindings in the DataTemplate (LeftClick switch,
            // MiddleClick close) don't hide the event from the reorder arming logic.
            ic.AddHandler(
                UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(ItemsControl_PreviewMouseLeftButtonDown),
                handledEventsToo: true);
            ic.PreviewMouseMove += ItemsControl_PreviewMouseMove;
            ic.PreviewMouseLeftButtonUp += ItemsControl_PreviewMouseLeftButtonUp;
            ic.DragOver += ItemsControl_DragOver;
            ic.Drop += ItemsControl_Drop;
            ic.DragLeave += ItemsControl_DragLeave;
            ic.PreviewKeyDown += ItemsControl_PreviewKeyDown;
            ic.GiveFeedback += ItemsControl_GiveFeedback;
        }
        else
        {
            ic.AllowDrop = false;
            ic.RemoveHandler(
                UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(ItemsControl_PreviewMouseLeftButtonDown));
            ic.PreviewMouseMove -= ItemsControl_PreviewMouseMove;
            ic.PreviewMouseLeftButtonUp -= ItemsControl_PreviewMouseLeftButtonUp;
            ic.DragOver -= ItemsControl_DragOver;
            ic.Drop -= ItemsControl_Drop;
            ic.DragLeave -= ItemsControl_DragLeave;
            ic.PreviewKeyDown -= ItemsControl_PreviewKeyDown;
            ic.GiveFeedback -= ItemsControl_GiveFeedback;
        }
    }

    // ----------------------------------------------------------------- input handlers

    private static void ItemsControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ItemsControl ic) return;
        if (e.OriginalSource is not DependencyObject src) return;

        // Only arm if the click landed on an element whose DataContext is a TabItemViewModel.
        var fe = FindAncestorWithTabVm(src);
        if (fe is null) return;

        _mouseStartPoint = e.GetPosition(ic);
        _draggedTab = (TabItemViewModel)fe.DataContext;
        _dragPending = true;
    }

    private static void ItemsControl_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragPending) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragPending = false;
            _draggedTab = null;
            return;
        }

        if (sender is not ItemsControl ic) return;

        var current = e.GetPosition(ic);
        var diff = current - _mouseStartPoint;
        if (Math.Abs(diff.X) < DragThresholdX && Math.Abs(diff.Y) < DragThresholdY)
            return;

        // Commit to drag — clear pending so subsequent moves don't re-arm.
        _dragPending = false;
        var dragTab = _draggedTab;
        if (dragTab is null) return;

        var data = new DataObject();
        data.SetData(DragDataFormat, dragTab);
        try
        {
            DragDrop.DoDragDrop(ic, data, DragDropEffects.Move);
        }
        finally
        {
            ClearAdorner();
            _draggedTab = null;
        }
    }

    private static void ItemsControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragPending = false;
    }

    private static void ItemsControl_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (!e.Data.GetDataPresent(DragDataFormat)) return;

        if (sender is not ItemsControl ic) return;
        if (e.OriginalSource is not DependencyObject src) return;

        // Identify the target tab container under the cursor.
        var targetFe = FindAncestorWithTabVm(src);
        if (targetFe is null)
        {
            ClearAdorner();
            // Empty area past the last item — still a legal drop-to-end.
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        var targetTab = (TabItemViewModel)targetFe.DataContext;
        if (ReferenceEquals(targetTab, _draggedTab))
        {
            // Hovering over self — no insertion line.
            ClearAdorner();
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        // Left half → insert before, right half → insert after.
        var posOnTarget = e.GetPosition(targetFe);
        var side = posOnTarget.X < targetFe.ActualWidth / 2.0
            ? InsertionSide.Before
            : InsertionSide.After;

        ShowAdorner(targetFe, side);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private static void ItemsControl_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.Data.GetDataPresent(DragDataFormat)) return;
            if (e.Data.GetData(DragDataFormat) is not TabItemViewModel dragged) return;
            if (sender is not ItemsControl ic) return;
            if (ic.ItemsSource is not ObservableCollection<TabItemViewModel> tabs) return;

            var oldIndex = tabs.IndexOf(dragged);
            if (oldIndex < 0) return;

            int newIndex;
            if (e.OriginalSource is DependencyObject src
                && FindAncestorWithTabVm(src) is FrameworkElement targetFe
                && targetFe.DataContext is TabItemViewModel targetTab
                && !ReferenceEquals(targetTab, dragged))
            {
                var targetIndex = tabs.IndexOf(targetTab);
                if (targetIndex < 0) return;

                // Compute the side from the current adorner (DragOver most recently set it).
                // If no adorner, compute from pointer position.
                InsertionSide side = _currentAdornerTarget == targetFe
                    ? _currentSide
                    : (e.GetPosition(targetFe).X < targetFe.ActualWidth / 2.0
                        ? InsertionSide.Before : InsertionSide.After);

                newIndex = side == InsertionSide.Before ? targetIndex : targetIndex + 1;

                // Self-removal adjustment (RESEARCH Pattern 6 line 794): when oldIndex < newIndex,
                // removing the source first shifts the target index left by one.
                if (oldIndex < newIndex) newIndex--;
            }
            else
            {
                // Drop on empty area → move to end.
                newIndex = tabs.Count - 1;
            }

            // Clamp defensively and ensure we're actually moving.
            if (newIndex < 0) newIndex = 0;
            if (newIndex >= tabs.Count) newIndex = tabs.Count - 1;
            if (newIndex == oldIndex) return;

            // CRITICAL: Move (not Remove + Insert) — preserves instance identity and
            // fires a single CollectionChanged Move event instead of two. Also means
            // HostContainer.Children stays untouched (it's keyed by Tag, not by index).
            tabs.Move(oldIndex, newIndex);
        }
        finally
        {
            ClearAdorner();
            e.Handled = true;
        }
    }

    private static void ItemsControl_DragLeave(object sender, DragEventArgs e)
    {
        ClearAdorner();
    }

    private static void ItemsControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // ESC during drag — WPF's drag system handles the cancel; we just clear visuals.
        if (e.Key == Key.Escape)
        {
            ClearAdorner();
        }
    }

    private static void ItemsControl_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = false;
        Mouse.SetCursor(e.Effects == DragDropEffects.None ? Cursors.No : Cursors.Hand);
        e.Handled = true;
    }

    // ----------------------------------------------------------------- adorner plumbing

    private static void ShowAdorner(FrameworkElement target, InsertionSide side)
    {
        if (ReferenceEquals(_currentAdornerTarget, target) && _currentSide == side) return;

        ClearAdorner();

        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer is null) return;

        _currentAdorner = new TabInsertionAdorner(target, side == InsertionSide.Before);
        _currentAdornerTarget = target;
        _currentSide = side;
        layer.Add(_currentAdorner);
    }

    private static void ClearAdorner()
    {
        if (_currentAdorner is not null && _currentAdornerTarget is not null)
        {
            var layer = AdornerLayer.GetAdornerLayer(_currentAdornerTarget);
            layer?.Remove(_currentAdorner);
        }
        _currentAdorner = null;
        _currentAdornerTarget = null;
    }

    // ----------------------------------------------------------------- helpers

    private static FrameworkElement? FindAncestorWithTabVm(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is FrameworkElement fe && fe.DataContext is TabItemViewModel)
                return fe;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}

/// <summary>
/// Adorner that draws a 2px vertical accent-coloured insertion line on the left
/// or right edge of the target tab container. UI-SPEC line 108 + 182: 2px thick,
/// <c>SystemAccentColorPrimaryBrush</c>, full tab-bar height. Hit-testing is off
/// so the adorner never steals drop events.
/// </summary>
internal sealed class TabInsertionAdorner : Adorner
{
    private readonly bool _before;

    public TabInsertionAdorner(UIElement adornedElement, bool before) : base(adornedElement)
    {
        _before = before;
        IsHitTestVisible = false;
    }

    public bool IsBefore => _before;

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = AdornedElement.RenderSize;

        var accent = (Application.Current?.TryFindResource("SystemAccentColorPrimaryBrush") as Brush)
            ?? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));

        var pen = new Pen(accent, 2.0);
        double x = _before ? 1.0 : Math.Max(0, size.Width - 1.0);
        drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, size.Height));
    }
}

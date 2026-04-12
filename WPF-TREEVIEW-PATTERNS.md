# WPF TreeView patterns that actually work

WPF's TreeView is single-select, paints selection highlights wrong, and marks mouse events as handled before your code sees them. Every project fights the same battles. This reference documents the patterns that survive contact with the WPF runtime on .NET 10 with WPF-UI 4.2.0, with complete code and explicit traps.

---

## Section 1 — Multi-select in WPF TreeView

### 1.1 Why WPF actively fights multi-select

WPF TreeView enforces single selection through three interlocking mechanisms in `TreeViewItem` and `TreeView`. Understanding these is non-negotiable before attempting any workaround.

**Mechanism 1 — `TreeViewItem.OnMouseLeftButtonDown` marks the event handled:**

```csharp
// From dotnet/wpf TreeViewItem.cs — this is the enemy
protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
{
    if (!e.Handled && IsEnabled)
    {
        if (Focus())               // focus gain triggers selection (see Mechanism 2)
        {
            e.Handled = true;      // YOUR handler never sees this event
        }
    }
}
```

**Mechanism 2 — `OnGotFocus` forces selection:**

```csharp
protected override void OnGotFocus(RoutedEventArgs e)
{
    Select(true);  // every focus gain → selection, no exceptions
}
```

**Mechanism 3 — `TreeView.ChangeSelection` deselects the previous item:**

```csharp
internal void ChangeSelection(object data, TreeViewItem container, bool selected)
{
    if (selected && container != _selectedContainer)
    {
        _selectedContainer.IsSelected = false;    // forcibly deselects previous
        _selectedContainer = container;
        SetSelectedItem(data);                    // SelectedItem is read-only DP
    }
}
```

Additionally, `TreeViewItem`'s static constructor registers a class handler with **`handledEventsToo: true`**:

```csharp
EventManager.RegisterClassHandler(typeof(TreeViewItem),
    Mouse.MouseDownEvent,
    new MouseButtonEventHandler(OnMouseButtonDown),
    true);  // fires even after e.Handled = true
```

The consequence: `TreeView.SelectedItem` is a read-only dependency property. Native `IsSelected` is forcibly single-valued. Standard XAML event handlers on `MouseLeftButtonDown` never fire. You must build a parallel selection system.

### 1.2 The four workaround patterns

**Pattern A — Attached behavior with parallel `IsItemSelected` DP.** Creates a custom attached property on `TreeViewItem` that operates independently of native `IsSelected`. Uses `AddHandler(..., handledEventsToo: true)` to intercept clicks. This is the recommended pattern. Maintains its own `SelectedItems` collection and anchor for shift-range.

**Pattern B — `Behavior<TreeView>` (Microsoft.Xaml.Behaviors).** Same mechanics as Pattern A but packaged as a `Behavior<T>` attached via `<i:Interaction.Behaviors>`. CodeMaid's implementation adds keyboard support. Trap: if `OnAttached` never fires (wrong namespace import, behavior not in visual tree), nothing happens and no error is shown.

**Pattern C — Full control replacement (ygoe/MultiSelectTreeView).** Replaces `TreeView` and `TreeViewItem` entirely. **Archived May 2022**, read-only on GitHub. Known issues: extremely slow with **>20,000 items** due to recursive enumeration, binding errors for `HoverHighlighting`/`ItemIndent`/`IsKeyboardMode`, `RelativeSource FindAncestor AncestorType=Window` fails in UserControls, and uses `headerBorder` instead of `PART_Header` breaking template portability. Hard to integrate with WPF-UI theming because it's a separate control type.

**Pattern D — ListBox-in-TreeView-clothing.** Use `ListBox` with `SelectionMode="Extended"` and a flattened tree model with manual indentation. Avoids fighting TreeView entirely but loses `HierarchicalDataTemplate`, requires manual expand/collapse, and has complex scroll management. Only viable for flat-ish trees.

### 1.3 Complete `TreeViewMultiSelectBehavior` implementation

This behavior uses Pattern A/B hybrid: a `Behavior<TreeView>` with a parallel `IsItemSelected` attached property, Ctrl+click toggle, Shift+click range, Escape-to-deselect, and ViewModel sync.

```csharp
using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Xaml.Behaviors;

namespace MyApp.Behaviors;

/// <summary>
/// Attached behavior enabling multi-select on WPF TreeView.
/// Uses a parallel IsItemSelected property to avoid fighting
/// TreeView's single-select enforcement.
/// </summary>
public class TreeViewMultiSelectBehavior : Behavior<TreeView>
{
    // ── Parallel selection state per TreeViewItem ──
    public static readonly DependencyProperty IsItemSelectedProperty =
        DependencyProperty.RegisterAttached(
            "IsItemSelected", typeof(bool), typeof(TreeViewMultiSelectBehavior),
            new FrameworkPropertyMetadata(false,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static bool GetIsItemSelected(DependencyObject d) =>
        (bool)d.GetValue(IsItemSelectedProperty);
    public static void SetIsItemSelected(DependencyObject d, bool value) =>
        d.SetValue(IsItemSelectedProperty, value);

    // ── Bound collection of selected data objects ──
    public static readonly DependencyProperty SelectedItemsProperty =
        DependencyProperty.Register(
            "SelectedItems", typeof(IList), typeof(TreeViewMultiSelectBehavior),
            new PropertyMetadata(null));

    public IList SelectedItems
    {
        get => (IList)GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    // ── Internal: shift-click anchor ──
    private TreeViewItem? _anchorItem;

    protected override void OnAttached()
    {
        base.OnAttached();
        // CRITICAL: handledEventsToo = true — without this, nothing works
        AssociatedObject.AddHandler(
            UIElement.MouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnItemMouseDown),
            handledEventsToo: true);
        AssociatedObject.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(OnKeyDown),
            handledEventsToo: true);
    }

    protected override void OnDetaching()
    {
        AssociatedObject.RemoveHandler(
            UIElement.MouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnItemMouseDown));
        AssociatedObject.RemoveHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(OnKeyDown));
        base.OnDetaching();
    }

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Walk up from click target to find the TreeViewItem
        var tvi = FindTreeViewItem(e.OriginalSource as DependencyObject);
        if (tvi == null) return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // ── Ctrl+Click: toggle individual item ──
            ToggleItem(tvi);
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // ── Shift+Click: range selection ──
            SelectRange(tvi);
        }
        else
        {
            // ── Plain click: single select (clear all, select this) ──
            ClearAll();
            SelectItem(tvi);
            _anchorItem = tvi;
        }

        // Suppress native single-select highlight to prevent dual-highlight
        SuppressNativeSelection();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClearAll();
            _anchorItem = null;
            SuppressNativeSelection();
            e.Handled = true;
        }
    }

    // ── Selection operations ──

    private void SelectItem(TreeViewItem tvi)
    {
        SetIsItemSelected(tvi, true);
        SyncToCollection(tvi, selected: true);
    }

    private void DeselectItem(TreeViewItem tvi)
    {
        SetIsItemSelected(tvi, false);
        SyncToCollection(tvi, selected: false);
    }

    private void ToggleItem(TreeViewItem tvi)
    {
        bool newState = !GetIsItemSelected(tvi);
        SetIsItemSelected(tvi, newState);
        SyncToCollection(tvi, newState);

        if (newState)
            _anchorItem ??= tvi;
    }

    private void SelectRange(TreeViewItem target)
    {
        if (_anchorItem == null)
        {
            SelectItem(target);
            _anchorItem = target;
            return;
        }

        // Flatten visible tree via DFS, then select between anchor and target
        var flatList = new List<TreeViewItem>();
        FlattenVisible(AssociatedObject, flatList);

        int anchorIdx = flatList.IndexOf(_anchorItem);
        int targetIdx = flatList.IndexOf(target);
        if (anchorIdx < 0 || targetIdx < 0) return;

        int lo = Math.Min(anchorIdx, targetIdx);
        int hi = Math.Max(anchorIdx, targetIdx);

        ClearAll();
        for (int i = lo; i <= hi; i++)
            SelectItem(flatList[i]);
        // anchor stays the same (don't update _anchorItem on shift-click)
    }

    private void ClearAll()
    {
        var flatList = new List<TreeViewItem>();
        FlattenVisible(AssociatedObject, flatList);
        foreach (var item in flatList)
        {
            if (GetIsItemSelected(item))
                DeselectItem(item);
        }
    }

    // ── Sync IsItemSelected ↔ SelectedItems collection ──

    private void SyncToCollection(TreeViewItem tvi, bool selected)
    {
        if (SelectedItems == null) return;
        object? dataItem = tvi.DataContext;
        if (dataItem == null) return;

        if (selected && !SelectedItems.Contains(dataItem))
            SelectedItems.Add(dataItem);
        else if (!selected)
            SelectedItems.Remove(dataItem);
    }

    /// <summary>
    /// Suppress native TreeView single-select highlight.
    /// Without this you get DUAL highlights: native blue + custom.
    /// </summary>
    private void SuppressNativeSelection()
    {
        // Defer to after TreeView finishes its internal selection pass
        AssociatedObject.Dispatcher.InvokeAsync(() =>
        {
            if (AssociatedObject.SelectedItem != null)
            {
                var selected = FindContainerForItem(
                    AssociatedObject, AssociatedObject.SelectedItem);
                if (selected != null)
                    selected.IsSelected = false;
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    // ── Tree traversal helpers ──

    /// <summary>
    /// DFS traversal of VISIBLE (expanded) tree to produce flat ordering.
    /// Items inside collapsed nodes are NOT included — this is correct behavior
    /// for shift-range selection (matches Windows Explorer).
    /// PITFALL: Returns null entries if virtualization is on and items
    /// are off-screen. Always null-check.
    /// </summary>
    private static void FlattenVisible(ItemsControl parent,
        List<TreeViewItem> result)
    {
        for (int i = 0; i < parent.Items.Count; i++)
        {
            var tvi = parent.ItemContainerGenerator
                .ContainerFromIndex(i) as TreeViewItem;
            if (tvi == null) continue;  // virtualized/unrealized — skip
            result.Add(tvi);
            if (tvi.IsExpanded)
                FlattenVisible(tvi, result);
        }
    }

    private static TreeViewItem? FindTreeViewItem(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is TreeViewItem tvi) return tvi;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static TreeViewItem? FindContainerForItem(
        ItemsControl parent, object item)
    {
        var tvi = parent.ItemContainerGenerator
            .ContainerFromItem(item) as TreeViewItem;
        if (tvi != null) return tvi;

        foreach (object child in parent.Items)
        {
            var childContainer = parent.ItemContainerGenerator
                .ContainerFromItem(child) as TreeViewItem;
            if (childContainer != null)
            {
                var result = FindContainerForItem(childContainer, item);
                if (result != null) return result;
            }
        }
        return null;
    }
}
```

### 1.4 XAML attachment and ViewModel binding

```xml
<Window xmlns:i="http://schemas.microsoft.com/xaml/behaviors"
        xmlns:b="clr-namespace:MyApp.Behaviors">
  <TreeView ItemsSource="{Binding Nodes}"
            HorizontalContentAlignment="Stretch">
    <i:Interaction.Behaviors>
      <b:TreeViewMultiSelectBehavior
          SelectedItems="{Binding SelectedNodes}" />
    </i:Interaction.Behaviors>

    <TreeView.Resources>
      <HierarchicalDataTemplate DataType="{x:Type vm:TreeNodeViewModel}"
                                ItemsSource="{Binding Children}">
        <StackPanel Orientation="Horizontal">
          <ui:SymbolIcon Symbol="Document24" Margin="0,0,6,0" />
          <TextBlock Text="{Binding Name}" />
        </StackPanel>
      </HierarchicalDataTemplate>
    </TreeView.Resources>

    <!-- Selection visual driven by the parallel IsItemSelected property -->
    <TreeView.ItemContainerStyle>
      <Style TargetType="TreeViewItem"
             BasedOn="{StaticResource {x:Type TreeViewItem}}">
        <Style.Triggers>
          <Trigger Property="b:TreeViewMultiSelectBehavior.IsItemSelected"
                   Value="True">
            <Setter Property="Background"
                    Value="{DynamicResource SystemAccentColorSecondaryBrush}" />
          </Trigger>
        </Style.Triggers>
      </Style>
    </TreeView.ItemContainerStyle>
  </TreeView>
</Window>
```

**ViewModel side — `SelectedNodes` must be pre-initialized:**

```csharp
// WRONG: null collection → behavior silently adds nothing
public IList SelectedNodes { get; set; }

// CORRECT: pre-initialized ObservableCollection
public ObservableCollection<TreeNodeViewModel> SelectedNodes { get; } = new();
```

### 1.5 Silent failure catalog for multi-select

**"This looks right but does nothing because..."**

- **XAML event handler `TreeViewItem.MouseLeftButtonDown="Handler"`** — never fires. `TreeViewItem.OnMouseLeftButtonDown` sets `e.Handled = true` before your handler runs. Fix: `AddHandler(..., handledEventsToo: true)`.

- **`PreviewMouseLeftButtonDown` with `e.Handled = true`** — this suppresses the bubbling phase entirely, which breaks expand/collapse on double-click and breaks focus management. Only use Preview for inspection, not for consuming the event.

- **Binding `ItemContainerStyle` setter `IsSelected` TwoWay for multi-select** — the native `IsSelected` is forcibly single-valued by `ChangeSelection`. Setting item A's `IsSelected = true` in ViewModel will be overridden when user clicks item B, because `ChangeSelection` sets A's `IsSelected = false`. The TwoWay binding faithfully propagates this deselection back to your ViewModel.

- **`SelectedItems` binding never updates** — the `IList` must be pre-initialized, and the binding must use an `ObservableCollection<T>`. If the backing field starts as `null`, `behavior.SelectedItems.Add(x)` throws `NullReferenceException` silently eaten by WPF's binding error swallowing.

- **Attached behavior `OnAttached` never called** — wrong xmlns, behavior declared outside the visual tree, or `Microsoft.Xaml.Behaviors.Wpf` NuGet not installed. Zero diagnostic output.

- **Dual highlight (native blue + custom green)** — if you don't suppress `TreeViewItem.IsSelected` after your custom selection, WPF's native highlight still shows on the last-clicked item. You get two different-colored highlights simultaneously.

- **Shift-range silently skips collapsed children** — `ContainerFromIndex` returns `null` for items inside collapsed nodes. The range algorithm skips them. This is actually correct behavior (matches Explorer/VS), but if you expected inclusive range across collapsed subtrees, it won't work.

---

## Section 2 — TreeViewItem full-row selection visual

### 2.1 Why the default template paints selection wrong

The default WPF `TreeViewItem` ControlTemplate uses a **3-column × 2-row Grid**:

```
Column 0 (Auto, 19px min): Expander toggle
Column 1 (Auto):           Border "Bd" wrapping ContentPresenter
Column 2 (*):              Empty — remaining space
```

The selection highlight border `Bd` lives in **Column 1 with `Width="Auto"`**. It auto-sizes to content width and never stretches to fill the row. Column 2 (star-sized) gets no highlight. This is why selection appears as a tight box around the text, not a full row.

### 2.2 Why approach C (Border inside DataTemplate) is the trap

```xml
<!-- THIS IS THE TRAP — looks right, does nothing useful -->
<HierarchicalDataTemplate DataType="{x:Type vm:Node}"
                          ItemsSource="{Binding Children}">
  <Border Background="{Binding SelectionBrush}" Padding="4">
    <StackPanel Orientation="Horizontal">
      <ui:SymbolIcon Symbol="Folder24" />
      <TextBlock Text="{Binding Name}" />
    </StackPanel>
  </Border>
</HierarchicalDataTemplate>
```

This only paints behind the `StackPanel` content. It **misses the indentation area** (left of the expander chevron) and the **trailing whitespace** (right of the text to the tree edge). Looks fine on root-level items but breaks visually as soon as items are indented. The deeper the nesting, the more obvious the gap on the left.

### 2.3 The working approach — full ControlTemplate replacement with depth converter

The solution moves the highlight border **outside the column layout** so it spans full width, then uses a depth-based margin converter to indent only the content within that full-width border.

**Depth extension method:**

```csharp
public static class TreeViewItemExtensions
{
    public static int GetDepth(this TreeViewItem item)
    {
        int depth = 0;
        DependencyObject parent = VisualTreeHelper.GetParent(item);
        while (parent != null)
        {
            if (parent is TreeViewItem) depth++;
            if (parent is TreeView) break;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return depth;
    }
}
```

**Indent margin converter:**

```csharp
public class IndentConverter : IValueConverter
{
    public double IndentSize { get; set; } = 19.0;

    public object Convert(object value, Type targetType,
        object parameter, CultureInfo culture)
    {
        if (value is TreeViewItem tvi)
            return new Thickness(IndentSize * tvi.GetDepth(), 0, 0, 0);
        return new Thickness(0);
    }

    public object ConvertBack(object value, Type targetType,
        object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

### 2.4 Complete retemplated TreeViewItem style

This template provides full-row selection bound to a ViewModel `IsSelected` property (for multi-select), with a **2px left accent stripe** using `SystemAccentColorPrimaryBrush` and a **background fill** using `SystemAccentColorSecondaryBrush`. It integrates with WPF-UI's Fluent design tokens.

```xml
<Window.Resources>
  <local:IndentConverter x:Key="IndentConverter" IndentSize="19" />

  <!-- Expander toggle button style (minimal chevron) -->
  <Style x:Key="TreeExpanderStyle" TargetType="ToggleButton">
    <Setter Property="Focusable" Value="False" />
    <Setter Property="Width" Value="19" />
    <Setter Property="Height" Value="19" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ToggleButton">
          <Border Background="Transparent" Width="19" Height="19">
            <Path x:Name="ExpandPath"
                  HorizontalAlignment="Center" VerticalAlignment="Center"
                  Data="M 4,2 L 8,6 L 4,10 Z"
                  Fill="{DynamicResource TextFillColorTertiaryBrush}"
                  RenderTransformOrigin="0.5,0.5">
              <Path.RenderTransform>
                <RotateTransform Angle="0" />
              </Path.RenderTransform>
            </Path>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="ExpandPath"
                      Property="RenderTransform">
                <Setter.Value>
                  <RotateTransform Angle="90" />
                </Setter.Value>
              </Setter>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Full-row TreeViewItem style -->
  <Style x:Key="FullRowTreeViewItemStyle" TargetType="TreeViewItem">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Foreground"
            Value="{DynamicResource TextFillColorPrimaryBrush}" />
    <Setter Property="Padding" Value="4" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="TreeViewItem">
          <StackPanel>
            <!--
              OUTER Border: no Grid.Column = stretches to full TreeView width.
              This is the selection highlight surface.
            -->
            <Border x:Name="RowBorder"
                    Background="Transparent"
                    BorderThickness="0"
                    MinHeight="28"
                    CornerRadius="{DynamicResource ControlCornerRadius}"
                    Margin="0,0,0,1"
                    SnapsToDevicePixels="True">
              <Grid>
                <!-- 2px left accent stripe (visible when selected) -->
                <Rectangle x:Name="AccentStripe"
                           Width="2" Height="16"
                           HorizontalAlignment="Left"
                           VerticalAlignment="Center"
                           Margin="4,0,0,0"
                           RadiusX="1" RadiusY="1"
                           Fill="{DynamicResource SystemAccentColorPrimaryBrush}"
                           Visibility="Collapsed" />

                <!-- Content indented by depth × 19px -->
                <Grid Margin="{Binding
                        Converter={StaticResource IndentConverter},
                        RelativeSource={RelativeSource TemplatedParent}}">
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="19" />
                    <ColumnDefinition Width="*" />
                  </Grid.ColumnDefinitions>

                  <ToggleButton x:Name="Expander"
                                Grid.Column="0"
                                Style="{StaticResource TreeExpanderStyle}"
                                ClickMode="Press"
                                IsChecked="{Binding IsExpanded,
                                  RelativeSource={RelativeSource TemplatedParent}}" />

                  <ContentPresenter x:Name="PART_Header"
                                    Grid.Column="1"
                                    ContentSource="Header"
                                    Margin="{TemplateBinding Padding}"
                                    VerticalAlignment="Center"
                                    HorizontalAlignment="{TemplateBinding
                                      HorizontalContentAlignment}" />
                </Grid>
              </Grid>
            </Border>

            <!-- Children container -->
            <ItemsPresenter x:Name="ItemsHost" Visibility="Collapsed" />
          </StackPanel>

          <ControlTemplate.Triggers>
            <!-- Expand/collapse children -->
            <Trigger Property="IsExpanded" Value="True">
              <Setter TargetName="ItemsHost"
                      Property="Visibility" Value="Visible" />
            </Trigger>

            <!-- Hide expander for leaf nodes -->
            <Trigger Property="HasItems" Value="False">
              <Setter TargetName="Expander"
                      Property="Visibility" Value="Hidden" />
            </Trigger>

            <!-- Hover highlight -->
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="RowBorder" Property="Background"
                      Value="{DynamicResource SubtleFillColorSecondaryBrush}" />
            </Trigger>

            <!--
              MULTI-SELECT: DataTrigger on ViewModel.IsSelected.
              This is separate from native TreeViewItem.IsSelected.
              Use this when driving selection from a behavior.
            -->
            <DataTrigger Binding="{Binding IsSelected}" Value="True">
              <Setter TargetName="RowBorder" Property="Background"
                      Value="{DynamicResource SystemAccentColorSecondaryBrush}" />
              <Setter TargetName="AccentStripe"
                      Property="Visibility" Value="Visible" />
            </DataTrigger>

            <!--
              ALTERNATIVE: If using the behavior's attached IsItemSelected
              property instead of ViewModel binding, use this trigger:
            -->
            <!--
            <Trigger Property="b:TreeViewMultiSelectBehavior.IsItemSelected"
                     Value="True">
              <Setter TargetName="RowBorder" Property="Background"
                      Value="{DynamicResource SystemAccentColorSecondaryBrush}" />
              <Setter TargetName="AccentStripe"
                      Property="Visibility" Value="Visible" />
            </Trigger>
            -->

            <!--
              NATIVE single-select (only use ONE of: DataTrigger, attached
              property trigger, or this native trigger — not multiple):
            -->
            <!--
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="RowBorder" Property="Background"
                      Value="{DynamicResource SystemAccentColorSecondaryBrush}" />
              <Setter TargetName="AccentStripe"
                      Property="Visibility" Value="Visible" />
            </Trigger>
            -->
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</Window.Resources>

<!-- Apply to TreeView -->
<TreeView ItemsSource="{Binding Nodes}"
          ItemContainerStyle="{StaticResource FullRowTreeViewItemStyle}"
          HorizontalContentAlignment="Stretch"
          ScrollViewer.HorizontalScrollBarVisibility="Disabled">
  <TreeView.Resources>
    <HierarchicalDataTemplate DataType="{x:Type vm:TreeNodeViewModel}"
                              ItemsSource="{Binding Children}">
      <StackPanel Orientation="Horizontal">
        <ui:SymbolIcon Symbol="Document24" Margin="0,0,6,0" FontSize="14" />
        <TextBlock Text="{Binding Name}" VerticalAlignment="Center" />
      </StackPanel>
    </HierarchicalDataTemplate>
  </TreeView.Resources>
</TreeView>
```

### 2.5 Critical requirements and pitfalls for full-row highlight

**`HorizontalContentAlignment="Stretch"` on the TreeView is mandatory.** Without it, `TreeViewItem` content auto-sizes and the outer border does not stretch to fill available width. This is the single most common reason full-row highlight "doesn't work."

**`ScrollViewer.HorizontalScrollBarVisibility="Disabled"`** prevents items from extending beyond the visible area. With `Auto` or `Visible`, the "full row" extends to the content's natural width, which may be wider than the viewport.

**`IndentSize` must match the expander column width.** The converter computes `left margin = depth × IndentSize`. If you change the expander `ToggleButton` width from 19, update the converter's `IndentSize` or the row indentation will drift from the actual expander position.

**`IsMouseOver` bubbles to parents.** When hovering a child item, `IsMouseOver` is `true` on all ancestor `TreeViewItem`s. The trigger on `RowBorder` fires correctly because it targets the template's named element, but if you add hover logic at the style level it affects the entire ancestor chain.

### 2.6 WPF-UI integration details

WPF-UI's `<ui:ControlsDictionary />` loads an implicit style for `TreeViewItem` that replaces the default template with a Fluent-themed one. This template includes an `ActiveRectangle` (3px selection pill) and uses theme resources like `TreeViewItemBackgroundSelected` and `TreeViewItemSelectionIndicatorForeground`.

When you provide a custom `ItemContainerStyle`, it overrides the implicit WPF-UI style entirely. To inherit WPF-UI's non-template setters (Foreground, Margin, Padding, CornerRadius), use `BasedOn`:

```xml
<Style TargetType="TreeViewItem"
       BasedOn="{StaticResource DefaultTreeViewItemStyle}">
  <Setter Property="Template">
    <!-- your custom template — fully replaces WPF-UI's template -->
  </Setter>
</Style>
```

**Trap:** `BasedOn` preserves property setters but **NOT** the template. When you set `Template`, you must reimplement everything — expander, selection indicator, hover states, all triggers. The WPF-UI visual elements (`ActiveRectangle`, chevron animation) do not carry over.

### 2.7 Selected vs keyboard-focused — they are distinct states

`IsSelected` indicates the item is in the TreeView's selected set. `IsSelectionActive` (an attached property from `Selector`) indicates whether the TreeView or a descendant currently has keyboard focus — this controls the "active" vs "inactive" selection color. `IsFocused` and `IsKeyboardFocused` indicate the specific item has focus, which is a completely different state.

For multi-select, `IsSelectionActive` matters because when the TreeView loses focus (user clicks another control), all selected items should dim to an "inactive" color. The default template uses a `MultiTrigger` combining `IsSelected=True` and `IsSelectionActive=False` to apply `SystemColors.ControlBrush` (gray) instead of `SystemColors.HighlightBrush` (blue). When using a custom ViewModel `IsSelected`, replicate this with a `MultiDataTrigger` checking both the ViewModel property and `IsSelectionActive`.

---

## Section 3 — Collapsible panel with persistent toggle bar

### 3.1 Architecture: two Grid rows, not one

The VS/VS Code pattern uses two separate layout regions: an **always-visible header strip** (24–32px) and a **collapsible body**. In WPF, implement this as separate Grid rows, not a single row with swapped content. A single-row approach requires complex template logic and fights `GridSplitter` behavior.

The Grid layout uses 4 rows: main content (`*`), GridSplitter (`Auto`), panel header (`Auto`), and panel body (pixel-valued, collapsible to 0). The `GridSplitter` and panel body are hidden via `Visibility.Collapsed` when the panel collapses. The header row is always visible.

**`RowDefinition.Height` is a `GridLength`, not a `double`.** Direct binding is technically possible but unreliable due to dotnet/wpf#4392: when `GridSplitter` modifies a bound `RowDefinition.Height`, it writes pixel values with `GridUnitType.Star`, corrupting the stored value. The reliable approach is code-behind manipulation using `ActualHeight` and constructing `new GridLength(value, GridUnitType.Pixel)`.

**`GridLength` is not animatable.** The `GridLengthAnimation` type does not exist in WPF. Animating requires targeting an inner container's `Height` property instead. Since the user prefers instant snap, this is not a concern — set `RowDefinition.Height` directly for an immediate layout pass.

### 3.2 Complete implementation

```xml
<Window x:Class="MyApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        xmlns:local="clr-namespace:MyApp"
        Title="Main" Height="600" Width="900">
  <Window.Resources>
    <BooleanToVisibilityConverter x:Key="BoolToVis" />
    <local:BoolToChevronConverter x:Key="BoolToChevron" />
  </Window.Resources>

  <Grid>
    <Grid.RowDefinitions>
      <!-- Row 0: Main content -->
      <RowDefinition Height="*" MinHeight="100" />
      <!-- Row 1: GridSplitter (collapses when panel hidden) -->
      <RowDefinition Height="Auto" />
      <!-- Row 2: Panel header — ALWAYS visible, fixed height -->
      <RowDefinition Height="Auto" />
      <!-- Row 3: Panel body — 0 when collapsed, pixel height when expanded -->
      <RowDefinition x:Name="PanelBodyRow" Height="200" MinHeight="0" />
    </Grid.RowDefinitions>

    <!-- Row 0: Main content -->
    <Border Grid.Row="0"
            Background="{DynamicResource ControlFillColorDefaultBrush}">
      <TextBlock Text="Main Content"
                 HorizontalAlignment="Center" VerticalAlignment="Center" />
    </Border>

    <!-- Row 1: GridSplitter — hidden when collapsed -->
    <GridSplitter x:Name="PanelSplitter" Grid.Row="1"
                  Height="4" HorizontalAlignment="Stretch"
                  ResizeDirection="Rows" ResizeBehavior="PreviousAndNext"
                  Background="{DynamicResource ControlStrokeColorDefaultBrush}"
                  Visibility="{Binding IsPanelExpanded,
                    Converter={StaticResource BoolToVis},
                    RelativeSource={RelativeSource AncestorType=Window}}"
                  DragCompleted="Splitter_DragCompleted" />

    <!-- Row 2: Panel header — always visible -->
    <Border Grid.Row="2" Height="28"
            Background="{DynamicResource SubtleFillColorSecondaryBrush}"
            BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}"
            BorderThickness="0,1,0,0">
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="Auto" />
          <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>

        <ToggleButton Grid.Column="0" Width="24" Height="24"
                      Margin="4,0,0,0" Padding="0"
                      BorderThickness="0" Background="Transparent"
                      IsChecked="{Binding IsPanelExpanded, Mode=TwoWay,
                        RelativeSource={RelativeSource AncestorType=Window}}"
                      Click="TogglePanel_Click">
          <ui:SymbolIcon Symbol="{Binding IsPanelExpanded,
              Converter={StaticResource BoolToChevron},
              RelativeSource={RelativeSource AncestorType=Window}}"
              FontSize="14" />
        </ToggleButton>

        <TextBlock Grid.Column="1" Text="PROPERTIES"
                   FontSize="11" FontWeight="SemiBold"
                   VerticalAlignment="Center" Margin="6,0,0,0"
                   Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
      </Grid>
    </Border>

    <!-- Row 3: Panel body — collapsible -->
    <Border x:Name="PanelBody" Grid.Row="3"
            Background="{DynamicResource ControlFillColorDefaultBrush}"
            BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}"
            BorderThickness="0,1,0,0"
            Visibility="{Binding IsPanelExpanded,
              Converter={StaticResource BoolToVis},
              RelativeSource={RelativeSource AncestorType=Window}}">
      <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="8">
          <TextBlock Text="Name:" FontWeight="SemiBold" Margin="0,0,0,4" />
          <TextBox Text="SelectedItem" Margin="0,0,0,8" />
          <TextBlock Text="Type:" FontWeight="SemiBold" Margin="0,0,0,4" />
          <TextBox Text="System.String" IsReadOnly="True" Margin="0,0,0,8" />
        </StackPanel>
      </ScrollViewer>
    </Border>
  </Grid>
</Window>
```

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MyApp;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private GridLength _savedHeight = new(200, GridUnitType.Pixel);
    private const double MinExpanded = 60;

    private bool _isPanelExpanded = true;
    public bool IsPanelExpanded
    {
        get => _isPanelExpanded;
        set { _isPanelExpanded = value; OnPropertyChanged(); }
    }

    public MainWindow()
    {
        InitializeComponent();
        UpdatePanelLayout(IsPanelExpanded);
    }

    private void TogglePanel_Click(object sender, RoutedEventArgs e)
    {
        // ToggleButton already flipped IsPanelExpanded via binding.
        // Drive the actual layout change here.
        if (IsPanelExpanded)
            ExpandPanel();
        else
            CollapsePanel();
    }

    private void CollapsePanel()
    {
        // Save current height BEFORE collapsing
        double actual = PanelBodyRow.ActualHeight;
        if (actual > MinExpanded)
            _savedHeight = new GridLength(actual, GridUnitType.Pixel);

        PanelBodyRow.Height = new GridLength(0);
        PanelBodyRow.MinHeight = 0;
        IsPanelExpanded = false;
        // GridSplitter + body hide via Visibility binding
    }

    private void ExpandPanel()
    {
        IsPanelExpanded = true;
        PanelBodyRow.Height = _savedHeight;
        PanelBodyRow.MinHeight = MinExpanded;
    }

    private void UpdatePanelLayout(bool expanded)
    {
        if (expanded)
        {
            PanelBodyRow.Height = _savedHeight;
            PanelBodyRow.MinHeight = MinExpanded;
        }
        else
        {
            PanelBodyRow.Height = new GridLength(0);
            PanelBodyRow.MinHeight = 0;
        }
    }

    /// <summary>
    /// Persist user-resized height after GridSplitter drag.
    /// CRITICAL: Read ActualHeight, not RowDefinition.Height.
    /// dotnet/wpf#4392: GridSplitter writes pixel values with
    /// GridUnitType.Star, corrupting the stored GridLength.
    /// </summary>
    private void Splitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        double actual = PanelBodyRow.ActualHeight;
        if (actual >= MinExpanded)
            _savedHeight = new GridLength(actual, GridUnitType.Pixel);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

**Chevron converter:**

```csharp
[ValueConversion(typeof(bool), typeof(Wpf.Ui.Controls.SymbolRegular))]
public class BoolToChevronConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true
            ? Wpf.Ui.Controls.SymbolRegular.ChevronUp24    // expanded: ▲
            : Wpf.Ui.Controls.SymbolRegular.ChevronDown24; // collapsed: ▼

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}
```

### 3.3 Key traps in the collapsible panel pattern

**Setting `IsEnabled = false` on GridSplitter instead of `Visibility.Collapsed`** — a disabled splitter still occupies its 4px layout slot, creating a visible dead strip. Use `Visibility.Collapsed` to remove it from layout entirely.

**Using `Visibility.Hidden` on the panel body instead of `Collapsed`** — `Hidden` preserves layout space. The row height stays at 200px, just invisible. `Collapsed` removes from layout, but you also need `RowDefinition.Height = 0` because `Collapsed` on a child does not collapse the row itself.

**Binding `RowDefinition.Height` TwoWay with a converter** — works for simple toggle but breaks when `GridSplitter` drags. The splitter writes back a `GridLength` with the wrong `GridUnitType` (dotnet/wpf#4392). Always use `ActualHeight` in `DragCompleted` to capture the real value.

**Forgetting to reset `MinHeight` on collapse** — if the body row has `MinHeight="60"` and you set `Height = 0`, the row still renders at 60px. Set `MinHeight = 0` before collapsing.

**Persisting across app restarts:** Save `_savedHeight.Value` and `IsPanelExpanded` to `Properties.Settings.Default` in the `Window.Closing` event.

---

## Section 4 — Common TreeView footguns not yet hit

### 4.1 Virtualization silently breaks multi-select range calculation

When `VirtualizingPanel.IsVirtualizing="True"` (which is the default for performance), WPF only generates `TreeViewItem` containers for items visible in the viewport. **`ItemContainerGenerator.ContainerFromIndex(i)` returns `null` for off-screen items.** The multi-select range algorithm in Section 1 calls `FlattenVisible`, which skips null entries — so Shift+click range selection silently omits off-screen items. No exception, no warning.

The fix is ViewModel-driven selection: flatten the tree model (not the visual tree), compute the range on model objects, and set `IsSelected` on each ViewModel. This eliminates the container dependency entirely:

```csharp
// CORRECT: operate on the model, not on containers
var flat = FlattenModelTree(rootNodes);
int a = flat.IndexOf(anchor);
int b = flat.IndexOf(target);
for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
    flat[i].IsSelected = true;
```

If you must use container-based selection, disable virtualization: `VirtualizingPanel.IsVirtualizing="False"`. This works for trees under ~1,000 items but kills performance at scale. There is no middle ground — virtualization and container-based multi-select are fundamentally incompatible.

Related dotnet/wpf issues: **#1962** (TreeView + VirtualizingStackPanel + expanded nodes freezes app), **#7321** (virtualization broken under .NET 7), **#11331** (TreeView hangs with virtualization + null item in ItemsSource).

### 4.2 `BringIntoView()` has three distinct silent failure modes

**Failure 1 — Container not generated.** If the target item's `TreeViewItem` container hasn't been realized (virtualization or collapsed parent), `BringIntoView()` operates on nothing. No scroll occurs, no error.

**Failure 2 — `CanContentScroll="False"` (the default!).** The default TreeView template sets `ScrollViewer.CanContentScroll="False"`, forcing pixel-based scrolling. This can cause `BringIntoView` to miscalculate offsets with virtualized content.

**Failure 3 — Async container generation.** Setting `parentItem.IsExpanded = true` triggers async container generation. Calling `BringIntoView()` immediately after will fail because children don't exist yet.

The fix requires expanding all ancestors, waiting for layout, then scrolling:

```csharp
// Expand all ancestors first
foreach (var ancestor in GetAncestorPath(targetNode))
{
    ancestor.IsExpanded = true;
    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
    treeView.UpdateLayout();  // force synchronous container generation
}

// NOW get the container and scroll
var tvi = GetNestedTreeViewItem(targetNode, treeView);
tvi?.BringIntoView();
```

**Additional trap:** `BringIntoView` scrolls the item to the nearest edge (top or bottom), not center. For centering, compute the offset manually using `TransformToAncestor` and `ScrollToVerticalOffset`.

### 4.3 Expand-on-select causes layout thrashing with multi-select

The naive binding `IsExpanded="{Binding IsSelected, RelativeSource={RelativeSource Self}}"` couples expansion to selection. Ctrl+clicking to select 5 items opens all 5 subtrees. Worse, when the user clicks elsewhere and items deselect, they all collapse, destroying the user's navigation context.

The correct pattern is ViewModel-driven independent expansion: expand when selected (optional), but **never auto-collapse** on deselect:

```csharp
public bool IsSelected
{
    get => _isSelected;
    set
    {
        _isSelected = value;
        if (value && HasChildren) IsExpanded = true;  // expand, never collapse
        OnPropertyChanged();
    }
}
```

### 4.4 Keyboard navigation wipes custom multi-selection

WPF's built-in keyboard handler calls `TreeViewItem.IsSelected = true` on the target item, which fires `TreeView.ChangeSelection()`, deselecting everything else. Arrow keys, Home, and End all trigger this. Custom multi-select state is silently wiped.

The fix: intercept `KeyDown` with `handledEventsToo: true` and set `e.Handled = true` to prevent the default handler. Implement Shift+Arrow for range extension and Ctrl+Arrow for focus-without-select manually.

**Trap after programmatic selection:** Setting `item.IsSelected = true` in code does NOT move keyboard focus. Arrow keys still navigate from the previously focused item. You must call `tvi.Focus()` alongside selection.

### 4.5 Drag initiation deselects multi-selection before drag starts

The click-to-drag sequence is: `MouseLeftButtonDown` → (drag threshold) → `MouseMove` → `DoDragDrop`. WPF's `TreeViewItem` handles `MouseLeftButtonDown` by calling `ChangeSelection`, deselecting all other items before the drag threshold is reached. By `MouseMove`, only one item is selected.

The fix is **deferred selection**: in `PreviewMouseLeftButtonDown`, if the clicked item is already selected and no modifier keys are held, set `e.Handled = true` to suppress the default handler and record a "pending deselect." In `MouseMove`, if drag threshold is reached, initiate drag with all selected items and cancel the pending deselect. In `MouseUp`, if no drag occurred, execute the pending deselect (single-select the clicked item).

### 4.6 `HierarchicalDataTemplate` with initially null `ItemsSource`

If the ViewModel's `Children` property is `null` when the container first generates, WPF marks the node as a leaf (no expander arrow). When `Children` is later populated, the binding may not re-evaluate because WPF never subscribed to `CollectionChanged` on a null reference.

```csharp
// WRONG: null initial value → binding never subscribes
public ObservableCollection<Node> Children { get; set; }  // starts null

// CORRECT: always-initialized collection with dummy sentinel
private ObservableCollection<Node> _children = new();
public ObservableCollection<Node> Children
{
    get => _children;
    set { _children = value; OnPropertyChanged(); }
}

public Node()
{
    // Sentinel forces WPF to show expander arrow
    Children.Add(new DummySentinel());
}
```

The dummy sentinel is a placeholder that signals "this node has children" before they're loaded. Use a `DataTemplate` for the sentinel type that renders nothing: `<DataTemplate DataType="{x:Type local:DummySentinel}" />`. On first expand, clear the sentinel and load real children into the existing collection (don't replace the collection object).

### 4.7 `ContainerFromItem` only searches the immediate level

`TreeView.ItemContainerGenerator.ContainerFromItem(deepItem)` returns `null` for any item that isn't a root-level node. Each `TreeViewItem` has its **own** `ItemContainerGenerator` for its children. You must recursively walk the tree:

```csharp
public static TreeViewItem? FindContainer(ItemsControl parent, object item)
{
    var tvi = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
    if (tvi != null) return tvi;

    foreach (object child in parent.Items)
    {
        var childContainer = parent.ItemContainerGenerator
            .ContainerFromItem(child) as TreeViewItem;
        if (childContainer == null) continue;  // virtualized
        var result = FindContainer(childContainer, item);
        if (result != null) return result;
    }
    return null;
}
```

**Best practice:** avoid needing containers at all. Bind `IsSelected` and `IsExpanded` TwoWay to ViewModel properties. This eliminates `ContainerFromItem` calls in nearly every scenario.

### 4.8 Hosting TreeView in infinity-measuring panels kills virtualization

Placing a TreeView inside a `StackPanel`, `ScrollViewer`, or `Grid` with `Height="Auto"` causes the parent to measure with infinite available height. The `VirtualizingStackPanel` inside TreeView receives infinite space, realizes **all** items at once, and virtualization silently does nothing. No warning, just degraded performance.

Always host TreeView in a container with constrained height: a `Grid` row with `Height="*"` or a fixed height. This is the single most common cause of "virtualization doesn't help" reports.

---

## Conclusion

WPF TreeView's three core hostilities — single-select enforcement via `ChangeSelection`, selection highlight confined to Column 1 of a 3-column Grid, and `e.Handled = true` in `OnMouseLeftButtonDown` — require parallel systems rather than incremental fixes. The multi-select behavior must use its own `IsItemSelected` attached property, intercepting clicks via `AddHandler(..., true)`. The full-row highlight must replace the entire `ControlTemplate`, moving the highlight border outside the column layout and computing indentation via a depth converter. The collapsible panel pattern works reliably with separate Grid rows and code-behind height manipulation (not binding, due to dotnet/wpf#4392).

The unifying principle across all four sections: **operate on ViewModels, not containers**. Container-based approaches break under virtualization, async loading, and collapsed nodes. ViewModel-driven `IsSelected`, `IsExpanded`, and tree flattening eliminate the entire class of `ContainerFromItem`-returns-null bugs.
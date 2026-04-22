namespace Deskbridge.ViewModels;

public abstract partial class TreeItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public Guid Id { get; init; }

    /// <summary>
    /// STAB-05: Nesting depth in the tree hierarchy.
    /// Computed once during BuildTree by AssignDepths, not reactive.
    /// Used by converters for indent margin and guide lines instead of walking the visual tree.
    /// </summary>
    public int Depth { get; set; }
}

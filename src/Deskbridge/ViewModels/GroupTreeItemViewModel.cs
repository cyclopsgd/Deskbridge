using System.Collections.ObjectModel;

namespace Deskbridge.ViewModels;

public partial class GroupTreeItemViewModel : TreeItemViewModel
{
    [ObservableProperty]
    public partial ObservableCollection<TreeItemViewModel> Children { get; set; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool HasCredentials { get; set; }

    public Guid? ParentGroupId { get; set; }

    public int SortOrder { get; set; }

    public int ConnectionCount => CountConnections(Children);

    private static int CountConnections(IEnumerable<TreeItemViewModel> items)
    {
        int count = 0;
        foreach (var item in items)
        {
            if (item is ConnectionTreeItemViewModel) count++;
            else if (item is GroupTreeItemViewModel g) count += CountConnections(g.Children);
        }
        return count;
    }
}

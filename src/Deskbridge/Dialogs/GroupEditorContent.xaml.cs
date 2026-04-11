using System.Windows.Controls;
using Deskbridge.ViewModels;

namespace Deskbridge.Dialogs;

public partial class GroupEditorContent : UserControl
{
    public GroupEditorContent()
    {
        InitializeComponent();
    }

    private void GroupPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is GroupEditorViewModel vm && sender is PasswordBox pb)
        {
            vm.SetPassword(pb.Password);
        }
    }
}

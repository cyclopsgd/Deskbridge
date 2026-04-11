using System.Windows.Controls;
using Deskbridge.ViewModels;

namespace Deskbridge.Dialogs;

public partial class ConnectionEditorContent : UserControl
{
    public ConnectionEditorContent()
    {
        InitializeComponent();
    }

    private void PasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ConnectionEditorViewModel vm && sender is PasswordBox pb)
        {
            vm.SetPassword(pb.Password);
        }
    }
}

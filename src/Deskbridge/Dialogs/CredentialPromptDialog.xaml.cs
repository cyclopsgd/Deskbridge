using System.Windows.Input;
using Wpf.Ui.Controls;

namespace Deskbridge.Dialogs;

/// <summary>
/// Credential prompt dialog for <c>CredentialMode.Prompt</c> connections. Shows
/// hostname (read-only), username, domain, and password fields. Credentials
/// entered here are one-time and are NOT persisted to Windows Credential Manager.
///
/// <para><b>Pitfall 1</b>: BasedOn style in XAML Resources for custom ContentDialog subclass.</para>
/// <para><b>Pitfall 8</b>: PreviewKeyDown intercepts Enter in TextBox fields to prevent
/// premature Primary button activation (WPF-UI Discussion #1404).</para>
/// </summary>
public partial class CredentialPromptDialog : ContentDialog
{
    /// <summary>Username entered by the user (read after dialog closes).</summary>
    public string EnteredUsername => UsernameBox.Text;

    /// <summary>Domain entered by the user (read after dialog closes).</summary>
    public string EnteredDomain => DomainBox.Text;

    /// <summary>Password entered by the user (read after dialog closes).</summary>
    public string EnteredPassword => PasswordBox.Password;

    public CredentialPromptDialog(
        ContentDialogHost dialogHost,
        string hostname,
        string? prefillUsername = null,
        string? prefillDomain = null)
        : base(dialogHost)
    {
        InitializeComponent();

        HostnameText.Text = hostname;

        if (!string.IsNullOrEmpty(prefillUsername))
            UsernameBox.Text = prefillUsername;

        if (!string.IsNullOrEmpty(prefillDomain))
            DomainBox.Text = prefillDomain;

        // Pitfall 8: intercept Enter in TextBox/PasswordBox to prevent premature
        // Primary button activation.
        PreviewKeyDown += Dialog_PreviewKeyDown;

        // Focus the appropriate field on load: if username is pre-filled, focus
        // password; otherwise focus username.
        Loaded += (_, _) =>
        {
            if (!string.IsNullOrEmpty(prefillUsername))
                PasswordBox.Focus();
            else
                UsernameBox.Focus();
        };
    }

    /// <summary>
    /// Pitfall 8 (WPF-UI Discussion #1404): Enter inside a TextBox or PasswordBox
    /// prematurely triggers the Primary button. Suppress Enter for TextBox; allow
    /// Enter in PasswordBox to submit (natural UX for password fields).
    /// </summary>
    private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.FocusedElement is System.Windows.Controls.TextBox)
            e.Handled = true;
    }
}

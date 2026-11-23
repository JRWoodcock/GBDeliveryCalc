using System.Windows;

namespace GBDeliveryCalc.App.Views;

/// <summary>Asks for a password, optionally twice, for API key backups.</summary>
public partial class PasswordWindow : Window
{
    private readonly bool _confirm;

    public PasswordWindow(string title, string prompt, bool confirm)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        _confirm = confirm;
        ConfirmPanel.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => PasswordBox1.Focus();
    }

    /// <summary>The entered password once OK was clicked.</summary>
    public string Password { get; private set; } = "";

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_confirm && PasswordBox1.Password != PasswordBox2.Password)
        {
            ErrorText.Text = "The passwords don't match.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Password = PasswordBox1.Password;
        PasswordBox1.Clear();
        PasswordBox2.Clear();
        DialogResult = true;
    }
}

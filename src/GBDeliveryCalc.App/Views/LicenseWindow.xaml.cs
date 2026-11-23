using System.IO;
using System.Windows;

namespace GBDeliveryCalc.App.Views;

/// <summary>Shows LICENSE.txt from beside the executable.</summary>
public partial class LicenseWindow : Window
{
    public LicenseWindow()
    {
        InitializeComponent();
        LicenseTextBox.Text = LoadLicenseText();
    }

    private static string LoadLicenseText()
    {
        var licensePath = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
        if (!File.Exists(licensePath)) return "LICENSE.txt was not found beside the application executable.";
        try
        {
            return File.ReadAllText(licensePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "LICENSE.txt could not be read from beside the application executable.";
        }
    }
}

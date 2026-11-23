using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Navigation;

namespace GBDeliveryCalc.App.Views;

/// <summary>About window, matching the one in GB PunchList, with this app's name and version.</summary>
public partial class AboutWindow : Window
{
    public const string ProductName = "GB DeliveryCalc";
    private const string EmailAddress = "jrwoodcock@outlook.com";
    private const string SupportUrl = "https://buymeacoffee.com/jrwoodcock";
    private const string PrivacyPolicyUrl = "https://jasonwoodcock.us/apps/privacy/";

    public AboutWindow()
    {
        InitializeComponent();
        ProductRun.Text = ProductName;
        VersionRun.Text = "  Version " + AppVersion();
        SupportText.Text = $"{ProductName} is free to use. If it helps your work, donations are accepted to support continued development.";
        LicenseProductRun.Text = ProductName;
        LicenseSummaryRun.Text = $"{ProductName} is proprietary freeware. It may be used at no cost for lawful personal, business, internal, commercial, nonprofit, educational, or government purposes. Redistribution, resale, repackaging, mirroring, hosting, sublicensing, or bundling is prohibited.";
    }

    /// <summary>The informational version from the assembly, without any source-revision suffix.</summary>
    public static string AppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info)) return info.Split('+')[0];
        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        OpenExternalUrl(e.Uri);
        e.Handled = true;
    }

    private void CopyEmailButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(EmailAddress);
        }
        catch (ExternalException)
        {
            // Windows refuses the copy while another program holds the clipboard open.
            MessageBox.Show(this,
                $"The email address could not be copied because another program is using the clipboard.{Environment.NewLine}{Environment.NewLine}The address is {EmailAddress}",
                "Copy email address", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BuyCoffeeButton_Click(object sender, RoutedEventArgs e)
    {
        Uri.TryCreate(SupportUrl, UriKind.Absolute, out var supportUri);
        OpenExternalUrl(supportUri);
    }

    /// <summary>Opens http(s) links only, in the default browser.</summary>
    private void OpenExternalUrl(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Thrown when Windows has no browser set up to open web links.
            MessageBox.Show(this,
                $"Windows could not open a web browser. You can visit this address instead:{Environment.NewLine}{Environment.NewLine}{uri.AbsoluteUri}",
                "Open link", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ViewLicenseButton_Click(object sender, RoutedEventArgs e) => new LicenseWindow { Owner = this }.ShowDialog();

    private void PrivacyPolicyButton_Click(object sender, RoutedEventArgs e) => OpenExternalUrl(new Uri(PrivacyPolicyUrl));
}

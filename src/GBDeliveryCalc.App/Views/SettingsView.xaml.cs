using System.Windows;
using System.Windows.Controls;
using GBDeliveryCalc.Presentation;

namespace GBDeliveryCalc.App.Views;

/// <summary>Settings tab. API keys are entered in PasswordBoxes and passed to the view model here.</summary>
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    // PasswordBox can't be data-bound, so the key is handed to the view model here and the box is cleared once saved.
    // The saved key is never loaded back into the box.
    private void SaveGoogleKey_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.SaveGoogleKey(GoogleKeyBox.Password) == true) GoogleKeyBox.Clear();
    }

    private async void TestGoogleKey_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var button = (Button)sender;
        button.IsEnabled = false;
        try { await vm.TestGoogleKeyAsync(GoogleKeyBox.Password); }
        finally { button.IsEnabled = true; }
    }

    private void RemoveGoogleKey_Click(object sender, RoutedEventArgs e)
    {
        Vm?.RemoveGoogleKey();
        GoogleKeyBox.Clear();
    }

    private async void SaveFuelKey_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && await vm.SaveFuelKeyAsync(FuelKeyBox.Password)) FuelKeyBox.Clear();
    }

    private async void TestFuelKey_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var button = (Button)sender;
        button.IsEnabled = false;
        try { await vm.TestFuelKeyAsync(FuelKeyBox.Password); }
        finally { button.IsEnabled = true; }
    }

    private async void RemoveFuelKey_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) await vm.RemoveFuelKeyAsync();
        FuelKeyBox.Clear();
    }

    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = Window.GetWindow(this) }.ShowDialog();
}

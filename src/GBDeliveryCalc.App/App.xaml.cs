using System.Windows;
using System.Windows.Threading;
using GBDeliveryCalc.Data;
using GBDeliveryCalc.Presentation;
using GBDeliveryCalc.Services;
using GBDeliveryCalc.App.Infrastructure;

namespace GBDeliveryCalc.App;

/// <summary>Application entry point and composition root.</summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        var dialogs = new WpfDialogService();
        var theme = new ThemeService();
        theme.Apply(theme.Current);
        try
        {
            var database = new SqliteDatabase(AppDataPaths.DatabasePath);
            await database.MigrateAsync();

            var services = new EstimatorServices(
                new SqliteSettingsStore(database),
                new SqliteEstimateStore(database),
                new SqliteFuelPriceCache(database),
                new SqliteMaintenance(database),
                new DpapiSecretStore(),
                keys => ProviderFactory.Create(keys),
                TimeProvider.System);

            var vm = new MainViewModel(services, new WpfClipboardService(), dialogs, theme);
            var window = new MainWindow { DataContext = vm };
            MainWindow = window;
            window.Show();
            await vm.InitializeAsync();
        }
        catch (Exception ex)
        {
            dialogs.ShowError("GB DeliveryCalc could not start",
                $"{ex.Message}\n\nData folder: {AppDataPaths.DataDirectory}");
            Shutdown(1);
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        new WpfDialogService().ShowError("Unexpected error", e.Exception.Message);
        e.Handled = true;
    }
}

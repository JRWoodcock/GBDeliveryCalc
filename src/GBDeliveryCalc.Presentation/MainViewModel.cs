using CommunityToolkit.Mvvm.ComponentModel;

namespace GBDeliveryCalc.Presentation;

/// <summary>Root view model; wires the three tabs together.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    public const int NewEstimateTab = 0;
    public const int HistoryTab = 1;
    public const int SettingsTab = 2;

    public NewEstimateViewModel NewEstimate { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] public partial int SelectedTab { get; set; }

    public MainViewModel(EstimatorServices services, IClipboardService clipboard, IDialogService dialogs, IThemeService? theme = null)
    {
        NewEstimate = new NewEstimateViewModel(services, clipboard);
        History = new HistoryViewModel(services, clipboard, dialogs);
        Settings = new SettingsViewModel(services, dialogs, theme);
        NewEstimate.EstimateSaved += async (_, _) => await Guard(History.LoadAsync).ConfigureAwait(true);
        History.EditRequested += async (_, snapshot) => await Guard(async () =>
        {
            await NewEstimate.LoadForEditAsync(snapshot).ConfigureAwait(true);
            SelectedTab = NewEstimateTab;
        }).ConfigureAwait(true);
        History.EstimateDeleted += (_, id) => NewEstimate.DetachDeleted(id);
        Settings.SettingsChanged += async (_, _) => await Guard(async () =>
        {
            await NewEstimate.LoadAsync().ConfigureAwait(true);
            await History.LoadAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Event handlers are async void, so an exception would end the app. Show it in the status line instead.
    /// </summary>
    private async Task Guard(Func<Task> action)
    {
        try { await action().ConfigureAwait(true); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            NewEstimate.StatusMessage = "Couldn't refresh the screen: " + ex.Message;
        }
    }

    public async Task InitializeAsync()
    {
        await Settings.LoadAsync().ConfigureAwait(true);
        await NewEstimate.StartNewAsync().ConfigureAwait(true); // also fetches today's fuel prices
        await History.LoadAsync().ConfigureAwait(true);
        // First run: send staff to Settings until at least one team exists.
        if (NewEstimate.Teams.Count == 0) SelectedTab = SettingsTab;
    }
}

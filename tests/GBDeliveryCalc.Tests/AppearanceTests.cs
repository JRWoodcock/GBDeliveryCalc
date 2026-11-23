using GBDeliveryCalc.Presentation;
using GBDeliveryCalc.Services;

namespace GBDeliveryCalc.Tests;

public class AppearanceTests
{
    private sealed class FakeTheme(AppTheme current) : IThemeService
    {
        public AppTheme Current { get; private set; } = current;
        public List<AppTheme> Applied { get; } = [];
        public void Apply(AppTheme theme) { Current = theme; Applied.Add(theme); }
    }

    [Fact]
    public async Task Appearance_defaults_to_the_saved_choice_and_applies_changes_immediately()
    {
        using var db = await TempDatabase.CreateAsync();
        var services = new EstimatorServices(new Data.SqliteSettingsStore(db.Database), new Data.SqliteEstimateStore(db.Database),
            new Data.SqliteFuelPriceCache(db.Database), new TestMaintenance(db.Database, db.Directory), new MemorySecretStore(),
            k => ProviderFactory.Create(k), TimeProvider.System);
        var theme = new FakeTheme(AppTheme.Dark);
        var vm = new MainViewModel(services, new FakeClipboard(), new FakeDialogs(), theme);
        await vm.InitializeAsync();

        Assert.Equal(AppTheme.Dark, vm.Settings.Appearance);
        Assert.Empty(theme.Applied); // loading doesn't re-apply
        Assert.Equal(new[] { AppTheme.FollowWindows, AppTheme.Light, AppTheme.Dark }, vm.Settings.Themes);

        vm.Settings.Appearance = AppTheme.Light;
        vm.Settings.Appearance = AppTheme.FollowWindows;
        Assert.Equal(new[] { AppTheme.Light, AppTheme.FollowWindows }, theme.Applied);
    }

    [Fact]
    public void Without_a_theme_service_appearance_is_follow_windows()
    {
        Assert.Equal(AppTheme.FollowWindows, default(AppTheme));
    }
}

using GBDeliveryCalc.Core.Integrations;
using GBDeliveryCalc.Services;

namespace GBDeliveryCalc.Tests;

public class StartingAddressTests
{
    private static IntegrationSet Google() => new(new FakePlaces(), new FakeRouting(), new NotConfiguredFuelPriceSource("n/a"));

    [Fact]
    public async Task Starting_address_is_verified_with_google_and_saved()
    {
        var (vm, _, db, services) = await WorkflowTests.CreateAsync(_ => Google());
        using var _db = db;
        var s = vm.Settings;
        s.OriginAddress = "Neshoba Central High School";
        Assert.False(s.OriginVerified);

        await s.SearchOriginAddressCommand.ExecuteAsync(null);
        var c = Assert.Single(s.OriginCandidates);
        Assert.False(s.ShowOriginPoweredByGoogle); // the fake provider is not Google
        Assert.Contains("Confirm match", s.OriginLookupMessage);

        s.SelectedOriginCandidate = c;
        await s.ConfirmOriginAddressCommand.ExecuteAsync(null);
        Assert.Equal("1125 Golf Course Rd, Philadelphia, MS 39350, USA", s.OriginAddress);
        Assert.True(s.OriginVerified);
        Assert.Empty(s.OriginCandidates);
        Assert.False(s.ShowOriginPoweredByGoogle);
        Assert.StartsWith("Verified by Google and saved", s.OriginLookupMessage);
        Assert.Equal("1125 Golf Course Rd, Philadelphia, MS 39350, USA", (await services.Settings.GetCompanySettingsAsync()).OriginAddress);

        s.OriginAddress += " Suite 2"; // edited by hand: no longer the verified address
        Assert.False(s.OriginVerified);
    }

    [Fact]
    public async Task Without_a_google_key_the_lookup_explains_how_to_enable_it()
    {
        var (vm, _, db, _) = await WorkflowTests.CreateAsync();
        using var _db = db;
        vm.Settings.OriginAddress = "Hattiesburg, MS";
        await vm.Settings.SearchOriginAddressCommand.ExecuteAsync(null);
        Assert.Contains("Google Maps Platform API key", vm.Settings.OriginLookupMessage);
        Assert.Empty(vm.Settings.OriginCandidates);
    }

    [Fact]
    public async Task Confirm_needs_a_selected_match()
    {
        var (vm, _, db, _) = await WorkflowTests.CreateAsync(_ => Google());
        using var _db = db;
        await vm.Settings.ConfirmOriginAddressCommand.ExecuteAsync(null);
        Assert.Equal("Select a match first.", vm.Settings.OriginLookupMessage);
    }
}

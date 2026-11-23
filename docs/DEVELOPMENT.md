# Development guide

Building, testing, packaging and maintaining the GB DeliveryCalc. For using the app, see the [README](../README.md).

## Solution layout

| Project | Purpose | References |
|---|---|---|
| `src/GBDeliveryCalc.Core` | Domain model, **calculation engine** (`Calculation/EstimateCalculator.cs`, `Calculation/ActualsComparer.cs`), cost-estimate text (`Quotes/CostEstimateFormatter.cs`), persistence and provider **interfaces**, fuel-price caching policy. No WPF, network or database code. | none |
| `src/GBDeliveryCalc.Data` | EF Core SQLite implementation of the stores, **migrations** (`Migrations/`), backup and restore (`DatabaseBackup.cs`), app-data paths. | Core |
| `src/GBDeliveryCalc.Services` | Google Places API (New) and Routes API adapters (`Google/`), the OilPriceAPI fuel adapter (`OilPriceApi/`), plus `ProviderFactory`, which returns "Not configured" providers for any integration without a saved key. | Core |
| `src/GBDeliveryCalc.Presentation` | View models (CommunityToolkit.Mvvm). No WPF types, so the main workflow is unit tested on any OS. | Core, Services |
| `src/GBDeliveryCalc.App` | WPF shell. **Entry point: `App.xaml.cs`** (composition root), views in `Views/`, and the DPAPI key store (`Infrastructure/DpapiSecretStore.cs`). | all |
| `tests/GBDeliveryCalc.Tests` | xUnit tests: calculator, actuals, SQLite persistence and history, providers and failures, and the view-model workflow. | all except App |
| `packaging/` | MSIX manifest template, logos, `build-msix.ps1`, `validate-msix.ps1`, `identity.json` placeholders. | n/a |

## Prerequisites

1. **Windows 11 x64** (needed to run the app or build an MSIX; the solution and tests also build on Linux and macOS).
2. **.NET SDK 10.0.100 or a later 10.0 feature band.** `global.json` pins `10.0.100` with `rollForward: latestFeature`.
   `winget install Microsoft.DotNet.SDK.10`
3. For MSIX only, the **Windows SDK** (MakeAppx, SignTool, App Certification Kit) and **PowerShell 7**:
   `winget install Microsoft.WindowsSDK.10.0.26100` and `winget install Microsoft.PowerShell`
4. Optional: Visual Studio 2026 or 2022 17.14+ (opens `GBDeliveryCalc.slnx`), or VS Code with C# Dev Kit.

## Build, run and test

From the repository root:

```powershell
dotnet --version                       # should print 10.0.x
dotnet tool restore                    # restores dotnet-ef (only needed for migrations)
dotnet build GBDeliveryCalc.slnx -c Release
dotnet test  GBDeliveryCalc.slnx -c Release
dotnet run --project src/GBDeliveryCalc.App -c Release      # Windows only
```

No live service, API key or network access is needed to build, test or run. The tests stub Google's and OilPriceAPI's HTTP responses.

To use a scratch database instead of your real one (for support or trying things out):

```powershell
$env:GROVER_ESTIMATOR_DATA_DIR = "$env:TEMP\grover-scratch"; dotnet run --project src/GBDeliveryCalc.App
```

## Data location, backup and restore (details)

| | Path |
|---|---|
| Unpackaged (`dotnet run`, or a copied folder) | `%LOCALAPPDATA%\GroverBrothers\Estimator\estimator.db` |
| Installed from MSIX or the Store | Windows redirects the same path into the package's private folder: `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\GroverBrothers\Estimator\estimator.db` |
| Override | set `GROVER_ESTIMATOR_DATA_DIR` |

Settings → *Backup and restore* shows the exact path in use.

The database is never stored in the installation directory, and `*.db` files are git-ignored.

**Uninstalling the MSIX deletes its private data folder.** Keep backups somewhere else.

### Back up (recommended weekly and before every upgrade)

* **In the app:** Settings → **Back up now…**, then save the `.estimator-backup` file to a network share, OneDrive or a USB drive. This uses SQLite's online backup API, so the copy is consistent even while the app is open.
* **Manually:** close the app, then copy `estimator.db` (and `estimator.db-wal` / `-shm` if they exist) somewhere safe.

### Restore

* **In the app:** Settings → **Restore from backup…**, pick the file, then confirm. The app checks that the file is an estimator database and passes `PRAGMA integrity_check`. It saves a safety copy of the current data first (`Backups\pre-restore-<timestamp>.estimator-backup`), restores, and then applies any newer schema migrations.
* **Manually:** close the app, rename the current `estimator.db` (and delete any `-wal`/`-shm` files), copy the backup in as `estimator.db`, and start the app. Migrations run on startup.

## Database migrations

Migrations live in `src/GBDeliveryCalc.Data/Migrations/` and are applied automatically at startup (`SqliteDatabase.MigrateAsync`).

```powershell
dotnet tool restore
dotnet ef migrations add <Name> --project src/GBDeliveryCalc.Data --output-dir Migrations
dotnet ef migrations script --project src/GBDeliveryCalc.Data --idempotent -o artifacts/migrate.sql   # to review the SQL
```

Saved estimates are stored as a versioned JSON snapshot (`EstimateSnapshot.SchemaVersion`), with a few denormalised columns for listing. If you change a snapshot type, keep old JSON readable: add properties with defaults, and never rename or remove them.

## Moving to a shared backend later

Persistence is behind `ISettingsStore`, `IEstimateStore` and `IFuelPriceCache` (in `Core/Persistence/Stores.cs`). To share one history across several PCs, implement those three interfaces against a company API or database and swap them in `App.xaml.cs`. Nothing else needs to change. `RetentionPolicy.EnsurePersistable` must still be called before any write.

## MSIX packaging and Microsoft Store

Short version (details, private-audience notes and the submission checklist are in **[docs/STORE.md](STORE.md)**):

```powershell
# 1. Put Partner Center's exact identity values in packaging/identity.local.json (git-ignored)
Copy-Item packaging/identity.json packaging/identity.local.json   # then edit

# 2. Build the package (the 4th version number must be 0; the first cannot be 0)
pwsh packaging/build-msix.ps1 -Version 1.0.0.0

# 3. Optional sideload test: sign with a test certificate whose subject equals Publisher
pwsh packaging/build-msix.ps1 -Version 1.0.0.0 -CertificatePath C:\certs\grover-test.pfx -CertificatePassword (Read-Host -AsSecureString)
Add-AppxPackage packaging/out/GBDeliveryCalc_1.0.0.0_x64.msix

# 4. Validate with the Windows App Certification Kit (elevated prompt)
pwsh packaging/validate-msix.ps1 -Package packaging/out/GBDeliveryCalc_1.0.0.0_x64.msix

# 5. Upload the unsigned .msix in Partner Center. The Store signs it.
```

## Themes

Standard controls use WPF's Fluent theme (`Application.ThemeMode`). The app's own colours are in `src/GBDeliveryCalc.App/Themes/Light.xaml` and `Dark.xaml`; `Infrastructure/ThemeService.cs` switches them and layers the app's control spacing on the Fluent styles. Keep new colours in both palettes and check them against WCAG AA (4.5:1).

## Further documents

* [ASSUMPTIONS.md](ASSUMPTIONS.md): calculation rules and design decisions.
* [INTEGRATIONS.md](INTEGRATIONS.md): how the Google and OilPriceAPI integrations work, and the provider evaluations.
* [GOOGLE_SETUP.md](GOOGLE_SETUP.md) and [FUEL_PRICES.md](FUEL_PRICES.md): key setup.
* [SECURITY_REVIEW.md](SECURITY_REVIEW.md): security review, findings and remaining risks.
* [STORE.md](STORE.md): Microsoft Store and MSIX steps.
* [VERIFICATION.md](VERIFICATION.md): what has been verified, how, and what has not.

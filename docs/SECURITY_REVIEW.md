# Security review

Reviewed 2026-11-19 on branch `feature/feedback-2026-11-19b`, before deployment of the desktop build to `%LOCALAPPDATA%\Programs\GBDeliveryCalc` and before any Microsoft Store submission.

## Scope and method

* **Dependencies:**
  * `dotnet list package --vulnerable --include-transitive`, and `--outdated`.
  * A licence check of every shipped package.
* **Static analysis:**
  * `AnalysisMode` is set to `Recommended` in `Directory.Build.props`.
  * Every rule in the Security category (CA2100, CA3xxx, CA5xxx) is raised to a warning in `.editorconfig`. Warnings are errors (`TreatWarningsAsErrors`), so any security finding fails the build.
  * The solution builds with **0 warnings**.
* **Manual review:**
  * Secret handling: the DPAPI key files, and whether keys can reach logs, exceptions, the database, backups or screenshots. Also PasswordBox handling.
  * Network: TLS and HTTP use.
  * Input validation and SQL.
  * Backup and restore file paths, and restore validation.
  * Error messages.
  * MSIX capabilities.
* **Secret scan:**
  * Searched the working tree and the full git history for the Google key pattern `AIza…` and for the two real keys on this PC. The keys were compared in memory and never printed.
  * Also searched the pre-rewrite history archive, the app data folder (database, backups, text files) and the screenshots.
  * Re-run on 2026-11-23 before the 1.0.0 release: the only key-like strings in the history are the obviously fake test keys in the test project, and no database, key, backup or certificate file has ever been committed.

## Findings

| # | Severity | Finding | Status |
|---|---|---|---|
| 1 | **High** | A plaintext Google API key file sat next to the repository in a cloud-synced folder. It was never in the repository or its history. | **Fixed.** The key is saved in the app (encrypted) and the file was deleted. Verified 2026-11-23. |
| 2 | Medium | The OilPriceAPI key was kept in a Windows user environment variable, readable by any program running as that user. The app no longer reads environment variables for keys. | **Fixed.** The key is saved in the app (encrypted) and the variable was removed. Verified 2026-11-23. |
| 3 | Low | SQLite connection strings were built by pasting a file path into `Data Source=…`. A backup or restore path containing `;` could add connection options. The paths come from the Windows file pickers, so exploiting this would need a deliberately crafted folder name. | **Fixed.** `SqliteConnectionStringBuilder` is used everywhere (`DatabaseBackup.ConnectionString`, `EstimatorDbContext.OptionsFor`). Test: `PersistenceTests.Backup_path_with_a_semicolon_is_treated_as_a_file_name`. |
| 4 | Low | `estimator.db` and backups are not encrypted at rest. They hold customer names, job addresses and labor rates. They contain **no API keys**. | Accepted. The data is protected by the Windows profile (the folder is readable only by the user and administrators). Use BitLocker on the PC, and store backups on a protected share. |
| 5 | Low | Restoring a backup made by a *newer* app version onto an older app isn't blocked. Older apps don't know the newer schema. | Accepted. One estimating PC is updated in place. Restore always takes a safety copy first, so this can be undone. |
| 6 | Info | Older commit messages carried co-author attribution trailers. | **Fixed 2026-11-20.** The history was rewritten before the public repository was created, so the published history has none. |

No issues were found in the following areas.

### Secrets
* **Storage:**
  * Keys are stored with Windows DPAPI, CurrentUser scope, with app-specific entropy, in `google-key.bin` and `oilpriceapi-key.bin`.
  * Files are written atomically (a temp file, then a move).
  * A file copied from another user or PC fails to decrypt and is treated as *no key*.
* **Where keys never appear:**
  * Keys are never written to `estimator.db`, backups, logs or settings files.
  * They are sent only in request headers: `X-Goog-Api-Key`, and `Authorization: Token`.
  * They are never put in URLs.
  * Provider error text is scrubbed of the key before it reaches the UI. Tests cover 400/401/403/429/500 responses, network errors and error bodies that echo the key.
* **In the UI:**
  * PasswordBoxes are never filled with the saved key, and are cleared after Save and Remove.
  * The Store screenshots in `packaging/StoreListing/Screenshots/` show no keys.

### Network
* All endpoints are HTTPS constants: `places.googleapis.com`, `routes.googleapis.com` and `api.oilpriceapi.com`.
* Certificate validation is never overridden (no `ServerCertificateCustomValidationCallback`).
* One `HttpClient` is shared for the app's lifetime.
* Each request has its own 15-second timeout.

### SQL
* All queries go through EF Core LINQ and are parameterized.
* The only raw SQL is constant text: the migration `UPDATE`, and the backup validation `SELECT` and `PRAGMA`.

### Input validation
* Numbers are parsed with en-US rules and clear messages. Negative values are rejected.
* Names are required and length-limited in the schema.
* The theme file is parsed with `Enum.TryParse` and `Enum.IsDefined`.
* JSON from the providers is checked: the code matches, the price is positive, and the currency and unit are correct.

### Restore
Before the live database is replaced, a backup must:
* open as SQLite;
* contain the app's tables;
* pass `PRAGMA integrity_check`.

A safety copy of the current database is written first, then migrations upgrade older backups.

### External links
The About window opens only `http`/`https` links, through the default browser.

### Error messages
* The unhandled-error dialog shows the exception message only, with no stack trace.
* Messages never contain keys.
* The startup error shows the data folder path, which helps support.

### MSIX capabilities
The package requests only `internetClient` (needed for the lookups) and `runFullTrust` (required for a packaged WPF app).

## Addendum 2026-11-20: API key backup

Settings → API keys → Back up keys writes both keys to a `.estimator-keys` file. It is encrypted with AES-256-GCM, using a key derived from a staff-chosen password (minimum 8 characters) with PBKDF2-SHA256 and 600,000 iterations, a random 16-byte salt and a random nonce. Wrong passwords and edited files are rejected, because the GCM tag is checked. The keys never appear in plain text in the file, on screen or in messages. Password boxes are cleared after use, and decrypted bytes are zeroed after use.

**Remaining risk:** the file is only as strong as its password. Use a long one, and keep it apart from the file.

## Dependencies

| Package | Version | Licence | Notes |
|---|---|---|---|
| Microsoft.EntityFrameworkCore.Sqlite (with SQLitePCLRaw and the SQLite engine) | 10.0.15 | MIT; SQLitePCLRaw Apache-2.0; SQLite public domain | No known vulnerabilities. EF Core 11.0.0 is available but targets the next .NET release, so it stays on 10.x to match .NET 10 LTS. |
| Microsoft.EntityFrameworkCore.Design | 10.0.15 | MIT | Build-time only (`PrivateAssets=all`). Not shipped. |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | Up to date. |
| .NET 10 / WPF (including the Fluent theme) | 10.0 | MIT | Self-contained in the published app. |
| xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk | 2.9.3 / 3.1.5 / 18.12.0 | Apache-2.0 / Apache-2.0 / MIT | Tests only. Runner 4.0.1 exists; not upgraded, since a major version change isn't needed for deployment. |

Bundled images: `JasonWoodcock.jpg` (the owner's photo) and `BuyMeACoffeeWhiteButton.png` (Buy Me a Coffee's button artwork, used as they provide it for linking to a page). Both are copied from GB PunchList.

`dotnet list package --vulnerable --include-transitive` reports **no vulnerable packages** in any project.

## Remaining risks and recommendations

1. Keep the Google key restricted to Places API (New) and Routes API, with daily quotas and a budget alert (docs/GOOGLE_SETUP.md). If the office has a fixed public IP, add an IP restriction.
2. Keep the OilPriceAPI plan terms on file (docs/FUEL_PRICES.md → Licensing).
3. Sign the MSIX through the Store (or with a trusted certificate for sideloading). The unsigned build in `%LOCALAPPDATA%\Programs` is for this PC only.
4. Re-run `dotnet list package --vulnerable --include-transitive` before each release.

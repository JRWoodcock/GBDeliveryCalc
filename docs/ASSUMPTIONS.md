# Assumptions and decisions (v1)

These are the decisions made where the requirements left room for interpretation. Each one is easy to revisit.

## Platform
* **.NET 10 LTS.** .NET 8 LTS support ended 10 Nov 2026, so .NET 10 is the current LTS. `global.json` pins `10.0.100` with `latestFeature` roll-forward, so any 10.0.1xx or later SDK on a developer PC builds it.
* **Self-contained publish** inside the MSIX, so Store users don't need to install a .NET runtime.
* The view models are in their own project (`Presentation`) so the main workflow can be tested without Windows. This adds one project to the "small solution", in return for much better test coverage.

## Calculation (cost-only since 2026-11-19)
* **Estimates are actual costs only.** Per Jason (2026-11-19): "All I want is ACTUAL costs across the board. The salesman will use this to figure out a markup for profitability." Customer billing was removed: team billing rates, the all-in and fuel-free mileage rates, billing modes, revenue, gross profit, margin and markup.
* **Lines:** Travel labor cost = drive hours × team loaded labor cost per hour; Installation labor cost = installation hours × loaded cost; Fuel = total miles ÷ MPG × price per gallon; Vehicle operating cost = total miles × operating cost per mile (excluding fuel); then each other direct cost. **Total estimated cost** = the sum of the rounded lines.
* **Time is held in whole minutes.** Hours are `minutes / 60m` at full decimal precision until each line is rounded.
* **Rounding:** each line is rounded to cents, **half away from zero**, and the total is the sum of the rounded lines, so the lines always add up to the total. Regression: Neshoba at $40/hr loaded, 19 MPG, $3.49, $0.20/mi → travel 268 min $178.67, install 180 min $120.00, fuel $45.92, vehicle $50.00, total **$394.59**.
* **Round partial labor hours up** (company setting, default off): travel and installation hours are each rounded up to the next whole hour (4 h 28 min → 5 h, so $200.00 at $40/hr). The line detail says so, and the setting is saved with each estimate, so changing it later doesn't alter saved estimates.
* **Paid-hours override:** when entered, one *Labor cost (paid hours override)* line (override × loaded cost) replaces the travel and installation lines; its detail shows what it replaced.
* **Missing inputs** (no team, no loaded labor cost, no vehicle, MPG, fuel price or operating cost, missing or stale route) leave that line blank, list what's needed, and make the total *Incomplete*. Copy and Save need a complete total. An entered `0` is a valid value.
* **Fuel type mismatch:** a price for one fuel type is never applied to a vehicle with another fuel type. A typed price is always tagged with the current vehicle's fuel type.
* The copied text names the team and vehicle (not the team description) because it is internal.

## Route handling
* A route lookup never sets the figures directly: it **copies** the miles and drive time into the manual-entry boxes (Google confirmed this is fine, 2026-11-19), labelled *From Google route lookup, copied into manual entry*. Per leg on a round trip = the average of both directions (rounded to 0.1 mi and the nearest minute); trip totals = the sum. Switching basis re-enters the looked-up legs rather than reinterpreting the numbers.
* Copied figures carry the origin, destination and trip type they were looked up for (case- and space-insensitive). Any change marks them stale until staff **Re-run route lookup** or **Keep these figures**. Typing over them makes them plain manual figures, which don't go stale (except manual trip totals when the trip type changes).
* **Confirm match** (and **Use as typed**) run the lookup automatically when Google is set up; there is no separate "Look up miles & time" button.
* **Trip-type changes recalculate.** Looked-up legs are kept, so switching Round trip ↔ One way re-enters them for the new trip type (per leg or trip totals) without a "keep figures" step. A lookup made one way has no return leg, so switching it to a round trip runs the lookup again (or doubles the outbound leg if Google isn't set up). Hand-typed trip totals are doubled (to round trip) or halved (to one way) and labelled, because the app can't know the other leg.
* If the return leg can't be looked up, the outbound leg is doubled, and the screen and copied text say so.

## Persistence and history
* IDs are GUIDs, so records can later move to a shared backend without renumbering.
* Each saved estimate stores a JSON snapshot of the inputs: frozen team and vehicle copies, every rate, the fuel price with its source and date, the round-up setting, the computed result and the copied text. History is rendered from the snapshot, so **settings changes never alter saved estimates**.
* **Edit** loads a saved estimate into the form with its own saved team and vehicle values. **Save changes** updates it in place with the same number and creation date; the previous version goes to `EstimateRevisions` (revision number, time replaced, full snapshot). After the first save, later saves of the same form update that estimate; **New estimate** starts another.
* **Delete** (after confirmation) removes the estimate, its actuals and its revisions. This replaced the earlier no-delete rule at Jason's request.
* **Actuals** live in a separate table and are editable. Missing actuals fall back to the estimate, and each fallback is labelled. Actual labor and vehicle cost use the rates frozen in the snapshot; actual drive and installation time are rounded up the same way when the estimate was.
* **Billing-era estimates** (schema 1, saved before 2026-11-19) still open, read-only, showing their stored cost lines and text; they can't be edited. The `CostOnlyEstimates` migration lists them by their stored cost total.
* Estimate numbers are `GB-YYYYMMDD-NNN` per local day, which is enough for one estimating PC.

## Data retention for providers
* `RetentionPolicy.EnsurePersistable` still refuses data marked as a provider lookup whose terms don't permit storage. Since 2026-11-19 the app copies Google's address and route figures into the editable fields (allowed per Google Support), so Google lookups no longer trigger it.
* **Root cause of "Save is broken" (2026-11-19):** with a Google key saved, every confirmed Google address and every route lookup was tagged as non-retainable provider data, so this policy refused each save with "cannot be saved under its terms". Reproduced on a copy of the real database (both through the view models and by driving the real app); fixed by the copy-into-manual-entry change; regression test `WorkflowTests.Save_works_with_google_address_and_route_lookup`. Save also now reports any unexpected error in the status line instead of failing silently.

## Integrations
* **No server.** Address and route lookups call Google Maps Platform directly from the app. The key is the company's own, entered on each PC, never built into the package or kept in Git.
* The key is stored **DPAPI-encrypted for the current Windows user** in `google-key.bin` in the app data folder, not in `estimator.db`, so backups never carry it and a new PC needs it entered again. Someone signed in as that user could recover it, so the key is restricted to Places API (New) and Routes API, with daily quotas and a budget alert (docs/GOOGLE_SETUP.md).
* Routes use `TRAFFIC_UNAWARE` (typical times, cheapest tier). A round trip is two calls; a failed return leg is doubled and labelled rather than failing the lookup.
* An address search with no matches is shown as "No matches" rather than an error, so staff can just type the address.
* **Fuel price** comes from OilPriceAPI's AAA series for the shop's state (the state in the Settings starting address, Mississippi if none; per Jason 2026-11-20, the shop's state rather than the job's) when an OilPriceAPI key is saved (the plan in use grants commercial use; see docs/FUEL_PRICES.md → Licensing), otherwise it is typed. Regular and diesel are fetched automatically at startup and on local date rollover (checked every 15 minutes), at most once per fuel type per local day, persisted in SQLite, with at most one retry per hour after a failure (also persisted). There is no Fetch button. A typed price always wins.
* Both keys follow the same rules: the saved key is never shown; Test key uses the typed key before saving; **Save / replace key** overwrites the stored key; Remove key deletes it. Saving or removing the fuel key clears the retry wait.

## UI
* English (US) formatting is forced for currency and numbers, so every estimate looks the same on every PC.
* First run opens Settings when no teams exist. No sample teams or vehicles are seeded, because made-up rates could leak into real estimates.
* The starting address is blank until staff enter the real Grover Brothers street address.
* Settings are ordered Company, Teams and vehicles, Backup and restore, then **API keys** (Google lookups and Fuel prices).
* **Appearance:** Light, Dark or Follow Windows (default), per Windows user. Built on WPF's Fluent theme (`Application.ThemeMode`, marked experimental in .NET 10 and suppressed as WPF0001); app colours meet WCAG AA in both themes, and selected grid rows use fixed palette colours rather than the Windows accent colour.
* **API key backup:** both keys can be exported to a password-protected `.estimator-keys` file (AES-256-GCM, PBKDF2-SHA256 with 600,000 iterations, minimum 8-character password) and restored on another PC. Database backups still never contain keys.
* **Uninstall warning:** Settings → Backup and restore and the README remind staff that uninstalling deletes saved estimates, so back up first. An MSIX package can't run code on uninstall, so the reminder is shown in the app instead.

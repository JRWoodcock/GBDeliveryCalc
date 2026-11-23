# Verification record

Last run: 2026-11-19, on the Windows 11 Pro estimating PC with .NET SDK 10.0.403.

## What actually ran

| Check | Result |
|---|---|
| `dotnet build GBDeliveryCalc.slnx -c Release` (all 6 projects, warnings as errors) | ✅ 0 warnings, 0 errors |
| `dotnet build GBDeliveryCalc.slnx -c Release` with `AnalysisMode=Recommended` and all Security rules as warnings | ✅ 0 warnings |
| `dotnet test GBDeliveryCalc.slnx -c Release` | ✅ **189 passed**, 0 failed, 0 skipped |
| `dotnet list package --vulnerable --include-transitive` | ✅ no vulnerable packages |
| `dotnet ef migrations add CostOnlyEstimates` (hand-checked), then all migrations applied to fresh SQLite files, to a billing-era database built in a test, and to a copy of the real database | ✅ |
| `dotnet publish src/GBDeliveryCalc.App -c Release -r win-x64 --self-contained` to `%LOCALAPPDATA%\Programs\GBDeliveryCalc`, then launched | ✅ |
| Live OilPriceAPI and Google calls with the real keys, by driving the published app with Windows UI Automation against a **copy** of the real data folder (`GROVER_ESTIMATOR_DATA_DIR`) | ✅ see the click-through below |

### "Save is broken" (2026-11-19): root cause

Reproduced first on a copy of the real database, both through the view models and by driving the real app:
* With manual figures and a typed address, Save worked (`Saved estimate GB-20261119-001`).
* With a Google-confirmed address, or after **Look up miles & time**, Save was refused with *"The address returned by Google Places API cannot be saved under its terms"* or *"Route figures from Google Routes API cannot be saved under its terms"*.

**Cause:** `RetentionPolicy.EnsurePersistable` refused any estimate containing a Google address or Google route figures, because the Google adapters report `PermitsRetention = false`. With a Google key saved, the normal workflow could therefore never save. **Fix:** Confirm match copies the address into the editable job-address field and the route figures into the manual-entry boxes (allowed per Google Support, 2026-11-19), so those estimates save. Save now also reports any unexpected error in the status line. Regression test: `WorkflowTests.Save_works_with_google_address_and_route_lookup`.

### Trip-type toggle (2026-11-19b): reproduced in the real app, then fixed
Reproduced on a copy of the real data. Confirm match filled 123.8 mi and 2 h 2 min per leg. Switching to **One way** left the summary at *One way: 247.6 mi total, 4 h 04 min* and marked the figures stale, blocking the total. Trip totals behaved the same. **Cause:** figures copied from a lookup kept the trip type they were looked up with, and the route key includes the trip type, so a toggle made them stale instead of recalculating. **Fix and tests:** `WorkflowTests.Trip_type_toggle_recalculates_figures_copied_from_a_lookup`, `Switching_a_one_way_lookup_to_round_trip_looks_the_route_up_again`, `Hand_typed_trip_totals_are_halved_or_doubled_when_trip_type_changes`.

### Shop address → state fuel prices (2026-11-20)
**Automated (`ShopStateFuelTests`, 9 tests):**
* Confirming a Google-verified shop address in Settings, for each of MS, AL, LA, TN, TX and FL, fetches exactly that state's regular and diesel series. New estimate then shows them for the regular and diesel vehicles, and the fuel cost uses them.
* Moving through all six states and back fetches each series exactly once per day: 12 calls, none repeated.
* Typed (saved, not looked up) addresses also switch the state, and an address with no state uses Mississippi.
* A state with no price series reports it, and a typed price still works.

**Live, in the installed app** (copy of the real data, real Google and OilPriceAPI keys). Each shop address was looked up and confirmed in Settings:

| Confirmed address | Regular | Diesel |
|---|---|---|
| Mobile Government Plaza, 205 Government St, Mobile, AL 36602 | $3.371 | $5.586 |
| Louisiana State Capitol, Baton Rouge, LA 70802 | $3.311 | $5.429 |
| 125 N Main St, Memphis, TN 38103 | $3.327 | $5.621 |
| 901 Bagby St, Houston, TX 77002 | $3.158 | $5.31 |
| 222 W Main St, Pensacola, FL 32502 | $3.485 | $5.627 |
| 1500 Main St, Hattiesburg, MS 39401 | $3.432 | $5.529 (reused from earlier that day, no new call) |

Every price was labelled "AAA &lt;state&gt; average via OilPriceAPI" and published 2026-11-20. There were no fetch errors.

### Themes and screenshots
Driven through UI Automation on a copy of the real data, with the real keys. The theme was switched **Light → Dark from the Settings dropdown while the app was running**. Sixteen screenshots were saved outside the repository: for each of `light-` and `dark-`, `1-new-estimate`, `2-new-estimate-team-dropdown`, `3-history`, `3b-history-comparison`, `4-settings-top`, `5-settings-bottom`, `6-about`, and `7-no-keys-banner-and-warnings`. One problem was found and fixed: in dark mode, selected History rows were white text on a pale Windows-accent colour. Grid selection now uses palette colours. All app text colours were checked against their backgrounds: lowest 6.0:1 light and 7.6:1 dark (WCAG AA needs 4.5:1).

### Click-through of the published app (copy of the real data, real keys)
* **Settings:** groups in order Company, Teams, Vehicles, Backup and restore, API keys. *Round partial labor hours up to the nearest whole hour* is present. *Loaded labor cost ($/hr, whole team)* is present. No billing, all-in or fuel-free fields.
* **New estimate on start:** fuel fetched automatically: *Regular unleaded $3.456/gal (AAA Mississippi average via OilPriceAPI, published 2026-11-19)*; the diesel price already fetched today was reused, with no new call. Buttons: Look up, Use as typed, Confirm match, Copy cost estimate, Save estimate, New estimate. No *Look up miles & time*, no *Fetch MS average*.
* **Team "Alex & Sam / Our truck"** (White Liftgate Truck, diesel, 19 MPG, $0.23/mi, $55/hr loaded), destination *Neshoba Central High School* → **Look up** showed *1123 Golf Course Rd, Philadelphia, MS 39350* with *Powered by Google*.
* **Confirm match** → Job address filled. Miles box 123.8 and drive time 2 h 2 min per leg were copied in automatically (*Route found. Miles and drive time were copied into the boxes below*), labelled *From Google route lookup, copied into manual entry (per leg)*. Round trip 247.6 mi, 4 h 04 min. Lines: travel labor $223.67, installation $165.00, fuel $72.49 (diesel $5.563), vehicle operating $56.95, **total $518.11**.
* **Save estimate** → *Saved estimate GB-20261119-001*.
* **History** → selected it, total $518.11. **Edit** → *Editing GB-20261119-001*. Changed installation to 4 h, **Save changes** → *Saved changes to GB-20261119-001 (version 2; the previous version is kept)*.
* **Settings** → turned rounding on and saved. New estimate, **Use as typed** → *Paid hours: 7 h (4 travel + 3 installation), each rounded up*, travel $220.00, total $514.02. Then turned rounding back off in the copy.
* **History → Delete** → the confirmation said *Delete estimate GB-20261119-001 (Click-through test) with its actuals and previous versions? This can't be undone.* Yes → the list was empty, and the database copy had 0 estimates, 0 revisions and 0 actuals.

### Test coverage by area (189 tests)
* **CalculatorTests (24):** Neshoba cost regression: travel 268 min $178.67, install 180 min $120.00, fuel $45.92, vehicle $0.20/mi $50.00, **total $394.59**. Round-up (travel 4 h 28 → 5 h = $200.00, install 3 h = $120.00, $415.92; per-category ceiling). One way. Trip totals = per leg. Paid-hours override line. Other direct costs. Doubled return leg. Copied lookup figures go stale and can be confirmed. Plain manual figures don't go stale. Trip-total staleness. Missing route, loaded cost and vehicle inputs flagged incomplete. Zero operating cost is valid. Fuel-type mismatch. Half-away-from-zero rounding. A 1,500-case fuzz check that lines sum to the total. The copied text names the team and vehicle, not the description, and has no profit or margin. Incomplete estimates are refused.
* **ActualsTests (5):** labelled assumptions; estimated vs actual per line (labor, fuel, vehicle operating, other direct) and total; labor override and time-based hours; round-up applied to actual time; missing loaded rate.
* **PersistenceTests (17):** backup path containing `;`, migrations and defaults, idempotent migrate, CRUD and archive, company settings (round-up), history unchanged after settings changes, actuals never touch the estimate JSON, refusal to save an existing id as new, numbering, retention policy, **copied lookup figures save**, **update keeps the number and stores the previous version**, **delete removes the estimate, actuals and revisions**, **billing-era snapshot opens read-only**, fuel cache, backup and restore, bad backup files.
* **MigrationTests (1):** a billing-era database (billing columns, a billing-era estimate) upgrades. Settings and team loaded cost are kept, and the estimate lists its stored cost total.
* **StateFuelAndKeyBackupTests (17):** finding the state in an address (codes, full names, ZIP forms, no state); OilPriceAPI codes and labels per state; separate daily fetch per state; New estimate uses the shop's state (not the job's); key backup round trip, no plain-text keys in the file, wrong password, tampering, foreign files and short passwords rejected; Settings back up and restore of both keys.
* **AppearanceTests (2):** saved theme loaded, changes applied immediately.
* **WorkflowTests (22):** trip-type toggle with copied lookup figures (per leg and trip totals), one-way lookup re-run on switching to round trip, hand-typed totals halved/doubled, first run. Main cost workflow: lines, copy text, save, no duplicate on double save, history unchanged after settings changes, estimated vs actual. **Save regression with Google address and route.** Trip-totals lookup and basis switch. Doubled return leg. Stale → Re-run route lookup / Keep. Typing over copied figures. Vehicle override. Archived team. Trip switch. Manual-total staleness. Address candidates. Routing failure → manual. Missing inputs block copy and save. Missing loaded cost. Round-up setting saved with the estimate. Paid-hours override. **Edit from History** (same number, previous version kept, saved team rate kept). **Delete from History** (confirm, cancel, detach the editing form).
* **FuelFetchLimitTests (7):** SQLite-backed once-per-day per fuel type, also after restart. Next day = one call. No retry within the hour. **Startup fetches both fuels once; vehicle changes, new estimates and the 15-minute check reuse them; date rollover fetches once more.** **15-minute check retries a failure at most hourly.** A new key allows an immediate retry. Test key before save; Save / replace and Remove for both keys.
* **OilPriceApiTests (31), GoogleProviderTests (40), GoogleKeyWorkflowTests (6), ProviderTests (5):** the OilPriceAPI and Google adapters, key handling and errors (as before). The Google flow now fills the manual boxes and saves. The New estimate screen uses the startup fuel prices for the vehicle's fuel type, and a typed price wins.

## Not verified
* The **Follow Windows** theme was not checked by flipping the Windows setting while the app was running. Light and Dark were each verified, and Follow Windows uses the same code path.
* High-contrast Windows themes.
* The **Delete** and **Edit** buttons were driven through UI Automation, and the delete dialog's Yes button by a window message, not by a person with a mouse.
* **Copy cost estimate** to the real clipboard, backup and restore dialogs, and the 15-minute timer actually firing after midnight in the running app. The rollover logic is tested at the view-model level.
* Running `build-msix.ps1`, installing the MSIX, and the Windows App Certification Kit.
* Partner Center submission: not attempted, and not authorised.

### Suggested manual smoke test on Windows
1. `dotnet run --project src/GBDeliveryCalc.App` with `GROVER_ESTIMATOR_DATA_DIR` pointing at a temp folder.
2. Settings: set the origin, add *2028 GMC Sierra* (regular, 19 MPG, $0.20/mi) and *Team B* ($40/hr loaded, default Sierra).
3. New estimate: Team B, destination "Neshoba Central High School" → **Use as typed**, 125 mi and 2 h 14 min per leg, fuel 3.49. Expect travel $178.67, installation $120.00, fuel $45.92, vehicle $50.00, **total $394.59**.
4. **Copy cost estimate**, paste into Notepad, and check that the team and vehicle are named and the team description is not. **Save estimate**.
5. Settings: change Team B's loaded cost to $80. History: the estimate still shows $394.59. **Edit** it, change installation to 4 h, **Save changes**: same number, total $434.59. Enter actuals and **Save actuals**.
6. Settings → Back up now… → change something → Restore from backup… → the change is reverted.

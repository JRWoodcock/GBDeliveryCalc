# GB DeliveryCalc

A Windows app that works out what a delivery and installation job will **actually cost** the company: team labor, fuel, vehicle running costs and any other direct costs. Use the total to set your price and markup.

Version 1.0.0 · Windows 10 or 11 (64-bit)

## Install

Install **GB DeliveryCalc** from the Microsoft Store, or download `GBDeliveryCalc-1.0.0-win-x64.zip` from this repository's **Releases** page, unzip it to a folder such as `%LOCALAPPDATA%\Programs\GBDeliveryCalc` and run `GBDeliveryCalc.exe`. The zip is self-contained; you don't need to install .NET.

## First-time setup

The app opens on **Settings** the first time. Fill in:

1. **Company**: the shop's starting address (click **Look up**, pick the right match and click **Confirm match** to verify it with Google and save it), and the usual installation time. Click **Save company settings**.
2. **Vehicles**: name, fuel type (regular or diesel), miles per gallon, and running cost per mile (maintenance, tires, insurance and so on, not fuel).
3. **Teams**: name, a short description, the team's **loaded labor cost per hour** (wages plus employer costs), and its usual vehicle.

Optional, under **API keys**:

* **Google lookups**: lets the app find addresses and fill in miles and drive time. See [Google setup](docs/GOOGLE_SETUP.md).
* **Fuel prices**: fetches today's average for regular and diesel automatically, for the state in the shop's starting address (Mississippi if it doesn't name one). See [Fuel prices](docs/FUEL_PRICES.md).

Without keys, you type the miles, drive time and fuel price yourself.

**Moving to another PC:** Settings → API keys → **Back up keys…** saves both keys to a password-protected file; **Restore keys…** on the new PC brings them back.

## Making an estimate

On the **New estimate** tab:

1. Pick the **team**. Its usual vehicle is filled in; change the vehicle for this job if needed.
2. Type the **destination**. Click **Look up**, pick the right address and click **Confirm match**. The miles and drive time are filled in for you. Or click **Use as typed** and enter them yourself.
3. Choose **Round trip** or **One way**. The figures update when you switch.
4. Check the **installation time** and the **fuel price** (filled in automatically if fuel prices are set up; type a price to override it).
5. Add any **other costs**, such as parking or a disposal fee.

The **Cost estimate** panel shows each cost and the **Total estimated cost**. If something is missing, it lists what's needed.

* **Copy cost estimate** copies a plain-text summary to paste into an email or quote.
* **Save estimate** saves it with a number such as `GB-20261119-001`.

## History and actuals

On the **History & actuals** tab, pick a saved estimate to:

* **Edit** it (it keeps its number, and the previous version is kept) or **Delete** it.
* Enter what the job **actually** took (miles, hours, fuel cost and other costs), then click **Save actuals** to compare estimated and actual cost line by line.

Saved estimates never change when you later edit teams, vehicles or settings.

## Settings

* **Round partial labor hours up**: costs travel and installation in whole hours (4 h 28 min counts as 5 h).
* **Appearance**: Light, Dark or Follow Windows.
* **Backup and restore**: **Back up now…** saves a copy of all your data; keep it on a network drive, OneDrive or USB stick. **Restore from backup…** brings a backup back (your current data is saved first).

  > **Back up before uninstalling.** Uninstalling the app deletes your saved estimates, teams, vehicles and settings. Back up first, and back up your API keys too.
* **About** (top right): version, developer contact, license and privacy policy.

Your data is stored on this PC, in `%LOCALAPPDATA%\GroverBrothers\Estimator`. API keys are encrypted for your Windows account and are never included in backups, so enter them again on a new PC.

## Help

* Developer: Jason Woodcock, jrwoodcock@outlook.com, [www.JasonWoodcock.us](https://www.JasonWoodcock.us)
* Privacy policy: [jasonwoodcock.us/apps/privacy](https://jasonwoodcock.us/apps/privacy/)
* License: [LICENSE.txt](LICENSE.txt) (free to use; no redistribution). Third-party components: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)
* Release notes: [CHANGELOG.md](CHANGELOG.md)
* For developers: [Development guide](docs/DEVELOPMENT.md)

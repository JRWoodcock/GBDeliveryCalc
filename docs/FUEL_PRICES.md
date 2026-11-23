# Fuel prices (OilPriceAPI)

GB DeliveryCalc can fetch the **statewide average retail price for the shop's state** for regular unleaded and diesel from [OilPriceAPI](https://www.oilpriceapi.com). The figures come from AAA. The app calls OilPriceAPI directly from each PC, with no server. Fuel prices are optional: without a key, staff type the price on each estimate, as before.

## What the app requests

**Which state:** the state named in the shop's starting address (Settings → Company), for example `…, Hattiesburg, MS 39401`. If that address doesn't name a state, Mississippi is used. Changing the starting address switches the state straight away.

| Vehicle fuel type | OilPriceAPI code (`XX` = state) | Label on the estimate |
|---|---|---|
| Regular unleaded | `GASOLINE_RETAIL_STATE_XX_USD` | *Regular unleaded $3.49/gal (AAA Mississippi average via OilPriceAPI, published 2026-11-18)* |
| Diesel | `DIESEL_RETAIL_STATE_XX_USD` | *Diesel $5.592/gal (AAA Mississippi average via OilPriceAPI, published 2026-11-18)* |

* **Request:** `GET https://api.oilpriceapi.com/v1/prices/latest?by_code=<code>`, with the header `Authorization: Token <key>` and a 15-second timeout. The key is never put in the address.
* **Checks:** the app uses a reply only if all of these hold:
  * the returned code matches the code requested;
  * the price is above 0;
  * the currency is USD and the unit is gallon.

  Any other reply is shown as an error.
* **Live or not:** a price counts as live (current) only when OilPriceAPI marks it `current` (or `fresh`) and not stale. A value that isn't current is shown once, but it's never cached as today's price.
* **Published date:** the reply's `as_of` timestamp, converted to the PC's local date.
* **Diesel trucks:** the fuel type always matches the selected vehicle. A regular price is never applied to a diesel truck, or the other way round.

## How often it calls OilPriceAPI

* **Automatic.** The app fetches regular and diesel when it starts, and checks every 15 minutes whether the local date has changed (or a fuel type still has no current price), so a new day's price arrives without anyone clicking anything. There is no Fetch button; a price typed on the estimate overrides it.
* **Once per fuel type and state per day, at most.** The first successful live fetch of the local calendar day is saved in the app's database, in the `FuelPriceCache` table. For the rest of that day, the app reuses it for that fuel type and state:
  * on every new estimate and every edited one;
  * whenever staff change the vehicle;
  * on every 15-minute check;
  * and after the app restarts.

  Regular and diesel count separately. A normal day therefore makes at most 2 calls on each PC.
* **The next day**, the first check after midnight (or the next start) makes one new call per fuel type.
* **After a failure, at most one retry per hour.** Failures include no internet, a timeout, an error reply, a rejected value, or a value OilPriceAPI says isn't current. The app records the time of the attempt in the `FuelFetchStates` table and doesn't call again for that fuel type for an hour, even after a restart. Meanwhile it shows the **last known price with its age** (for example *showing the last known value, 21 h old*) and the time of the next try. Saving or removing the key clears this wait, so a corrected key is tried at once.
* **A price typed on the estimate always wins** over the fetched one, and is labelled *manual entry*.

The rules are in `Core/Integrations/FuelPriceService.cs` and are covered by `FuelFetchLimitTests`. Those tests check:
* a second request the same day makes no call, even after rebuilding the services over the same database;
* the next day makes exactly one call;
* there is no retry within the hour after a failure;
* the Fetch button and vehicle changes reuse the day's value.

## Setting up the key

1. In the OilPriceAPI dashboard, copy the API key for the plan the business uses.
2. In GB DeliveryCalc, open **Settings → API keys → Fuel prices (optional)** (at the bottom).
3. Paste the key into **OilPriceAPI key**. The box hides what you type. Then click **Test key**. The app fetches today's regular price for the shop's state and shows it with its date, for example *Test passed: Regular unleaded $3.49/gal (AAA Mississippi average via OilPriceAPI, published 2026-11-18)*. Testing doesn't save the key.
4. Click **Save key**. The status changes to **Key saved**, and New estimate starts fetching prices right away, with no restart.

### Replacing or removing the key

* **To replace:** paste the new key and click **Test key** first. The test uses the key in the box, not the saved one. Then click **Save / replace key**, which is the button's label once a key exists. The new key replaces the old one immediately.
* **To remove:** click **Remove key** and confirm. Fuel prices then go back to manual entry.
* **If the key may have leaked:** regenerate it in the OilPriceAPI dashboard first, then save the new key on each PC.

## Moving the keys to another PC

Settings → API keys → **Back up keys…** saves the Google and OilPriceAPI keys to a `.estimator-keys` file protected by a password you choose (at least 8 characters). On the new PC, **Restore keys…** asks for the file and the password and saves the keys encrypted for that Windows user. Without the password the file can't be read: it is encrypted with AES-256-GCM using a key derived from the password (PBKDF2-SHA256, 600,000 iterations), and any change to the file is detected.

## Where the key is kept

* The key is encrypted with Windows' per-user protection (DPAPI) in `oilpriceapi-key.bin` in the app data folder. That folder is `%LOCALAPPDATA%\GroverBrothers\Estimator\`, or the folder in `GROVER_ESTIMATOR_DATA_DIR` if that variable is set. Only the same Windows user on the same PC can decrypt it.
* It is **not** in `estimator.db` and **not** in database backups. On a new PC, restore it from a key backup or enter it again.
* It is sent only to OilPriceAPI, in the `Authorization` header over HTTPS. The app never shows the saved key, and removes it from any error text before display.

## Licensing

Per Jason (Grover Brothers), 2026-11-18: the OilPriceAPI API plan in use grants usage in commercial applications under 10,000 seats. The Mississippi figures are sourced from AAA. Keep a copy of the plan terms/confirmation on file and re-check on plan changes.

Each saved estimate stores the fuel price with its source and published date. That is the basis for treating the price as retainable (`PermitsRetention = true` in `OilPriceApiFuelSource`).

## Troubleshooting

| Message | What to check |
|---|---|
| *OilPriceAPI refused the key (401)* | The key was mistyped, regenerated or revoked. Paste the current key, test it, then save it. |
| *…plan doesn't allow this data (402/403)* | The plan doesn't include state retail prices. Check the plan in the OilPriceAPI dashboard. |
| *request limit … used up (429)* | The plan's request quota is used up. Enter the price manually; the app retries after an hour. |
| *could not reach OilPriceAPI* / *did not answer within 15 seconds* | No internet, or a firewall or proxy is blocking `api.oilpriceapi.com`. |
| *returned a different series* / *no usable price* | OilPriceAPI's reply didn't match the request. Enter the price manually and report it to OilPriceAPI. |
| *…will try again after 10:00 AM* | A recent attempt failed, so the app is waiting an hour; the 15-minute check retries after that. Type the price, or wait. |

# Setting up Google address and route lookups

GB DeliveryCalc can look up job addresses and driving miles and time with Google Maps Platform. The app calls Google itself, from each PC. **You don't need a server or any other service.** You only need a Google Maps Platform API key, and you enter it in the app.

Lookups are optional. Without a key the app works fully by hand: staff type the address, miles and drive time. Fuel prices use a separate OilPriceAPI key; see [FUEL_PRICES.md](FUEL_PRICES.md).

## 1. Create the Google Cloud project

1. Sign in at [console.cloud.google.com](https://console.cloud.google.com) with a company Google account. A shared admin account is better than one person's own account.
2. Create a project, for example **gb-deliverycalc**.
3. Under **Billing**, link a billing account. Google requires billing for Maps Platform, even if usage stays within the monthly free allowance. Check current prices on Google's [pricing page](https://mapsplatform.google.com/pricing/).

## 2. Turn on the two APIs

In **APIs & Services → Library**, enable exactly these two:

* **Places API (New)**: address lookup. Make sure it's the one marked *(New)*, not the legacy "Places API".
* **Routes API**: driving miles and time.

The app uses nothing else.

## 3. Create and restrict the key

1. Go to **APIs & Services → Credentials → Create credentials → API key**. Copy the key; it starts with `AIza`.
2. Open the key and click **Edit**. Then:
   * **Name:** e.g. *GB DeliveryCalc desktop*.
   * **API restrictions:** choose **Restrict key**, and tick only **Places API (New)** and **Routes API**. With this restriction, a leaked key can't be used for other Google services.
   * **Application restrictions:** if the office has a **static public IP address**, choose **IP addresses** and enter it. The key then works only from the office network. If staff quote from home or on the road, or the office IP changes, leave this at **None** and rely on the quotas and the budget alert below. (The *Websites*, *Android* and *iOS* options don't apply to a desktop app.)
3. Click **Save**. Restrictions can take a few minutes to apply.

## 4. Cap usage and set a budget alert

These steps limit what a leaked or misused key could cost.

* **Daily quotas:** in **APIs & Services → Enabled APIs**, open each API, then **Quotas & System Limits**. Find the *per day* request limit for **Text Search** (Places API (New)) and for **ComputeRoutes** (Routes API), click the pencil, and set a cap. For a few dozen quotes a day, **200 per day for each** leaves plenty of headroom. Each address lookup uses 1 Text Search. Each round-trip route lookup uses **2** ComputeRoutes calls (out and back); a one-way lookup uses 1. **Test key** uses 1 Text Search.
* **Budget alert:** in **Billing → Budgets & alerts**, create a budget for the project (for example $25 per month) with email alerts at 50%, 90% and 100%. A budget only sends alerts; it doesn't stop usage. The quotas stop usage.

## 5. Enter the key in the app

On each PC that runs GB DeliveryCalc:

1. Open the **Settings** tab and find **API keys → Google lookups (optional)** at the bottom.
2. Paste the key into **Google Maps Platform API key**. The box hides what you type.
3. Click **Test key**. The app makes one address search for *Hattiesburg, MS*. *Test passed* means the key works. If it fails, the message gives Google's reason, for example a mistyped key, an API not enabled, billing off, or an IP restriction that doesn't match this network.
4. Click **Save key** (labelled **Save / replace key** once a key is saved; it overwrites the old one). The status changes to **Key saved**, and the box clears.
5. Go to **New estimate**. You don't need to restart. The *Not configured* banner for address and route lookup goes away, and **Look up** becomes active. Confirming an address then fills in the miles and drive time automatically.

**Test key** tests whatever is in the box. If the box is empty, it tests the saved key.

### Rotating or removing the key

* **To rotate:** create a new key in Google Cloud Console (or use **Regenerate key**), restrict it the same way, then paste it into each PC and **Save key**. Delete the old key in the console once every PC has the new one.
* **To remove from a PC:** click **Remove key** and confirm. Lookups turn off on that PC, and staff go back to manual entry.
* **If a key may have leaked:** regenerate or delete it in Google Cloud Console first, since that stops it working everywhere at once. Then enter the new key on each PC.

## Where the key is kept

* The key is encrypted with Windows' built-in per-user protection (DPAPI) and saved as `google-key.bin` in the app data folder. That folder is `%LOCALAPPDATA%\GroverBrothers\Estimator\`, or the folder in `GROVER_ESTIMATOR_DATA_DIR` if that variable is set. Under the Store (MSIX) install, Windows redirects this folder to the package's private storage.
* Only the **same Windows user on the same PC** can decrypt it. Copying the file to another PC or user doesn't work; the app treats it as "No key".
* It is **not** in `estimator.db` and **not** in backups. Restoring a backup on a new PC brings back teams, vehicles, settings and estimates, but not the key, so **enter the key again on a new PC**.
* The app never shows the saved key again, and never puts it in a web address, an error message or the screen. It is sent only to Google, in the `X-Goog-Api-Key` request header, over HTTPS.
* Anyone signed in to that PC as that Windows user could in principle recover the key. That is why steps 3 and 4 (restrictions, quotas, budget alert) matter.

## What Google's terms allow you to keep

Google's Maps Platform terms don't grant permission to store Places addresses or Routes distances and durations as provider data in permanent records (details in [INTEGRATIONS.md](INTEGRATIONS.md#google-places-api-and-routes-api-evaluation)).

**Clarification from Google Support, 2026-11-19 (per Jason, Grover Brothers):** copying looked-up miles and drive time into the manual-entry fields is acceptable under the licensing. The app is built around that:

* **Confirm match** copies the chosen address into the editable **Job address** field and immediately looks up the route (both directions for a round trip). The miles and drive time are copied into the manual-entry boxes, labelled *From Google route lookup, copied into manual entry*. Staff can adjust them.
* These estimates **save like manually entered ones**. Saved estimates record that the figures came from a Google lookup.
* Address results show **Powered by Google**, as Google's policies require when results appear without a Google map.

Keep a note of the Google Support case or email on file with the plan documents.

## Troubleshooting

| Message | What to check |
|---|---|
| *Google refused the API key (403)* | The key is restricted to other APIs, **Places API (New)** or **Routes API** isn't enabled, billing is off, or the IP restriction doesn't match this network's public IP. |
| *Google rejected the request (400)* … *API key not valid* | The key was mistyped or deleted. Paste it again. |
| *Google's quota for this key is used up (429)* | The daily cap was reached. Enter figures by hand, or raise the quota. |
| *could not reach Google* / *did not answer within 15 seconds* | No internet, or a firewall or proxy is blocking `places.googleapis.com` or `routes.googleapis.com`. |
| *Google found no driving route* | Check both addresses. Use the full street address with city and state. |
| *Route details changed since these figures were looked up* | The origin, job address or trip type changed. Click **Re-run route lookup**, or **Keep these figures**. |
| *…the return leg couldn't be looked up, so the outbound leg was doubled* | The way back couldn't be looked up, so the app doubled the outbound leg and says so on the estimate. |

# Address, routing and fuel-price integrations

**Current state:** address lookup and route lookup call **Google Maps Platform directly from the app** once a Google API key is entered in **Settings → Google lookups**. The Mississippi average fuel price comes from **OilPriceAPI** once an OilPriceAPI key is entered in **Settings → Fuel prices**. There is no server or company backend. Without a key, that integration shows as *Not configured* and staff enter the values by hand. Manual entry is complete and tested.

| Integration | Interface (Core) | Adapter (Services) | Status |
|---|---|---|---|
| Address search | `IPlaceSearchProvider` | `Google/GooglePlaceSearch` (Places API (New) Text Search) | On when a key is saved. The confirmed address is copied into the editable job-address field and saved. |
| Driving miles and time | `IRoutingProvider` | `Google/GoogleRouting` (Routes API computeRoutes) | On when a key is saved. Runs automatically on Confirm match; figures are copied into the manual-entry boxes and saved. |
| State average fuel price (shop's state) | `IFuelPriceSource` | `OilPriceApi/OilPriceApiFuelSource` (AAA data) | On when an OilPriceAPI key is saved. Stored with saved estimates. See [FUEL_PRICES.md](FUEL_PRICES.md). |

`ProviderFactory.Create(IntegrationKeys)` returns the Google adapters when a Google key is present and the OilPriceAPI adapter when an OilPriceAPI key is present, and the `NotConfigured*` providers otherwise. Those always throw `ProviderUnavailableException` with a reason, and the UI shows it. No adapter returns sample or fallback data.

**Setup steps for the key (Google Cloud project, restrictions, quotas, entering it in the app) are in [GOOGLE_SETUP.md](GOOGLE_SETUP.md).**

## How the Google calls work

| | Address lookup | Route lookup |
|---|---|---|
| Request | `POST https://places.googleapis.com/v1/places:searchText` | `POST https://routes.googleapis.com/directions/v2:computeRoutes` |
| Field mask (`X-Goog-FieldMask`) | `places.id,places.displayName,places.formattedAddress` | `routes.distanceMeters,routes.duration` |
| Body | `{"textQuery":"…","regionCode":"US"}` | `{"origin":{"address":"…"},"destination":{"address":"…"},"travelMode":"DRIVE","routingPreference":"TRAFFIC_UNAWARE"}` |
| Result | display name, formatted address, place ID per match | miles = meters ÷ 1,609.344, rounded to 0.1; minutes = seconds rounded to the nearest minute |

* **Key handling:** the key goes only in the `X-Goog-Api-Key` header. It is never in a URL, error message or log. Google's own error text is scrubbed of it before it is shown. The key is stored DPAPI-encrypted per Windows user in `google-key.bin` next to (not inside) `estimator.db`, so backups never contain it.
* **Round trips** make two calls: origin→destination and destination→origin. If the return call fails or finds no route, the app doubles the outbound leg and labels the figures that way.
* **Errors:** non-2xx responses, network failures, the 15-second timeout, unreadable JSON and "no route" all become a friendly `ProviderUnavailableException` (for example *Google refused the API key (403)…*). The lookup message shows it, and the manual fields stay usable.
* **Traffic-unaware routing** gives typical driving times, not today's traffic. It is cheaper and stable for quoting.
* **Attribution:** address results show *Powered by Google*.
* **Copy into manual entry (2026-11-19):** the adapters still report `PermitsRetention = false`, but the app never saves their output as provider data. **Confirm match** copies the address into the editable job-address field and runs the route lookup, copying the miles and drive time into the manual-entry boxes (labelled *From Google route lookup, copied into manual entry*). Google Support confirmed to Jason on 2026-11-19 that this is acceptable, so these estimates save. Before this change, `RetentionPolicy` refused every save that used a Google address or route, which was the cause of the "Save is broken" report.

## Google Places API and Routes API: evaluation

Checked on 2026-11-18 against the [Google Maps Platform Service Specific Terms](https://cloud.google.com/maps-platform/terms/maps-service-terms), last modified 15 Oct 2026:

* **Places API** (§14.3) and **Routes API** (§19.3): the only caching expressly permitted is **latitude and longitude for up to 30 consecutive calendar days**, which must then be deleted.
* **Place IDs** (§A.3) may be cached.
* The terms give no permission to store a **formatted address** from Places, or a **distance or duration** from Routes. The Agreement's general restriction on caching or storing Google Maps Content therefore applies. (Geocoding API §6.3.2 does allow indefinite caching of addresses, but only for end-user-facing features, isolated per end user, so it does not obviously cover a business's saved quotes.)

**Conclusion:** the rights to keep Google-sourced addresses and route figures in permanent saved estimates are **unclear or not granted**. Provider-dependent persistence is therefore **disabled**: the Google adapters report `PermitsRetention = false`. Staff can use a lookup to help quote, but a saved estimate keeps only figures and addresses that staff entered themselves. Before enabling Google, have counsel or Google sales confirm in writing what may be stored, or pick a provider whose licence allows retention.

**What was done:** the app calls Places API (New) and Routes API directly with a key restricted to those two APIs (see [GOOGLE_SETUP.md](GOOGLE_SETUP.md)) and shows Google attribution on address results.

**Update 2026-11-19, Google Support (per Jason):** copying looked-up miles and drive time into the manual-entry fields is acceptable under licensing. The app now does exactly that (and copies the confirmed address into the editable job-address field), and those estimates save. Keep the Google Support confirmation on file.

## AAA fuel prices: evaluation

Checked on 2026-11-18:

* [gasprices.aaa.com](https://gasprices.aaa.com/?state=MS) showed a Mississippi average of $3.49 for regular and $5.59 for diesel on 11/18/26. There is no public API or data feed.
* AAA's [About page](https://gasprices.aaa.com/about/) says the data comes from **OPIS**. It grants a limited licence "for personal, non-commercial use only", prohibits reproduction, distribution and archiving, and says "Any commercial use or exploitation of protected content is strictly prohibited."

**Conclusion (AAA directly):** scraping AAA's site, or storing or displaying its numbers in a business app, is **not permitted** under AAA's own terms.

**Update 2026-11-18, OilPriceAPI:** OilPriceAPI publishes the same AAA state averages (`GASOLINE_RETAIL_STATE_MS_USD`, `DIESEL_RETAIL_STATE_MS_USD`, source field `aaa`). Per Jason (Grover Brothers), the OilPriceAPI API plan in use grants usage in commercial applications under 10,000 seats, so the app uses OilPriceAPI as its fuel source. Keep the plan terms on file and re-check on plan changes ([FUEL_PRICES.md → Licensing](FUEL_PRICES.md#licensing)).

**Other options considered**
* License state-average retail data directly from **OPIS** (AAA's provider), or from another licensed vendor, and add an `IFuelPriceSource` adapter for it.
* The free U.S. **EIA** weekly retail series cover national and regional (PADD) averages, not a Mississippi statewide average, so they would be a different number and must be labelled that way.

The New estimate screen fetches regular and diesel automatically at startup and on local date rollover (checked every 15 minutes); there is no Fetch button. The service layer (`Core/Integrations/FuelPriceService.cs`) calls the source at most once per fuel type per local day, reusing the verified live value (persisted in SQLite) across estimates and restarts; after a failure it waits an hour before retrying (attempt time persisted) and shows the last known value with its age; a manual price always wins. Regular unleaded and diesel are separate requests and labels.

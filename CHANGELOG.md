# Changelog

## 1.0.0 (2026-11-23)

First release of **GB DeliveryCalc**, a Windows app that works out what a delivery and installation job will actually cost before you quote it.

### Estimates
* Cost lines for travel labor, installation labor, fuel, vehicle operating cost and any other direct costs, with a total estimated cost.
* Round trip or one way. Round trips look up both directions, and the figures update when you switch.
* Optional rounding of labor hours up to the next whole hour, and a paid-hours override.
* **Copy cost estimate** puts a plain-text summary on the clipboard for emails and quotes.

### Address, route and fuel lookups (optional)
* Google Maps Platform address search and driving miles and time, using your own API key. Results are copied into the editable fields.
* Today's average regular and diesel price for the state in the shop's starting address, from OilPriceAPI with your own key. Fetched automatically at most once a day per fuel type.
* Everything also works by hand without any keys.

### History and actuals
* Every estimate is saved with the rates and prices used, so later settings changes never alter it. Estimates can be searched, edited (previous versions are kept) and deleted.
* Record actual miles, drive time, installation time, labor, fuel and other costs after the job, and compare estimated against actual cost line by line.

### Settings
* Shop starting address verified with Google; teams with loaded labor rates; vehicles with fuel type, MPG and operating cost per mile. Teams and vehicles can be archived.
* Backup and restore of all data, and a password-protected backup of the API keys (AES-256-GCM).
* Light, dark or follow-Windows appearance.
* About window with developer contact, license and privacy policy.

### Security and privacy
* API keys are encrypted with Windows DPAPI, sent only to their own service in request headers over HTTPS, and never stored in the database or backups.
* All data stays on the PC. No accounts, analytics or telemetry. Privacy policy: https://jasonwoodcock.us/apps/privacy/

### Requirements
* Windows 10 version 2004 (build 19041) or later, 64-bit. The release zip is self-contained; .NET does not need to be installed.

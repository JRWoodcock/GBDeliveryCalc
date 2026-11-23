# Microsoft Store (MSIX) packaging and submission

Checked against Microsoft Learn on 2026-11-18. Recheck these pages before submitting, because they change:
[App package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements),
[Create an app submission](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/create-app-submission),
[Upload app packages](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages),
[Visibility / private audience](https://learn.microsoft.com/windows/uwp/publish/choose-visibility-options).

## What is in the repo
* `packaging/Package.appxmanifest.template`: a full-trust desktop app (`runFullTrust`, `internetClient`), x64, targeting Windows.Desktop with MinVersion 10.0.19041.0.
* `packaging/identity.json`: the Partner Center identity (reserved 2026-11-23). `packaging/identity.local.json` (git-ignored) overrides it if present.

| Partner Center field | Value |
|---|---|
| Store ID | 9P24VC3PN43L |
| Package/Identity/Name | JasonWoodcock.GBDeliveryCalc |
| Package/Identity/Publisher | CN=BAA6192D-7BA9-4F65-A67D-EFCB29701DF2 |
| Package/Properties/PublisherDisplayName | Jason Woodcock |
| Package family name (PFN) | JasonWoodcock.GBDeliveryCalc_8bxhscytm4f0w |
| Store link | https://apps.microsoft.com/detail/9P24VC3PN43L |
* `packaging/Assets/`: the GB DeliveryCalc package logos (44, 50, 150 and 310×150, plus scale-200, and the 16–256 px taskbar sizes).
* `packaging/StoreListing/`: Store logo, box art, poster and hero art, and six 1920 × 1080 screenshots. The listing text is in [STORE_LISTING.md](STORE_LISTING.md).
* `packaging/art/generate_art.py`: regenerates every logo, the app icon (`src/GBDeliveryCalc.App/Assets/AppIcon.ico` and `.png`) and the promotional art. Needs Python with Pillow.
* `packaging/build-msix.ps1`: self-contained `dotnet publish` → layout → manifest tokens → `MakeAppx pack` → optional `SignTool` (for sideload testing only).
* `packaging/validate-msix.ps1`: runs the Windows App Certification Kit.

## Facts that shape the package
* The **identity must match Partner Center exactly** (case-sensitive): `Identity/Name`, `Identity/Publisher` and `PublisherDisplayName`, from *Product management → Product identity* after you reserve the app name. These are not known yet, so they stay configurable.
* **Version:** the fourth section is reserved for the Store and **must be 0**, and the first section cannot be 0. The script enforces both, so use `1.0.0.0`, `1.0.1.0`, and so on.
* **Signing:** Store MSIX submissions do **not** need your own certificate, because the Store re-signs after certification. Sign only for sideload testing. The test certificate's subject must equal the manifest `Publisher`.
* **Accepted uploads:** .msix, .msixbundle, .msixupload (and the appx equivalents). Microsoft recommends .msixupload, which Visual Studio produces. This CLI script produces a plain .msix, which is accepted.
* **`runFullTrust`** is a restricted capability. The submission's *Submission options* page asks why it is needed. Suggested text: "WPF desktop application (.NET) packaged with MSIX; full trust is required to run a Win32 desktop process."

## Private audience
* Under *Pricing and availability → Audience*, choose **Private audience** and add at least one known user group of email addresses.
* Those addresses must be **personal Microsoft accounts**. Work or school (Entra ID) accounts can't view or get private-audience apps, even when the Partner Center account is a company account. Staff may each need a personal MSA. If that is unacceptable, consider distributing the signed MSIX internally instead (for example through Intune or sideloading) and confirm the current options with Microsoft.
* Testers install from the private link on the *App identity* page (*URL if your app is only visible to certain people*), not the normal Store URL.
* **Once a submission goes out as Public, later submissions cannot switch back to Private.**
* A person removed from the audience keeps using the app but stops getting updates.

## Build, validate and submit (Windows 11 x64)

```powershell
winget install Microsoft.DotNet.SDK.10 Microsoft.PowerShell
# MakeAppx/SignTool: install Microsoft.WindowsSDK.10.0.26100, or restore the Microsoft.Windows.SDK.BuildTools NuGet package (no admin needed)
dotnet test GBDeliveryCalc.slnx -c Release

pwsh packaging/build-msix.ps1 -Version 1.0.0.0

# sideload smoke test (needs a test cert whose subject == Publisher, trusted in LocalMachine\TrustedPeople)
pwsh packaging/build-msix.ps1 -Version 1.0.0.0 -CertificatePath C:\certs\grover-test.pfx -CertificatePassword (Read-Host -AsSecureString)
Add-AppxPackage packaging/out/GBDeliveryCalc_1.0.0.0_x64.msix

# certification kit (elevated)
pwsh packaging/validate-msix.ps1 -Package packaging/out/GBDeliveryCalc_1.0.0.0_x64.msix
```

Submission checklist in Partner Center:
1. ~~Reserve the name~~ (done 2026-11-23; values above). Build **unsigned** with `build-msix.ps1 -Version 1.0.0.0`.
2. *Pricing and availability:* markets, **Private audience** with groups, discoverability, free.
3. *Properties:* category (Business or Productivity). **Privacy policy URL** is required if the app collects or transmits personal info, which it does when a Google key is saved: address and route lookups send job addresses to Google. Use https://jasonwoodcock.us/apps/privacy/ (the shared policy for all of Jason Woodcock's apps; source in [PRIVACY.md](PRIVACY.md)). Business contact info is required for company accounts.
4. *Age ratings* questionnaire.
5. *Packages:* upload the `.msix` and wait for **Validated**.
6. *Store listings:* copy the fields from [STORE_LISTING.md](STORE_LISTING.md) and upload the screenshots and art from `packaging/StoreListing/`.
7. *Submission options:* justify `runFullTrust`.
8. **Submit for certification.** Publishing is a separate, human-authorised step.

Nothing has been submitted or published from this repository.

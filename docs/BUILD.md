# Windows build

Use 64-bit Windows 11 and PowerShell 7 for development. The release compiler is the C# compiler under Windows .NET Framework v4.0.30319. The application targets .NET Framework 4.8 compatibility. Windows supplies WPF, shell COM, Media Foundation, Windows Runtime metadata and the platform codecs. Some archive operations require archiveint.dll from a recent Windows 11 installation. The finished application does not require PowerShell 7.

Run from the repository root:

```powershell
./scripts/build.ps1 -Test -Installer
./scripts/check-package.ps1
```

The first command compiles the application and test executable, uses the checked-in transparent multi-resolution ICO, runs the fixtures, and creates the cabinet, MSI and setup executable. The second command opens the payload embedded in the actual setup, extracts the installed executable, compares hashes, and starts its UI and conversion self-test. The scripts throw on errors. Build intermediates go under `build/`, deliverables under `release/`. The screenshot in docs/screenshots is rendered from the live WPF controls by the tests.

For app development run `build/Hepta.exe`. Use `--tray` for background startup or `--self-test` for an isolated executable smoke test. This test uses temporary preferences and files. The PDFium preparation script accepts an already verified DLL through HEPTA_PDFIUM_DLL or the pinned pypdfium2 5.13.0 Windows wheel. Without a verified local copy it uses the official Windows x64 wheel, pinned to SHA256 `47dcca2a8d507b5fd24f94c3c9d48fb379430f097bc20f01beff6c963ffbcedb` as published on PyPI. The entire wheel is checked before extracting the DLL, and the extracted file is checked after moving. The original tested DLL has its own independent SHA256 pin; the official wheel fallback requires fresh Windows regression testing. An offline developer can supply the exact verified wheel to scripts/prepare-pdfium.ps1 -Wheel before building. The user installer includes the native engine. Build and packaging do not require NuGet packages. CAB and MSI packaging uses Windows tools and Windows Installer COM. The MSI Summary Information package ID changes on each build. Compare the supplied hash against the corresponding artifact, not a later rebuild.

For a new version update assembly, setup and MSI version metadata, select a new MSI product code for a major upgrade, retain the upgrade code and verify the upgrade table’s version range. Run CI on Windows and test the actual installer in a clean offline Windows VM. Test startup, shortcuts, Explorer and desktop selections, repair, upgrade and uninstall with sample files. The release account, source license and external links are owner decisions.

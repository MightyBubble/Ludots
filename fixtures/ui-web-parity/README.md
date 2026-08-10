# UI Web Parity Fixture

Shared HTML/CSS pause-menu used to prove Ludots Markup layout matches Chrome reference boxes across desktop / tablet / phone viewports.

- Regenerate Chrome golden: `node scripts/ui-web-parity/dump-chrome-layout.mjs`
- Assert: `dotnet test src/Tests/UiShowcaseTests/UiShowcaseTests.csproj --filter UiWebParity`
- Capture: `dotnet run --project src/Tools/Ludots.UI.ShowcaseCapture -- artifacts/acceptance`

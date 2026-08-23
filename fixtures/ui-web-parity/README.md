# UI Web Parity Fixture

Shared HTML/CSS pause-menu used to prove Ludots Markup layout matches Chrome reference boxes across desktop / tablet / phone viewports.

`#menu-shell` uses `flex-wrap` (no `@media`): wide stages keep card + rail side-by-side; phone stages stack the rail under the card so the hero title stays readable.

- Regenerate Chrome golden: `node scripts/ui-web-parity/dump-chrome-layout.mjs`
- Assert: `dotnet test src/Tests/UiShowcaseTests/UiShowcaseTests.csproj --filter UiWebParity`
  - desktop/tablet: box model within 2.5px
  - phone: stacked rail + readable hero (wrap line cross-size still engine-sensitive)
- Capture: `dotnet run --project src/Tools/Ludots.UI.ShowcaseCapture -- artifacts/acceptance`

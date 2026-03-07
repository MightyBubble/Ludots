# UI Runtime Execution Task Table

Date: 2026-03-07
Status: In Progress
Scope: UI Runtime / Native DOM / Native CSS Profile / Skia Rendering / Text and Fonts / Form / Table / Showcase / Skin Swap / Tween Animation
Type: UI-only execution artifact

## 1 Summary

- Unified UI runtime is in place: `UiScene`, `UiNode`, `UiDocument`, `UiElement`
- Unified parser chain is in place: `AngleSharp` for HTML/DOM, `ExCSS` for CSS, `FlexLayoutSharp` for primary layout
- Unified authoring modes are in place: Compose Fluent, Reactive Fluent, Markup plus C# CodeBehind
- Unified host consumption is in place: Web and Raylib consume `UiScene` / `UiSceneDiff`
- Legacy UI entry points were removed: `IUiSystem`, `ShowUiCommand`, `UIRoot.Content`
- UI SSOT docs no longer use `v1` / `v2` suffixes; approved scope is backfilled in-place with change history
- Batch 1 shipped gradients, single-layer shadows, outline, `font-family`, `white-space`, wrapping, and font registry plumbing
- Batch 2 shipped runtime focus flow, radio exclusive-group behavior, baseline table semantics, Fluent `Radio/Table` API, and showcase examples

## 2 Completed Baseline

| Item | Status | Evidence |
|------|--------|----------|
| Runtime scene tree | Done | `src/Libraries/Ludots.UI/Runtime/UiScene.cs` |
| Native DOM | Done | `src/Libraries/Ludots.UI/Runtime/UiDocument.cs` |
| CSS computed style | Done | `src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs` |
| Flex primary layout | Done | `src/Libraries/Ludots.UI/Runtime/UiLayoutEngine.cs` |
| Skia base rendering | Done | `src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs` |
| Compose Fluent | Done | `src/Libraries/Ludots.UI/Compose/` |
| Reactive Fluent | Done | `src/Libraries/Ludots.UI/Reactive/` |
| Markup plus CodeBehind | Done | `src/Libraries/Ludots.UI.HtmlEngine/Markup/` |
| Skin / theme showcase | Done | `mods/UiSkinShowcaseMod/` |
| Runtime tests | Done | `src/Tests/UiRuntimeTests/` |
| Showcase tests | Done | `src/Tests/UiShowcaseTests/` |

## 3 Approved Scope Still In Progress

| Item | Status | Evidence / Gap |
|------|--------|----------------|
| Advanced Skia appearance: filters, backdrop blur, frosted glass | Partial | Gradients, single shadows, and outline shipped; `filter` / `backdrop-filter` are still open in `src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs` and `src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs` |
| Text shaping, custom fonts, multilingual layout, RTL / BiDi | Partial | `font-family`, `white-space`, base wrapping, and `UiFontRegistry` shipped; shaping / RTL / BiDi remain open in `src/Libraries/Ludots.UI/Runtime/UiTextLayout.cs` and `src/Libraries/Ludots.UI/Runtime/UiFontRegistry.cs` |
| Advanced Flex features: `wrap`, `align-content`, fuller `gap` semantics | Planned | Core layout is on `FlexLayoutSharp`, but the full capability surface is not exposed yet in `src/Libraries/Ludots.UI/Runtime/UiLayoutEngine.cs` |
| Runtime pseudo flow: `focus` | Done | Click-to-focus flow, `:focus` matching, and showcase examples shipped in `src/Libraries/Ludots.UI/Runtime/UiScene.cs` and `src/Tests/UiRuntimeTests/UiDomAndCssTests.cs` |
| Structural pseudo classes: `:first-child`, `:last-child`, `:nth-child()` | Planned | Still unsupported in `docs/reference/ui_native_css_html_support_matrix.md` |
| Form semantics: `radio`, validation states, consistent controls | Partial | Radio exclusive-group behavior plus `:checked` / `:focus` shipped; browser-native validation is still not implemented |
| Table semantics: `table`, `thead`, `tbody`, `tr`, `td`, `th` native layout | Partial | Baseline table / row / cell semantics and equal-width layout shipped; full browser-style auto table algorithm is still open |
| Advanced image features: nine-slice, `object-fit`, themed image skins | Planned | `object-fit` remains unsupported per `docs/reference/ui_native_css_html_support_matrix.md` |
| Tween animation and transitions | Planned | Not on current mainline yet; historical commit `fb8b167` exists outside current main ancestry |
| Showcase expansion: appearance, animation, forms, tables, fonts | Partial | Radio / table baseline examples shipped across all three authoring modes; dedicated appearance / animation / font pages are still open |

## 4 Removed Legacy Paths

| Legacy Path | Status | Replacement |
|-------------|--------|-------------|
| `src/Core/UI/IUiSystem.cs` | Removed | `UiScene` |
| `src/Core/Commands/ShowUiCommand.cs` | Removed | `UiScene` mount / event / diff |
| `UIRoot.Content` | Removed | `UIRoot.MountScene(UiScene)` |
| Web `html/css` primary payload | Removed | `UiSceneDiff` |
| Old widget / reconciler runtime | Removed | Compose / Reactive / Markup |

## 5 Current Acceptance Commands

- `dotnet test src/Tests/UiRuntimeTests/UiRuntimeTests.csproj -v minimal`
- `dotnet test src/Tests/UiShowcaseTests/UiShowcaseTests.csproj -v minimal`
- `dotnet run --project src/Tools/Ludots.UI.ShowcaseCapture/Ludots.UI.ShowcaseCapture.csproj -v minimal`
- `powershell -ExecutionPolicy Bypass -File scripts/validate-docs.ps1`

## 6 UI SSOT

- Support matrix: `docs/reference/ui_native_css_html_support_matrix.md`
- ADR: `docs/adr/ADR-0002-ui-runtime-unification.md`
- RFC: `docs/rfcs/RFC-0001-ui-runtime-fluent-authoring.md`

## 7 Change History

| Date | Change | Notes |
|------|--------|-------|
| 2026-03-07 | Establish execution table | Backfilled unified runtime, DOM, CSS, Flex, authoring modes, and showcase baseline |
| 2026-03-07 | Expand approved scope | Added advanced appearance, text / font, multilingual, forms, tables, structural pseudo classes, tween, and nine-slice targets |
| 2026-03-07 | Remove doc version suffixing | UI SSOT stays in-place without `v1` / `v2` doc forks |
| 2026-03-07 | Backfill batch 2 | Focus flow, radio behavior, table baseline semantics, Fluent API, showcase examples, tests, and screenshot artifacts shipped |

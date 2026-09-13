# Genre Info Showcase Acceptance

## Scenario Card
- Goal: validate one localized cross-genre info-panel runtime for 4X, RTS, and MOBA selection surfaces.
- Map: `genre_info_showcase`
- Mods: `LudotsCoreMod`, `CoreInputMod`, `CameraProfilesMod`, `FourXDemoMod`, `EntityCommandPanelMod`, `RtsDemoMod`, `MobaDemoMod`, `EntityInfoPanelsMod`, `GenreInfoShowcaseMod`
- Viewport: `1920x1080` headless Skia export.
- Evidence: four screenshots plus trace, battle report, and path mermaid.

## Timeline
- [T+001] Loaded genre_info_showcase, seeded RTS squad as the active live selection, and rendered the English deck + insight layout.
- [T+002] Scrolled the Formation Capability unit-card grid and advanced the virtual window without remounting the scene.
- [T+003] Recalled the MOBA hero control group and captured the portrait-first single-select treatment in English.
- [T+004] Switched locale to zh-CN, recalled the 4X governor control group, and captured the localized strategic portrait card.
- [T+005] Recalled the RTS barracks control group under zh-CN and captured the structure-oriented info panel variant.

## Outcome
- success: yes
- verdict: one insight runtime now covers portrait blend, SC2/War3-style single-select portrait focus, Formation Capability-style unit cards, localized copy, and control-group driven selection recall.
- screenshots:
  - `screens/01-rts-squad-en.png`
  - `screens/02-moba-hero-en.png`
  - `screens/03-fourx-governor-zh.png`
  - `screens/04-rts-barracks-zh.png`

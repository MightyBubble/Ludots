# UI Showcase Skin Swap Battle Report

## Scenario Card
- Goal: validate visible, interactive, acceptance-ready official UI showcase behavior.
- Viewport: 1280x720 for full-scene captures, focused crops for below-the-fold capability blocks.
- Driver: headless Skia renderer + deterministic click simulation.

## Battle Log
- skin-classic: Skin showcase initial theme hash=97E729683ACEBC49.
- skin-theme-scifi: Switch to Sci-Fi HUD skin pack.
- skin-scifi: Skin showcase Sci-Fi hash=97E729683ACEBC49.
- skin-theme-paper: Switch to Paper skin pack.
- skin-paper: Skin showcase Paper hash=97E729683ACEBC49.

## Acceptance Verdict
- PASS: DOM hash remains stable across skins = 97E729683ACEBC49.
- PASS: computed style changes through Classic / Sci-Fi / Paper skin packs.
- PASS: runtime switch stays inside the same unified UiScene.

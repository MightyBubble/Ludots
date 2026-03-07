# UI Showcase Style Parity Battle Report

## Scenario Card
- Goal: validate visible, interactive, acceptance-ready official UI showcase behavior.
- Viewport: 1280x720.
- Driver: headless Skia renderer + deterministic click simulation.

## Battle Log
- parity-compose: Compose baseline for style parity.
- parity-reactive: Reactive baseline for style parity.
- parity-markup: Markup baseline for style parity.

## Acceptance Verdict
- PASS: parity baseline captured across three official modes = True.
- INFO: compose root bg = #ff1d2433.
- INFO: reactive root bg = #ff08131f.
- INFO: markup root bg = #00ffffff.

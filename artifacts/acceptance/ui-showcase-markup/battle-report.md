# UI Showcase Markup Battle Report

## Scenario Card
- Goal: validate visible, interactive, acceptance-ready official UI showcase behavior.
- Viewport: 1280x720.
- Driver: headless Skia renderer + deterministic click simulation.

## Battle Log
- markup-initial: Markup showcase compiles HTML/CSS into native DOM and binds C# code-behind.
- markup-inc: Markup code-behind increments counter.
- markup-counter: Markup counter rerender is visible after C# action.
- markup-modal-toggle: Markup opens overlay from code-behind.
- markup-modal: Markup overlay and diagnostics remain visible.

## Acceptance Verdict
- PASS: markup scene exposes Overview / Controls / Forms / Collections / Overlays / Styles plus PrototypeImportPage.
- PASS: markup action path stays in pure C# code-behind.
- PASS: prototype diagnostics are visible instead of silent fallback.

# UI Showcase Markup Battle Report

## Scenario Card
- Goal: validate visible, interactive, acceptance-ready official UI showcase behavior.
- Viewport: 1280x720 for full-scene captures, focused crops for below-the-fold capability blocks.
- Driver: headless Skia renderer + deterministic click simulation.

## Battle Log
- markup-initial: Markup showcase compiles HTML/CSS into native DOM and binds C# code-behind.
- markup-forms: Markup forms block shows required, pattern, password, and textarea validation surfaces from external HTML/CSS assets.
- markup-appearance: Markup appearance block shows backdrop blur, filter blur, flex wrap, RTL text, and image skin samples.
- markup-phase1: Markup selector and stack labs validate advanced selectors plus transformed z-index hit alignment.
- markup-phase2: Markup Phase 2 visual lab shows multi-background, multi-shadow, dashed border, mask, and clip-path in one native card.
- markup-phase3: Markup Phase 3 text lab shows multilingual copy, RTL alignment, ellipsis, and text decoration in one native panel.
- markup-phase4: Markup Phase 4 image lab shows CSS background-image url, inline SVG import, and native canvas binding in one panel.
- markup-phase5-start: Markup Phase 5 keyframe lab captures the initial animation state from external CSS files.
- advance 0.24s: Markup keyframe lab advances to a deterministic mid-frame.
- markup-phase5-mid: Markup Phase 5 keyframe lab shows mid-animation color, blur, and opacity interpolation.
- advance 0.32s: Markup keyframe lab reaches the deterministic end frame.
- markup-phase5-end: Markup Phase 5 keyframe lab shows the finite animation end state and alternate fill behavior.
- markup-transition-probe: Markup transition probe enters focus state without JS.
- advance 0.16s: Markup transition runtime advances native tween interpolation.
- markup-transition: Markup transition probe shows mid-animation color and opacity interpolation.
- markup-scroll-host: Markup scroll host consumes native wheel input.
- markup-scroll: Markup scroll container clips content, updates offsets, and keeps clip host bounded.
- markup-item-2: Markup keeps collection selection on item 2 for the focused crop.
- markup-table: Markup table block shows native table semantics, content-aware columns, rowspan, and colspan.
- markup-inc: Markup code-behind increments counter.
- markup-counter: Markup counter rerender is visible after C# action.
- markup-modal-toggle: Markup opens overlay from code-behind.
- markup-modal: Markup overlay and diagnostics remain visible.

## Acceptance Verdict
- PASS: markup scene exposes Overview / Controls / Forms / Collections / Overlays / Styles plus PrototypeImportPage.
- PASS: markup forms block covers required, pattern, password, and textarea validation surfaces from external HTML/CSS assets.
- PASS: markup appearance block covers blur, wrap, RTL text, and image skin samples.
- PASS: markup selector and stack labs cover advanced selectors with transform-aware stacking.
- PASS: markup Phase 2 visual lab covers multi-background, multi-shadow, border-style, mask, and clip-path.
- PASS: markup Phase 3 text lab covers multilingual copy, RTL, ellipsis, and text decoration.
- PASS: markup Phase 4 image lab covers CSS background-image url, inline SVG import, and native canvas binding.
- PASS: markup Phase 5 keyframe lab exports deterministic start / mid / end animation frames from external CSS assets.
- PASS: markup transition probe advances native tween interpolation without JS.
- PASS: markup scroll and clip demos are visible and interactive.
- PASS: markup table crop makes rowspan, colspan, and column sizing visible.
- PASS: markup action path stays in pure C# code-behind.
- PASS: prototype diagnostics are visible instead of silent fallback.

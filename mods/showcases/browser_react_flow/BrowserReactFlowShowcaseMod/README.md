# Browser React Flow Showcase

This mod is the browser UI integration showcase for a real packaged React app.

## Modes

- Default: React Flow feature showcase with animated edges, MiniMap, Controls, DataPlane publisher, keyboard probe, and alpha hit-test pass-through demo.
- `LUDOTS_BROWSER_REACT_FLOW_MODE=baseline`: lightweight browser perf baseline. It loads `?perf=baseline`, disables the DataPlane publisher, avoids React Flow, and renders a deterministic canvas plus small input panel.

## Hit-Test Comparison

- Default: `LUDOTS_BROWSER_REACT_FLOW_HIT_TEST=alpha`
- Bounds comparison: `LUDOTS_BROWSER_REACT_FLOW_HIT_TEST=bounds`

In alpha mode, the transparent web cutout at `520,300` / `320x160` lets clicks pass through to the native Ludots panel below it. In bounds mode, the same transparent area is still owned by the browser canvas.

## Keyboard Synthetic UAT

Raylib synthetic UI playback supports pointer, scroll, and keyboard injection through the same `UIRoot` path used by real input.

- `LUDOTS_RAYLIB_SYNTHETIC_UI_KEY_FRAME`: frame that injects keyboard input.
- `LUDOTS_RAYLIB_SYNTHETIC_UI_KEY`: control key routed as `Down`/`Up`, for example `Backspace`, `Enter`, or `ArrowLeft`.
- `LUDOTS_RAYLIB_SYNTHETIC_UI_KEY_TEXT`: text routed as `Character`/browser text input.

The React Flow mode contains a `Keyboard probe` input in the top-right DataPlane panel. Click it first or target it with synthetic pointer playback, then inject `KEY_TEXT` and optionally `KEY`.

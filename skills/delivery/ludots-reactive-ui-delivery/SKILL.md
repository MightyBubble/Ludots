---
name: ludots-reactive-ui-delivery
description: Deliver Ludots ReactivePage incremental diff and virtualization work with explicit runtime-window ownership, retained-tree observability, and playable mod design packets. Use when implementing, auditing, or closing reactive UI diff / virtualization work in Ludots.UI and UI mods.
---

# Ludots Reactive UI Delivery

Use this skill when `ReactivePage`, `UIRoot`, runtime-window virtualization, or retained diff closure needs a formal delivery path.

## Load References

1. Read `docs/reference/reactive_ui_runtime_window_contract.md`.
2. Read `references/runtime_window_closure_checklist.md`.
3. Read `references/playable_mod_design_template.md`.
4. Read `../../README.md` only when shared skill registry or hook responsibilities matter.

## Mandatory Rules

1. Make lifecycle ownership explicit.
- Either integrate runtime-window refresh into the generic mounted-page lifecycle.
- Or keep caller-owned refresh and update the formal contract/doc paths in the same change.
- Do not rely on fixture-only behavior without documenting the ownership model.

2. Preserve retained diff observability.
- Keep `UiReactiveUpdateMetrics`, `UiScene.TryGetVirtualWindow(...)`, and page-level counters usable from tests or diagnostics.
- Virtualized scroll changes must be observable without inferring hidden adapter behavior.

3. Reuse one runtime.
- Stay inside `src/Libraries/Ludots.UI/` and the owning mod/UI layer unless the task explicitly extends into adapters.
- Do not create a second reconciler, shadow scene tree, or host-only UI contract.

4. Put future designs in the right layer.
- Current implemented contract belongs in `docs/reference/` or `docs/architecture/`.
- Unimplemented closure targets and playable mod designs belong in `docs/rfcs/`.

5. Close with evidence.
- Link exact code paths, test paths, and doc paths in the issue packet or final summary.
- If visuals changed, route through the visual evidence chain instead of hand-waving.

## Workflow

1. Inspect current ownership.
- Review `ReactivePage.RefreshRuntimeDependencies()`.
- Review `ReactiveContext.GetVerticalVirtualWindow(...)`.
- Review `UIRoot.Update(...)`, `UIRoot.HandleInput(...)`, and the active page mounting path.

2. Choose closure mode.
- Generic lifecycle integration.
- Or explicit caller-owned contract.

3. Wire observability and tests.
- Assert `RuntimeWindowChange`, visible range, composed item budget, and no full remount.
- Keep diagnostics paths usable from fixtures and headless tests.

4. Write formal docs.
- Current contract -> `docs/reference/`
- Future closure / playable mod designs -> `docs/rfcs/`

5. Prepare reviewer communication when requested.
- Summarize ownership decision, evidence paths, and residual risks for the issue / PR packet.

## Output Requirements

Provide:
- ownership decision and affected lifecycle paths
- observability evidence paths
- playable mod design packet paths when scope includes follow-ups
- residual risks if closure is still partial

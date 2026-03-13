# Ludots Reactive UI Delivery

Use this skill when `ReactivePage` / `UIRoot` incremental diff or runtime-window virtualization work needs a formal closure path.

## Load

- `docs/reference/reactive_ui_runtime_window_contract.md`
- `references/runtime_window_closure_checklist.md`
- `references/playable_mod_design_template.md`

## Rules

- Make runtime-window ownership explicit.
- Keep retained diff and virtualization observable from tests or diagnostics.
- Put future playable mod designs in `docs/rfcs/`, not SSOT runtime docs.

## Outputs

- Updated runtime contract or lifecycle implementation
- Playable mod design packet when requested
- Evidence-backed issue / PR summary when requested

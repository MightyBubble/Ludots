# Documentation Governance Report

Date: 2026-04-01
Scope:
- `docs/architecture/README.md`
- `docs/architecture/control_buff_infrastructure.md`
- `docs/architecture/entity_selection_architecture.md`
- `docs/architecture/gas_layered_architecture.md`
- `docs/architecture/interaction/README.md`
- `docs/architecture/launcher_ssot_user_first.md`
- `docs/architecture/mod_architecture.md`
- `docs/architecture/mod_runtime_single_source_of_truth.md`
- `docs/architecture/narrative_frontend_kit.md`
- `docs/architecture/narrative_quest_dialogue_cinematic.md`
- `docs/architecture/order_navigation_movement.md`
- `docs/architecture/startup_entrypoints.md`
- `docs/architecture/time_flow.md`
- `docs/reference/README.md`
- `docs/reference/cli_runbook.md`
- `docs/rfcs/README.md`
- `skills/README.md`
- `skills/registry.json`
- `scripts/run-mod-launcher.ps1`
- `src/Tools/Ludots.Editor.Bridge/Program.cs`
Ruleset: `ludots-doc-governance` checklist plus launcher SSOT, user-first remediation goals, and evidence-backed architecture claims

## Summary

- Total findings remaining in scope: 0
- P0: 0
- P1: 0
- P2: 0
- P3: 0

## Findings

- Launcher entrypoint docs now align with implementation:
  - wrapper canonical form is `.\scripts\run-mod-launcher.cmd cli ...`
  - canonical browser entry is `http://localhost:5299/launcher/index.html`
  - `/` and `/launcher` redirect to `/launcher/index.html`
- Startup and runtime docs now describe the current product chain consistently:
  - launcher graph artifact is the runtime planning authority
  - `launcher.runtime.json` is the adapter bootstrap carrier
  - one resolved launch plan loads through one shared `ModLoadContext`
- Reference and RFC index pages now only point to files that exist in the repository.
- Skill docs remain aligned with `skills/registry.json`; `skills/contracts/` is documented as support material, not a skill layer.
- `docs/architecture/README.md` continues to index `docs/architecture/control_buff_infrastructure.md`, and the document's v1 claims stay backed by concrete code, tests, or runtime artifacts.

## Fix Order

1. No documentation fixes required in the reviewed scope.
2. Keep the architecture document aligned if the control-state contract changes again.
3. Extend the same evidence style when `disarm` or other control effects enter scope.
4. Keep launcher wrapper, bridge routes, and runbook examples synchronized whenever entry URLs or CLI verbs change.
5. Keep startup documentation aligned with the current graph-backed runtime contract until a separate lock artifact is implemented.
6. Continue shrinking direct-debug and string-key compatibility paths in code, then backwrite those removals to docs in the same slice.

## Residual Risks

- Product startup is graph-backed, but direct-debug compatibility paths still exist for explicit `modPaths`; docs must continue to keep them outside the default creator workflow.
- A distinct launcher lock artifact still does not exist; when that contract lands, code and docs must evolve together.
- `docs/rfcs/` currently contains two historical files under `RFC-0059`; indexing is accurate to the repository, but future RFC governance should normalize duplicate identifiers.
- The control-buff architecture document intentionally reflects only the implemented v1 surface; if future work reintroduces tag-authoritative cast gating, docs and runtime evidence must evolve together.

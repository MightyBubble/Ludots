# Ludots Siege Overnight Delivery

Use this skill when the overnight RTS siege scenario must be delivered in an isolated Ludots worktree with full evidence and no architecture shortcuts.

## Load

- `../ludots-feature-delivery/references/reuse-first-policy.md`
- `../ludots-feature-delivery/references/minimal-scenario-template.md`
- `../ludots-feature-delivery/references/showcase-ui-acceptance-checklist.md`
- `../../evidence/ludots-visual-capture/references/capture-checklist.md`
- `../../tooling/ludots-ci-audit-gate/references/ci-gate-checklist.md`
- `references/overnight-board-spec.md`

## Rules

- Stay inside the isolated overnight worktree.
- Update `artifacts/overnight/siege_assault_board.md` after each implementation or test loop.
- Reuse existing Ludots runtime, HUD surfaces, navigation, relation spine, and evidence chain before adding new code.
- Do not introduce fallback paths, duplicate truths, or parallel runtime stacks.
- Treat screenshots, video, and Claude review as release requirements, not optional polish.

## Outputs

- updated overnight board
- deterministic acceptance evidence
- screenshot and video evidence
- Claude verification notes
- gate result

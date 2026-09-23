---
name: ludots-siege-overnight-delivery
description: Drive an isolated overnight Ludots RTS siege delivery from reusable infra through tests, screenshots, video evidence, board updates, and Claude verification without introducing fallback or duplicate runtime stacks.
---

# Ludots Siege Overnight Delivery

Use this skill when delivering the overnight RTS siege scenario in an isolated worktree with a hard playable deadline.

## Load References

1. Read `../ludots-feature-delivery/references/reuse-first-policy.md`.
2. Read `../ludots-feature-delivery/references/minimal-scenario-template.md`.
3. Read `../ludots-feature-delivery/references/showcase-ui-acceptance-checklist.md`.
4. Read `../../evidence/ludots-visual-capture/references/capture-checklist.md`.
5. Read `../../tooling/ludots-ci-audit-gate/references/ci-gate-checklist.md`.
6. Read `references/overnight-board-spec.md`.

## Mandatory Rules

1. Work only inside the isolated overnight worktree and branch.
2. Keep `artifacts/overnight/siege_assault_board.md` updated after every major implementation or test iteration.
3. Reuse existing Ludots pipelines and showcase/capability mods before adding new code.
4. Do not create fallback paths, parallel truths, or duplicate runtime stacks.
5. Ship headless deterministic evidence before calling the feature playable.
6. Ship screenshot and video evidence for HUD, selection, command, and siege interactions.
7. Require a Claude verification pass before the run is considered complete.

## Workflow

1. Build a reuse map from existing mods, capabilities, systems, and UI surfaces.
2. Implement the smallest formal scenario mod that proves the required mechanics.
3. After each implementation slice:
   - run the relevant tests
   - update the overnight board
   - log blockers and next cuts
4. Once playable:
   - capture screenshots and video
   - extract reviewable evidence
   - run Claude verification
   - run the CI/evidence gate

## Output Requirements

- updated overnight board
- deterministic acceptance artifacts
- screenshot evidence
- video evidence
- Claude review result
- final gate result

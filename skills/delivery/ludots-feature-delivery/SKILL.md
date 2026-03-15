---
name: ludots-feature-delivery
description: Deliver Ludots gameplay and application features with infrastructure-first reuse, headless end-to-end acceptance evidence, MUD-style readable battle logs, and visualized test paths from minimal scenarios. Use when implementing or refactoring features in Core, Mods, Apps, Adapters, or Tools.
---

# Ludots Feature Delivery

Use this skill to deliver production-grade features with consistent acceptance evidence.

## Load References

1. Read `references/reuse-first-policy.md`.
2. Read `references/minimal-scenario-template.md`.
3. Read `references/mud-battle-log-spec.md`.
4. Read `references/test-path-visualization-spec.md`.
5. If the feature changes UI, HUD, overlay, or showcase presentation, read `references/showcase-ui-acceptance-checklist.md`.

## Mandatory Delivery Rules

1. Reuse infrastructure first.
- Reuse existing core systems, registries, queues, and config pipelines.
- Do not create one-off mod-only engines or parallel runtime stacks.
- For input/order/camera work, keep render-frame sampling (`PlayerInputHandler`) separate from fixed-step authoritative consumption (`CoreServiceKeys.AuthoritativeInput`), and keep camera transitions inside `CameraManager` / `VirtualCameraBrain`, not presenter/adapters.

2. Ship headless end-to-end acceptance evidence.
- Produce deterministic headless E2E output for the target feature.
- Output a readable gameplay acceptance log in MUD battle-report style.

3. Ship visualized test path evidence.
- Provide a minimal scenario.
- Output visual path artifacts describing state transitions and key branches.

4. Escalate cross-layer defects immediately.
- If upper-layer implementation reveals lower-layer defects, do not hide or bypass.
- Trigger technical debt escalation and fuse decision workflow.
- Use `ludots-tech-debt-fuse`.

5. Verify wrapper-script invocation against implementation.
- Before publishing or running repository wrapper commands, inspect the wrapper script and its receiving entrypoint.
- Do not invent separators or shim arguments unless the wrapper explicitly requires them.
- For Ludots `scripts/run-mod-launcher.cmd`, the canonical form is `.\scripts\run-mod-launcher.cmd cli ...`, not `.\scripts\run-mod-launcher.cmd -- cli ...`.
- For mod-specific launches, verify the exe-adjacent `game.json` or equivalent runtime artifact after `gamejson write`; process spawn alone is not acceptance evidence.

6. For UI and showcase work, prove player-visible correctness.
- Identify the owning UI surface and any takeover / restore path.
- Require first-frame readability, bounds safety, and world-click safety.
- Do not stop at engine-side correctness; collect adapter-visible evidence.

## Workflow

1. Define feature scenario card.
- Describe player intent, key actions, expected game outcomes.
- Define deterministic seed, map, entities, and clocks.

2. Design with reuse-first policy.
- Identify existing modules to reuse.
- Record any new extension points and why reuse is insufficient.

3. Build minimal scenario.
- Create the smallest runnable setup that still expresses the gameplay behavior.
- Keep scenario independent from unrelated mod content.

4. Implement and wire tests.
- Add or extend headless E2E tests for the scenario.
- Ensure outputs include event trace and acceptance narrative.

5. Generate acceptance artifacts.
- `artifacts/acceptance/<feature>/battle-report.md`
- `artifacts/acceptance/<feature>/trace.jsonl`
- `artifacts/acceptance/<feature>/path.mmd`

6. Run UI/showcase acceptance checks when applicable.
- Apply `references/showcase-ui-acceptance-checklist.md`.
- Record the owning surface, takeover contract, and any temporary mitigation.

7. Run gate checks.
- Confirm expected gameplay outcomes in battle report.
- Confirm visual path covers core happy path + failure branch.
- Confirm no ad-hoc runtime duplication was introduced.

## Output Requirements

For each feature delivery, provide:
- changed files summary
- minimal scenario definition
- headless E2E execution evidence
- MUD-style acceptance report
- visual path artifact
- UI/showcase acceptance evidence when presentation behavior changed
- open technical debt list (if any)


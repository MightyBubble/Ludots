# Ludots Feature Delivery

Use this skill for Ludots feature work that must reuse existing infrastructure and ship acceptance evidence.

## Load

- `references/reuse-first-policy.md`
- `references/minimal-scenario-template.md`
- `references/mud-battle-log-spec.md`
- `references/test-path-visualization-spec.md`
- `references/showcase-ui-acceptance-checklist.md` when UI or showcase behavior changes

## Rules

- Reuse registries, pipelines, and systems before adding new ones.
- Produce deterministic headless acceptance evidence.
- Request visual capture when UI or visual behavior matters.
- Verify surface ownership, first-frame readability, and interaction safety for showcase/UI work.
- Escalate lower-layer defects with `ludots-tech-debt-fuse`.

## Outputs

- `artifacts/acceptance/<feature>/battle-report.md`
- `artifacts/acceptance/<feature>/trace.jsonl`
- `artifacts/acceptance/<feature>/path.mmd`

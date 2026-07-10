# Ludots GAS Composition Gate

**Mandatory before any GAS entity lifecycle, morph/spawn, effect preset, or graph op implementation.**

## Load

- `references/composition-judgment-standard.md`
- `references/self-review-checklist.md`
- `references/layer-model.md`
- `gitbook/architecture/entity-lifecycle-atomic-ops.md`

## Rules

- New variant = graph node / effect step, **not** profile enum or preset switch.
- Do not extend `morph_profiles.json` or similar DSL.
- Output `artifacts/gas-composition-gate.md` with filled checklist before coding.

## Hard stop

If the answer to the core judgment question is "new profile field / inherit mode" → do not implement; design atomic ops or open refactor issue.

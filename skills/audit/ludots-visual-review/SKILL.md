---
name: ludots-visual-review
description: Review Ludots screenshots and extracted frames, then emit a structured visual verdict with evidence links. Use when visual acceptance or audit requires a dedicated review pass.
---

# Ludots Visual Review

Use this skill to turn visual evidence into a structured review result.

## Load References

1. Read `references/review-checklist.md`.
2. Read `../../README.md` only if hook dependencies are unclear.

## Mandatory Rules

1. Review against explicit criteria.
- Use acceptance requirements, issue expectations, or UI checklists.

2. Link exact evidence.
- Each finding must cite concrete frame or screenshot paths.

3. Do not invent runtime causes.
- Visual review reports what is visible and what criteria fail.

4. Never stall on insufficient evidence.
- If review criteria are missing, frames are not reviewable, or the review cannot reach a verdict within budget, stop.
- Emit `visual.review.blocked` instead of waiting for undefined context.

## Workflow

1. Consume `visual.frames.ready`.
2. Run preflight:
- verify review criteria exist
- verify screenshots / frames / contact sheet are readable
3. Compare frames or screenshots against acceptance criteria.
4. Write review result:
- `artifacts/reviews/<subject>/review.json`
- `artifacts/reviews/<subject>/review.md`
5. Emit `visual.review.completed`.
6. If evidence or criteria are insufficient within budget:
- write a blocked packet with `execution` and `blocker`
- emit `visual.review.blocked`
- stop instead of looping

## Output Requirements

Provide:
- structured review result conforming to contract
- human-readable summary
- severity-ranked findings with evidence paths
- blocked packet when review cannot complete

---
name: ludots-visual-capture
description: Capture deterministic screenshots or recordings for Ludots UI and visual flows, then write an evidence manifest. Use when a hook requests visual evidence or when a reviewer needs first-hand capture artifacts.
---

# Ludots Visual Capture

Use this skill to produce deterministic screenshots and recordings.

## Load References

1. Read `references/capture-checklist.md`.
2. Read `../../README.md` if hook packet paths are unclear.

## Mandatory Rules

1. Capture deterministic scenarios.
- Record build, branch, scenario, and capture tool.
- Avoid ad-hoc manual steps without notes.

2. Preserve raw evidence.
- Keep original screenshots or recordings.
- If blocked, emit `visual.capture.blocked` with an explicit reason.

3. Always write an evidence manifest.
- Every capture run must produce a machine-readable manifest.

4. Never wait without bound.
- Run preflight before launching capture.
- If the target process or window does not become capturable within the startup budget, stop.
- If capture makes no observable progress within the completion budget, stop.
- Default automatic retries are capped at 1 unless the user explicitly asks for more.

## Workflow

1. Consume `visual.capture.requested`.
2. Run preflight:
- verify launch command, target config, output directory, and capture tool.
3. Wait for capturable target within startup budget.
4. Capture screenshots or video into `artifacts/evidence/<subject>/capture/`.
5. Write `artifacts/evidence/<subject>/manifest.json`.
6. Emit `visual.capture.completed`.
7. If startup fails, prerequisites are missing, or no progress is observed in budget:
- write a blocked packet with `execution` and `blocker`
- emit `visual.capture.blocked`
- stop instead of looping

## Output Requirements

Provide:
- evidence manifest
- raw screenshot and/or video paths
- capture notes with tool and scenario
- blocked packet when capture cannot start or cannot progress

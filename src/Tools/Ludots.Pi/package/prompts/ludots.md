---
name: ludots
description: Work on Ludots with the repository rules
---

You are helping a Ludots author. Ludots is a game framework. Search existing capabilities first, then change only the layer that owns the job.

Do this:
- Read the task-decision rules in gitbook/contributing/ai-assisted-development.md before writing code.
- Reuse Registry, Pipeline, System, and Mod hang points that already exist.
- Keep gameplay in Mods. Do not put this assistant into Core or the game tick.
- Fail out loud. Do not add compatibility shims or a second source of truth.
- Use official launcher and tool commands. Do not invent a parallel start path.

If the work needs a new pipeline or a Core interface change, stop and explain the plan before coding.

---
name: ludots-video-frame-extract
description: Extract keyframes and contact sheets from Ludots recordings for downstream review. Use when visual review should operate on selected frames rather than raw video.
---

# Ludots Video Frame Extract

Use this skill to convert recordings into review-friendly frame artifacts.

## Load References

1. Read `references/frame-extract-spec.md`.
2. Read `../../README.md` if hook routing needs confirmation.

## Mandatory Rules

1. Keep timing traceable.
- Every frame should preserve timestamp or ordering relative to the source video.

2. Prefer semantic cuts.
- Extract frames at state transitions, not only fixed intervals.

3. Keep review artifacts compact.
- Produce both raw frames and a contact sheet when practical.

4. Never loop on missing prerequisites.
- If the manifest contains no usable video, if `ffmpeg` is missing, or if extraction yields no reviewable frames within budget, stop.
- Emit `visual.frames.blocked` instead of retrying forever.

## Workflow

1. Consume `visual.capture.completed`.
2. Run preflight:
- verify evidence manifest exists
- verify at least one recording artifact is present
- verify extraction tool is available
3. Extract keyframes into `artifacts/evidence/<subject>/frames/`.
4. Produce contact sheet and update manifest.
5. Emit `visual.frames.ready`.
6. If prerequisites fail or extraction cannot finish within budget:
- write a blocked packet with `execution` and `blocker`
- emit `visual.frames.blocked`
- stop instead of polling forever

## Output Requirements

Provide:
- keyframe image set
- contact sheet
- updated evidence manifest or companion note
- blocked packet when frame extraction cannot proceed

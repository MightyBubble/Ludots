# Frame Extract Spec

Recommended outputs:

- `artifacts/evidence/<subject>/frames/frame-0001.png`
- `artifacts/evidence/<subject>/contact-sheet.png`
- updated or companion manifest entry with timestamps

Extraction guidance:

- cover initial state, first meaningful transition, primary success state, failure state if present
- store timestamp or sequence index per frame
- define extraction timeout and maximum retry budget before starting
- if no usable video, no frames, or no progress in budget, emit `visual.frames.blocked`
- blocked packets must include `execution` and `blocker`
- emit `visual.frames.ready` after artifacts are written

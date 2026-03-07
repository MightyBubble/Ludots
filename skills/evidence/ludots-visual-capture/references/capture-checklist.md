# Capture Checklist

- confirm subject key and scenario id
- record branch and commit when available
- record capture tool and window context
- preserve raw screenshots or raw recording
- define startup timeout, completion timeout, poll interval, and max retries
- if the target never becomes capturable or no progress is observed, emit `visual.capture.blocked`
- write `manifest.json` using `skills/contracts/evidence-manifest.schema.json`
- blocked packets must include `execution` and `blocker`
- emit `visual.capture.completed` or `visual.capture.blocked`

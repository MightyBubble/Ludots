# Visual Review Checklist

- confirm subject and acceptance target
- load screenshots, frames, and contact sheet
- verify review criteria exist before starting
- compare state progression against expected path
- classify findings by severity
- link exact evidence paths in every finding
- if evidence or criteria are insufficient within budget, emit `visual.review.blocked`
- blocked packets must include `execution` and `blocker`
- write `review.json` using `skills/contracts/review-result.schema.json`
- emit `visual.review.completed` or `visual.review.blocked`

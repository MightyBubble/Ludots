# Triage Labels

Map skill roles only to labels that already exist in `MightyBubble/Ludots`.
Do not create labels implicitly.

| Skill role | Repository label | Meaning |
| --- | --- | --- |
| `needs-triage` | `question` | Maintainer evaluation or clarification is required |
| `needs-info` | `question` | Reporter information is required |
| `ready-for-agent` | `ready-for-agent` | Fully specified and safe for an AFK agent |
| `ready-for-human` | `help wanted` | Human attention or implementation is required |
| `wontfix` | `wontfix` | Will not be actioned |

Implementation issues produced for an autonomous coding agent should use
`ready-for-agent` only after their acceptance criteria and dependencies are complete.


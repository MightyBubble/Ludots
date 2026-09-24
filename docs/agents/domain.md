# Domain Docs

Ludots is a single-context repository. It currently has no root `CONTEXT.md`;
use the following sources instead of inventing parallel terminology or decisions.

## Read before implementation

- Root `AGENTS.md` or `CLAUDE.md` for repository-wide constraints.
- `gitbook/contributing/ai-assisted-development.md` for task execution rules.
- Relevant pages under `gitbook/architecture/` for the formal architecture contract.
- Relevant ADRs under `docs/adr/` for accepted decisions.
- Relevant RFCs under `docs/rfcs/` as supporting history; formal GitBook pages win on conflict.

Entity Association Core is an exception: GitHub issues #239 and #244 are its plan and ADR SSOT.
Do not create a parallel AAC ADR under `docs/adr/`.

## Consumer rules

- Use existing domain terms from the formal architecture pages.
- Surface conflicts between implementation, GitBook contracts, RFCs, and ADRs explicitly.
- Do not treat showcases or tests as the architecture SSOT when they contradict formal docs.
- Keep capability-specific behavior in Mods and shared mechanisms in Core.

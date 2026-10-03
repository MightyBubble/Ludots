# Ludots Design Constitution

Use this skill when designing a new Ludots subsystem, writing a 面向新人的设计文档, or reviewing configuration structure. It enforces the **general** design decisions, architecture red lines, and doc discipline that apply to any subsystem (GAS, UI, network, input, ...).

## Load

- `references/architecture-principles.md` — SSOT, no fallback, reuse infrastructure, layering, structure-over-discipline, mechanism-is-code/content-is-config, minimal contract.
- `references/design-decision-rules.md` — first principles, split concepts, reuse infrastructure, cut redundant config, simple authoring, run one case first, accurate naming.
- `references/doc-discipline.md` — newbie-facing docs, real config structures, anti-pattern list.

## Workflow

1. Start from first principles with a concrete scenario; do not start from existing code.
2. Split concepts that mix domains; keep each concept single-purpose.
3. Reuse existing infrastructure; do not invent parallel systems.
4. Cut any config that can be derived from existing config (one file = one SSOT).
5. Keep authoring simple: scan-and-compile at init, one-line author declarations.
6. Run one case end-to-end before generalizing.

## Red lines

- SSOT single; no fallback/silent degrade; no wheel reinvention.
- Layering: overrides/prefs bind to a base; do not put "original" and "patch" on the same layer.
- Structure guarantees invariants (containers auto-clean, compile-time purity), not author discipline.
- Mechanism is code; content is config. Authoring shape explicit.
- Minimal contract: cross-boundary events/interfaces carry only necessary facts.

## Anti-patterns (check before output)

- 此地无银三百两 (explaining what does not exist)
- 壮胆文学 / meta-narration (explaining how you will write)
- 假设上下文 (assumes reader was in the discussion)
- Being pulled by existing code ("提议/真实字段")
- Misleading names
- Treating a subsystem's conclusion as a general principle

## Output

- Design doc: follow `references/doc-discipline.md`.
- Config structure: real fields + example + explicit mechanism/content split.
- Review: use the self-check list in `references/doc-discipline.md`.

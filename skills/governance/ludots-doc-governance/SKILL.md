---
name: ludots-doc-governance
description: Enforce Ludots documentation governance, including SSOT structure, path integrity, evidence links, and document-type compliance. Use when creating or updating docs, fixing broken references, running design-code alignment checks, or preparing documentation for merge.
---

# Ludots Doc Governance

Use this skill to keep Ludots documentation consistent, traceable, and auditable.

## Load References

1. Read `references/doc-governance-checklist.md`.
2. Read `references/link-validation.md`.
3. Read `references/report-template.md` before writing the final output.

## Workflow

1. Scope the review.
- Include: `docs/**/*.md` except `docs/**/07_*/**` and `docs/external/**`.
- Exclude: third-party external material and generated binary assets.

2. Validate structure and SSOT consistency.
- Ensure each subsystem has one entry document (for example `00_*.md`).
- Ensure referenced SSOT entry paths exist and are current.
- Flag stale migration paths that still point to removed locations.

3. Validate path integrity.
- Validate markdown links and backtick code-path references.
- Prefer repository-relative paths only (`docs/...`, `src/...`, `assets/...`).
- Flag absolute local paths and missing targets.
- For CLI or launcher docs, validate command examples against the actual wrapper scripts and receiving entrypoints.
- Flag synthetic separators, missing prerequisite steps, or examples that do not match the canonical wrapper invocation.

4. Validate doc-type contract.
- For each document type, verify required sections and expected evidence style.
- Ensure claims are backed by concrete code/doc/test paths.

5. Emit governance report.
- Use the template in `references/report-template.md`.
- Group findings by severity and include exact file references.

## Output Requirements

Produce:
- `artifacts/doc-governance-report.md`
- Optional raw machine-readable path check output (for large runs):
  - `artifacts/doc-governance-missing-paths.tsv`

The report must include:
- scope
- rule set used
- severity-ranked findings
- fix order

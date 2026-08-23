# Issue #1087 Final Audit

Date: 2026-08-24
Branch: `codex/issue1087-entity-history`

## Independent Review Seats

- PI Opus 5: invoked read-only; the session reported no registered file/shell tools and produced no evidence-backed review.
- PI DeepSeek v4 Flash: invoked read-only; the session emitted an unavailable tool call and produced no evidence-backed review.
- Result: neither seat is marked pass. The implementation is reviewed against the repository and targeted tests below.

## Findings

### P1-01 Real Showcase acceptance is blocked by launcher restore

- `scripts/run-mod-launcher.cmd cli launch preset:effect_history_raylib --adapter raylib` reaches launcher build, then fails with NuGet `Value cannot be null (Parameter 'path1')` in existing ExCSS/DotRecast/HtmlEngine projects.
- No Agent Bridge `/health` evidence was recorded; the delivery must not be described as real-play complete.

### P1-02 Existing delayed-effect and delivery consumers still carry raw Entity fields

- `GameplayEffectFactory` now has an overload that stores `EffectTargetRef` in `EffectContext`, but existing `EffectRequest`, `ProjectileState`, and delivery systems still use their legacy `Entity` fields.
- This commit provides the generic contract and an adoption boundary; full migration of every delayed consumer remains a follow-up slice.

### P2-01 Snapshot capture requires a registered reader

- `EntitySnapshotCapture` centralizes the Arch destruction event, but the caller must supply the component reader and instantiate the capture service for a World.
- No silent fallback is performed; a reader failure is currently an observable absence only through the caller's `TryCapture` result.

## Passing Evidence

- Core build: 0 errors.
- Effect History Showcase build: 0 errors; one existing obsolete API warning at the mod event bridge.
- EntityHistory targeted tests: 4/4 passed.
- `git diff --check`: passed.
- Core contracts contain no Combat/Damage/Kill/Missile/BattleReport domain types.

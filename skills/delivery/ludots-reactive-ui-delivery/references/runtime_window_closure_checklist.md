# Runtime Window Closure Checklist

## 1 Ownership

- Is `RefreshRuntimeDependencies()` owned by a generic lifecycle, or by a documented caller?
- Is there exactly one owner for each mounted reactive page?
- Does the change avoid fixture-private lifecycle assumptions?

## 2 Observability

- Can tests read `UiReactiveUpdateMetrics.Reason`?
- Can tests or diagnostics read `UiScene.TryGetVirtualWindow(...)`?
- Can the change prove incremental patch vs full remount?

## 3 Scope Boundaries

- Does the change stay inside `Ludots.UI` plus the owning mod/UI layer?
- If adapter behavior is implicated, was it escalated instead of silently absorbed?
- Does the change avoid introducing another reconciler or host-only UI contract?

## 4 Documentation

- Current implemented contract updated in `docs/reference/` or `docs/architecture/`
- Future closure targets or unimplemented designs updated in `docs/rfcs/`
- Index docs updated for any new files

## 5 Evidence

- Code paths linked
- Test paths linked
- Issue / PR packet updated when reviewer communication is in scope

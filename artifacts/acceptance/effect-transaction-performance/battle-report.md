# Effect Transaction Performance Acceptance

## Header

- Scenario: 10k distinct entities, one persistent effect per entity
- Build: Release/net9.0/x64
- Base: `81a1b5f543`
- Clock: fixed frame

## Timeline

- `[T+000]` Create 10,000 targets and 10,000 effects; all effects remain committed.
- `[T+016]` Scan with period and expiration far in the future; all entities remain valid; median `0.239 ms`, P95 `0.244 ms`, frame allocation `0`.
- `[T+032]` Stage/read/commit all effect states; median `1.826 ms`, P95 `1.876 ms`, allocation `0`.
- `[T+048]` Abort an unfinished expiry slice; attributes, tags, listeners, timers, entities, and event buffer return to their pre-slice state.
- `[T+064]` Complete the same work in slices of `2500`, `137`, and `1`; cleanup and event order match a single pass.

## Outcome

- Success: 61 existing transaction/lifecycle/attachment/allocation checks and 7 index-specific checks pass.
- Guard: capacity overflow, missing service, invalid entity, and stale entity version continue to throw explicitly.
- Remaining acceptance: whole-window GpuSkinned FPS requires a persistent interactive desktop run; it is not claimed by this headless evidence.

## Summary Stats

- Performance regression checks: pass
- Semantic checks: pass
- Dropped events or silent overflow: 0
- Frame allocations in steady state: 0


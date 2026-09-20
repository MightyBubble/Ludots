# Presenter Allocation Comparison

Date: 2026-08-24

## Scope

This evidence compares the Presenter retained/static lane benchmark at the exact
`origin/main` baseline and the PR head. It measures the same 10,000 Presenter
scenario used by `PresenterSubtypePerformanceBenchmarkTests`:

- baseline: `origin/main` at `a45452f2284a6cdc5383b334477fa21e46627925`
- PR: `codex/issue-1092-deepseek` at `547173e3b135c2c28680aad45f73b5ad0346274f`
- test: `src/Tests/PresentationTests/Presenter/PresenterSubtypePerformanceBenchmarkTests.cs`
- command: `dotnet test src/Tests/PresentationTests/PresentationTests.csproj --filter FullyQualifiedName~PresenterSubtypePerformanceBenchmarkTests`

The allocation counters were added only in the temporary benchmark harness. No
production source was changed for this measurement. Each side was run five times
after a full build, with a full GC before the creation phase. Allocation was read
with `GC.GetAllocatedBytesForCurrentThread()` around creation, first Emit, and
the 60-frame steady-state loop.

## Results

All five runs produced the same allocation counts on both commits:

| Phase | `origin/main` | PR | Delta |
|---|---:|---:|---:|
| Create 10,000 presenters | 21,923,528 bytes | 21,923,528 bytes | 0 |
| First Emit | 7,469,176 bytes | 7,469,176 bytes | 0 |
| Steady-state, 60 frames | 88 bytes | 88 bytes | 0 |
| Steady-state per frame | 1.47 bytes | 1.47 bytes | 0 |

The behavioral benchmark contract also passed on every run:

- first-frame requests: `6000`
- stable cache entries: `4000`
- steady-state requests per frame: `0`
- stable cache range: `4000 / 4000`

## Conclusion

This comparison finds no managed-allocation regression in the Presenter path
covered by the benchmark. In particular, the PR does not add a per-frame
allocation increase in the unchanged steady state.

## Boundary

`GC.GetAllocatedBytesForCurrentThread()` covers managed allocations on the
benchmark thread. It does not measure allocations on worker threads or native
renderer memory. A renderer-wide allocation budget requires a separate adapter
benchmark and is outside this PR's Presenter authoring change.

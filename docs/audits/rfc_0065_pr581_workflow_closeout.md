# RFC-0065 PR581 Workflow Closeout

Date: 2026-07-06

Scope: PR #581 follow-up review against `main`, latest GitHub PR reviews, `docs/audits/rfc-0065-implementation-handoff.md`, and `docs/rfcs/RFC-0065-unified-interaction-collection-casting-architecture.md`.

## Executive Status

PR581 is not at the RFC-0065 terminal state.

| Question | Status | Evidence |
|---|---|---|
| Are all follow-up TODOs done? | No | Workflow B1 is completed in this closeout; Workflow A visible UAT/showcases and Workflow C migrations still have explicit blockers. |
| Are all UAT/showcases done? | No | A1-A4 still require Windows raylib visible runs and recordings; A4 also depends on C2. |
| Is Selection retired? | No | RFC-0065 now states Selection retirement is a terminal goal, while PR581 remains dual-track. `SelectionRuntime` is still formal selection SSOT during this transition. |

Latest PR review checked on 2026-07-06:

| Review time UTC | Commit | Review status used by this closeout |
|---|---|---|
| 2026-07-06 02:52 | `dc3c1758a8f2dddbb360dc85b58204fc707c3641` | Request-changes-equivalent comment: fail-fast, control-plane, loader, knowledge gate, and benchmark concerns. |
| 2026-07-06 04:26 | `dc62547c047e0d5c2351f7883f15f66d38a3bbbb` | Request-changes-equivalent comment: multi-profile grants, multi-writer domain semantics, partial projection, association churn, reverse-index wrapper, and Team/PlayerOwner sequencing. |
| 2026-07-06 07:15 | `132d742563a2358e72f42a07c7108405701005f3` | Request-changes-equivalent comment: benchmark hardening still missing, old Selection target path still present, and partial-domain budget not fixed in repo tests. |
| 2026-07-06 07:33 | `132d742563a2358e72f42a07c7108405701005f3` | Supplemental audit: Selection is not retired; PR581 is a dual-track transition. |

## Workflow A - Visible UAT And Showcase

Current status: blocked for full completion in this environment.

Reason: A1-A4 require a Windows raylib visible run and recordings. A1 also needs a Ludots CEF WebUI panel check. This headless audit environment can run contract/headless tests, but cannot replace visible marker/color/input-handling UAT or produce the required recordings.

| Item | Current status | Remaining work |
|---|---|---|
| A1 control-plane projection | Headless path, launcher binding, mod/map, O-key toggle, DataPlane topic/command, and tests exist. | Convert marker performer rules to the final topology graph-rule path, add CEF WebApp panel, and record raylib+CEF UAT. |
| A2 command panel aggregation | Registry, source API, by-family fragment, host mod, and tests exist. | Add runtime UI profile switching and M6/P3 showcase recordings. |
| A3 superweapon context | CTX-6/7 and context-bound collection kernel tests exist. | Build a showcase mod with ability data, targeting collection, indicator performer, IMC switching, headless acceptance, and visible recording. |
| A4 pointer intent/dispatch/scheme | CommandIntent, CastDispatch, ControlScheme, AxisMove kernel and assets exist. | Blocked by C2 production input-chain wiring and PR #535/#577 arbitration; then build playable showcase and recording. |

Validated headless evidence from the showcase explorer:

- `ControlPlaneProjectionDataPlaneTests`: 4/4 passed.
- GasTests Release filtered A1/A2/A3/A4 kernel subsets: A1 8, A2 13, A3 25, A4 46 passed.

## Workflow B - Benchmark Hardening

Current status: B1 completed in repository tests; B2 remains environment-blocked.

This closeout hardens the missing B1 probes as repo tests:

| Probe | Test file | Added or confirmed guard |
|---|---|---|
| Reverse index CopyIncoming specific/any and runtime CollectIncoming wrapper | `src/Tests/GasTests/RelationshipReverseIndexTests.cs` | Already present before this closeout: `bench.reverse_index_*`, `bench.relationship_runtime_collect_*`, ns/source budgets, and 0Alloc assertions. |
| Partial-domain projection | `src/Tests/GasTests/ControlPlaneViewUnitGrantTests.cs` | Upgraded to 50,000 foreign rows, 64 grants, ns/foreign-row budget, bench output, and 0Alloc assertion. |
| Domain-routed ReplaceRouted flatness | `src/Tests/GasTests/DomainRoutedCollectionTests.cs` | Upgraded to 64 domains, 12,288 rows, single-domain comparison, flatness ratio, ns/row budget, bench output, and 0Alloc assertion. |
| AssociationControlProfile single-rep tag flip | `src/Tests/GasTests/AssociationControlProfileTests.cs` | Upgraded from allocation-only to elapsed, evaluated-pair, bench output, and 0Alloc assertions. |
| AssociationControlProfile physical grant/revoke churn | `src/Tests/GasTests/AssociationControlProfileTests.cs` | Already present before this closeout: real Controls EnsureLink/RemoveLink churn after warmup, evaluated-pair bound, elapsed budget, and 0Alloc assertion. |

Latest local validation:

```text
dotnet test src/Tests/GasTests/GasTests.csproj --no-build --filter "FullyQualifiedName~RelationshipReverseIndexTests|FullyQualifiedName~ControlPlaneViewUnitGrantTests|FullyQualifiedName~DomainRoutedCollectionTests|FullyQualifiedName~AssociationControlProfileTests" --logger "console;verbosity=normal"

Passed: 41/41
bench.association_profile_tag_flip reps=64 profiles=4 toggle_cycles=64 elapsed_ms=78.28 ms_per_cycle=1.223 alloc_bytes=0 evaluated_pairs=16128
bench.association_profile_churn reps=64 toggle_cycles=32 elapsed_ms=25.21 ms_per_cycle=0.788 alloc_bytes=0 evaluated_pairs=8064
bench.control_plane_partial_view foreign_rows=50000 direct_grants=64 returned=65 iterations=80 elapsed_ms=357.75 ns_per_foreign_row=89.4 alloc_bytes=0
bench.domain_routed_write_single domains=1 rows=12288 iterations=32 elapsed_ms=253.72 ms_per_write=7.929 ns_per_row=645.3 alloc_bytes=0
bench.domain_routed_write domains=64 rows=12288 iterations=32 elapsed_ms=221.58 ms_per_write=6.924 ns_per_row=563.5 alloc_bytes=0 flatness_ratio_vs_single=0.87
bench.reverse_index_specific sources=4096 iterations=400 elapsed_ms=89.39 ns_per_source=54.6 alloc_bytes=0
bench.reverse_index_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=120.84 ns_per_source=73.8 alloc_bytes=0
bench.relationship_runtime_collect_specific sources=4096 iterations=400 elapsed_ms=90.18 ns_per_source=55.0 alloc_bytes=0
bench.relationship_runtime_collect_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=100.76 ns_per_source=61.5 alloc_bytes=0
```

B2 is still open because the handoff explicitly asks for rerun in a stable, non-current-VM performance environment. This closeout records a local pass, not a replacement for that stable-environment acceptance.

## Workflow C - Migration Work

Current status: discovery complete; broad migrations intentionally not performed in PR581.

| Item | Status | Reason |
|---|---|---|
| C1 CTRL-3 consumer migration and embodied `PlayerOwner`/`Team` deletion | Not safe in PR581 | Actual consumers span GAS targeting, projectile hit checks, queries, AI predicates, input/Selection, presentation, visibility, participant view, MassNavigation, lifecycle, save, and spawn. This must be split into independent breaking PRs, each with post-migration zero-old-component assertions. |
| C2 input main-chain wiring | Blocked | PR #535 and PR #577 are both still OPEN with `mergedAt: null` and `mergeCommit: null`. Human arbitration is required before canonical wiring. |
| C3 presentation/provider follow-ups | Not safe in PR581 | `VisibilityCondition` graph emit still throws on graph visibility; marker/referee/palette still need production wiring plus visible UAT. |
| C4 INT-8, M10, DOC-1 | Deferred by design | Tag/stance knowledge-fact projection is new infrastructure; replay acceptance needs a replay harness; gitbook rewrite waits for RFC acceptance. |

Safe follow-up slices:

- Move `SelectionEligibility.CanAcquire` off `Team` as a narrow headless PR.
- Migrate GAS targeting/projectile/query/AI consumers to `ControlDomainQuery` and `DomainStanceQuery`.
- Migrate participant visibility and ParticipantView to participant bindings and relationship topology.
- Migrate presentation phase/palette after C3 topology and palette contracts are complete.
- Migrate MassNavigation only after command intake and OrderQueue ownership are canonical.

Do not delete `PlayerOwner`, `Team`, `TeamManager`, `SelectionRuntime`, or `InteractionModeType` in PR581 without the corresponding migration and architecture bans.

## Closeout Decision

Completed now:

- Latest PR review rechecked.
- Handoff Workflow A/B/C remapped to completed, blocked, and follow-up buckets.
- B1 benchmark hardening completed for the missing repo tests.
- Selection status corrected: not retired, dual-track transition only.

Still open by dependency:

- A visible UAT/showcase recordings.
- B2 stable performance rerun.
- C1/C2/C3/C4 migrations and RFC/gitbook terminal-state work.

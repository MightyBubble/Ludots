# RFC-0065 PR581 Workflow Closeout

Date: 2026-07-07

Scope: PR #581 follow-up review against `main`, latest GitHub PR reviews, PR head `2417820e9`, `docs/audits/rfc-0065-implementation-handoff.md`, and `docs/rfcs/RFC-0065-unified-interaction-collection-casting-architecture.md`.

## Executive Status

PR581 is not at the RFC-0065 terminal state.

| Question | Status | Evidence |
|---|---|---|
| Are all follow-up TODOs done? | No | A1 full CEF toggle/revoke, A2 WebUI War3 command panel, SHOW-3 GUI marker/palette, A3 timeline, A4 command-source/scheme, A4 blink/mixed-selection UI timeline, B1 benchmarks, B2 current-workstation perf rerun, launcher bindings, and focused acceptance are complete. Terminal RFC work still includes Workflow C migrations, full video files where reviewers require video beyond timeline PNGs, and a dedicated isolated B2 perf host rerun if that stricter gate is required. |
| Are all UAT/showcases done? | Yes for the current framebuffer/timeline UAT pass | A1 has readable CEF toggle/revoke evidence; A2 now has readable WebUI/CEF War3 bottom-panel Template -> Family -> Ability evidence; SHOW-3 has readable GUI referee marker/palette evidence; A3 and A4 have player-readable timelines. Full RFC §6 video recordings remain a separate artifact request if reviewers require video files instead of accepted timeline PNG evidence. |
| Is Selection retired? | No | Selection is not retired repo-wide. PR581 retires Selection from the RFC-0065 ground `Command` authority path and closes the MassNavigation core/input-arbitration coupling, but `SelectionRuntime`, `CurrentSelectionApplySystem`, selection presentation, minimap/UI readers, and legacy skill/cast selected-provider paths remain live. |

Latest PR review checked on 2026-07-06:

| Review time UTC | Commit | Review status used by this closeout |
|---|---|---|
| 2026-07-06 02:52 | `dc3c1758a8f2dddbb360dc85b58204fc707c3641` | Request-changes-equivalent comment: fail-fast, control-plane, loader, knowledge gate, and benchmark concerns. |
| 2026-07-06 04:26 | `dc62547c047e0d5c2351f7883f15f66d38a3bbbb` | Request-changes-equivalent comment: multi-profile grants, multi-writer domain semantics, partial projection, association churn, reverse-index wrapper, and Team/PlayerOwner sequencing. |
| 2026-07-06 07:15 | `132d742563a2358e72f42a07c7108405701005f3` | Request-changes-equivalent comment: benchmark hardening still missing, old Selection target path still present, and partial-domain budget not fixed in repo tests. |
| 2026-07-06 07:33 | `132d742563a2358e72f42a07c7108405701005f3` | Supplemental audit: Selection is not retired; PR581 is a dual-track transition. |
| after latest review | `2417820e9ed225aff3761737f861f234094985d5` | Latest commit folds axis-move into per-scheme `ControlScheme.axisMove`, deletes global `axis_move.json` / `AxisMoveConfig`, and removes the global toggle dual-truth. No submitted review covers this commit yet. |

## Workflow A - Visible UAT And Showcase

Current status: A1 headless/WebApp/DataPlane evidence completed; A1 CEF toggle/revoke framebuffer captured; A2 official launcher binding, WebUI/CEF War3-style bottom command panel, and player-readable Template -> Family -> Ability screenshots captured; A3 official launcher binding and player-readable Raylib timeline screenshots captured; A4 official launcher binding, command-source/scheme evidence, and blink/mixed-selection UI timeline captured; SHOW-3 referee multi-control-domain projection headless evidence and GUI marker/palette evidence completed.

Reason: the current environment can produce real Raylib/CEF framebuffer screenshots and multi-frame Raylib timelines. This pass completes the A1 headless path, launcher binding check, packaged CEF WebApp build, DataPlane topic/command contract, full CEF off -> on -> revoke screenshots, and standard artifacts under `artifacts/acceptance/control-plane-projection-showcase/`. It also completes A2 WebUI/CEF evidence under `artifacts/rfc0065-visible-uat/entity-command-panel/a2_webui_war3_final9_*.png`: the War3-style bottom command panel shows the same three command-source heroes across Template, Family, and Ability aggregation profiles. SHOW-3 referee projection headless evidence under `artifacts/acceptance/rfc0065-referee-projection-showcase/` and GUI evidence under `artifacts/rfc0065-visible-uat/control-plane-projection-cef/show3_player_referee_markers2_*.png` show marked=1/grants=2/total=3, then after the second grant revoke the view shrinks to marked=1/grants=1/total=2 while the outsider row remains excluded. A4 blink/mixed-selection now has a readable UI timeline for All Together / One By One / Nearest Top-N; it is UI timeline evidence, not in-world 3D motion.

| Item | Current status | Remaining work |
|---|---|---|
| A1 control-plane projection | Headless path, launcher binding, packaged CEF WebApp assets, DataPlane topic/command, O-key toggle, profile-owned Controls grant/revoke, standard artifacts, and CEF toggle/revoke screenshots are complete. The captured panel visibly shows Proxy Off -> Proxy On -> Proxy Off/revoke, command acknowledgements, owned/proxy/view counts, and ring shrink. | Keep marker performer topology graph-rule conversion as a separate follow-up if RFC owner still requires the final PROV-4b rule form. |
| SHOW-3 referee projection | Headless referee projection evidence is complete, and GUI marker/palette evidence now shows `SHOW-3 Referee`: phase0=1, phase1=2, foreign excluded=1, then `P2 Revoked` with phase1=1/view=2. | None for GUI marker/palette UAT. |
| A2 / SHOW-4 command panel aggregation | Headless/runtime aggregation evidence exists, and the latest WebUI/CEF War3-style 45/135/225 screenshots pass player readability: Template shows 3 hero sheets x 8 commands = 24 tiles; Family shows 8 shared family tiles with 3 owners each; Ability keeps repeated labels owner-qualified across Arcweaver, Vanguard, and Commander. | None for the current framebuffer/timeline UAT pass. |
| A3 superweapon context | `SuperweaponContextShowcaseMod`, ability-owned interaction frame, target collection routing, confirm IMC path, standard headless artifacts, formal launcher binding `superweapon_context_showcase`, and a real Raylib pending -> complete/restored timeline are complete. | Add video only if terminal closeout requires video instead of timeline PNGs. |
| A4 pointer intent/dispatch/scheme | Formal launcher binding `interaction_showcase`, readable Raylib timeline, and headless production path evidence exist for right-click ground command -> shared moveTo `OrderBuffer`; tests and screenshots show default command mode, hover ignored, active command group rows, and blink dispatch variants over a mixed command group. | Boundary: blink evidence is a readable UI timeline, not animated in-world displacement. |

Validated headless evidence from the showcase explorer:

- `ControlPlaneProjectionDataPlaneTests`: 4/4 passed.
- `ControlPlaneRefereeProjectionShowcase_ProjectsTwoControlDomainsAndShrinksAfterRevoke`: 1/1 passed.
- `EntityCommandPanelShowcaseAcceptanceTests|SuperweaponContextShowcaseAcceptanceTests|Rfc0065ShowcaseWorkflowBoundaryAcceptanceTests`: 67/67 passed in the latest focused closeout filter; `Rfc0065ShowcaseWorkflowBoundaryAcceptanceTests` alone is now 3/3.
- GasTests Release filtered A1/A2/A3/A4 kernel subsets: A1 8, A2 13, A3 25, A4 46 passed.

Latest visible framebuffer evidence captured and cross-checked on 2026-07-08:

| Slice | Binding / selector | Screenshot | Static readability verdict | Boundary |
|---|---|---|---|---|
| A1 / SHOW-2 | `control_plane_projection_showcase` with CEF provider | `artifacts/rfc0065-visible-uat/control-plane-projection-cef/a1_player_command_grant_001_f3000.png`, `a1_player_command_grant_002_f10000.png`, `a1_player_command_grant_003_f15000.png` | PASS: Ally Off -> Ally On -> Ally Off/revoke is readable; Mine/Ally/Total counts change 1/0/1 -> 1/1/2 -> 1/0/1. | Timeline PNGs, not a video file. |
| A2 / SHOW-4 | `entity_command_panel_showcase` WebUI | `artifacts/rfc0065-visible-uat/entity-command-panel/a2_webui_war3_final9_001_f0045.png`, `a2_webui_war3_final9_002_f0135.png`, `a2_webui_war3_final9_003_f0225.png` | PASS: WebUI/CEF War3-style bottom panel is readable; Template shows 24 tiles across three heroes, Family shows 8 shared families with x3 owners, and Ability shows owner-qualified splits. | Timeline PNGs, not a video file. |
| A3 / SHOW-1 | `superweapon_context_showcase` | `artifacts/rfc0065-visible-uat/superweapon-context/a3_superweapon_context_final_001_f0020.png`, `a3_superweapon_context_final_002_f0090.png`, `a3_superweapon_context_final_003_f0180.png` | PASS: first frame shows Superweapon Targeting pending with Arcweaver + Vanguard locked; later frames show Confirmed and targeting restored. | Timeline PNGs, not a video file. |
| A4 / SHOW-5/6 | `interaction_showcase` | `artifacts/rfc0065-visible-uat/interaction/a4_blink_mixed_final_001_f0045.png`, `a4_blink_mixed_final_002_f0135.png`, `a4_blink_mixed_final_003_f0225.png` | PASS with boundary: All Together, One By One, and Nearest Top-N blink routing variants are readable over the same mixed command group. | UI timeline evidence, not in-world 3D motion. |
| SHOW-3 / referee | `control_plane_projection_showcase` with CEF provider | `artifacts/rfc0065-visible-uat/control-plane-projection-cef/show3_player_referee_markers2_001_f0060.png`, `show3_player_referee_markers2_002_f0160.png`, `show3_player_referee_markers2_003_f0300.png` | PASS: Marked=1/Grants=2/Total=3 is readable, then Grant Revoked shrinks grants to 1 and total to 2 while outsiders stay excluded. | Timeline PNGs, not a video file. |

Old `001` and `002` screenshots are not counted as final evidence. A2 `005` / `006`, A3 `004`, A3 `a3_superweapon_context_readable_*`, and A4 `004` were superseded by the Cucumber/player-readability pass. A2 `a2_webui_final_*` was a later but failed waiting-state rerun; it is superseded by the accepted WebUI/CEF War3-style evidence `a2_webui_war3_final9_*`.

Local ignored summary artifact: `artifacts/rfc0065-visible-uat/visible-uat-summary.md`. It is useful operator evidence, but this tracked closeout does not rely on it as PR-tracked proof.

Subagent E A1 rerun on 2026-07-06:

```text
cd mods/showcases/control_plane_projection/ControlPlaneProjectionShowcaseMod/WebApp
npm test

Passed: 4/4 node:test cases.
- standard Ludots facade is the only host transport entrypoint
- missing Ludots facade fails instead of adapting provider globals
- toggleProxy command targets the control plane topic
- production client source does not depend on CEF provider globals
```

```text
cd mods/showcases/control_plane_projection/ControlPlaneProjectionShowcaseMod/WebApp
npm run build

Passed: Vite production build.
Output:
- ../assets/control-plane-app/index.html
- ../assets/control-plane-app/assets/index-B0sADD6R.css
- ../assets/control-plane-app/assets/index-C05TbGjq.js
```

```text
dotnet test src/Tests/WebUiDataPlaneTests/WebUiDataPlaneTests.csproj --filter "FullyQualifiedName~ControlPlaneProjectionDataPlaneTests" --logger "console;verbosity=normal"

Passed: 4/4
```

```text
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~ControlPlaneProjectionShowcaseAcceptanceTests" --logger "console;verbosity=normal"

Passed: 1/1
Note: log confirms this environment has no browser runtime capability, so the dataplane installer stayed inactive instead of pretending a CEF-visible run occurred.
```

SHOW-3 referee projection acceptance on 2026-07-06:

```text
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~ControlPlaneRefereeProjectionShowcase_ProjectsTwoControlDomainsAndShrinksAfterRevoke" --logger "console;verbosity=normal"

Passed: 1/1
Note: headless fixture seeds referee/P1/P2/foreign CommandSource rows, grants Controls(referee->P1Rep/P2Rep), verifies owned=1/proxied=2/foreign=0, revokes Controls(referee->P2Rep), and verifies owned=1/proxied=1/foreign=0.
```

A1 artifacts generated:

- `artifacts/acceptance/control-plane-projection-showcase/battle-report.md`
- `artifacts/acceptance/control-plane-projection-showcase/trace.jsonl`
- `artifacts/acceptance/control-plane-projection-showcase/path.mmd`
- `artifacts/acceptance/control-plane-projection-showcase/visible-checklist.md`

SHOW-4 / Entity command panel rerun on 2026-07-06:

```text
dotnet test src\Tests\GasTests\GasTests.csproj -c Release --filter "FullyQualifiedName~EntityCommandPanelShowcaseAcceptanceTests" --logger "console;verbosity=normal"

Passed: 2/2
Evidence covered: source registry resolves `gas.collection-ability-slots` to `CollectionGasEntityCommandPanelSource`; Core template/ability profiles and EntityCommandPanelMod by-family fragment are installed; EntityCommandPanelShowcaseMod publishes the `collection.command.source` host collection; toolbar runtime switches Family/Template/Ability profiles; copied slots prove 8 by-family groups and 24 identity-profile commands; visible-UAT auto timeline cycles Template -> Family -> Ability.
```

SHOW-4 artifacts generated:

- `artifacts/acceptance/entity-command-panel-showcase/aggregation-profile-report.md`
- `artifacts/acceptance/entity-command-panel-showcase/battle-report.md`
- `artifacts/acceptance/entity-command-panel-showcase/trace.jsonl`
- `artifacts/acceptance/entity-command-panel-showcase/path.mmd`

A2 WebUI/CEF War3 panel rerun on 2026-07-07:

```text
cd mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod/WebApp
npm test

Passed: 3/3
```

```text
cd mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod/WebApp
npm run build

Passed: Vite production build into mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod/assets/entity-command-panel-app/.
```

```text
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~EntityCommandPanelShowcaseAcceptanceTests"

Passed: 2/2
```

```text
LUDOTS_ENTITY_COMMAND_PANEL_AUTO_PROFILE_TIMELINE=1
LUDOTS_AUTO_EXIT_FRAME=260
LUDOTS_TAKE_SCREENSHOT_FRAMES=45,135,225
launch entity_command_panel_showcase --adapter raylib --build auto

Captured:
- artifacts/rfc0065-visible-uat/entity-command-panel/a2_webui_war3_final9_001_f0045.png
- artifacts/rfc0065-visible-uat/entity-command-panel/a2_webui_war3_final9_002_f0135.png
- artifacts/rfc0065-visible-uat/entity-command-panel/a2_webui_war3_final9_003_f0225.png

Note: launcher returned nonzero after capture because Chromium logged `Failed opening key Software\Chromium to set usagestats`; CEF rendered and the screenshots were captured.
```

SHOW-1 / superweapon context rerun on 2026-07-07:

```text
dotnet test src\Tests\GasTests\GasTests.csproj --no-restore --filter "FullyQualifiedName~SuperweaponContextShowcaseAcceptanceTests"

Passed: 3/3
Evidence covered: ability-owned interaction context frame, target collection routing, confirm IMC path, event-gated completion, default-frame restoration, and visible-UAT auto confirm timeline.
```

Visible UAT rerun on 2026-07-08:

```text
LUDOTS_SUPERWEAPON_CONTEXT_AUTO_CONFIRM_FRAME=20
LUDOTS_TAKE_SCREENSHOT_FRAMES=20,90,180
launch superweapon_context_showcase --adapter raylib --build never

Captured and visually accepted:
- artifacts/rfc0065-visible-uat/superweapon-context/a3_superweapon_context_final_001_f0020.png
- artifacts/rfc0065-visible-uat/superweapon-context/a3_superweapon_context_final_002_f0090.png
- artifacts/rfc0065-visible-uat/superweapon-context/a3_superweapon_context_final_003_f0180.png
```

SHOW-5/6 production path rerun on 2026-07-07:

```text
dotnet test src\Tests\GasTests\GasTests.csproj --no-restore --filter "FullyQualifiedName~Rfc0065ShowcaseWorkflowBoundaryAcceptanceTests"

Passed: 3/3
Evidence covered: production startup active `scheme.default`, default command intent, startup command-source collection, hover ambiguity ignored for ground commands, `dispatch.all_together`, shared moveTo order id, and OrderBuffer promotion; visible-UAT default -> WASD scheme timeline; plus hot-switch to `scheme.wasd_move`, WASD `Move` Axis2D input through the authoritative snapshot, and `AxisMoveOrderSystem` moveTo promotion.
```

Visible blink-routing UAT rerun on 2026-07-08:

```text
LUDOTS_INTERACTION_SHOWCASE_AUTO_BLINK_TIMELINE=1
LUDOTS_INTERACTION_SHOWCASE_SEED_HOVER_TARGET=1
LUDOTS_TAKE_SCREENSHOT_FRAMES=45,135,225
launch interaction_showcase --adapter raylib --build never

Captured and visually accepted:
- artifacts/rfc0065-visible-uat/interaction/a4_blink_mixed_final_001_f0045.png
- artifacts/rfc0065-visible-uat/interaction/a4_blink_mixed_final_002_f0135.png
- artifacts/rfc0065-visible-uat/interaction/a4_blink_mixed_final_003_f0225.png
```

SHOW-3 referee projection artifacts generated:

- `artifacts/acceptance/rfc0065-referee-projection-showcase/battle-report.md`
- `artifacts/acceptance/rfc0065-referee-projection-showcase/trace.jsonl`
- `artifacts/acceptance/rfc0065-referee-projection-showcase/path.mmd`

## Workflow B - Benchmark Hardening

Current status: B1 completed in repository tests; B2 current-workstation rerun passed on 2026-07-07 with all reported hot-window allocations at 0 bytes. If the RFC owner requires a dedicated isolated perf host, that external stable-machine rerun remains outside this PR workspace.

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

B2 current-workstation rerun on 2026-07-07:

```text
dotnet test src/Tests/GasTests/GasTests.csproj --no-build --filter "FullyQualifiedName~RelationshipReverseIndexTests|FullyQualifiedName~ControlPlaneViewUnitGrantTests|FullyQualifiedName~DomainRoutedCollectionTests|FullyQualifiedName~AssociationControlProfileTests" --logger "console;verbosity=normal"

Passed: 41/41
bench.association_profile_tag_flip reps=64 profiles=4 toggle_cycles=64 elapsed_ms=32.99 ms_per_cycle=0.516 alloc_bytes=0 evaluated_pairs=16128
bench.association_profile_churn reps=64 toggle_cycles=32 elapsed_ms=11.84 ms_per_cycle=0.370 alloc_bytes=0 evaluated_pairs=8064
bench.control_plane_partial_view foreign_rows=50000 direct_grants=64 returned=65 iterations=80 elapsed_ms=125.30 ns_per_foreign_row=31.3 alloc_bytes=0
bench.domain_routed_write_single domains=1 rows=12288 iterations=32 elapsed_ms=66.07 ms_per_write=2.065 ns_per_row=168.0 alloc_bytes=0
bench.domain_routed_write domains=64 rows=12288 iterations=32 elapsed_ms=85.10 ms_per_write=2.659 ns_per_row=216.4 alloc_bytes=0 flatness_ratio_vs_single=1.29
bench.reverse_index_specific sources=4096 iterations=400 elapsed_ms=33.95 ns_per_source=20.7 alloc_bytes=0
bench.reverse_index_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=38.80 ns_per_source=23.7 alloc_bytes=0
bench.relationship_runtime_collect_specific sources=4096 iterations=400 elapsed_ms=34.20 ns_per_source=20.9 alloc_bytes=0
bench.relationship_runtime_collect_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=33.55 ns_per_source=20.5 alloc_bytes=0
```

```text
dotnet test src/Tests/GasTests/GasTests.csproj -c Release --no-restore --filter "FullyQualifiedName~RelationshipReverseIndexTests|FullyQualifiedName~ControlPlaneViewUnitGrantTests|FullyQualifiedName~DomainRoutedCollectionTests|FullyQualifiedName~AssociationControlProfileTests" --logger "console;verbosity=normal"

Passed: 41/41
bench.association_profile_tag_flip reps=64 profiles=4 toggle_cycles=64 elapsed_ms=21.45 ms_per_cycle=0.335 alloc_bytes=0 evaluated_pairs=16128
bench.association_profile_churn reps=64 toggle_cycles=32 elapsed_ms=8.90 ms_per_cycle=0.278 alloc_bytes=0 evaluated_pairs=8064
bench.control_plane_partial_view foreign_rows=50000 direct_grants=64 returned=65 iterations=80 elapsed_ms=61.99 ns_per_foreign_row=15.5 alloc_bytes=0
bench.domain_routed_write_single domains=1 rows=12288 iterations=32 elapsed_ms=61.88 ms_per_write=1.934 ns_per_row=157.4 alloc_bytes=0
bench.domain_routed_write domains=64 rows=12288 iterations=32 elapsed_ms=78.08 ms_per_write=2.440 ns_per_row=198.6 alloc_bytes=0 flatness_ratio_vs_single=1.26
bench.reverse_index_specific sources=4096 iterations=400 elapsed_ms=16.67 ns_per_source=10.2 alloc_bytes=0
bench.reverse_index_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=21.42 ns_per_source=13.1 alloc_bytes=0
bench.relationship_runtime_collect_specific sources=4096 iterations=400 elapsed_ms=16.70 ns_per_source=10.2 alloc_bytes=0
bench.relationship_runtime_collect_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=23.29 ns_per_source=14.2 alloc_bytes=0
```

Conclusion: the B1 benchmark guards pass on this Windows workstation in both Debug/no-build and Release, and every benchmark line reports `alloc_bytes=0`. The Release rerun is the preferred local number set. This is still not claimed as a dedicated lab-machine stability run if that stricter B2 gate is required by reviewers.

Subagent D rerun on 2026-07-06:

```text
dotnet test src/Tests/GasTests/GasTests.csproj --no-build --filter "FullyQualifiedName~RelationshipReverseIndexTests|FullyQualifiedName~ControlPlaneViewUnitGrantTests|FullyQualifiedName~DomainRoutedCollectionTests|FullyQualifiedName~AssociationControlProfileTests" --logger "console;verbosity=normal"

Passed: 41/41
bench.association_profile_tag_flip reps=64 profiles=4 toggle_cycles=64 elapsed_ms=27.55 ms_per_cycle=0.430 alloc_bytes=0 evaluated_pairs=16128
bench.association_profile_churn reps=64 toggle_cycles=32 elapsed_ms=10.13 ms_per_cycle=0.317 alloc_bytes=0 evaluated_pairs=8064
bench.control_plane_partial_view foreign_rows=50000 direct_grants=64 returned=65 iterations=80 elapsed_ms=137.99 ns_per_foreign_row=34.5 alloc_bytes=0
bench.domain_routed_write_single domains=1 rows=12288 iterations=32 elapsed_ms=77.03 ms_per_write=2.407 ns_per_row=195.9 alloc_bytes=0
bench.domain_routed_write domains=64 rows=12288 iterations=32 elapsed_ms=93.76 ms_per_write=2.930 ns_per_row=238.4 alloc_bytes=0 flatness_ratio_vs_single=1.22
bench.reverse_index_specific sources=4096 iterations=400 elapsed_ms=62.64 ns_per_source=38.2 alloc_bytes=0
bench.reverse_index_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=66.85 ns_per_source=40.8 alloc_bytes=0
bench.relationship_runtime_collect_specific sources=4096 iterations=400 elapsed_ms=73.44 ns_per_source=44.8 alloc_bytes=0
bench.relationship_runtime_collect_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=36.44 ns_per_source=22.2 alloc_bytes=0
```

## Selection Retirement Second Audit - 2026-07-07

Verdict: Selection is **not repo-wide retired**. PR581 has improved the RFC-0065 command-source path, and the final closeout removes the interaction-showcase `LivePrimary` command-source bridge plus MassNavigation core authority reads. The repository is still in a dual-track transition: formal `SelectionRuntime` remains a registered core service and several production systems still read it for view/UI or legacy skill/cast paths.

Search commands used for this pass:

```text
rg -n "Selection|CurrentSelection|Selected|selection fallback|fallback" src/Core/Input/Selection src/Core/Engine/GameEngine.cs mods/CoreInputMod mods/showcases src/Core/MassNavigation src/Core/Presentation src/Core/Gameplay/Camera mods/EntityCommandPanelMod --glob '!**/bin/**' --glob '!**/obj/**'
rg -n "command\.source|command-source|CommandSource|collection\.command\.source|EntityCollectionKeys\.CommandSource|CurrentSelection|SelectionRuntime|LivePrimary|fallback|Selected entity" src/Core/Input/Orders mods/CoreInputMod mods/EntityCommandPanelMod mods/showcases/interaction src/Tests/GasTests --glob '!**/bin/**' --glob '!**/obj/**'
rg -n "Selected|Selection|SelectionContextRuntime|SelectionViewRuntime|LivePrimary" src/Core/Gameplay/Camera src/Core/Presentation/Minimap src/Core/MassNavigation mods/showcases/interaction mods/capabilities/participant_view --glob '!**/bin/**' --glob '!**/obj/**'
```

| Audit question | Finding | Evidence |
|---|---|---|
| Does the RFC-0065 ground `Command` command-source path depend on Selection? | No for the routed `Command` slice when RFC-0065 services are installed. It reads the active interaction frame, `ControlSchemeRuntime`, command intent profiles, dispatch profiles, and `EntityCollectionStore`; it copies actors from the active collection and never calls the selected-provider path. | `src/Core/Input/Orders/InputOrderMappingSystem.cs` routes command actions to `SubmitRfc0065Command`; that method resolves `frame.ActiveCollectionKeyId`, copies actors from `EntityCollectionStore`, routes through `CommandIntentProfileRegistry.RouteGroup`, dispatches through `CastDispatchProfileRegistry.SelectDispatchTargets`, then submits orders. `mods/CoreInputMod/Systems/LocalOrderSourceHelper.cs` fail-fast configures these services instead of silently falling back. |
| Is repo-wide Selection retired? | No. **Selection is not repo-wide retired.** | `src/Core/Engine/GameEngine.cs` still constructs and registers `SelectionRequestQueue`, `SelectionResponseBuffer`, `SelectionRuntime`, `SelectionRuleRegistry`, and `SelectionPresentationEventSystem`. `src/Core/Input/Selection/*` remains active infrastructure. |
| Is the EntityCommandPanel command-source path selection-free? | The aggregation source is command-source based. | `mods/EntityCommandPanelMod/Runtime/CollectionGasEntityCommandPanelSource.cs` resolves `context.TargetEntity + config.CollectionKey` through `EntityCollectionStore`; no `SelectionRuntime` read is present in that source. The showcase host still has to publish `collection.command.source`, but the panel source itself does not use Selection as its command source. |
| Which Selection consumers are still legitimate in the current repo state? | Formal selection acquisition/view/control-group code, presentation readers, camera/minimap readers, participant-view/showcase projections, and legacy skill/cast targeting remain live consumers. They are not proof of retirement; they are allowed only because repo-wide retirement is not complete. | `mods/CoreInputMod/Triggers/InstallCoreInputOnGameStartTrigger.cs`, `mods/CoreInputMod/Systems/SelectedMovePathPresentationSystem.cs`, `mods/CoreInputMod/Systems/SkillBarOverlaySystem.cs`, `mods/CoreInputMod/Systems/SelectionBoxOverlaySystem.cs`, `src/Core/Presentation/Minimap/MinimapRuntime.cs`, `src/Core/Gameplay/Camera/FollowTargets/ViewedSelectionPrimaryFollowTarget.cs`, `src/Core/Gameplay/Camera/FollowTargets/SelectedGroupFollowTarget.cs`, `mods/capabilities/participant_view/**`, and road/camera showcase code. |

Fallback / dual-truth findings:

- `mods/CoreInputMod/Systems/InputInteractionContextAccessor.cs` remains a dual-track adapter: `TryGetSelectedEntity`, `TryGetSelectedEntities`, and `GetControlledActor` prefer the active command-source collection, then fall back to `SelectionRuntime` / local player. This is not used by the routed RFC-0065 ground `Command` slice after `SubmitRfc0065Command` takes over, but it is still an architecture smell for terminal retirement because the provider name hides two truths.
- `src/Core/Input/Orders/InputOrderMappingSystem.cs` still has the selected-entity fallback in `TryBuildOrderSmartCast`. This is legacy skill/cast targeting, not the RFC-0065 ground-command route, but it blocks any claim that selected-provider semantics are retired.
- `mods/showcases/interaction/InteractionShowcaseMod/Runtime/InteractionShowcaseRuntime.cs` no longer projects `SelectionSetKeys.LivePrimary` into `collection.command.source`; the showcase seeds command-source rows directly for the RFC-0065 command path.
- `src/Core/MassNavigation/**` no longer references Selection/CommandSource/InteractionContextStack authority APIs. It consumes explicit `OrderBuffer` move orders; remaining MassNavigation follow-up work is the separate `PlayerOwner`/`Team` domain migration.
- `mods/capabilities/participant_view/**` projects participant membership into `SelectionRuntime.LivePrimary`; minimap/camera/UI readers consume the current selection view. These are valid readers for the current state, but they are not RFC-0065 command-source ownership.

Remaining tasks before anyone can claim repo-wide Selection retirement:

- Split the selected-provider API into explicit command-source-only and selection-backed providers. The ground `Command` route should keep using only `InteractionContextStack + EntityCollectionStore`; legacy skill/cast paths may keep selection-backed providers until their own migration lands.
- Add a focused guard that a `Command` action with missing active command-source collection / missing active intent does not route through `TryGetSelectedEntity`, `TryGetSelectedEntities`, or `SelectionRuntime`.
- Keep the interaction showcase command-source direct-seeding path guarded; do not reintroduce a `LivePrimary -> collection.command.source` bridge.
- Keep MassNavigation on explicit OrderQueue ingestion; do not migrate it to CommandSource or InteractionContext reads.
- Migrate or explicitly scope minimap, camera, participant-view, skill-bar, selection-box, entity-info, and showcase readers before deleting `SelectionRuntime`.
- Only after the above lands, add architecture bans that prevent new `SelectionRuntime` readers outside the remaining allowed selection infrastructure.

Small fixes made during this audit:

- `mods/CoreInputMod/Systems/InputInteractionContextAccessor.cs` no longer uses `EntityCollectionStore.CopyEntities` in the Issue200-guarded input/knowledge consumer path; it reads the active command-source view with `TryGetEntityAt` per row.
- `mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod/Runtime/EntityCommandPanelShowcaseRuntime.cs` makes the aggregation toolbar visible after publishing the showcase `collection.command.source`, restoring the SHOW-4 runtime switch acceptance path.
- `mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod/DataPlane/EntityCommandPanelShowcaseDataPlane.cs` imports the existing GAS `AbilityIdRegistry` instead of relying on an unresolved name.
- `src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs` now fails fast when `exec` is missing, rejects non-object `exec.items[]`, and rejects more than `AbilityExecSpec.MAX_ITEMS` instead of truncating.
- `mods/showcases/superweapon_context/SuperweaponContextShowcaseMod/Runtime/SuperweaponContextShowcaseRuntime.cs` now starts the showcase ability through `OrderQueue.TryEnqueue` with `OrderArgs.I0` instead of hardcoding an order id and calling `OrderBuffer.SetActiveDirect`; cleanup goes through `OrderSubmitter.CancelAll`.
- `src/Libraries/Arch.Extended/Arch.Relationships/Relationship.cs` no longer compiles reflection accessors over `SortedList` private `values` / `version` fields. It keeps the public `SortedList` backing store required by persistence and uses public APIs after the local binary key lookup.

Validation after the second-audit patch:

```text
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter "FullyQualifiedName~Rfc0065InteractionCastingBoundaryContractTests" --logger "console;verbosity=normal"

Passed: 8/8
```

```text
dotnet test src/Tests/GasTests/GasTests.csproj --no-build --filter "FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~InputOrderConvergenceValidationTests|FullyQualifiedName~InteractionSelectionConvergenceTests|FullyQualifiedName~Rfc0065ShowcaseWorkflowBoundaryAcceptanceTests|FullyQualifiedName~EntityCommandPanelShowcaseAcceptanceTests" --logger "console;verbosity=normal"

Passed: 77/77
```

Final PR581 command-authority closeout validation:

```text
dotnet test src/Tests/GasTests/GasTests.csproj --no-restore --filter "FullyQualifiedName~InteractionSelectionConvergenceTests|FullyQualifiedName~InputOrderConvergenceValidationTests|FullyQualifiedName~Rfc0065ShowcaseWorkflowBoundaryAcceptanceTests|FullyQualifiedName~InputOrderContractTests" --logger "console;verbosity=normal"

Passed: 59/59
```

```text
dotnet test src/Tests/PresentationTests/PresentationTests.csproj --no-restore --filter "FullyQualifiedName~MassNavigationLocalCommandInputSystemTests|FullyQualifiedName~MassNavigationRouteExecutionContractTests|FullyQualifiedName~MassNavigationAuthoredAgentBindingIncrementalTests" --logger "console;verbosity=normal"

Passed: 28/28
```

```text
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter "FullyQualifiedName~Rfc0065InteractionCastingBoundaryContractTests" --logger "console;verbosity=normal"

Passed: 10/10
```

```text
rg -n "SelectionRuntime|SelectionSetKeys|LivePrimary|CurrentSelection|EntityCollectionKeys\.CommandSource|InteractionContextStack|OrderSelectionReference|MassNavigationSelectionSync|MassNavigationLocalCommandInputSystem" src/Core/MassNavigation

No matches.
```

Final 2026-07-08 validation after Superweapon OrderQueue and Relationship reflection fixes:

```text
dotnet test src/Tests/WebUiDataPlaneTests/WebUiDataPlaneTests.csproj --no-restore --filter "FullyQualifiedName~ControlPlaneProjectionDataPlaneTests"

Passed: 4/4
```

```text
dotnet test src/Tests/GasTests/GasTests.csproj --no-restore --filter "FullyQualifiedName~ControlPlaneProjectionShowcaseAcceptanceTests|FullyQualifiedName~ControlPlaneRefereeProjectionShowcase_ProjectsTwoControlDomainsAndShrinksAfterRevoke|FullyQualifiedName~EntityCommandPanelShowcaseAcceptanceTests|FullyQualifiedName~SuperweaponContextShowcaseAcceptanceTests|FullyQualifiedName~Rfc0065ShowcaseWorkflowBoundaryAcceptanceTests|FullyQualifiedName~InputOrderContractTests"

Passed: 29/29
```

```text
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter "FullyQualifiedName~Rfc0065InteractionCastingBoundaryContractTests"

Passed: 10/10
```

```text
dotnet test src/Tests/PresentationTests/PresentationTests.csproj --no-restore --filter "FullyQualifiedName~MassNavigationLocalCommandInputSystemTests|FullyQualifiedName~MassNavigationRouteExecutionContractTests|FullyQualifiedName~MassNavigationAuthoredAgentBindingIncrementalTests"

Passed: 28/28
```

```text
DOTNET_ROLL_FORWARD=Major dotnet test src/Libraries/Arch.Extended/Arch.Relationships.Tests/Arch.Relationships.Tests.csproj --no-restore

Passed: 5/5
Note: roll-forward is needed on this workstation because .NET 7 runtime is not installed.
```

```text
git diff --check
scripts/validate-docs.ps1

Passed. `git diff --check` reported only the Windows LF-to-CRLF warning for Relationship.cs.
```

## Workflow C - Migration Work

Current status: discovery complete; broad migrations intentionally not performed in PR581.

| Item | Status | Reason |
|---|---|---|
| C1 CTRL-3 consumer migration and embodied `PlayerOwner`/`Team` deletion | Not safe in PR581 | Actual consumers span GAS targeting, projectile hit checks, queries, AI predicates, input/Selection, presentation, visibility, participant view, MassNavigation, lifecycle, save, and spawn. This must be split into independent breaking PRs, each with post-migration zero-old-component assertions. |
| C2 input main-chain wiring | Partially wired; terminal migration blocked | The RFC-0065 `Command` ground path now resolves through `CommandIntentArbiter` -> `CommandIntentProfileRegistry.RouteGroup` -> `CastDispatchProfileRegistry.SelectDispatchTargets` -> `OrderQueue`, with focused acceptance coverage. The broader skill/cast fan-out path and `InteractionModeType` retirement still depend on PR #535 vs #577 arbitration and must stay as follow-up migration work. |
| C3 presentation/provider follow-ups | Not safe in PR581 | `VisibilityCondition` graph emit still throws on graph visibility; marker/referee/palette still need production wiring plus visible UAT. |
| C4 INT-8, M10, DOC-1 | Deferred by design | Tag/stance knowledge-fact projection is new infrastructure; replay acceptance needs a replay harness; gitbook rewrite waits for RFC acceptance. |

Selection double-check result: not retired repo-wide. Local source audit still finds live production paths through `GameEngine` (`SelectionRuntime` creation/service registration and `SelectionPresentationEventSystem`), `CoreInputMod` selection installers, `InputInteractionContextAccessor`, legacy `InputOrderMappingSystem` selected-provider skill/cast paths, `OrderSelectionReference` infrastructure outside MassNavigation, and minimap/entity-info/Raylib UI readers. The RFC-0065 ground `Command` route and MassNavigation core boundary are now Selection-free command-authority slices, but repo-wide Selection retirement is not complete.

Workflow C source blockers rechecked on 2026-07-07:

- C1 cannot delete embodied `PlayerOwner`/`Team` yet. Live consumers still include `src/Core/Gameplay/GAS/TargetResolverFanOutHelper.cs`, `src/Core/Gameplay/GAS/Systems/ProjectileRuntimeSystem.cs`, `src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs`, `src/Core/Gameplay/GAS/BuiltinHandlers.cs`, `src/Core/Gameplay/Lifecycle/LifecycleSnapshot.cs`, `src/Core/Gameplay/Lifecycle/EntityLifecycleAtomicOps.cs`, `src/Core/NodeLibraries/GASGraph/Host/GasGraphRuntimeApi.cs`, `src/Core/Gameplay/AI/Utility/UtilityAiRuntimeEvaluator.cs`, `src/Core/ParticipantVisibility/DynamicParticipantVisibilityPublisher.cs`, `mods/capabilities/participant_view/ParticipantViewCapabilityMod/Runtime/ParticipantViewProjection.cs`, `mods/CoreInputMod/Systems/InputInteractionContextAccessor.cs`, and the MassNavigation runtime/systems under `src/Core/MassNavigation/`.
- C2 cannot retire `InteractionModeType` in PR581. Live consumers still include `src/Core/Input/Orders/InputOrderMapping.cs`, `src/Core/Input/Orders/InputOrderMappingSystem.cs`, `src/Core/Gameplay/GAS/AbilityDefinitionRegistry.cs`, `src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs`, `mods/CoreInputMod/ViewMode/ViewModeManager.cs`, `mods/EntityCommandPanelMod/Runtime/GasEntityCommandPanelSource.cs`, `mods/EntityCommandPanelMod/UI/EntityCommandPanelController.cs`, and `mods/MobaDemoMod/Systems/MobaLocalOrderSourceSystem.cs`.
- C3 graph visibility emit remains intentionally fail-fast, not wired. `src/Core/Presentation/Systems/PerformerEmitSystem.cs` still throws when `PerformerDefinition.VisibilityCondition.GraphProgramId > 0`; wiring it requires a per-viewer graph visibility contract and visible SHOW-3/palette evidence.
- Selection remains a live view/UI/runtime service, not retired repo-wide. `src/Core/Engine/GameEngine.cs` still constructs/registers `SelectionRuntime` and `SelectionPresentationEventSystem`; `mods/CoreInputMod/Triggers/InstallCoreInputOnGameStartTrigger.cs` and `mods/CoreInputMod/Systems/SelectedMovePathPresentationSystem.cs` still consume formal selection for selection acquisition/presentation. The interaction showcase no longer bridges `LivePrimary` into command-source rows.

Safe follow-up slices:

- Move `SelectionEligibility.CanAcquire` off `Team` as a narrow headless PR.
- Migrate GAS targeting/projectile/query/AI consumers to `ControlDomainQuery` and `DomainStanceQuery`.
- Migrate participant visibility and ParticipantView to participant bindings and relationship topology.
- Migrate presentation phase/palette after C3 topology and palette contracts are complete.
- Continue MassNavigation's separate `PlayerOwner`/`Team` domain migration after command intake and OrderQueue ownership remain guarded; MassNavigation core now consumes explicit move orders and has no Selection/CommandSource/InteractionContextStack authority reads.

Do not delete `PlayerOwner`, `Team`, `TeamManager`, `SelectionRuntime`, or `InteractionModeType` in PR581 without the corresponding migration and architecture bans.

## Closeout Decision

Completed now:

- Latest PR review rechecked.
- Handoff Workflow A/B/C remapped to completed, blocked, and follow-up buckets.
- A1 control-plane projection headless/WebApp/DataPlane evidence completed with standard artifacts; CEF toggle/revoke screenshots captured and player-readable.
- A2/A3/A4 formal launcher bindings added and verified through `run-mod-launcher` resolve.
- A2 WebUI/CEF War3-style bottom command panel captured and player-readable for Template -> Family -> Ability at frames 45/135/225.
- A3 and A4 real Raylib framebuffer timelines captured through the formal binding selectors and cleaned into player-readable Gherkin-style screenshots.
- A3 and A4 headless acceptance artifacts regenerated through tests with launcher binding guards.
- A4 headless acceptance now covers default right-click dispatch, startup CommandSource rows, hover ambiguity, visible-UAT scheme timeline, and `scheme.wasd_move` WASD hot-switch through the production input snapshot/order path; blink/mixed-selection UI timeline screenshots cover all_together, one_by_one, and nearest_top_n.
- SHOW-3 referee multi-control-domain projection headless evidence completed with standard artifacts; GUI marker/palette recording now shows phase0/phase1/foreign exclusion and revoke shrink.
- Selection is retired from the RFC-0065 ground `Command` authority path: configured command routing fail-fasts when partially wired, consumes missing active intent without legacy fallback, resolves actors from the active command-source collection, and does not call selected-provider fallbacks.
- MassNavigation core is decoupled from input arbitration: it consumes explicit `OrderBuffer` move orders, no longer references Selection/CommandSource/InteractionContextStack/`OrderSelectionReference`, and its self-contained move orders encode a null selection reference through the shared `OrderArgs` factory.
- B1 benchmark hardening completed for the missing repo tests.
- B2 current-workstation Debug and Release reruns passed 41/41 with all reported benchmark windows at `alloc_bytes=0`; Release is the preferred local evidence set.
- Selection status corrected and double-checked: not retired, dual-track transition only, with live production consumers still present.

Still open by dependency:

- Full video recordings where required beyond the accepted A1/A2/A3/A4/SHOW-3 timeline PNG evidence.
- Dedicated isolated B2 perf host rerun, if reviewers require more than the current-workstation Debug/Release evidence above.
- C1/C2/C3/C4 migrations and RFC/gitbook terminal-state work.

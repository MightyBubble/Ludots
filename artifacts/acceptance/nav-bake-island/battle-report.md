# Scenario: nav-bake-island

## Header
- scenario name: NavMesh Bake Island production Mass Navigation chain
- build/version: local Release Raylib app from `codex/issue-1402`
- seed/map/clock: seed 1402 / `nav_bake_island` / realtime pacemaker
- execution timestamp: 2026-08-31 (Asia/Shanghai)

## Timeline
- [T+000] Release launcher entered `nav_bake_island` with `LudotsCoreMod`, `CoreInputMod`, `CameraProfilesMod`, `MassNavigationMod`, `NavBakeIslandShowcaseMod`, and `AgentBridgeMod`.
- [T+001] Agent Bridge health checks returned `ok:true`; `pumpCount` advanced from 75 to 77 on the first run and from 437 to 440 on the recorded moving frame.
- [T+002] Entity query returned 65 authored entities: 64 `MassNavigation.Agent` units plus the hotspot marker. Both Azure and Crimson teams used the KayKit soldier Presenter and Animator configuration.
- [T+003] Production nav query projected `(3500, 2000)` onto tile `10,10,0`; path query from `(-4800,100)` to `(3500,2000)` returned `Ok` with travel cost `8514.693cm`.
- [T+004] A canonical `massNavigationMove` order for agent `12` was accepted and became active; HUD showed `moving 1 | routes 1` and `Walking_A`.
- [T+005] The ordered agent moved from approximately `(-4889,141)` to `(3253,0)` and then reached approximately `(3356,-32)`; the order cleared and HUD returned to `moving 0` / `Idle`.
- [T+006] The same canonical order intake was issued to all 32 Azure agents. All 32 submissions were accepted; after one second all 32 had crossed `x > -4000`, and after arrival all 32 were at `x > 3000`.

## Outcome
- success/failure decision: success for the #1402 production showcase path
- failed assertions: none in the #1402 contract and route execution tests
- reason codes: real_heightmap, navmesh_projection_ok, route_ok, kaykit_presenter_loaded, named_animation_loaded, canonical_order_accepted, batch_agents_moved, arrival_returns_idle

## Summary Stats
- total authored agents: 64
- Azure agents batch-ordered: 32
- canonical orders accepted: 32/32
- navmesh tiles: 800
- navigation profiles: 2 (`light`, `heavy`)
- animation states: 2 (`Idle`, `Walking_A`)
- route errors observed by Bridge: 0
- unrelated startup warning: static mesh asset `meshAssetId=7` was skipped; KayKit Mass Navigation units loaded and rendered, so this remains outside #1402.

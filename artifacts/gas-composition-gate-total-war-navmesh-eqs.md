# GAS Composition Gate - Self Review

- **Task / Issue**: Total War Flow self-roster navmesh reachability and debug projection
- **Date**: 2026-09-23
- **Agent / Author**: Codex

## 1. Core judgment

New variant primary delivery (A/B/C/D): A - existing graph/query composition plus a single-purpose EQS test.

Conclusion: PASS

Reason: reachability is expressed as a data-declared EQS test in the Total War query; the order drain only supplies actor/source/path context and does not own walkable/unwalkable policy.

## 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|-------------------|-------|------------------------|
| Actor source/path context for EQS | 0 | `EqsContext` fields populated by `CommandIntentBufferDrainSystem` |
| Path reachability candidate filter | 0 | `PathReachableTest` using `IPathService` and `PathStore` |
| Total War siege ring policy | 2 | `assets/Spatial/eqs_queries.json` query `engage.siege.ring.navmesh` |
| RTS graph profile selection | 2 | `tw.rts.command.json` points at the TW-specific EQS query |
| Navmesh/pathing/debug assets | 2 | Total War Flow mod assets and launch graph config |

## 3. Reuse list

- Handlers: existing `SubmitEngageBatch`, `CompositeOrderPlanner`, `MovePlan`/cast order flow.
- Queues / Systems: existing command intent submission buffer, order drain, MassNavigation runtime, `PathStore`.
- Resolvers / Registries: `EqsQueryRegistry`, `EqsInfluenceConfigLoader`, `IPathService`, `NavQueryServiceRegistry`, `AutoPathService`.
- Existing presets / graphs: Ballista `engage.siege.ring` remains shared and unchanged; Total War uses a mod-local query variant.

## 4. New Layer 0 ops

| Op name | Single responsibility | Why existing ops cannot express it |
|---------|-----------------------|------------------------------------|
| `PathReachableTest` | Mark one EQS candidate filtered when the active path service cannot solve actor source to candidate. | Existing EQS tests score distance/influence/overlap, but none can ask the path service for actor-specific reachability. |

## 5. Transaction boundary

N/A. EQS candidate filtering is read-only. Path handles allocated by reachability probes are released immediately.

## 6. Config SSOT

Behavior config lives in mod graph/query/catalog assets:

- `mods/showcases/total_war_flow/TotalWarFlowShowcaseMod/assets/Spatial/eqs_queries.json`
- `mods/showcases/total_war_flow/TotalWarFlowShowcaseMod/assets/GAS/graphs/tw.rts.command.json`
- `mods/showcases/total_war_flow/TotalWarFlowShowcaseMod/assets/config_catalog.json`

New JSON schema: YES - `agentTypeId` on EQS test config. This is a required parameter for the EQS reachability test, not a profile enum or lifecycle preset switch.

## 7. Red flag scan

- [x] No profile inherit/placement enum added.
- [x] No parallel spawn/materialization pipeline added.
- [x] No placement validation moved into lifecycle ops.
- [x] No silent fallback: missing `agentTypeId`, source position, path service, or path store fails closed.

## 8. Next variant test

The next map variant changes graph/query assets: choose a different `agentTypeId`, add/remove the `PathReachable` test, or tune the ring generator. Core enum changes are not needed.

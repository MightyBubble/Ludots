# Agent Profile

Parent: #281. Implements NAV-2 #284. Depends on the scale vocabulary from #282.

## Background

Before NAV-2, agent shape and navigation vocabulary drifted across several files:

- `Navigation/navmesh.json` owned bake profiles with `radiusCm` and `heightCm`.
- `Navigation/pathing.json` owned agent routing types and their navmesh `layer`.
- `MassNavigationConfig.json` owned execution profiles with `navMass` and `bodyRadiusCm`.
- Road and graph pathing code carried profile names independently.

That meant changing one unit type could update bake but not runtime avoidance, or runtime movement but not pathing. NAV-2 makes agent geometry a single config registry.

## Authority

`Navigation/agent_profiles.json` is the only authored source for shared agent geometry and avoidance identity.

Catalog contract:

```json
{ "Path": "Navigation/agent_profiles.json", "Policy": "ArrayById", "IdField": "id" }
```

Every profile id is case-sensitive. Missing fields, unknown fields, duplicate ids, or references to unknown ids fail during config load. Mods may override one profile id without replacing the whole array.

## Fields

| Field | Unit | Owner | Consumers | Rule |
|---|---:|---|---|---|
| `id` | name | AgentProfile registry | bake, route, MassNavigationFlow | Required, case-sensitive, no aliases |
| `radiusCm` | cm | AgentProfile registry | navmesh clearance, MassNavigationFlow body radius | `> 0` |
| `heightCm` | cm | AgentProfile registry | navmesh agent height | `> 0` |
| `clearanceCm` | cm | AgentProfile registry | bake/pathing clearance decisions | `>= 0` |
| `draftCm` | cm | AgentProfile registry | NodeGraph edge `depthCm` passability | `>= 0` |
| `beamCm` | cm | AgentProfile registry | NodeGraph edge `widthCm` passability | `>= 0` |
| `mass` | scalar | AgentProfile registry | MassNavigationFlow resolve share and dominance | `> 0` |
| `layer` | integer | AgentProfile registry | NavTile layer | `>= 0` |
| `navProfileId` | name | AgentProfile registry | `Navigation/navmesh.json` `profiles.<id>` | Required. Several bodies may share one capability. Every capability profile declares `agentProfileId`, the body Recast bakes, resolved with Require. `navProfileId` is checked for the map's bound agents: pathing types whose body `navProfileId` is listed in `maps.<mapId>.navProfiles`, plus agent types named by that map's crowd unit templates |

Speed is not part of this registry. MassNavigation keeps `speedCmPerSecond` in `MassNavigationConfig.agentProfiles.profiles[]`. Crowd units write `speedCmPerSecond` on the unit template component `CrowdSimulationAgent`. That number is the initial speed. Agent type and terrain area together decide cost, speed divisor, and passability in `pathing.json`.

## Consumers

- `Navigation/navmesh.json` keeps capability profiles as an id-keyed object: `maxClimbCm`, optional `maxSlopeDeg` (omit means unlimited; `0` and negative values are rejected; Recast bake requires `> 0` and `< 90`), optional `cellSizeCm`, optional `jump`, and required `agentProfileId`. Catalog order is the property order after merge. Areas are an id-keyed object of `areaId` and `slopeFree`, and do not carry `cost`. Agent profiles point at a capability with `navProfileId`. Every `maps.<mapId>` entry requires `solver`, one of `navmesh`, `mass-navigation`, or `crowd-simulation`. Nav-tagged maps list the capability profiles they bake in `maps.<mapId>.navProfiles`. Mesh queries are built only for `PreferMesh` / `AutoCheapest` agent types whose body's `navProfileId` is in that list. Maps without the nav tag, or without `navProfiles`, build no mesh queries at startup. A path request for an agent type with no query throws, and the message names the agent id, the capability profile id, and the map id. Core `Humanoid` stays `PreferMesh`; mods do not override it.
- `Navigation/pathing.json` keeps route strategy fields. `agentTypes` is an id-keyed object; catalog order is the property order after merge. Entries reference AgentProfile ids and must not define `layer`; the layer comes from the AgentProfile. `selection` and `nodeGraph` may be omitted. AutoPath throws when it compiles an agent type that has no `selection`, and `ResolveAgent` throws on a blank or unknown id. `areaCosts[]` may set `speedDivisor` (absent means the same number as `cost`; both must be `> 0`) and `passable` (`false` excludes the area). Completeness is the map's bound agents times `maps.<mapId>.navAreas`. A missing row is an error. `areas == null` throws. An area added to the catalog but not listed in that map's `navAreas` is not required. `MassNavigationAgent.agentTypeId` outside the bound set fails when the map binds the template, with the template id and the agent id.
- `MassNavigationConfig.json` keeps execution strategy fields: `heavy`, `visualScale`, `speedCmPerSecond`, `everyNth`, `nthOffset`. It must not define `navMass` or `bodyRadiusCm`; MassNavigationFlow resolves `mass` and `radiusCm` through AgentProfile.
- NodeGraph transport capacity checks compare `draftCm` to edge `depthCm` and `beamCm` to edge `widthCm`. A zero profile value means the agent does not require that capacity dimension; a zero edge value means the edge has no limit for that dimension.

## UAT Showcase

Shared showcase preset: `mass_navigation`.

Command:

```powershell
.\scripts\run-mod-launcher.cmd cli launch mass_navigation --adapter raylib
```

| Operation | Visible feedback |
|---|---|
| Start with light and heavy formations beside a gate | HUD shows each profile id plus `radiusCm` and `clearanceCm`; heavy units draw larger circles |
| Command both formations through the gate | Light units pass more easily; heavy units queue or block when clearance exceeds gate width |
| Override only the heavy profile in a mod `Navigation/agent_profiles.json` | Only heavy geometry and avoidance spacing change; light behavior stays unchanged |

## Config To Behavior Tests

- Changing `radiusCm` changes navmesh passability and MassNavigationFlow body radius.
- Changing `mass` changes MassNavigationFlow resolve share.
- Changing `layer` changes which navmesh layer pathing queries use.
- Changing `draftCm` or `beamCm` changes whether a NodeGraph transport edge with finite depth/width is traversable.
- Changing `speedCmPerSecond` in MassNavigation changes movement speed without changing bake geometry.

## Merge And Reuse

No external branch is merged for NAV-2. It builds on the Core MassNavigation runtime already merged from PR #235 and uses existing config catalog / `ArrayById` infrastructure.

## DoD

NAV-2 is complete when `Navigation/agent_profiles.json` is the single geometry registry, navmesh/pathing/MassNavigationFlow all reference it, old duplicate fields fail-fast, contract tests cover strict casing and unknown fields, gitbook indexes include this page, and this page links back to #281 / #284.

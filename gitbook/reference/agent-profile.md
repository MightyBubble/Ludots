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
| `id` | name | AgentProfile registry | bake, route, MassFlow | Required, case-sensitive, no aliases |
| `radiusCm` | cm | AgentProfile registry | navmesh clearance, MassFlow body radius | `> 0` |
| `heightCm` | cm | AgentProfile registry | navmesh agent height | `> 0` |
| `clearanceCm` | cm | AgentProfile registry | bake/pathing clearance decisions | `>= 0` |
| `mass` | scalar | AgentProfile registry | MassFlow resolve share and dominance | `> 0` |
| `layer` | integer | AgentProfile registry | navmesh query layer | `>= 0` |

Speed is not part of this registry. `speedCmPerSecond` stays in execution movement config such as `MassNavigationConfig.agentProfiles.profiles[]`, because speed is a movement strategy, not geometry.

## Consumers

- `Navigation/navmesh.json` keeps bake-only fields such as `maxClimbCm` and `maxSlopeDeg`. Its profile entries reference AgentProfile ids and must not define `radiusCm` or `heightCm`.
- `Navigation/pathing.json` keeps route strategy fields. Its `agentTypes[]` entries reference AgentProfile ids and must not define `layer`; the layer comes from the AgentProfile.
- `MassNavigationConfig.json` keeps execution strategy fields: `heavy`, `visualScale`, `speedCmPerSecond`, `everyNth`, `nthOffset`. It must not define `navMass` or `bodyRadiusCm`; MassFlow resolves `mass` and `radiusCm` through AgentProfile.

## UAT Showcase

Shared showcase preset: `nav_profile`.

Command:

```powershell
.\scripts\run-mod-launcher.cmd cli launch nav_profile --adapter raylib
```

| Operation | Visible feedback |
|---|---|
| Start with light and heavy formations beside a gate | HUD shows each profile id plus `radiusCm` and `clearanceCm`; heavy units draw larger circles |
| Command both formations through the gate | Light units pass more easily; heavy units queue or block when clearance exceeds gate width |
| Override only the heavy profile in a mod `Navigation/agent_profiles.json` | Only heavy geometry and avoidance spacing change; light behavior stays unchanged |

## Config To Behavior Tests

- Changing `radiusCm` changes navmesh passability and MassFlow body radius.
- Changing `mass` changes MassFlow resolve share.
- Changing `layer` changes which navmesh layer pathing queries use.
- Changing `speedCmPerSecond` in MassNavigation changes movement speed without changing bake geometry.

## Merge And Reuse

No external branch is merged for NAV-2. It builds on the Core MassCrowd runtime already merged from PR #235 and uses existing config catalog / `ArrayById` infrastructure.

## DoD

NAV-2 is complete when `Navigation/agent_profiles.json` is the single geometry registry, navmesh/pathing/MassFlow all reference it, old duplicate fields fail-fast, contract tests cover strict casing and unknown fields, gitbook indexes include this page, and this page links back to #281 / #284.

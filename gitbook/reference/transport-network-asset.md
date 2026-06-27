# Transport Network Asset

Parent: [Epic #415](https://github.com/MightyBubble/Ludots/issues/415). This is the authoring reference for `TransportNetwork/transport_network.json`.

## Catalog

Every mod that owns a transport network asset must declare it in `assets/Configs/config_catalog.json`:

```json
{ "Path": "TransportNetwork/transport_network.json", "Policy": "Replace" }
```

The asset is a complete topology source. It is not deep-merged and does not extend another asset.

## Root Fields

| Field | Unit | Rule |
|---|---:|---|
| `id` | string | Required canonical id |
| `sampleStepCm` | cm | Required, `> 0`; default sampling distance for segments |
| `defaultVisualWidthMeters` | meters | Required, `> 0`; ribbon width when a segment does not override |
| `nodes` | array | Required explicit array |
| `segments` | array | Required non-empty array |

## Nodes

```json
{ "id": "west_port", "xcm": -2400, "ycm": 0, "kind": "Port", "tags": [] }
```

| Field | Rule |
|---|---|
| `id` | Required canonical string, unique in the asset |
| `xcm` / `ycm` | World position in centimeters |
| `kind` | String enum: `Normal`, `Port`, `Embark`, `Bridge`, `Ford` |
| `tags` | Explicit canonical string array |

Non-normal node kinds are converted to node tags, for example `Port` becomes `Transport.NodeKind.Port`.

## Segments

```json
{
  "id": "deep_channel",
  "points": [
    { "nodeId": "west_port" },
    { "xcm": 0, "ycm": 1800 },
    { "nodeId": "east_port" }
  ],
  "sampleStepCm": 0,
  "direction": "Bidirectional",
  "flowDirection": "None",
  "areaId": "Transport.Area.DeepWater",
  "tags": ["Transport.Area.Water", "Transport.Area.OpenSea"],
  "depthCm": 600,
  "widthCm": 1600,
  "laneCount": 0,
  "visualWidthMeters": 2.8
}
```

| Field | Unit | Rule |
|---|---:|---|
| `id` | string | Required canonical string |
| `points` | array | At least two points; each point is `{ "nodeId": "..." }` or `{ "xcm": n, "ycm": n }` |
| `sampleStepCm` | cm | `0` uses root `sampleStepCm`; otherwise `>= 0` |
| `direction` | enum | `Bidirectional`, `ForwardOnly`, `ReverseOnly` |
| `flowDirection` | enum | `None`, `Forward`, `Reverse` |
| `areaId` | tag string | Optional canonical tag-like id; registered as an edge tag when non-empty |
| `tags` | tags | Explicit canonical string array |
| `depthCm` | cm | `>= 0`; `0` means no draft limit |
| `widthCm` | cm | `>= 0`; `0` means no beam limit |
| `laneCount` | count | `>= 0`; reserved for future lane semantics |
| `visualWidthMeters` | meters | `0` uses root default; otherwise `>= 0` |

`flowDirection` adds asymmetric tags after direction expansion. A forward segment edge receives `Transport.Flow.Downstream`; the reverse edge receives `Transport.Flow.Upstream`. If `flowDirection` is `Reverse`, the tags are swapped.

## Cost Ownership

The transport asset owns topology, geometry, area/tags, and capacity. It does not own per-agent policy. Route cost comes from:

- derived edge length as the static geometric base;
- `Navigation/pathing.json` `nodeGraph.tagCostRules[]`;
- `AgentProfileConfig.draftCm` / `beamCm` capacity checks;
- optional `GraphEdgeCostOverlay` when `nodeGraph.useDynamicOverlay` is `true`.

## In-session Editor

The Live Map Editor transport panel edits this same asset in memory. It does not create a second graph, second ribbon source, or JavaScript geometry source.

| Editor surface | Asset fields |
|---|---|
| Node mode | `nodes[].id`, `xcm`, `ycm`, `kind`, `tags` |
| Segment mode | `segments[].points`, `areaId`, `tags`, `direction`, `flowDirection`, `depthCm`, `widthCm`, `laneCount`, `visualWidthMeters`, `sampleStepCm` |
| Root settings | `sampleStepCm`, `defaultVisualWidthMeters` |
| Route validation | Reads baked graph and agent/pathing config only; does not mutate the asset |
| Save | Writes `TransportNetwork/transport_network.json`, ensures catalog registration, reloads through `TransportNetworkAssetLoader`, and re-bakes |

Every edit runs `TransportNetworkAsset.Validate()` before the editor refreshes graph/ribbon derived outputs through `TransportNetworkBaker.Bake(asset, chunkSizeCm)`.

## Example

See:

```text
mods/showcases/capability_standard/CapabilityStandardTransportNetworkMod/assets/TransportNetwork/transport_network.json
```

That example contains a shallow river, a deep water channel, ports, a bridge node, a ford node, flow tags, and capacity fields.

Its companion `Navigation/pathing.json` and `Navigation/agent_profiles.json` define route agent types that exercise the asset without moving cost into the asset itself: foot traffic rejects `Transport.Area.Water`, shallow boats can pass the shallow river, and deep-draft ships are blocked by shallow `depthCm` / `widthCm` capacity.

## Known Limitations

- If different segments sample the same directed node pair, the baker deduplicates by first occurrence. Later duplicates are dropped, including their area and capacity fields.
- NodeGraph A* uses straight-line distance as its heuristic. When a tag cost rule has `CostMul < 1`, or an overlay lowers an edge below geometric distance, the heuristic is not admissible and the route may be non-optimal.

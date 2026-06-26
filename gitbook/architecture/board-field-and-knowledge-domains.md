# Board Field And Knowledge Domains

Related: [Terrain Data Budget SSOT](terrain-data-budget-ssot.md), [Spatial Scale SSOT](spatial-scale-and-resolution-ssot.md), [NAV-14 #372](https://github.com/MightyBubble/Ludots/issues/372), [NAV-15 #373](https://github.com/MightyBubble/Ludots/issues/373), [TERR-15 #432](https://github.com/MightyBubble/Ludots/issues/432).

This page defines the boundary between two different species of map-scale data. They can both be spatial, but they are not the same domain.

## Two Species

### Relationship Knowledge

Knowledge, fog, line-of-sight memory, confidence, visibility TTL, and per-faction observations stay in `KnowledgeProjectionStore` under `src/Core/Knowledge/`.

These records are keyed by observer and target relationships. They are compacted by time, confidence, and relation semantics. They are not board fields, and they must not be stored as per-board dense or sparse cell layers.

### Board Fields

Board fields are per-cell data sampled by board coordinates or world centimeters. They are global to a board unless a specific field domain says otherwise. They use `BoardFieldStore<T>` under `src/Core/Map/Fields/` for sparse resident chunks, per-chunk dirty state, streaming unload, and world sampling.

`BoardFieldStore<T>` owns storage mechanics only. It does not know terrain, wind, fertility, ownership, cost, area, or faction semantics. Each field domain supplies its own typed cell value and SoA chunk codec. LogicTerrain is the first consumer; Navigation reads LogicTerrain instead of owning the field storage.

## Five-Axis Classification

| Field | Authority source | Persistence | Change frequency | Scope | Value type |
|---|---|---|---|---|---|
| LogicTerrain | Authored board data plus deterministic projection from VisualHeightmap | Persistent SSOT, sparse chunks | Static or slow authoring edits | Global board field | `height/areaId/flags` keys; costs resolve through area x agent tables |
| Wind | Simulation derived or authored climate rules | Transient unless a mod declares authored climate | Slow simulation | Global board field | Vector or packed scalar direction/speed |
| Fertility | Authored biome data plus simulation modifiers | Persistent or recomputable from biome rules | Slow simulation / gameplay rewrite | Global board field | Scalar or enum id |
| Administrative region | Authored scenario rules or gameplay rewrite | Persistent SSOT | Static or slow gameplay rewrite | Global board field | Region id / enum |
| Fog / vision | Observation relationships in `KnowledgeProjectionStore` | Transient or relation-memory persistence | Every tick / event driven | Per-faction / per-observer relationship knowledge | Relation record with TTL, confidence, and aspect flags |

## Storage Rules

- Use `BoardFieldStore<T>` for board-cell fields that need sparse resident chunks, dirty tracking, and world sampling.
- Use SoA chunk codecs: a chunk stores layers such as height, area, flags, scalar components, or bitsets in separate arrays or packed bit planes. Do not store chunk cells as `T[]` AoS arrays in production field codecs.
- Missing chunks represent the field default and are not instantiated or written.
- Hot paths such as `GetCell`, `SetCell`, and `SampleWorldCm` must stay allocation-free after the target chunk is resident.
- Chunk unload events from `ILoadedChunks` may remove resident chunks. Persistence owners decide whether dirty chunks are flushed before unload.

## Navigation Alignment

NAV-14 and NAV-15 require terrain and polygons to store classification keys, not per-agent traversal cost. Board fields may store `areaId` and flags. Navigation resolves cost later through `NavAreaCostTable` using the area x agent matrix.

The production order remains:

```text
Board -> VisualHeightmap -> LogicTerrain -> NavMesh
```

LogicTerrain consumes `BoardFieldStore<T>` as a storage base. Nav bake consumes LogicTerrain. NavMesh output remains a derived product, not a board field.

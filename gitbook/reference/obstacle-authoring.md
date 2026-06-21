# Obstacle Authoring

Parent: #281. NAV-3: #285.

This page defines the single source of truth for navigation and physics obstacles after NAV-3.

## Source Of Truth

Obstacle authoring is ECS component authoring:

```text
Map / template components
  -> ManifestationObstacleIntent2D or CompoundObstacle2D
  -> ShapeDataStorage2D + CompoundObstacle2DState
  -> ManifestationObstacleBridge2DSystem
  -> MassFlowObstacleProjection + WorldPositionCm
  -> MassCrowdEnvironmentBindingSystem
  -> MassFlow runtime obstacle snapshots
```

The same authored obstacle data also feeds bake tooling through the Physics2D navigation adapter:

```text
Map / template components
  -> NavObstacleAuthoringCatalog
  -> NavObstacleAuthoringAdapter
  -> Recast nav bake obstacle carve input
```

Do not create `ObstacleGeometryProfile2D`. It is not a mainline type. The mainline SSOT is `ManifestationObstacleIntent2D` + `ShapeDataStorage2D` + `CompoundObstacle2DState`.

## Supported Geometry

| Geometry | Authoring component | Bake precision | MassFlow runtime precision |
| --- | --- | --- | --- |
| Circle | `ManifestationObstacleIntent2D` or `CompoundObstacle2D` piece | exact circle carve | exact radius |
| Box | `ManifestationObstacleIntent2D` or `CompoundObstacle2D` piece | exact box carve, including rotation | enclosing runtime radius |
| Polygon | `ManifestationObstacleIntent2D` + `ManifestationObstaclePolygon2D`, or `CompoundObstacle2D` piece | exact polygon carve, including rotation | enclosing runtime radius |

Runtime exact polygon stamping is a follow-up. NAV-3 keeps MassFlow runtime safe by using each piece's `navRadiusCm` while preserving exact geometry for bake.

## Static And Dynamic Semantics

- Static structural obstacles are authored in map/template data and participate in navmesh bake.
- Runtime dynamic obstacles are ECS entities with obstacle authoring components and `WorldPositionCm`; the bridge projects them into `MassFlowObstacleProjection`.
- Temporary unit-to-unit avoidance remains MassFlow agent avoidance, not navmesh rebuild input.

## Limits

- `CompoundObstacle2D.MaxPieces` is the per-entity piece cap.
- Circle radius, box half extents, polygon vertices, local offsets, and `navRadiusCm` are explicit authored data.
- `navRadiusCm` must be positive for each piece that sinks to navigation.
- Field names and component names are case-sensitive; aliases are not accepted.

## Removed Sidecars

These are not valid authoring sources:

- `MassNavigationConfig.world.obstacles[]`
- `assets/Data/Maps/{mapId}.obstacles.json`
- private C# obstacle loaders
- hardcoded MassFlow blocker circles in scenario bootstrap

`MassNavigationConfig.world.obstacles[]` is an obsolete key. Strict config loading must reject it instead of ignoring or aliasing it.

## Configuration Example

Single circle:

```json
{
  "WorldPositionCm": { "Value": { "X": 5000, "Y": 5000 } },
  "ManifestationObstacleIntent2D": {
    "shape": "Circle",
    "sinkPhysicsCollider": false,
    "sinkNavigationObstacle": true,
    "radiusCm": 250,
    "navRadiusCm": 250,
    "localOffsetCm": { "x": 0, "y": 0 }
  }
}
```

Compound obstacle:

```json
{
  "WorldPositionCm": { "Value": { "X": 9000, "Y": 9000 } },
  "CompoundObstacle2D": {
    "sinkPhysicsCollider": true,
    "sinkNavigationObstacle": true,
    "pieces": [
      {
        "shape": "Box",
        "halfWidthCm": 200,
        "halfHeightCm": 80,
        "navRadiusCm": 216,
        "localOffsetCm": { "x": -120, "y": 0 }
      },
      {
        "shape": "Circle",
        "radiusCm": 120,
        "navRadiusCm": 120,
        "localOffsetCm": { "x": 180, "y": 0 }
      }
    ]
  }
}
```

## Configuration To Behavior

| Change | Expected behavior |
| --- | --- |
| Add a map-authored obstacle entity | Recast bake carves it; MassFlow runtime binds it after bridge + environment binding. |
| Move `WorldPositionCm` | MassFlow obstacle snapshot changes and flow fields rebuild. |
| Rotate `FacingDirection` on box/polygon | Bake geometry and MassFlow projection offsets use the rotated pose. |
| Remove the obstacle entity or disable navigation sink | Runtime projection is removed and MassFlow rebuilds without it. |

## UAT

Run the NAV-3 preset:

```powershell
.\scripts\run-mod-launcher.cmd cli launch nav_obstacle --adapter raylib
```

Expected showcase flow:

| Operation | Feedback |
| --- | --- |
| Start the preset | HUD shows obstacle piece count. |
| Draw a Box obstacle and bake | Navmesh overlay has a carved hole; piece count increases. |
| Move a precision squad through the region | Route avoids the carved hole. |
| Add a runtime Circle obstacle in front of the army | MassFlow units split around it. |
| Delete the obstacle | Runtime obstacle count drops; after bake, the navmesh hole disappears. |

## Merge Notes

NAV-3 is built on #186 plus the existing mainline obstacle types. Rotation support comes from the already-present `ShapeWorldTransform2D` / collision rotation fix; do not merge or invent a separate obstacle profile branch.

DoD: one obstacle data source, no sidecar obstacle file, no hardcoded solver circles, fail-fast strict authoring, contract tests, and back-link to #281.

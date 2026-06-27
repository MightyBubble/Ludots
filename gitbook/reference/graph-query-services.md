# Graph Query Services

Parent: [Epic #415](https://github.com/MightyBubble/Ludots/issues/415). These helpers live under `Ludots.Core.Navigation.GraphQuery` and are reusable for any NodeGraph domain.

## Services

| Type | Responsibility |
|---|---|
| `GraphEdgeProjectionQuery` | Finds the nearest outgoing edge around a position and returns the projected point, segment parameter, and edge endpoints |
| `PolylineGoalSnapQuery` | Mutates a path polyline so it ends at the point nearest a requested goal |
| `GraphHybridRouteBuilder` | Solves and stitches multiple route legs through `IPathService` and `PathStore` |
| `LoadedChunkSolvePrimer` | Updates `WorldGridLoadedChunks` around a set of points before solving |

## Boundaries

These services are intentionally domain-neutral. They do not score business routes, choose named route variants, own arrival behavior, or render previews. Domain-specific policy remains in mods.

Use them when a mod needs shared graph mechanics such as edge projection, snapping, or multi-leg stitching. Do not clone equivalent helpers into a showcase-specific namespace.

## Failure Semantics

- Empty or invalid input returns `false` or a short failure string.
- `GraphHybridRouteBuilder` releases path handles after copying each leg.
- It does not downgrade to a direct route if a leg fails; the caller receives the failure.

## Verification

`TransportNetworkCoreTests.GraphQueryServices_ProjectSnapAndStitchWithoutDuplicateLegBoundary` covers projection, snapping, and multi-leg stitching without duplicating the shared leg boundary point.

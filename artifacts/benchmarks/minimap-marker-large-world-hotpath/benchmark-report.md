# Minimap Marker Large World Hot Path Benchmark

- map: `presenter_blacksmith_minimap_marker_large_world_showcase`
- source: authored presenter `MinimapMarker` behavior
- world: 256x256 chunks, visual heightmap scene, 30k moving marker balls
- path: `PresenterWorldPlanePosition/PresenterWorldFacing -> MinimapMarkerBuffer -> MinimapScreenMarkerBuffer -> SkiaOverlayRenderer`
- secondary path: none; no `Name`, `Team`, or `MapEntity` minimap signal
- timing note: `Avg Tick FPS` is computed from the engine tick only; offscreen CPU Skia marker raster is measured separately and is not part of that FPS.

## Summary

| Markers | Screen | Buckets | Oriented | Screen Oriented | Drop markers | Drop screen | Avg Tick | P95 Tick | Max Tick | Avg Tick FPS | Avg collect | Avg projection | Avg Skia build | Avg offscreen CPU Skia draw | Avg offscreen CPU Skia total | Avg alloc |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 30000 | 30000 | 64 | 30000 | 30000 | 0 | 0 | 92.2736 ms | 175.5785 ms | 3174.9044 ms | 10.8 | 1.1599 ms | 0.9032 ms | 0.2041 ms | 85.9116 ms | 86.4324 ms | 8513616 B |

## Detail

| Metric | Avg | P95 | Max |
|---|---:|---:|---:|
| total tick | 92.2736 ms | 175.5785 ms | 3174.9044 ms |
| presentation | 34.0189 ms | 91.0919 ms | 163.1107 ms |
| simulation | 58.1290 ms | 154.9554 ms | 3139.8655 ms |
| presenter minimap collect | 1.1599 ms | 3.3260 ms | 28.9665 ms |
| minimap projection | 0.9032 ms | 8.1363 ms | 18.9582 ms |
| Skia marker batch build | 0.2041 ms | 0.3435 ms | 1.9208 ms |
| offscreen CPU Skia marker draw | 85.9116 ms | 129.2419 ms | 168.6706 ms |
| offscreen CPU Skia render wrapper | 86.4324 ms | 129.7349 ms | 169.0644 ms |
| terrain height sync | 3.5635 ms | 4.9562 ms | 9.9518 ms |
| primitive render diag | 0.0000 ms | 0.0000 ms | 0.0000 ms |

## Presentation Top Systems

| Rank | Most frequent system | Avg when ranked |
|---:|---|---:|
| 1 | PresenterEntityTransformSyncSystem | 4.8479 ms |
| 2 | TerrainHeightSyncSystem | 3.1333 ms |
| 3 | TerrainHeightSyncSystem | 3.9948 ms |

## Counts

- marker count avg/max: `30000.0` / `30000`
- screen marker count avg/max: `30000.0` / `30000`
- Skia bucket count avg/max: `64.0` / `64`
- orientation bucket count avg/max: `64.0` / `64`
- per-frame marker drops max: `0`; screen drops max: `0`

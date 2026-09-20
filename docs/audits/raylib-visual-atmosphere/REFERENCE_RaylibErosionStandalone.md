# Reference study: Delvix000/RaylibErosionStandalone

Upstream: <https://github.com/Delvix000/RaylibErosionStandalone> (no LICENSE in repo — **do not copy shaders/assets**; reimplement techniques + use Ludots/CC0 assets).

## Author-visible bar (what “that level” means)

1. **Sky** — cubemap/gradient skybox with animated day/night (not solid clear color)
2. **Sun light** — one directional light driving terrain + vegetation shading + ambient ramp over day
3. **Terrain** — heightmap-displaced mesh, height gradient coloring, rock normal detail
4. **Water** — planar reflection + refraction FBOs + DUDV distortion (not a flat translucent quad alone)
5. **Vegetation** — dense billboards with **alpha cutout** (`discard` below threshold) + lit
6. **Atmosphere** — day cycle colors; optional mild postprocess FBO
7. **Erosion sim** — demo-only; **out of Ludots scope** (visual heightmap/editor already covers terrain authoring)

## Frame graph (learned)

```
reflection FBO  → flip camera Y → draw sky+terrain (clip above water)
refraction FBO  → draw sky+terrain+floor (clip below water)
main            → sky + clouds + terrain + floor + water(use RTs) + trees
optional post   → fullscreen blit
```

## Ludots mapping (reuse first)

| Reference | Ludots hook |
|-----------|-------------|
| Skybox pass | New client env pass before opaque; Host URI for gradient/cubemap |
| `rlights` directional | Client `RaylibFrameLighting` fed by day phase / map env |
| Terrain heightmap shader | Upgrade `terrain.vs/fs` + ContinuousHeightmap / VertexMap |
| Water RTs | New `RaylibWaterPass` beside existing `water.fs` |
| Vegetation cutout | Billboard lane + cutout shader (extend Mesh Billboard) |
| DayNight | Existing `GlobalDayNight` presentation event → drive lighting/sky |
| Postprocess | Optional last; not DoD for first acceptance |

## Non-goals

- Porting hydraulic erosion UI/controls
- Copying upstream HDR/PNG/shader source
- Full cascaded shadows / multi-light PBR in this track

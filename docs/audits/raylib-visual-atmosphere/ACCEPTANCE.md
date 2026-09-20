# Raylib Visual Atmosphere — Acceptance Screenshots

SSOT: `MASTER.md` / `STATUS.md`.  
Visual bar studied from RaylibErosionStandalone (**techniques only**; no upstream asset/shader copy).

Evidence lands in:

- `artifacts/raylib-visual-atmosphere/acceptance/`
- `/opt/cursor/artifacts/raylib-visual-atmosphere/acceptance/`

## Screenshot contract

| File | Proves |
|------|--------|
| `01_sky_day.png` | Sky pass + day lighting (not solid black clear) |
| `02_sky_night.png` | Night phase darkens sky + ambient |
| `03_cutout_vegetation.png` | Billboard vegetation with alpha cutout (no solid quad) |
| `04_blend_modes.png` | AlphaBlend vs Additive VFX distinguishable |
| `05_distance_fog.png` | Atmospheric distance fog (not FoW overlay) |
| `06_water_reflect.png` | Planar reflection/refraction water silhouette |
| `07_beach_decals.png` | 故事脚印/焦痕/血迹/裂痕贴在近景沙滩上 |
| `08_decal_fields.png` | P0–P2 不把此机位当 GPU 合同。对照区大小/厚度/染色/朝向由 `RaylibVisualAtmosphereFieldUatTests` 锁发射载荷；投影视觉尺寸见 P3/#933 |
| `09_spline_ribbons.png` | P0–P2 不把此机位当 GPU 合同。岸边细/宽/描边弯带由发射载荷 UAT 锁住；去路名见 P4/#931 |
| `10_cue_flashes.png` | P0–P2 不把此机位当 GPU 合同。小绿 / 大黄击中闪光由发射载荷 UAT 锁住（叶子网格，不是 Prefab） |

## Host

Playable showcase binding `raylib_visual_atmosphere` (category `demo` in `showcase.registry.json`).

```bash
export LD_LIBRARY_PATH=src/Platforms/Desktop:$LD_LIBRARY_PATH
export LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UNDERLAY=1
export LUDOTS_RAYLIB_DISABLE_SKIA_FRAMEBUFFER_UNDERLAY=1
dotnet exec src/Tools/Ludots.Launcher.Cli/bin/Release/net9.0/Ludots.Launcher.Cli.dll \
  launch raylib_visual_atmosphere --adapter raylib --build auto
```

Optional capture framing (also used by the acceptance script):

- `LUDOTS_ATMOSPHERE_SHOT=01_sky_day|02_sky_night|03_cutout_vegetation|04_blend_modes|05_distance_fog|06_water_reflect|07_beach_decals|08_decal_fields|09_spline_ribbons|10_cue_flashes`
- `LUDOTS_DAY_PHASE=0.42` (day) / `0.92` (night)

## Recapture

Screenshot paths must be **absolute** (Raylib host cwd is the app bin dir).

```bash
bash tools/raylib_visual_atmosphere_acceptance/capture.sh /workspace \
  /workspace/artifacts/raylib-visual-atmosphere/acceptance \
  /opt/cursor/artifacts/raylib-visual-atmosphere/acceptance
```

The script:

1. Builds the showcase mod + launches each shot via the launcher
2. Waits for the PNG (launcher may return before the host exits)
3. Rejects near-black frames and day/night pairs that are too similar
4. Requires AlphaBlend + Additive draw evidence in `04_blend_modes.diag.txt`
5. Requires reflective water FBO creation in the `06` launch log
6. Copies every PNG into both acceptance directories

## Notes

- Linux cloud: keep Skia GPU + framebuffer underlays disabled (same as Raylib parity showcase).
- Reflective water uses ContinuousHeightmap terrain into reflection/refraction FBOs plus a Host ocean plane (`tropical_island.height`); do not capture `06` if the water FBO pass is inactive.
- Assets are procedural CC0 / in-repo Ludots content — no copy from `/tmp/RaylibErosionStandalone`.
- P3 projected Decal contract is proven by `ProjectedDecalContractTests` (author scale → `VisualProxy` → `ProjectedDecalVolume` → fail-loud projector). Camera `08` field-contrast player shot is **not taken** on this branch; tropical-island field gallery belongs to the gallery PR.

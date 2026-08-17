# Raylib Client Parity — Acceptance Screenshots

SSOT: `MASTER.md` / `STATUS.md`.  
Capture tool: `tools/raylib_client_parity_acceptance` (production shaders from `src/Platforms/Desktop/`).  
Playable showcase: `mods/showcases/raylib_client_parity/RaylibClientParityShowcaseMod` (binding `raylib_client_parity`).

Evidence lands in both:

- `artifacts/raylib-client-parity/acceptance/`
- `/opt/cursor/artifacts/raylib-client-parity/acceptance/`

## Screenshot contract

| File | Proves |
|------|--------|
| `01_static_ism.png` | Static GPU instancing path: Kenney/blacksmith glTF drawn with `instancing.vs/fs` + `DrawMeshInstanced` (48 building instances). Matches W1 static ISM lane / showcase IRaylibBenchmarkRenderer scene. |
| `02_gpu_skinned_walk_a.png` | Real GPU bone skinning (not VAT): `skinning_instanced` + `UpdateModelAnimationBones` + `rlSetUniformMatrices(boneMatrices)` + `DrawMeshInstanced` on KayKit-retargeted `mannequin_large_walk.glb` walk clip (frame 0). |
| `02_gpu_skinned_walk_b.png` | Same crowd at a later walk frame (frame 20). Byte-diff vs `_a` is required — pose change proves animation, not a static bind-pose fake. |
| `03_material_bind.png` | Host Material albedo baseline (W2): left building keeps imported maps; right building applies `sourceUris[0]` albedo override (`parity_albedo_override.png` cyan/magenta checker). |
| `04_vfx_shader.png` | Effect shader baseline (W3): billboard meshes drawn with production `vfx_unlit_tint.vs/fs` (tint + `uTime` pulse), not placeholder spheres. |

## Showcase scene (playable)

`raylib_client_parity_showcase` installs:

1. Static ISM building cluster via `IRaylibBenchmarkRenderer` (blacksmith meshes from `PerformerBlacksmithShowcaseMod`).
2. GpuSkinnedInstance crowd performers on `raylib_client_parity.mannequin` (GLB contains Walk/Run animations).
3. Host Material bind demo entity (`raylib_client_parity.albedo_demo` → albedo URI).
4. VFX performer (`assetKind=VFX`) + `prefabs.json` VFX part for `vfx_unlit_tint` path.

Launcher (playable showcase; Linux cloud agents may need Skia underlay disabled):

```bash
export LD_LIBRARY_PATH=src/Platforms/Desktop:$LD_LIBRARY_PATH
export LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UNDERLAY=1
export LUDOTS_RAYLIB_DISABLE_SKIA_FRAMEBUFFER_UNDERLAY=1
dotnet exec src/Tools/Ludots.Launcher.Cli/bin/Release/net8.0/Ludots.Launcher.Cli.dll \
  launch raylib_client_parity --adapter raylib --build auto
```

Formal UAT PNGs (`01`–`04`) are produced by `tools/raylib_client_parity_acceptance` using the same production shaders (`instancing` / `skinning_instanced` / `vfx_unlit_tint`) and showcase assets.

Launcher smoke (`00_launcher_showcase.png` + `.diag.txt`) must show `gpuSkinned` instances &gt; 0 with the crowd performers active. Benchmark ISM and performer primitive/skinned lanes draw independently (host loop must not short-circuit performer draw when the benchmark scene is enabled).

## Recapture

```bash
export LD_LIBRARY_PATH=src/Platforms/Desktop:$LD_LIBRARY_PATH
dotnet run --project tools/raylib_client_parity_acceptance -c Release -- \
  /workspace \
  artifacts/raylib-client-parity/acceptance \
  /opt/cursor/artifacts/raylib-client-parity/acceptance
```

Fail-loud: missing assets, invalid skinning animation, or identical `02_*` frames abort the capture.

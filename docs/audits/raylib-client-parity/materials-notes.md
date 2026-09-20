# Raylib host material binding (W2 → minimal directional MR)

## Contract

- Mod `material_assets` stay logical (id / domain / flags only).
- Host `Presentation/host_assets.json` rows with `backendId=raylib` + `assetKind=Material` inject `sourceUris` into `MaterialAssetDescriptor` via `PresentationHostAssetConfigLoader`.
- Client binder (`RaylibMaterialHostBinder`) maps host `sourceUris` slots:

| Index | Role | Required | Default when URI absent |
|------:|------|----------|-------------------------|
| 0 | Albedo | **yes** (when any host override is present) | — (empty `sourceUris` = no host override) |
| 1 | Roughness map | optional | roughness **scalar 0.85** |
| 2 | Metallic map | optional | metallic **scalar 0** |
| 3 | Normal map | optional | geometric normal only |

- Tint still comes from performer/draw color (existing instanced tint path).
- This is **minimal directional PBR** on `instancing.fs` / `skinning_instanced.fs`: single directional light + ambient + fog, Cook-Torrance GGX. **No IBL**, no BRDF LUT, no cubemap, no cascaded shadows.

## Optional maps — no fake textures

- If an optional slot URI is absent (array shorter, or whitespace): **do not** invent 1×1 placeholder textures; use the scalar defaults above and leave that `MATERIAL_MAP_*` unbound.
- If a URI is present (non-empty) but VFS resolve / file / `LoadTexture` fails: **throw** (fail-loud; no silent fallback).

## Normal maps / tangents

- Host may list `sourceUris[3]` and the binder will load `MATERIAL_MAP_NORMAL`.
- Raylib ISM primitives (`GenMeshCube` / `GenMeshSphere`) and current `instancing.vs` **do not** supply tangents / TBN.
- Therefore normal mapping is **skipped** in the lit shaders (geometric `fragNormal` only) until a mesh lane provides tangents. Binding the texture is still valid for contract readiness; sampling without TBN is forbidden.

## Fail-loud

- Unknown `materialId`, unresolvable URI, missing file, or `LoadTexture` failure throw.
- Empty `sourceUris` means “no host material override” (keep imported / default material maps; lit shaders still use roughness scalar 0.85 / metallic 0).

## Draw hooks

- Model instanced / GPU-skinned: after `TryResolveInstancedModelMaterial`, apply host maps + PBR uniforms.
- Immediate model draw: apply host maps to imported model materials before `DrawModelEx`.
- Procedural mesh: apply host maps per submesh `PrefabMaterialBinding.MaterialAssetId` (default material shader; MR uniforms apply when that material uses the lit instancing program).
- Primitive StaticMesh (cube/sphere) with `materialId > 0`: host maps applied on the instanced `_material` before `DrawMeshInstanced`.

## Raylib map indices (binder → shader samplers)

- `MATERIAL_MAP_ALBEDO` → `texture0`
- `MATERIAL_MAP_METALNESS` → `texture1`
- `MATERIAL_MAP_NORMAL` → `texture2` (bound only; not sampled without TBN)
- `MATERIAL_MAP_ROUGHNESS` → `texture3`

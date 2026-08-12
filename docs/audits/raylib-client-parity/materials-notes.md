# Raylib host material binding (W2)

## Contract

- Mod `material_assets` stay logical (id / domain / flags only).
- Host `Presentation/host_assets.json` rows with `backendId=raylib` + `assetKind=Material` inject `sourceUris` into `MaterialAssetDescriptor` via `PresentationHostAssetConfigLoader`.
- Client binder (`RaylibMaterialHostBinder`) treats **`sourceUris[0]` as albedo** only.
- Tint still comes from performer/draw color (existing instanced tint path).
- This is an **albedo bind baseline**, not full PBR (no metallic/roughness/normal/emissive host maps).

## Fail-loud

- Unknown `materialId`, unresolvable URI, missing file, or `LoadTexture` failure throw.
- Empty `sourceUris` means “no host albedo override” (keep imported / default material maps).

## Draw hooks

- Model instanced / GPU-skinned: after `TryResolveInstancedModelMaterial`, apply host albedo.
- Immediate model draw: apply host albedo to imported model materials before `DrawModelEx`.
- Procedural mesh: apply host albedo per submesh `PrefabMaterialBinding.MaterialAssetId`.

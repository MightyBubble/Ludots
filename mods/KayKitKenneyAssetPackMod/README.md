# KayKitKenneyAssetPackMod

Pure-resource mod that holds the built-in CC0 KayKit and Kenney model library.

## Directory layout

```
mods/KayKitKenneyAssetPackMod/
  mod.json
  packs.json
  assets/
    game.json
    Models/
      LICENSE.txt
      README.md
      KayKit/
        MedievalHexagon/...
        CharacterAdventures/...
      Kenney/
        PrototypeKit/...
        MiniTownKit/...
    Presentation/
      mesh_assets.json
      host_assets.json
      animation_clips.json
      animation_profiles.json
      animator_controllers.json
```

## How to import the actual CC0 packs

Run from the repository root:

```powershell
.\scripts\import-kaykit-kenney-assets.ps1
```

The script downloads the packs listed in `packs.json`, places their `.gltf` / `.glb`
files under `assets/Models/<Vendor>/<Pack>/`, and regenerates
`assets/Presentation/mesh_assets.json` and `host_assets.json` following the same
convention as `PerformerBlacksmithShowcaseMod`:

- Core semantic mesh ids are declared in `mesh_assets.json`.
- Platform locators are declared in `host_assets.json` with `backendId: "raylib"`.
- `sourceUris` use VFS paths: `KayKitKenneyAssetPackMod:assets/Models/...`.

## Animation reference convention

Animated characters keep their clip locators in `animation_clips.json`, e.g.:

```json
{
  "id": "kaykitkenney.knight.clip.idle",
  "assetKind": "Clip",
  "locators": [
    {
      "backendId": "raylib",
      "assetRef": "KayKitKenneyAssetPackMod:assets/Models/KayKit/CharacterAdventures/Knight.glb#anim:0"
    }
  ]
}
```

Gameplay mods author their own `performers.json` and bind an `Animator` behavior to
`animatorControllerId` / `animationProfileId`, exactly like the blacksmith worker in
`PerformerBlacksmithShowcaseMod`.

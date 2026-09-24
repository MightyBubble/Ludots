# KayKit / Kenney Model Library

This directory is the built-in host-asset library for CC0 model packs.

## Placement convention

Follow the existing PerformerBlacksmithShowcaseMod convention:

- `assets/Models/**/*.gltf|*.glb` — model source files.
- `assets/Textures/**/*.png` — billboard/sprite textures used by `Presentation/host_assets.json`.
- `assets/Presentation/mesh_assets.json` — Core semantic mesh ids and types (`Model` / `Billboard`).
- `assets/Presentation/host_assets.json` — platform locators with `backendId: raylib` and VFS source URIs:
  `KayKitKenneyAssetPackMod:assets/Models/...`.
- `assets/Presentation/animation_clips.json` — clip locators, e.g.
  `KayKitKenneyAssetPackMod:assets/Models/KayKit/CharacterAdventures/Knight.glb#anim:0`.
- `assets/Presentation/animation_profiles.json` — state-to-clip mapping.
- `assets/Presentation/animator_controllers.json` — controllers and transitions.

Run `scripts/import-kaykit-kenney-assets.ps1` to download the packs into
`assets/Models/KayKit/...` and `assets/Models/Kenney/...` and regenerate the
`assets/Presentation/*.json` files from the files that are actually present.

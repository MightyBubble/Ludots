# Textures

Billboard/sprite source textures imported from the CC0 packs are placed here.

The import script currently copies model files into `assets/Models`. If a pack
contains standalone billboard/sprite textures that should be exposed as
`MeshAssetType.Billboard`, copy them under this directory and add matching
`Billboard` entries to `assets/Presentation/mesh_assets.json` and raylib
`sourceUris` to `assets/Presentation/host_assets.json`.

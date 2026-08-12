# STATUS — Raylib Visual Atmosphere

Updated by agents. Use: `pending` | `in_progress` | `blocked` | `done`.

| ID | Package | Status | Owner | Notes |
|----|---------|--------|-------|-------|
| V1 | Skybox + day/night env | done | cloud-agent | cool night gradient + moonlight ambient |
| V2 | Directional + ambient lighting | done | cloud-agent | moon remap of same directional slot |
| V3 | Cutout + Alpha/Additive blend | done | cloud-agent | vegetation_cutout + VFX blends |
| V4 | Distance fog | done | cloud-agent | strategic fog range + night cool-dim |
| V5 | Reflective/refractive water FBO | done | cloud-agent | VH ocean plane + water FBO |
| V6 | Showcase + screenshot acceptance | done | cloud-agent | VH island; capture.sh |
| V7 | Terrain albedo layers | done | cloud-agent | 4 tiled albedos via `terrain_albedo_environments.json` |
| V8 | Minimal directional MR (no IBL) | done | cloud-agent | Host `sourceUris[0..3]`; GGX in instancing/skinning; normals need TBN (skipped) |
| V9 | Anti-tiling | done | cloud-agent | hash-rotated UV + IGN in `terrain.fs` |
| V10 | Real weight layers | done | cloud-agent | baked RGBA control map (R sand/G grass/B dirt/A rock); not height-band fake when URI set |
| V11 | Decals (GroundOverlay) | done | cloud-agent | beach Ring marks via blacksmith pattern; 12 placements |

## Blockers

| Item | Why |
|------|-----|
| Full IBL / cascaded shadows / projection decals | MASTER P2 / performer-raylib-uat: no native projected decals; GroundOverlay is the reused low-fi channel |
| Normal-mapped PBR | Meshes lack tangents; binder loads normal URI but shader skips without TBN |

## Evidence

| Artifact | Path |
|----------|------|
| Master | `docs/audits/raylib-visual-atmosphere/MASTER.md` |
| Materials contract | `docs/audits/raylib-client-parity/materials-notes.md` |
| Shots | `/opt/cursor/artifacts/raylib-visual-atmosphere/acceptance/` |

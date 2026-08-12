# STATUS — Raylib Visual Atmosphere

Updated by agents. Use: `pending` | `in_progress` | `blocked` | `done`.

| ID | Package | Status | Owner | Notes |
|----|---------|--------|-------|-------|
| V1 | Skybox + day/night env | done | cloud-agent | `RaylibSkyEnvironment` + skybox shaders; cool night gradient columns |
| V2 | Directional + ambient lighting | done | cloud-agent | ambient ramp moonlight floor; sun-below → same slot moon remap + cool key; billboards tinted |
| V3 | Cutout + Alpha/Additive blend | done | cloud-agent | Cutout / AlphaBlend / Additive; vegetation_cutout discard |
| V4 | Distance fog | done | cloud-agent | widened fog; night fog cool-dim |
| V5 | Reflective/refractive water FBO | done | cloud-agent | VH ocean plane + water FBO |
| V6 | Showcase + screenshot acceptance | done | cloud-agent | VH island; acceptance recapture loop |
| V7 | Height-band terrain albedo | done | cloud-agent | `terrain_albedo_environments.json` + 4 tiled albedos; not splat/PBR |
| V8 | Full PBR material spheres | blocked | — | No Core MR schema / Raylib BRDF; declared P2 in MASTER — do not invent parallel engine |

## Blockers

| Item | Why |
|------|-----|
| Full PBR | materials-notes + MASTER: host albedo-only; no metallic/roughness/normal BRDF. Needs dedicated materials track. |

## Evidence

| Artifact | Path |
|----------|------|
| Master | `docs/audits/raylib-visual-atmosphere/MASTER.md` |
| Acceptance | `docs/audits/raylib-visual-atmosphere/ACCEPTANCE.md` |
| Shots | `/opt/cursor/artifacts/raylib-visual-atmosphere/acceptance/` |

# STATUS — Raylib Visual Atmosphere

Updated by agents. Use: `pending` | `in_progress` | `blocked` | `done`.

| ID | Package | Status | Owner | Notes |
|----|---------|--------|-------|-------|
| V1 | Skybox + day/night env | pending | — | |
| V2 | Directional + ambient lighting | done | agent-v2 | RaylibFrameLighting + terrain/ISM/skin N·L+ambient; ambient_day_ramp.json |
| V3 | Cutout + Alpha/Additive blend | done | cloud-agent | material flags Cutout / Transparent|AlphaBlend / Additive; vegetation_cutout discard; VFX BeginBlendMode |
| V4 | Distance fog | pending | — | |
| V5 | Reflective/refractive water FBO | pending | — | |
| V6 | Showcase + screenshot acceptance | pending | — | depends V1–V4 (+V5 for 06) |

## Blockers

_None._

## Evidence

| Artifact | Path |
|----------|------|
| Master | `docs/audits/raylib-visual-atmosphere/MASTER.md` |
| Reference study | `docs/audits/raylib-visual-atmosphere/REFERENCE_RaylibErosionStandalone.md` |
| Acceptance | `docs/audits/raylib-visual-atmosphere/ACCEPTANCE.md` |
| Shots (opt) | `/opt/cursor/artifacts/raylib-visual-atmosphere/acceptance/` |
| Shots (repo local) | `artifacts/raylib-visual-atmosphere/acceptance/` |

# STATUS — Raylib Visual Atmosphere

Updated by agents. Use: `pending` | `in_progress` | `blocked` | `done`.

| ID | Package | Status | Owner | Notes |
|----|---------|--------|-------|-------|
| V1 | Skybox + day/night env | done | cloud-agent | `RaylibSkyEnvironment` + skybox shaders; phase from `GlobalDayNight` via latch; `Presentation/sky_environments.json` (`backendId=raylib`) |
| V2 | Directional + ambient lighting | done | agent-v2 | RaylibFrameLighting + terrain/ISM/skin N·L+ambient; ambient_day_ramp.json |
| V3 | Cutout + Alpha/Additive blend | done | cloud-agent | material flags Cutout / Transparent|AlphaBlend / Additive; vegetation_cutout discard; VFX BeginBlendMode |
| V4 | Distance fog | done | cloud-agent | `distance_fog.json` → `uFogColor`/`uFogParams` on terrain+ISM+skin; FoW untouched |
| V5 | Reflective/refractive water FBO | done | cloud-agent | `RaylibWaterPass` + upgraded `water.*`; HostLoop reflection/refraction RTs then main water; enable via `Presentation/water_environments.json` (`backendId=raylib`) |
| V6 | Showcase + screenshot acceptance | pending | — | depends V1–V5 |

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

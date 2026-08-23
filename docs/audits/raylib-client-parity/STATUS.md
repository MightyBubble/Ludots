# STATUS — Raylib Client Parity

Updated by agents. Use: `pending` | `in_progress` | `blocked` | `done`.

| ID | Package | Status | Owner | Notes |
|----|---------|--------|-------|-------|
| W1 | GPU skinned production + playback API | done | subagent-w1 | production GpuSkinnedInstance: skinning_instanced + playback + boneMatrices upload |
| W2 | Host material binding | done | subagent-w2 | albedo baseline via RaylibMaterialHostBinder; see materials-notes.md |
| W3 | Effect shader baseline | done | subagent-w3 | vfx_unlit_tint + RaylibEffectShaderRegistry; Prefab VFX → billboard mesh + tint/time shader |
| W4 | Showcase + screenshot acceptance | done | subagent-w4 | formal 01–04 via acceptance tool; launcher smoke `gpuSkinned=16/6` after host-loop fix (benchmark no longer skips performer draw) |

## Blockers

_None._

## Evidence

| Artifact | Path |
|----------|------|
| Master list | `docs/audits/raylib-client-parity/MASTER.md` |
| Acceptance write-up | `docs/audits/raylib-client-parity/ACCEPTANCE.md` |
| Showcase mod | `mods/showcases/raylib_client_parity/RaylibClientParityShowcaseMod` |
| Capture tool | 引擎画廊 `Ludots.App.RaylibEngineGallery`（原独立工具已退役） |
| Acceptance shots (repo) | `artifacts/raylib-client-parity/acceptance/` |
| Acceptance shots (opt) | `/opt/cursor/artifacts/raylib-client-parity/acceptance/` |
| `01_static_ism.png` | both acceptance dirs |
| `02_gpu_skinned_walk_a.png` / `_b.png` | both acceptance dirs (frames differ) |
| `03_material_bind.png` | both acceptance dirs |
| `04_vfx_shader.png` | both acceptance dirs |
| Capture report | `artifacts/raylib-client-parity/acceptance/capture-report.md` |

# Graph behavior pressure report

## Contract

- Present: 60 FPS
- AI think interval: 0.2s (L2 systems)
- Hard gate: one think wave for A=10_000 → **T_ai &lt; 5ms**
- Graph layer: no stagger/sleep/LOD

## Headless gates (Release, GasTests ci-gate)

| Suite | Scale | Topology | T_ai_ms | Gate |
|-------|-------|----------|--------|------|
| BT AlwaysSuccess | 10_000 | N_topo=16 | &lt;5 (ci-gate) | PASS |
| HFSM sentry | 10_000 | 6 states hierarchical | &lt;5 | PASS |
| HFSM sentry + Script lifecycle | 10_000 | + condition/OnEnter/OnTick/OnExit Scripts | &lt;5 | PASS |
| Level director | 128 armed / peakUnits 10k marker | — | &lt;5 | PASS |
| Combined BT+HFSM+Level | 10_000 | N_topo=8 | avg ~1ms | PASS |

## Matrices

### M1 — agents × BT topology (`matrix-m1.csv`)

AlwaysSuccess sequence. At A=10_000, N_topo 8→64 stays well under 5ms (≈0.02ms in matrix probe after warmup).

### M2 — agents × registered graph count G (`matrix-m2.csv`)

Fixed N_topo=16, A=10_000 split across G worlds. Sum of think waves stays flat-ish as G grows (≈0.02–0.12ms) — no linear scan by graph id.

### M3 — L1 ExecuteSlice instruction length (`matrix-m3.csv`)

| A | I | T_ms | Note |
|---|---|------|------|
| 10_000 | 32 | 6.2 | Full Script every agent — over 5ms |
| 10_000 | 128 | 21.8 | Leaves must stay sparse |
| 10_000 | 256 | 42.0 | |
| 10_000 | 1024 | 145.7 | Budget pressure |

**Interpretation:** cheap BT/HFSM topology walks clear the 5ms gate; dense Script-per-agent does not. Showcase leaves use HoldRunning / tiny ConstHalt Scripts, not long chains.

### M4 — HFSM (`matrix-m4.csv`)

Sentry hierarchy 10k half-stimulated ≈0.23ms (topology-only row).

### M5 — Level (`matrix-m5.csv`)

Armed-trigger stress ≈0.20ms.

### M6 — Ability cast waves (`matrix-m6.csv`)

| targets | I | T_ms |
|---------|---|------|
| 250 | 8–128 | ≪1ms |
| 1_000 | 32 | 0.11ms |
| 10_000 | 32 | 1.15ms |
| 10_000 | 128 | 4.10ms (still under 5ms) |

## Showcases (Raylib evidence)

主镜头是**可读剧本**（约 12 个特色角色 + 路径/触发圈），不是螺旋点阵。万人压力在无头门禁与可选灰点带。

| Showcase | Preset | 剧本 | Evidence |
|----------|--------|------|----------|
| BT | `…behavior_tree_arena_raylib` | 黄线巡逻→见红敌追打 | `*-vignette.png/mp4` |
| HFSM | `…hfsm_sentry_arena_raylib` | 门岗 Idle/警戒/交战/撤退 | same |
| Level | `…level_blueprint_trial_raylib` | 踩圈刷怪→开门 | same |
| Ability | `…ability_graph_sandbox_raylib` | 施法线打弧形靶 | same |
| Integration | `…graph_behavior_integration_raylib` | 左巡逻/右门岗/上触发短剧 | same |

Budget bar turns green when last think wave &lt; 5ms.

## Files

- `matrix-m1.csv` … `matrix-m6.csv`
- `baseline-l1-script.json`
- Generator: `GasTests` → `GraphBehaviorPressureMatrixTests`

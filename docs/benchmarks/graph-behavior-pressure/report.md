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

| Showcase | Preset | Screenshot / video |
|----------|--------|--------------------|
| BT arena | `capability_standard_behavior_tree_arena_raylib` | bt-arena.png / bt-arena.mp4 |
| HFSM sentry | `capability_standard_hfsm_sentry_arena_raylib` | hfsm-sentry.png / hfsm-sentry.mp4 |
| Level trial | `capability_standard_level_blueprint_trial_raylib` | level-trial.png / level-trial.mp4 |
| Ability sandbox | `capability_standard_ability_graph_sandbox_raylib` | ability-sandbox.png / ability-sandbox.mp4 |
| Integration | `capability_standard_graph_behavior_integration_raylib` | graph-integration.png / graph-integration.mp4 |

Budget bar (debug draw under the field) turns green when last think wave &lt; 5ms.

## Files

- `matrix-m1.csv` … `matrix-m6.csv`
- `baseline-l1-script.json`
- Generator: `GasTests` → `GraphBehaviorPressureMatrixTests`

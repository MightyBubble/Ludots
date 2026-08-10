# Graph behavior pressure report (WIP)

## Contract

- Present: 60 FPS
- AI think interval: 0.2s (L2 systems)
- Hard gate: one think wave for A=10_000 → **T_ai &lt; 5ms**
- Graph layer: no stagger/sleep/LOD

## Measured (Release, this workspace)

| Suite | A / scale | Topology | T_ai_ms | Gate |
|-------|-----------|----------|---------|------|
| L1 Script slice (drink) | 10_000 | 15 nodes → 29 instr | 9.398 (per-agent full slice) | L1-only bound; not BT think |
| BT AlwaysSuccess sequence | 10_000 | N_topo=16 | **2.134** | PASS &lt;5ms |
| FSM sentry (half stimulated) | 10_000 | 4 states | **0.057** | PASS &lt;5ms |
| Level director stress | 128 armed triggers (peakUnits marker 10k) | — | **0.202** | PASS &lt;5ms |

## Interpretation

- Cheap SoA BT/FSM walks clear the 5ms gate for 10k.
- Full Script-per-agent every think still ~9ms — Showcase leaves must stay sparse Script / small budgets.
- Next: wire Script leaves + showcases; fill M2/M3/M6 CSVs.

## Files

- `matrix-m1.csv`, `matrix-m4.csv`, `baseline-l1-script.json`

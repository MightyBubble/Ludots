# Ludots Agent Bridge

Use this skill whenever a running Ludots game process needs inspection, driving, or forensic evidence — showcase acceptance, runtime debugging, input simulation, screenshot capture.

## Rules

- Step zero is liveness: `GET /health` twice, `pumpCount` must increase; a stale pump means every later answer is untrusted.
- Always run the observe → drive → verify loop: no drive without a matching verification (gas.entity / ui.tree / screenshot / logs.tail).
- Order keys live in `mods/LudotsCoreMod/assets/GAS/order_types.json` (castAbility/moveTo/attackTarget/stop…), not in the inspect tool.
- `input.inject` press must be paired with release; `ui.click` `handled:false` means a container was hit — retry with a real elementId from `ui.query`.
- Errors carry next-step guidance; read `data.code` before retrying.

## Outputs

Structured JSON-RPC results over `POST http://127.0.0.1:47921/rpc`, evidence files under `artifacts/agent-bridge/` (shots/, recordings/).

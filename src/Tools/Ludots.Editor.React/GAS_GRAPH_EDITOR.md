# GAS Query Graph Editor (MVP)

Minimal real editor for showcase graph `ui.panel.player.resource.aggregate`.
The showcase graph is authored as Query ControlFlow pin IR, not a fake next-chain view.

It reads/writes:

`mods/showcases/ui_player_aggregate_graph_mvp/UiPlayerAggregateGraphMvpShowcaseMod/assets/GAS/graphs.json`

and validates through the Bridge with `GraphControlFlowCompiler.CompileWithOutputs` for CF documents.
`Kind: Query` / `Kind: Script` Next-chain graphs are rejected (same contract as load-time `GraphProgramConfigLoader`).

## Run

Terminal 1 — Bridge (API on `:5299`):

```bash
dotnet run --project src/Tools/Ludots.Editor.Bridge
```

Terminal 2 — React editor (Vite on `:5173`, proxies `/api` → `5299`):

```bash
cd src/Tools/Ludots.Editor.React
npm install
npm run dev
```

Open: <http://localhost:5173/gas-graphs>

Defaults:

- `modId` = `UiPlayerAggregateGraphMvpShowcaseMod`
- `graphId` = `ui.panel.player.resource.aggregate`

Toolbar link: **GAS Graphs** (top-left of the map editor).

## Bridge APIs

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/mods/{modId}/gas/graphs` | List `{ id, kind }` from `assets/GAS/graphs.json` |
| `GET` | `/api/mods/{modId}/gas/graphs/{graphId}` | Full graph JSON; CF documents include `controlEdges`/`valueEdges` |
| `PUT` | `/api/mods/{modId}/gas/graphs/{graphId}` | Replace graph in array (atomic temp+replace); Query/Script must be CF pin IR |
| `POST` | `/api/mods/{modId}/gas/graphs/{graphId}/validate` | Body graph JSON or empty (load file) → CF compiler when `controlEdges`/`valueEdges` are present; Query/Script Next-chain fails closed |

Validate response shape:

```json
{
  "ok": true,
  "diagnostics": [{ "severity": "Error", "code": "GASG0004", "message": "...", "graphId": "...", "nodeId": "..." }],
  "instructionCount": 5
}
```

`ok` is false when compile fails. Save in the UI validates first and refuses write on failure.

## Query CF pins

- Control edges render in blue and use `fromPort: "next"`.
- Value edges render by pin type: `list` edges in green and `value` edges in violet.
- `QueryFilterTeam` uses an explicit `list` value input and either a `teamId` node field or `teamId` int value input.
- `AggSumAttribute` uses an explicit `list` value input and an `attribute` node field.
- The Bridge rejects mixed CF and legacy JSON (`controlEdges`/`valueEdges` plus `nodes[].next`).
- Query Next-chain without CF edges fails closed (use `controlEdges`/`valueEdges`).

## curl smoke

```bash
MOD=UiPlayerAggregateGraphMvpShowcaseMod
GID=ui.panel.player.resource.aggregate
BASE=http://localhost:5299

curl -s "$BASE/api/mods/$MOD/gas/graphs" | jq .
curl -s "$BASE/api/mods/$MOD/gas/graphs/$GID" | jq .

# Validate file on disk
curl -s -X POST "$BASE/api/mods/$MOD/gas/graphs/$GID/validate" | jq .

# Validate body (OK)
curl -s -X POST "$BASE/api/mods/$MOD/gas/graphs/$GID/validate" \
  -H 'Content-Type: application/json' \
  -d @<(curl -s "$BASE/api/mods/$MOD/gas/graphs/$GID" | jq '.graph') | jq .

# Invalid op must fail
curl -s -X POST "$BASE/api/mods/$MOD/gas/graphs/$GID/validate" \
  -H 'Content-Type: application/json' \
  -d @<(curl -s "$BASE/api/mods/$MOD/gas/graphs/$GID" | jq '.graph | .nodes[0].op="NotARealOp"') | jq .
```

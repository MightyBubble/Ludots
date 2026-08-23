# Ludots Graph Editor

This is the authoring surface for the runtime `GraphControlFlowDocument` contract. It reads the graph JSON through `Ludots.Editor.Bridge`, projects node ports from the runtime descriptor table, validates through `GraphProgramAuthoringFrontDoor`, and writes only author data to `graphs.json`.

Canvas layout is stored separately in `assets/GAS/graph_editor.json`; viewport/editor state never enters the runtime graph contract.

## Live debug

Start the game with `AgentBridgeMod`, then open `/gas-graphs`. `Refresh Live` discovers mounted TriggerGraph entries. Select one and press `Watch`. The bridge uses `ludots.graph.debug` to configure an opt-in fixed-capacity trace ring and polls only records newer than the last sequence. Node execution, suspension/halt, cursor state, and changed register pins are shown without sending a full VM snapshot each frame.

The trace is disabled by default. A full ring is reported as `gap: true` with an explicit `droppedCount`; the editor clears its local event window instead of silently presenting incomplete history.

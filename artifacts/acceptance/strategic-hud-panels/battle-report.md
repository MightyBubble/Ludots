# Strategic HUD Panels 运行验收

## 结果

markup skin 的真实 Raylib 入口已启动并完成首屏验收。Agent Bridge 的 `pumpCount` 从 3572 增长到 3610，地图为 `strategic_hud_panels`；UI 树包含八个 panel surface，左右中位面板的 y 坐标均为 384，符合垂直居中合同。

## MUD-style timeline

```text
[T+00] Launcher 载入 StrategicHudPanelsMod + AgentBridgeMod
[T+01] MapLoaded -> Graph.StrategicHud.OpenAll
[T+02] PanelHost 创建 time/view/minimap/selection/entities/events/command/subsystems
[T+03] Graph.StrategicHud.Values 投影面板状态实体属性
[T+04] UiSurfaceHost 挂载 8 个 retained panel surface
[T+05] AgentBridge health pumpCount 3572 -> 3610，主循环持续运行
[T+06] 截图确认左中/右中面板位于 y=384，未错误堆到底部
```

## Evidence

- `artifacts/acceptance/strategic-hud-panels/trace.jsonl`
- `artifacts/acceptance/strategic-hud-panels/path.mmd`
- `artifacts/agent-bridge/shots/strategic-hud-panels-first-viewport.png`
- `mods/showcases/strategic_hud_panels/StrategicHudPanelsMod/assets/Panels/panel_templates.json`
- `src/Libraries/Ludots.UI.Panels/PanelPresentationSystem.cs`

## 未覆盖

本次证据只覆盖 markup 首屏；compose、reactive、default 的具体运行截图，以及列表、图片、Canvas 小地图和按钮命令链不在本次验收范围内。

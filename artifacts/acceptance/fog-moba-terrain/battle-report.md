# Fog Moba Terrain 验收记录

## 状态

已通过无头生产路径验收，并完成真实 Raylib + Agent Bridge 启动、交互和截图取证。

## Feature

Feature: 玩家在 MOBA 地形中读取战争迷雾

  Scenario: 移动观察者改变真实视野场
    Given 玩家进入 `fog_moba_terrain_showcase`
    When 玩家用 WASD 移动并用方向键转向
    Then Core `VisionSystem` 更新同一 `FogField`
    And Raylib presenter 上传对应的可见/已探索/未知覆盖层

  Scenario: 墙体与草丛规则可消融
    Given 玩家看到中央墙体和两条草丛带
    When 玩家按 `F`
    Then HUD 显示 Rules OFF 或 Rules ON
    And 同一观察者位置的场状态随规则变化

  Scenario: 离开区域后保留探索记忆
    Given 玩家已经走过中路入口
    When 玩家离开该区域并按 `M`
    Then HUD 显示 memory 状态
    And Core 字段在 Enabled 时保留 Explored 状态

## 无头证据

- `FogMobaTerrainShowcaseAcceptanceTests`: 1/1 通过。
- Mod build: 0 errors.
- 使用的生产组件：`VisionSystem`、`FogCellMap`、`FogFieldStore`、`FogGlobalFieldVisualProjector`、`GlobalFieldVisualBuffer`。
- Agent Bridge session: `mapId=fog_moba_terrain_showcase`; mods include `FogMobaTerrainShowcaseMod` and `AgentBridgeMod`; `/health` pumpCount advanced 54 -> 57.
- Screens: `screens/001-initial.png`, `screens/002-after-follow.png` and `artifacts/agent-bridge/shots/fog-moba-controls.png`.

# cfg-06 配置说明 · 游戏配置

> 配置写法与行为。第一性需求见 [cfg-06 PRD](../prd/cfg-06-game-config.md)；编辑器需求见 [UXD](../uxd/cfg-06-game-config.md)；现状见 [reference](../reference/cfg-06-game-config.md)。

## 1. 示例配置

引擎默认基线（`Core:game.json` 节选）：

```json
{
  "defaultCoreMod": "LudotsCoreMod",
  "windowWidth": 1280, "windowHeight": 720, "windowResizable": true,
  "targetFps": 0,
  "simulationBudgetMsPerFrame": 4,
  "simulationMaxSlicesPerLogicFrame": 120,
  "gasRuntimeCapacity": { "orderQueueCapacity": 4096, "orderAdmissionResultCapacity": 8192 },
  "gridCellSizeCm": 100
}
```

`targetFps` 基线为 0（代码默认 60）；`startupMapId` 与 `presentation` 不在引擎基线里，由核心 mod 的覆盖提供。

## 2. 字段与行为

| 分组 | 字段 | 这样配会产生什么效果 |
|---|---|---|
| 启动 | `defaultCoreMod` / `startupMapId` / `startupLocalPlayerId` / `startupInputContexts` | 默认核心 mod、启动地图、本地玩家、启动即激活的输入上下文 |
| 窗口 | `windowWidth` / `windowHeight` / `windowResizable` / `windowStartMaximized` / `windowTitle` | 窗口形态，默认 1280×720 |
| 帧率 | `targetFps` | 目标帧率，默认 60 |
| 仿真 | `simulationBudgetMsPerFrame` / `simulationMaxSlicesPerLogicFrame` | 每帧仿真预算（毫秒）与最大切片数，默认 4 / 120 |
| 世界 | `gridCellSizeCm` / `worldWidthInMacroTiles` / `worldHeightInMacroTiles` | 网格与世界尺寸，默认 100 / 64 / 64 |
| 物理 | `physics2D.enabled` | 2D 物理开关 |
| 容量 | `gasRuntimeCapacity.*`（项数与逐项取值见 [事实与取值表](../facts.md)，当前 17 项，含效果请求队列容量） | 各运行时队列/快照上限；交叉约束：准入结果 ≥ 订单队列 × 2、准入拒绝 ≥ 订单队列；两项工作预算另校验有限 |
| 表现 | `presentation` | 表现运行时配置，**必填** |
| 其余 | `logging` / `browserRuntime` / `commandSource` | 日志、内嵌浏览器（编辑器 UI 依赖）、指令源 |
| 常量 | `constants.*` 五张表 | 订单号、响应链订单号、属性名、通用整数/字符串——数据驱动常量 |

## 3. 文件结构

`game.json` 不走目录：引擎默认在 assets/ 根，各 mod 可带一份，深合并成单一配置。建议项目只覆盖用到的字段。

## 4. 运行时加载效果

启动期合并一次、生成单一运行配置；窗口/地图/玩家/预算立即生效；常量表供引擎与 mod 查号；此后不变。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| `presentation` 缺失 | 启动失败 |
| 容量非正或无限 | 启动失败，指明字段 |
| 交叉约束不满足 | 启动失败，指明字段与要求 |
| 字段类型错误 | 启动失败 |

## 6. 实例

- 引擎基线：`assets/game.json`；项目覆盖示例：`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/game.json`

**相关文档**：[cfg-06 PRD](../prd/cfg-06-game-config.md) · [cfg-04 配置说明](cfg-04-config-tables.md)（目录内表的对照）

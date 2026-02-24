---
文档类型: 配置结构
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v1.0
适用范围: 核心引擎 - 总架构 - game.json配置
状态: 已实现
依赖文档:
  - docs/02_核心引擎/01_总架构/03_ConfigPipeline_架构设计.md
---

# game.json 配置结构

# 1 概述

## 1.1 本文档定义

本文档定义 `game.json` 的文件结构、字段说明、多层合并规则，以及不同层（App/Core/Mod）的典型配置内容。

## 1.2 使用场景与边界

使用场景：
- 定义引擎运行参数（窗口尺寸、帧率、预算）
- 定义 Mod 加载路径
- 定义游戏常量（OrderTags、GasOrderTags、Attributes）
- 定义启动地图

边界：
- 本文档只定义 `game.json` 的格式
- 不定义其他配置文件（如 `templates.json`、`hud.json`）

## 1.3 关联运行时消费点

- `src/Core/Config/GameConfig.cs`：配置数据结构
- `src/Core/Config/ConfigPipeline.cs`：配置合并管线
- `src/Core/Engine/GameEngine.cs`：运行时消费 `MergedConfig`

# 2 文件位置与命名

## 2.1 目录位置

| 层级 | 路径 | 用途 |
|---|---|---|
| App 层 | `src/Apps/{AppName}/game.json` | 定义 ModPaths |
| Core 层 | `assets/Configs/game.json` | 定义 DefaultCoreMod、引擎默认值 |
| Mod 层 | `src/Mods/{ModName}/assets/game.json` | 定义 Constants、StartupMapId 等 |

## 2.2 命名规则

- 文件名固定为 `game.json`
- 大小写敏感：必须小写

## 2.3 发现与加载机制

1. App 层：由 `GameBootstrapper` 直接读取
2. Core 层：由 `ConfigPipeline` 从 `coreAssetsRoot/Configs/` 读取
3. Mod 层：由 `ConfigPipeline` 从 `{modPath}/assets/` 读取

加载顺序：
1. Core 层（基础配置）
2. CoreMod（priority=-1000）
3. 其他 Mods（按 mod.json 中的 priority 升序排列）

# 3 完整示例

## 3.1 最小可用示例

Core 层（`assets/Configs/game.json`）：
```json
{
  "defaultCoreMod": "LudotsCoreMod",
  "windowWidth": 1280,
  "windowHeight": 720,
  "windowTitle": "Ludots Engine (Raylib)",
  "targetFps": 60,
  "simulationBudgetMsPerFrame": 4,
  "simulationMaxSlicesPerLogicFrame": 120,
  "gridCellSizeCm": 100,
  "worldWidthInTiles": 64,
  "worldHeightInTiles": 64
}
```

LudotsCoreMod 层（`src/Mods/LudotsCoreMod/assets/game.json`）：
```json
{
  "startupMapId": "entry",
  "constants": {
    "orderTags": {
      "castAbility": 100,
      "moveTo": 101,
      "attackTarget": 102,
      "stop": 103
    },
    "gasOrderTags": {
      "chainPass": 1,
      "chainNegate": 2,
      "chainActivateEffect": 3
    },
    "attributes": {
      "health": "Health",
      "mana": "Mana"
    }
  }
}
```

App 层（`src/Apps/Raylib/Ludots.App.Raylib/game.json`）：
```json
{
  "modPaths": [
    "Mods/LudotsCoreMod",
    "Mods/MobaDemoMod",
    "Mods/PerformanceVisualizationMod"
  ]
}
```

## 3.2 常见变体

自定义 Mod 扩展 OrderTags：
```json
{
  "constants": {
    "orderTags": {
      "customAbility": 200,
      "specialMove": 201
    }
  }
}
```

覆盖启动地图：
```json
{
  "startupMapId": "custom_entry"
}
```

# 4 字段说明

## 4.1 顶层字段

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---:|---|---|
| modPaths | string[] | 否 | [] | Mod 路径列表（相对于 App 根目录） |
| defaultCoreMod | string | 否 | null | 默认核心 Mod 名称 |
| startupMapId | string | 否 | null | 启动地图 ID |
| startupInputContexts | string[] | 否 | [] | 启动时激活的输入上下文 |
| windowWidth | int | 否 | 1280 | 窗口宽度（像素） |
| windowHeight | int | 否 | 720 | 窗口高度（像素） |
| windowTitle | string | 否 | "Ludots Engine" | 窗口标题 |
| targetFps | int | 否 | 60 | 目标帧率 |
| simulationBudgetMsPerFrame | int | 否 | 4 | 每帧仿真预算（毫秒） |
| simulationMaxSlicesPerLogicFrame | int | 否 | 120 | 每逻辑帧最大切片数 |
| gridCellSizeCm | int | 否 | 100 | 网格单元尺寸（厘米） |
| worldWidthInTiles | int | 否 | 64 | 世界宽度（Tile 数） |
| worldHeightInTiles | int | 否 | 64 | 世界高度（Tile 数） |
| constants | GameConstants | 否 | {} | 游戏常量字典 |

## 4.2 constants 字段

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---:|---|---|
| orderTags | Dict<string, int> | 否 | {} | 命令标签（用于 OrderDispatch） |
| gasOrderTags | Dict<string, int> | 否 | {} | GAS 命令标签（用于响应链） |
| attributes | Dict<string, string> | 否 | {} | 属性注册表（键为代码标识，值为显示名） |

## 4.3 常用 orderTags 键

| 键 | 典型值 | 说明 |
|---|---:|---|
| castAbility | 100 | 施放技能 |
| moveTo | 101 | 移动到目标点 |
| attackTarget | 102 | 攻击目标 |
| stop | 103 | 停止当前动作 |

## 4.4 常用 gasOrderTags 键

| 键 | 典型值 | 说明 |
|---|---:|---|
| chainPass | 1 | 响应链传递 |
| chainNegate | 2 | 响应链否决 |
| chainActivateEffect | 3 | 响应链激活效果 |

# 5 校验规则

## 5.1 加载期强校验

| 规则 | fail-fast 行为 |
|---|---|
| JSON 格式错误 | 抛出异常，包含文件路径与行号 |
| 字段类型不匹配 | 抛出异常，包含字段名与期望类型 |
| 必需字段缺失 | 由消费方校验，缺失时抛出异常 |

## 5.2 错误信息与诊断

错误信息格式：
```
[ConfigPipeline] Failed to parse game.json at {filePath}: {errorMessage}
```

示例：
```
[ConfigPipeline] Failed to parse game.json at src/Mods/CustomMod/assets/game.json: 
  Unexpected character encountered while parsing value: }. Path 'constants.orderTags', line 5, position 3.
```

# 6 版本兼容性

## 6.1 版本号策略

- 当前版本：v1.0
- 版本号记录在文档元信息中，不嵌入 JSON
- 破坏性变更需提升主版本号

## 6.2 破坏性变更与迁移方式

本架构明确不保留向后兼容。破坏性变更时：
1. 更新本文档
2. 更新 `GameConfig.cs` 数据结构
3. 全量更新所有 `game.json` 文件
4. 更新相关单元测试

# 7 合并规则

## 7.1 字典字段合并

`constants.orderTags`、`constants.gasOrderTags`、`constants.attributes` 为字典字段，采用深度合并：

```
Core:    { "orderTags": { "moveTo": 101 } }
CoreMod: { "orderTags": { "moveTo": 101, "castAbility": 100 } }
CustomMod: { "orderTags": { "customTag": 200 } }

Merged:  { "orderTags": { "moveTo": 101, "castAbility": 100, "customTag": 200 } }
```

## 7.2 非字典字段覆盖

其他字段（如 `startupMapId`、`windowWidth`）采用直接覆盖：

```
Core:    { "startupMapId": null }
CoreMod: { "startupMapId": "entry" }
CustomMod: { "startupMapId": "custom_entry" }

Merged:  { "startupMapId": "custom_entry" }  // 最后加载的 Mod 覆盖
```

## 7.3 优先级排序

Mods 按 `mod.json` 中的 `priority` 字段升序排列：
- `priority = -1000`：LudotsCoreMod（最低，最先加载）
- `priority = 0`：默认值
- `priority > 0`：高优先级 Mod（后加载，可覆盖）

---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v1.0
适用范围: 核心引擎 - 总架构 - CoreMod架构
状态: 已实现
依赖文档:
  - docs/02_核心引擎/01_总架构/01_GameEngine_架构设计.md
  - docs/01_底层框架/02_Mod与VFS/00_总览.md
---

# CoreMod 架构设计

# 1 设计概述

## 1.1 本文档定义

本文档定义 Core 层与 LudotsCoreMod 的职责切分、ConfigPipeline 的分层合并机制，以及数据驱动常量系统的设计目标与边界。

边界：
- 本文档只定义架构层面的职责切分与数据流
- 不定义具体 Mod 的业务逻辑实现
- 不覆盖 Mod 加载与 VFS 细节（参见 `docs/01_底层框架/02_Mod与VFS/`）

非目标：
- 向后兼容：本架构明确不保留任何 fallback 或兼容分支
- 硬编码常量：所有游戏常量必须通过 ConfigPipeline 从 JSON 加载

## 1.2 设计目标

1. **Core 层纯基建化**：Core 层只提供引擎能力与注册机制，不持有任何游戏内容
2. **LudotsCoreMod 作为核心框架**：所有游戏框架默认配置、系统、资产由 LudotsCoreMod 提供
3. **ConfigPipeline 分层合并**：App → Core → Mods 的 `game.json` 自动深度合并
4. **数据驱动常量**：`OrderTags`、`GasOrderTags`、`Attributes` 等常量从 `GameConfig.Constants` 读取

## 1.3 设计思路

参照 StarCraft 2 的 Core.SC2Mod 设计：
- 核心引擎是纯基建，只提供"能力"而不提供"内容"
- 游戏框架的默认规则、常量、系统由一个特殊的"核心 Mod"提供
- Mods 之间平等，只是加载优先级不同；Core 本身的 `game.json` 也只是一个最底层的配置层

# 2 功能总览

## 2.1 术语表

| 术语 | 定义 |
|---|---|
| Core 层 | `src/Core/` 纯基建引擎，不含任何游戏内容 |
| LudotsCoreMod | 核心游戏框架 Mod，提供默认配置、系统、资产 |
| ConfigPipeline | 配置合并管线，负责收集与深度合并各层 `game.json` |
| GameConfig | 合并后的最终配置对象 |
| GameConstants | `GameConfig.Constants`，数据驱动的常量字典 |
| MergedConfig | 引擎持有的合并后配置，通过 `ContextKeys.GameConfig` 暴露 |

## 2.2 功能导图

```
App game.json (ModPaths)
       │
       ▼
Core game.json (DefaultCoreMod, 引擎默认值)
       │
       ▼
LudotsCoreMod game.json (Constants, StartupMapId, 默认配置)
       │
       ▼
Other Mods game.json (覆盖/扩展)
       │
       ▼
   ConfigPipeline.Merge()
       │
       ▼
   GameConfig (MergedConfig)
```

## 2.3 架构图

```
┌────────────────────────────────────────────────────────────────┐
│ App Layer (src/Apps/)                                           │
│   game.json: ModPaths                                           │
└─────────────────────────────────┬──────────────────────────────┘
                                  │
┌─────────────────────────────────▼──────────────────────────────┐
│ Core Layer (src/Core/)                                          │
│   assets/Configs/game.json: DefaultCoreMod, 引擎参数            │
│   Engine/GameEngine.cs: 纯调度，从 MergedConfig 读取常量       │
│   Config/ConfigPipeline.cs: 合并管线                            │
│   Config/GameConfig.cs: 配置数据结构                            │
└─────────────────────────────────┬──────────────────────────────┘
                                  │
┌─────────────────────────────────▼──────────────────────────────┐
│ LudotsCoreMod (src/Mods/LudotsCoreMod/)                         │
│   assets/game.json: Constants, StartupMapId                     │
│   assets/Maps/entry.json: 默认入口地图                          │
│   assets/Entities/templates.json: 默认模板                      │
│   Triggers/InstallCoreSystemsOnGameStartTrigger.cs              │
└────────────────────────────────────────────────────────────────┘
                                  │
┌─────────────────────────────────▼──────────────────────────────┐
│ Other Mods (MobaDemoMod, PerformanceMod, etc.)                  │
│   可覆盖 Constants、添加 OrderTags、扩展配置                    │
└────────────────────────────────────────────────────────────────┘
```

## 2.4 关联依赖

- GameEngine 生命周期：`docs/02_核心引擎/01_总架构/01_GameEngine_架构设计.md`
- Mod 加载机制：`docs/01_底层框架/02_Mod与VFS/00_总览.md`
- ConfigPipeline 详细设计：`docs/02_核心引擎/01_总架构/03_ConfigPipeline_架构设计.md`
- game.json 配置结构：`docs/02_核心引擎/01_总架构/04_game.json_配置结构.md`

# 3 业务设计

## 3.1 业务用例与边界

用例：
- 引擎启动：ConfigPipeline 自动合并所有层的 `game.json`，生成 `MergedConfig`
- 系统读取常量：所有系统从 `MergedConfig.Constants` 读取 `OrderTags`、`GasOrderTags` 等
- Mod 覆盖配置：高优先级 Mod 的配置自动覆盖低优先级 Mod 的同名字段

边界：
- Core 层不定义任何 `OrderTags`/`GasOrderTags`/`Attributes` 常量
- Core 层不直接注册 `WorldHudCollectorSystem`、`Orbit3CCameraController` 等游戏系统
- 所有默认配置与系统由 LudotsCoreMod 提供

## 3.2 业务主流程

```
GameBootstrapper.InitializeFromBaseDirectory()
  │
  ├─ 1. 加载 App/game.json → 获取 ModPaths
  │
  ├─ 2. 加载 Core/assets/Configs/game.json → 获取 DefaultCoreMod, 引擎参数
  │
  ├─ 3. 按 ModPaths 顺序加载各 Mod 的 assets/game.json
  │
  ├─ 4. ConfigPipeline.MergeGameConfig() → 深度合并 → GameConfig
  │
  └─ 5. GameEngine.InitializeWithConfigPipeline(config) → MergedConfig 注入 GlobalContext
```

## 3.3 关键场景与异常分支

| 场景 | 处理方式 |
|---|---|
| Mod 的 `game.json` 不存在 | 跳过该 Mod 的配置层，不报错 |
| 必需常量缺失 | fail-fast，抛出异常明确指出缺失的键 |
| 常量类型不匹配 | fail-fast，JSON 反序列化时抛出异常 |

# 4 数据模型

## 4.1 概念模型

```
GameConfig
├── ModPaths: List<string>           // App 层定义
├── DefaultCoreMod: string           // Core 层定义
├── StartupMapId: string             // CoreMod 或上层 Mod 定义
├── WindowWidth/Height/Title: int    // Core 层默认值
├── TargetFps: int
├── SimulationBudgetMsPerFrame: int
├── SimulationMaxSlicesPerLogicFrame: int
├── GridCellSizeCm: int
├── WorldWidthInTiles: int
├── WorldHeightInTiles: int
└── Constants: GameConstants
    ├── OrderTags: Dictionary<string, int>
    ├── GasOrderTags: Dictionary<string, int>
    └── Attributes: Dictionary<string, string>
```

## 4.2 数据结构与不变量

1. `GameConfig` 为不可变对象：一旦 `MergedConfig` 生成，运行期间不可修改
2. `Constants` 字典键唯一：同名键由高优先级 Mod 覆盖
3. 合并顺序为 SSOT：App → Core → Mods（按 priority 排序）

## 4.3 生命周期/状态机

```
Created (Empty)
    │
    ▼
Merging (ConfigPipeline 执行中)
    │
    ▼
Finalized (MergedConfig 注入 GlobalContext)
```

# 5 落地方式

## 5.1 模块划分与职责

| 模块 | 职责 |
|---|---|
| `src/Core/Config/GameConfig.cs` | 定义配置数据结构 |
| `src/Core/Config/ConfigPipeline.cs` | 实现配置收集与深度合并 |
| `src/Core/Hosting/GameBootstrapper.cs` | 编排启动流程，调用 ConfigPipeline |
| `src/Core/Engine/GameEngine.cs` | 持有 MergedConfig，从中读取常量 |
| `src/Mods/LudotsCoreMod/` | 提供默认 game.json、Maps、Entities、Systems |

## 5.2 关键接口与契约

常量读取方式：
```csharp
// 从 MergedConfig 读取 OrderTag
int moveToTag = engine.MergedConfig.Constants.OrderTags["moveTo"];

// 从 GlobalContext 读取 GameConfig
var config = context.Get<GameConfig>(ContextKeys.GameConfig);
int chainPass = config.Constants.GasOrderTags["chainPass"];
```

## 5.3 运行时关键路径与预算点

- 配置合并只在启动时执行一次，不影响运行时性能
- 常量读取为 O(1) 字典查找

# 6 与其他模块的职责切分

## 6.1 切分结论

| 模块 | 职责 | 不负责 |
|---|---|---|
| Core 层 | 引擎能力、调度机制、配置合并管线 | 任何游戏内容、常量定义、默认系统 |
| LudotsCoreMod | 默认常量、默认系统、默认资产 | 引擎调度机制 |
| 业务 Mods | 扩展/覆盖配置与系统 | 修改引擎核心逻辑 |

## 6.2 为什么如此

- 保持 Core 层的稳定性：业务变更不影响引擎
- 支持完全替换核心框架：可以用另一个 CoreMod 替换 LudotsCoreMod
- 遵循 StarCraft 2 的成功模式：分离基建与内容

## 6.3 影响范围

已删除的硬编码常量文件：
- `src/Core/Gameplay/GAS/Orders/OrderTags.cs`
- `src/Core/Gameplay/GAS/Orders/GasOrderTags.cs`
- `src/Core/Gameplay/GAS/Registry/GameAttributes.cs`
- `src/Core/Map/MapIds.cs`

已迁移到 LudotsCoreMod 的资产：
- `assets/Configs/Maps/entry.json` → `src/Mods/LudotsCoreMod/assets/Maps/entry.json`
- `assets/Configs/Entities/templates.json` → `src/Mods/LudotsCoreMod/assets/Entities/templates.json`
- `assets/Configs/Presentation/hud.json` → `src/Mods/LudotsCoreMod/assets/Presentation/hud.json`

# 7 当前代码现状

## 7.1 现状入口

- 配置数据结构：`src/Core/Config/GameConfig.cs`
- 配置合并管线：`src/Core/Config/ConfigPipeline.cs`
- 启动编排：`src/Core/Hosting/GameBootstrapper.cs`
- LudotsCoreMod：`src/Mods/LudotsCoreMod/`
- LudotsCoreMod game.json：`src/Mods/LudotsCoreMod/assets/game.json`

## 7.2 差距清单

| 设计口径 | 代码现状 | 差距等级 | 风险 | 证据 |
|---|---|---|---|---|
| Core 层纯基建化 | ✅ 已完成 | 无 | 无 | `src/Core/Engine/GameEngine.cs` |
| 硬编码常量删除 | ✅ 已删除 | 无 | 无 | 文件已删除 |
| LudotsCoreMod 提供默认配置 | ✅ 已完成 | 无 | 无 | `src/Mods/LudotsCoreMod/assets/game.json` |
| ConfigPipeline 合并机制 | ✅ 已实现 | 无 | 无 | `src/Core/Config/ConfigPipeline.cs` |
| 所有 Mods 使用数据驱动常量 | ✅ 已完成 | 无 | 无 | 各 Mod 文件 |

## 7.3 迁移策略与风险

不适用。本架构为全新实现，无历史迁移需求。

# 8 验收条款

1. **Core 层无游戏内容**：`src/Core/` 目录下不存在 `OrderTags.cs`、`GasOrderTags.cs`、`GameAttributes.cs`、`MapIds.cs` 等硬编码常量文件
   - 验证方法：`grep -r "OrderTags\|GasOrderTags\|GameAttributes\|MapIds" src/Core/ --include="*.cs"` 无结果（除 tests）
   
2. **LudotsCoreMod 提供默认配置**：`src/Mods/LudotsCoreMod/assets/game.json` 存在且包含 `constants` 字段
   - 验证方法：文件存在且 JSON 有效
   
3. **所有系统从 MergedConfig 读取常量**：无系统直接引用已删除的常量类
   - 验证方法：`dotnet build` 成功
   
4. **ConfigPipeline 正确合并配置**：`MergedConfig.Constants` 包含 LudotsCoreMod 定义的所有常量
   - 验证方法：单元测试 `GasTests` 全部通过（118/118）

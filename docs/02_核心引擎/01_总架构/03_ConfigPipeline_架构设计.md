---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v1.0
适用范围: 核心引擎 - 总架构 - ConfigPipeline
状态: 已实现
依赖文档:
  - docs/02_核心引擎/01_总架构/02_CoreMod_架构设计.md
---

# ConfigPipeline 架构设计

# 1 设计概述

## 1.1 本文档定义

本文档定义 ConfigPipeline 的职责边界、配置收集顺序、深度合并规则，以及与 Mod 系统的集成方式。

边界：
- 本文档只定义配置合并管线的设计
- 不定义 `game.json` 的具体字段（参见 `04_game.json_配置结构.md`）
- 不定义 Mod 加载机制（参见 `docs/01_底层框架/02_Mod与VFS/`）

非目标：
- 运行时配置修改：合并只在启动时执行一次
- 向后兼容：不保留旧格式 fallback

## 1.2 设计目标

1. **分层合并**：App → Core → Mods 的配置按层级自动合并
2. **深度合并**：字典类型字段（如 `Constants.OrderTags`）按键合并，非字典字段直接覆盖
3. **优先级控制**：高优先级 Mod 的配置覆盖低优先级
4. **fail-fast**：配置解析失败或必需字段缺失时立即报错

## 1.3 设计思路

采用"多层叠加"模式：
1. 从最底层（Core）开始，逐层叠加配置
2. 字典类型深度合并，允许 Mod 只定义增量
3. 非字典类型直接覆盖，后加载的配置优先

# 2 功能总览

## 2.1 术语表

| 术语 | 定义 |
|---|---|
| ConfigPipeline | 配置合并管线，负责收集与合并各层配置 |
| ConfigLayer | 单个配置层（App/Core/Mod） |
| DeepMerge | 字典字段按键合并的合并策略 |
| Priority | Mod 优先级，数值越小优先级越低（-1000 为 CoreMod） |

## 2.2 功能导图

```
1. CollectConfigLayers()
   ├─ App Layer: {appRoot}/game.json
   ├─ Core Layer: {coreAssetsRoot}/Configs/game.json
   └─ Mod Layers: {modPath}/assets/game.json (按 priority 排序)

2. MergeGameConfig(layers)
   ├─ 基础配置 = Core Layer
   ├─ foreach layer in [CoreMod, ...Mods by priority]:
   │   └─ result = DeepMerge(result, layer)
   └─ return result

3. Output: GameConfig (MergedConfig)
```

## 2.3 架构图

```
┌─────────────────────────────────────────────────────────────┐
│ ConfigPipeline                                               │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐       │
│  │ App Layer    │  │ Core Layer   │  │ Mod Layers   │       │
│  │ (ModPaths)   │  │ (Defaults)   │  │ (by priority)│       │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘       │
│         │                 │                 │                │
│         └────────────────┼─────────────────┘                │
│                          │                                   │
│                          ▼                                   │
│                 ┌────────────────┐                           │
│                 │  DeepMerge()   │                           │
│                 └────────┬───────┘                           │
│                          │                                   │
│                          ▼                                   │
│                 ┌────────────────┐                           │
│                 │  GameConfig    │                           │
│                 └────────────────┘                           │
└─────────────────────────────────────────────────────────────┘
```

## 2.4 关联依赖

- CoreMod 架构：`docs/02_核心引擎/01_总架构/02_CoreMod_架构设计.md`
- game.json 配置结构：`docs/02_核心引擎/01_总架构/04_game.json_配置结构.md`
- Mod 加载：`docs/01_底层框架/02_Mod与VFS/00_总览.md`

# 3 业务设计

## 3.1 业务用例与边界

用例：
- 引擎启动时自动合并所有配置
- Mod 只需定义增量配置，无需复制完整配置
- 高优先级 Mod 可覆盖低优先级 Mod 的配置

边界：
- ConfigPipeline 只负责合并，不负责验证业务逻辑
- 合并后的配置由消费方自行验证必需字段

## 3.2 业务主流程

```
ConfigPipeline.MergeGameConfig()
│
├─ 1. 读取 Core game.json → baseConfig
│
├─ 2. 获取 ModPaths 列表
│
├─ 3. 按 priority 排序 Mods
│
├─ 4. foreach mod in sortedMods:
│   │
│   ├─ 读取 {modPath}/assets/game.json
│   │
│   └─ result = DeepMerge(result, modConfig)
│       │
│       ├─ 字典字段: 合并键值对
│       │   Constants.OrderTags["moveTo"] = 101
│       │   Constants.OrderTags["customTag"] = 200  (新增)
│       │
│       └─ 非字典字段: 直接覆盖
│           StartupMapId = "custom_map"  (覆盖)
│
└─ 5. return result (MergedConfig)
```

## 3.3 关键场景与异常分支

| 场景 | 行为 |
|---|---|
| Mod 的 game.json 不存在 | 跳过该 Mod 的配置层 |
| JSON 解析失败 | fail-fast，抛出异常并指明文件路径 |
| 字段类型不匹配 | fail-fast，JSON 反序列化异常 |
| 字典键冲突 | 高优先级覆盖低优先级 |

# 4 数据模型

## 4.1 概念模型

```
ConfigLayer
├── source: string (文件路径)
├── priority: int (排序依据)
└── config: GameConfig (解析后的配置)

MergeResult
├── layers: List<ConfigLayer> (已合并的层)
└── config: GameConfig (最终配置)
```

## 4.2 数据结构与不变量

1. **合并顺序不变量**：`Core (priority=-∞) → CoreMod (priority=-1000) → Mods (by priority asc)`
2. **字典合并规则**：`result[key] = later[key] ?? earlier[key]`
3. **非字典覆盖规则**：`result = later ?? earlier`

## 4.3 生命周期/状态机

```
Init
 │
 ▼
Collecting (收集配置层)
 │
 ▼
Merging (逐层合并)
 │
 ▼
Finalized (输出 GameConfig)
```

# 5 落地方式

## 5.1 模块划分与职责

| 模块 | 职责 |
|---|---|
| `ConfigPipeline.cs` | 收集配置层、执行深度合并 |
| `GameConfig.cs` | 定义配置数据结构 |
| `GameBootstrapper.cs` | 调用 ConfigPipeline，传递结果给 GameEngine |

## 5.2 关键接口与契约

```csharp
public class ConfigPipeline
{
    /// <summary>
    /// 合并所有配置层，返回最终 GameConfig
    /// </summary>
    /// <param name="appConfigPath">App 层 game.json 路径</param>
    /// <param name="coreAssetsRoot">Core 层 assets 根目录</param>
    /// <param name="modPaths">Mod 路径列表</param>
    /// <returns>合并后的 GameConfig</returns>
    public GameConfig MergeGameConfig(
        string appConfigPath,
        string coreAssetsRoot,
        IReadOnlyList<string> modPaths);
}
```

深度合并伪代码：
```
DeepMerge(base, overlay):
    foreach field in overlay:
        if field is Dictionary:
            base[field] = MergeDictionary(base[field], overlay[field])
        else if overlay[field] != default:
            base[field] = overlay[field]
    return base
```

## 5.3 运行时关键路径与预算点

- 只在启动时执行一次，不影响运行时性能
- 配置文件通常很小（<10KB），IO 开销可忽略

# 6 与其他模块的职责切分

## 6.1 切分结论

| 模块 | 职责 | 不负责 |
|---|---|---|
| ConfigPipeline | 配置收集与合并 | 配置验证、业务逻辑 |
| GameBootstrapper | 编排启动流程 | 配置合并细节 |
| GameEngine | 持有 MergedConfig | 配置来源 |
| ModLoader | Mod 发现与加载 | 配置合并 |

## 6.2 为什么如此

分离关注点：
- ConfigPipeline 只做"合并"，不关心配置如何使用
- GameEngine 只做"消费"，不关心配置如何合并
- ModLoader 只做"加载"，不关心配置如何合并

## 6.3 影响范围

- 新增配置字段只需修改 `GameConfig.cs`
- 修改合并规则只需修改 `ConfigPipeline.cs`
- 不影响已有的 Mod 实现

# 7 当前代码现状

## 7.1 现状入口

- ConfigPipeline：`src/Core/Config/ConfigPipeline.cs`
- GameConfig：`src/Core/Config/GameConfig.cs`
- GameBootstrapper：`src/Core/Hosting/GameBootstrapper.cs`

## 7.2 差距清单

| 设计口径 | 代码现状 | 差距等级 | 风险 | 证据 |
|---|---|---|---|---|
| 分层合并 | ✅ 已实现 | 无 | 无 | `ConfigPipeline.MergeGameConfig()` |
| 深度合并字典 | ✅ 已实现 | 无 | 无 | `MergeGameConfig()` 内部逻辑 |
| 优先级排序 | ✅ 已实现 | 无 | 无 | 按 mod.json priority 排序 |
| fail-fast | ✅ 已实现 | 无 | 无 | JSON 解析异常直接抛出 |

## 7.3 迁移策略与风险

不适用。本架构为全新实现，无历史迁移需求。

# 8 验收条款

1. **分层合并正确**：Core + CoreMod + 业务 Mod 的配置按顺序合并
   - 验证方法：`MergedConfig.Constants.OrderTags` 包含 CoreMod 定义的所有键
   
2. **字典深度合并**：高优先级 Mod 可新增键，也可覆盖键
   - 验证方法：定义自定义 OrderTag 的 Mod 加载后，`MergedConfig.Constants.OrderTags` 包含该键
   
3. **fail-fast**：配置解析失败时抛出异常并指明文件路径
   - 验证方法：故意损坏 JSON，启动时报错包含文件路径
   
4. **单元测试通过**：所有 GasTests 通过（118/118）
   - 验证方法：`dotnet test src/Tests/GasTests/GasTests.csproj`

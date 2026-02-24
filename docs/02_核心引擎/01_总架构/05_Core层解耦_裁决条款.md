---
文档类型: 裁决条款
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v1.0
适用范围: 核心引擎 - 总架构 - Core层解耦裁决
状态: 已裁决
---

# Core层解耦 裁决条款

# 1 裁决背景

## 1.1 背景问题与约束

历史问题：
1. Core 层包含硬编码常量（`OrderTags.cs`、`GasOrderTags.cs`、`GameAttributes.cs`、`MapIds.cs`）
2. Core 层直接注册游戏系统（`WorldHudCollectorSystem`、`Orbit3CCameraController`）
3. Core 层持有默认资产（`entry.json`、`templates.json`、`hud.json`）
4. 导致"两个 P2 HUD"等 bug：配置重复加载与合并逻辑不清晰

约束：
- 参照 StarCraft 2 的 Core.SC2Mod 模式
- 需要支持 Mod 完全覆盖核心框架
- 不保留向后兼容

## 1.2 适用范围与边界

适用范围：
- `src/Core/` 下所有代码
- `assets/Configs/` 下所有配置
- 所有 Mods 的常量读取方式

边界：
- 本裁决不涉及渲染层、音频层
- 本裁决不涉及第三方库封装

# 2 裁决结论

## 2.1 必须遵守的规则

**规则 1：Core 层禁止持有游戏内容**

禁止项：
- 禁止在 `src/Core/` 下定义 `static class OrderTags`、`GasOrderTags`、`GameAttributes`、`MapIds` 等硬编码常量
- 禁止在 `GameEngine.InitializeCoreSystems()` 中直接实例化游戏系统
- 禁止在 `assets/Configs/` 下存放 `Maps/`、`Entities/`、`Presentation/` 等游戏资产

允许项：
- Core 层可以定义引擎参数（窗口尺寸、帧率、预算）
- Core 层可以定义 `DefaultCoreMod` 指向默认核心 Mod

**规则 2：所有游戏常量必须通过 ConfigPipeline 从 JSON 加载**

必须：
- `OrderTags` 从 `MergedConfig.Constants.OrderTags` 读取
- `GasOrderTags` 从 `MergedConfig.Constants.GasOrderTags` 读取
- `Attributes` 从 `MergedConfig.Constants.Attributes` 读取
- `StartupMapId` 从 `MergedConfig.StartupMapId` 读取

禁止：
- 禁止在 C# 代码中硬编码 `const int MoveTo = 101;`
- 禁止使用 `#if` 或 `??` 做兼容 fallback

**规则 3：禁止向后兼容与静默 fallback**

禁止项：
- 禁止使用 `??` 运算符为缺失配置提供默认值
- 禁止在代码注释中出现"backward compatibility"、"fallback"、"兼容"字样
- 禁止保留已废弃的接口签名

必须：
- 配置缺失时 fail-fast，抛出异常明确指出缺失项
- 接口变更时直接修改所有调用方

**规则 4：LudotsCoreMod 作为核心框架的唯一提供者**

LudotsCoreMod 必须提供：
- 默认 `game.json`（包含 `constants`、`startupMapId`）
- 默认地图（`assets/Maps/entry.json`）
- 默认模板（`assets/Entities/templates.json`）
- 默认 HUD 配置（`assets/Presentation/hud.json`）
- 核心系统注册（通过 Trigger 机制）

## 2.2 例外条件与退出条件

**例外 1：测试代码**

允许在 `src/Tests/` 下定义测试专用常量（如 `TestGasOrderTags.cs`），用于单元测试的隔离。

退出条件：测试常量不得被非测试代码引用。

**例外 2：引擎内部常量**

允许在 Core 层定义与引擎机制相关的内部常量（如系统组优先级、时间切片预算），这些常量不属于"游戏内容"。

退出条件：内部常量不得暴露给 Mod 或业务代码。

# 3 裁决理由

## 3.1 为什么这样做

1. **消除 bug 根源**：硬编码常量与配置合并逻辑冲突导致"两个 P2 HUD"问题
2. **支持完全替换**：Mod 开发者可以创建自己的 CoreMod 替换 LudotsCoreMod
3. **遵循成熟模式**：StarCraft 2 的 Core.SC2Mod 证明了此架构的可行性
4. **简化维护**：常量集中在 JSON 中，无需重新编译即可修改

## 3.2 放弃了哪些备选方案

**方案 A：保留 C# 常量作为默认值**
- 问题：导致"两套真相"，合并逻辑复杂
- 放弃原因：不符合 SSOT 原则

**方案 B：使用 `??` 运算符提供 fallback**
- 问题：静默行为难以调试，隐藏配置错误
- 放弃原因：违反 fail-fast 原则

**方案 C：保留 `OrderTags.cs` 但标记为 `[Obsolete]`**
- 问题：废弃代码仍会被意外引用
- 放弃原因：不如直接删除干净

# 4 影响范围

## 4.1 受影响模块与代码入口

已删除文件：
- `src/Core/Gameplay/GAS/Orders/OrderTags.cs`
- `src/Core/Gameplay/GAS/Orders/GasOrderTags.cs`
- `src/Core/Gameplay/GAS/Registry/GameAttributes.cs`
- `src/Core/Map/MapIds.cs`

已迁移资产：
- `assets/Configs/Maps/entry.json` → `src/Mods/LudotsCoreMod/assets/Maps/entry.json`
- `assets/Configs/Entities/templates.json` → `src/Mods/LudotsCoreMod/assets/Entities/templates.json`
- `assets/Configs/Presentation/hud.json` → `src/Mods/LudotsCoreMod/assets/Presentation/hud.json`

已修改的 Mods：
- `src/Mods/MobaDemoMod/`：所有 `OrderTags.MoveTo` 改为 `config.Constants.OrderTags["moveTo"]`
- `src/Mods/PerformanceVisualizationMod/`：`MapIds.Entry` 改为 `engine.MergedConfig.StartupMapId`
- `src/Mods/TerrainBenchmarkMod/`：同上
- `src/Mods/Physics2DPlaygroundMod/`：同上
- `src/Mods/GasBenchmarkMod/`：同上
- `src/Mods/PerformanceMod/`：同上

已修改的 Core 系统：
- `src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs`
- `src/Core/Presentation/Systems/ResponseChainHumanOrderSourceSystem.cs`
- `src/Core/Presentation/Systems/ResponseChainAiOrderSourceSystem.cs`

## 4.2 迁移与过渡策略

无过渡策略。本裁决为一次性全量变更，所有受影响代码已在同一 PR 中修改完毕。

验证方法：
1. `dotnet build` 成功（无编译错误）
2. `dotnet test src/Tests/GasTests/GasTests.csproj` 全部通过（118/118）

# 5 验证方法与证据入口

| 规则 | 验证方法 | 证据入口 |
|---|---|---|
| Core 层无硬编码常量 | `grep -r "static class OrderTags" src/Core/` 无结果 | 文件已删除 |
| 常量从 JSON 加载 | 检查 `GameEngine.InitializeCoreSystems()` | `src/Core/Engine/GameEngine.cs` |
| 无 fallback 代码 | `grep -r "backward\|fallback\|兼容" src/Core/` 无结果 | 代码无匹配 |
| LudotsCoreMod 提供默认配置 | 文件存在检查 | `src/Mods/LudotsCoreMod/assets/game.json` |
| 测试全部通过 | `dotnet test` | 118/118 passed |

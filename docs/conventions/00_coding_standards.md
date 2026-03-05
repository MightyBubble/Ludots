# 编码标准

本篇定义 Ludots 仓库的编码规范。第 1 节为核心架构铁律，是本仓库最高优先级的约束，其余章节为具体编码规则。所有人类开发者和 AI Agent 均须遵守。

## 1 核心架构铁律

以下三条铁律不可商量。违反任意一条的代码不得合入主干，PR Review 时一票否决。

### 1.1 六边形架构：Core 无平台依赖，gameplay 可无头测试

Core 层（`src/Core/`）对平台库的依赖为零。所有平台交互通过接口（`IInputBackend`、`IRenderBackend`）在 Adapter 层完成。gameplay 逻辑必须能在无窗口、无 GPU、无渲染器的环境下通过自动化测试验证。

**铁律**：

*   Core 不得引用 Raylib、Godot、Unity 或任何平台 API，包括 `using` 和文档注释
*   Core 不得直接初始化平台后端（如 `Log.Backend`），应由 Adapter 注入
*   所有 gameplay 测试必须无头运行——不依赖窗口、渲染器或输入设备
*   数据在边界层翻译：`Fix64Vec2` ↔ `float`、`ResourceHandle` ↔ `Texture2D`

**本仓库实际发生过的违规**：

| 问题 | 位置 | 说明 |
|------|------|------|
| Core 检查并初始化 `Log.Backend` | `GameEngine.cs:224` | Core 不应知道 Backend 是什么实现，应由 Adapter 注入 |
| Core 文档注释提及 Raylib | `GamePreset.cs:10`、`RightHandedYUpMapper.cs:9` | 文档注释耦合了具体平台名，应使用通用描述 |
| RaylibHostLoop 硬编码调试 UI | Issue #18 | 调试 UI 直接写在 Host 循环里，已迁移到 `DiagnosticsOverlayMod` |

**自动化守护**：`CoreBoundaryTests.LudotsCore_DoesNotReference_Raylib_Client_OrAdapter` 确保 Core 程序集不引用平台层。

参考文档：`docs/developer-guide/03_adapter_pattern.md`

### 1.2 一切皆 Mod

Core 本身也作为 Mod 挂载。所有 gameplay 内容（技能、效果、地图逻辑、UI）通过 Mod 机制注入，不允许硬编码在引擎中。

**铁律**：

*   Mod 唯一入口是 `IMod.OnLoad(IModContext)`——不允许静态构造器、反射扫描、程序集约定
*   `mod.json.main` 是唯一 DLL 入口——缺失时视为资源型 Mod，不扫描程序集
*   Mod 位于 `mods/`（仓库根目录），不在 `src/` 内——与 UGC 分发布局一致
*   System 注册通过 `SystemFactoryRegistry`，不在 `GameEngine` 中硬编码业务 System
*   配置通过 `ConfigPipeline` 合并，不自建加载器

**本仓库实际发生过的违规**：

| 问题 | 位置 | 说明 |
|------|------|------|
| 文档和 gitignore 引用 `src/Mods` | `README_CN.md:56`、`.gitignore:19` | Mod 实际位于 `mods/`，`src/Mods` 是历史残留路径 |
| PR #11 实体外观系统直接侵入 Host | 收束矩阵 doc 18 | Draft PR 侵入面过大，与 FeatureHub/DiagnosticsOverlay 方案重叠，关闭 |
| PR #7 分叉过深无法合并 | 收束矩阵 doc 18 | 与当前链路重构冲突面大，已被覆盖后关闭 |

参考文档：`docs/developer-guide/02_mod_architecture.md`、`docs/developer-guide/17_mod_runtime_single_source_of_truth.md`

### 1.3 四个禁止：fallback、向后兼容、重复造轮子、跨越职责

#### 禁止 fallback

代码不得包含"如果 A 失败就试 B"的隐式回退路径。配置错误、路径缺失、字段不匹配应直接报错，不应静默降级。

**本仓库实际发生过的违规**：

| 问题 | 位置 | 说明 |
|------|------|------|
| InputOrderMappingSystem fallback tag 解析 | `InputOrderMappingSystem.cs:544` | 当带后缀的 tag key 找不到时，fallback 到 base tag key |
| ConfigCatalogEntry IdField 隐式 fallback | `ConfigCatalogEntry.cs:14` | `idField` 为空时静默回退为 `"id"`，应显式要求调用方指定 |
| Editor Bridge MergeMapConfig board fallback | 审计 doc 15 | 找不到 `default` board 时 fallback 到第一个含 DataFile 的 board |

#### 禁止向后兼容

不保留"为了兼容旧版本"的代码路径。旧机制废弃时直接删除，不加 `[Obsolete]` 过渡。

**本仓库实际发生过的违规**：

| 问题 | 位置 | 说明 |
|------|------|------|
| OrderBufferSystem 保留全局队列"for backwards compatibility" | `OrderBufferSystem.cs:70` | 注释明确写了 backward compatibility，应迁移调用方后删除 |
| FireMapEvent 兼容全局 trigger | 审计 doc 15 §2.2 | 初始版本为兼容旧 trigger 增加了 fallback 链，Phase 2 测试已验证移除后不影响功能 |
| PR #13 "生产最优方案"被关闭 | 收束矩阵 doc 18 | 主方向"去 fallback/去旧链路"正确，但整包引入有噪声 |

**自动化守护**：`ArchitectureGuardTests.Codebase_MustNotContainCompatibilityOrFallbackMarkers` 在 Core、mods、Platforms 中扫描禁止标记（`向后兼容`、`backward compatibility`、`legacy support`、`legacy alias` 等）。

#### 禁止重复造轮子

仓库已有 20+ 个 Registry、3 条事件管线、完整的 ConfigPipeline。新增功能必须挂靠已有基础设施。

**本仓库已有的事件管线（各自职责明确，不要新建第四条）**：

| 管线 | 职责 | 不适用场景 |
|------|------|-----------|
| `GameplayEventBus` | GAS gameplay 事件（EffectApplied、CastCommitted 等） | 表现、UI |
| `TriggerManager` | Map/Script 触发器（MapLoaded 等） | 高频 gameplay 事件 |
| `PresentationEventStream` | 表现事件，由 Performer 消费 | gameplay 状态变更 |

**本仓库实际发生过的违规**：

| 问题 | 说明 |
|------|------|
| PR #9 效果预设验收被关闭 | 核心价值已被其他方式吸收，PR 本身存在与已有链路的重复 |
| PR #11 实体外观系统被关闭 | 有价值片段已在 `GmConsoleMod` + `RaylibPrimitiveRenderer` 重新实现 |

完整 Registry 清单见 `02_ai_assisted_development.md` §4.1。

#### 禁止跨越职责

每一层只做自己该做的事。Core 不碰表现，表现不改 gameplay 状态，Mod 不直接操作引擎内部。

| 层 | 职责边界 | 禁止的事 |
|----|---------|---------|
| Core | gameplay 状态、ECS、GAS、配置 | 调用平台 API、初始化渲染后端、直接写表现数据 |
| Adapter | 平台接口实现、数据翻译 | 修改 gameplay 状态、绕过 Core 接口 |
| Presentation | 渲染、UI、音频 | 修改 ECS 组件的 gameplay 字段 |
| Mod | 注册 System/Config/Trigger | 修改 Core 内部类、绕过 `IModContext`、硬编码路径 |

参考文档：`docs/developer-guide/03_adapter_pattern.md`、`docs/developer-guide/11_gas_layered_architecture.md`

## 2 ECS 硬性约束

以下规则不可违反，违反任意一条视为 blocking issue：

1. 组件必须是 **blittable struct**——不得包含 `string`、`class`、`List<T>` 等引用类型
2. 核心循环 **零 GC**——System 的 `Update` 方法内不得产生托管堆分配
3. `QueryDescription` **缓存为 `private readonly` 字段**——不得在热循环内创建
4. 结构变更使用 `CommandBuffer`——不得在 query 循环内直接 Create/Destroy/Add/Remove
5. gameplay 状态使用 `Fix64`/`Fix64Vec2`——不得使用 `float`
6. 不得依赖 `Dictionary` 迭代顺序、`System.Random`、或其他不确定性来源

参考实现：`docs/developer-guide/01_ecs_soa_principles.md`

## 3 命名规则

### 3.1 组件与系统

| 类型 | 后缀 | 示例 |
|-----|------|-----|
| 数据组件 | `Cm` 或无后缀名词 | `WorldPositionCm`、`Velocity` |
| 标签组件 | `Tag` | `IsPlayerTag`、`IsDeadTag` |
| 事件组件 | `Event` | `CollisionEvent`、`DamageEvent` |
| 系统 | `System` | `DamageCalculationSystem` |
| Registry | `Registry` | `AttributeRegistry` |

### 3.2 命名原则

*   命名禁止耦合具体业务——不要叫 `MobaHealthSystem`，应叫 `HealthSystem`
*   业务差异通过配置驱动，不通过类名区分
*   Mod 命名使用 PascalCase：`XxxMod`（如 `MobaDemoMod`）

### 3.3 文件位置

| 类型 | 位置 |
|-----|------|
| Core 组件 | 所属子模块的 Components 目录 |
| Core 系统 | 所属子模块的 Systems 目录 |
| Registry | 所属子模块根目录或 Registry 子目录 |
| Mod | `mods/XxxMod/`（仓库根目录下，不在 `src/` 内） |

## 4 SystemGroup 归属

```
SchemaUpdate → InputCollection → PostMovement → AbilityActivation →
EffectProcessing → AttributeCalculation → DeferredTriggerCollection →
Cleanup → EventDispatch → ClearPresentationFlags
```

新增 System 必须明确归属某个 phase，不得游离。每个 System 属于且只属于一个 group。

参考实现：`src/Core/Engine/GameEngine.cs`

## 5 测试标准

### 5.1 基本规范

*   测试遵循 AAA（Arrange/Act/Assert）模式
*   测试类命名：`<Subsystem>Tests`
*   测试方法命名：`<Subject>_<Scenario>_<Expected>`
*   框架：NUnit 4.2.2，断言使用 `Assert.That(actual, Is.EqualTo(expected))`

### 5.2 隔离规则

*   每个测试拥有独立的 `World`——`using var world = World.Create();`——不允许跨测试共享
*   在 `[TearDown]` 或 `finally` 中清理静态 Registry（如 `TagOps.ClearRuleRegistry()`）

### 5.3 禁止项

*   不使用 `Console.WriteLine`——仅在诊断失败时使用 `TestContext.WriteLine`
*   GAS 热路径测试中不使用 LINQ
*   `GameplayEventBus.Events` 通过索引访问（`for` + `events[i]`），不使用 `foreach`

完整测试风格指南：`src/Tests/GasTests/TESTING_STYLE.md`

## 6 Commit 格式

```
<type>(<scope>): <description>

[可选 body]
```

| type | 用途 |
|------|------|
| `feat` | 新功能 |
| `fix` | Bug 修复 |
| `refactor` | 重构（不改变外部行为） |
| `docs` | 文档变更 |
| `test` | 测试变更 |
| `chore` | 构建、工具链变更 |

scope 使用模块名（如 `gas`、`physics2d`、`editor`、`mod/MobaDemoMod`）。

## 7 架构挂靠原则

所有新代码必须挂靠到已有架构管线，不得创建平行体系：

| 要做的事 | 正确做法 | 错误做法 |
|---------|---------|---------|
| 新增 gameplay 属性 | `AttributeRegistry.Register` | 自建 dictionary 存属性 |
| 新增系统 | `GameEngine.RegisterSystem` 或 `SystemFactoryRegistry` | 在 Update 里手动调用 |
| 跨层数据传递 | 通过 Sink（`AttributeSinkRegistry`） | 直接写组件跨 phase |
| 配置加载 | 接入 `ConfigPipeline` + `ConfigCatalogEntry` | 自建 JSON 加载器 |
| 事件通信 | `GameplayEventBus` 或 `TriggerManager.OnEvent` | 自建事件系统 |
| 表现更新 | Performer 管线 + `ResponseChain` | 在 Core 系统里直接调平台 API |
| Mod 入口 | `IMod.OnLoad(IModContext)` | 静态构造器或反射扫描 |

## 8 相关文档

*   ECS 开发实践：见 `docs/developer-guide/01_ecs_soa_principles.md`
*   GAS 分层架构：见 `docs/developer-guide/11_gas_layered_architecture.md`
*   Feature 开发工作流：见 [01_feature_development_workflow.md](01_feature_development_workflow.md)
*   AI 辅助开发规范：见 [02_ai_assisted_development.md](02_ai_assisted_development.md)

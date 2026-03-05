# 编码标准

本篇定义 Ludots 仓库的编码规范，包括 ECS 约束、命名规则、测试标准和 Commit 格式。所有人类开发者和 AI Agent 均须遵守。

## 1 ECS 硬性约束

以下规则不可违反，违反任意一条视为 blocking issue：

1. 组件必须是 **blittable struct**——不得包含 `string`、`class`、`List<T>` 等引用类型
2. 核心循环 **零 GC**——System 的 `Update` 方法内不得产生托管堆分配
3. `QueryDescription` **缓存为 `private readonly` 字段**——不得在热循环内创建
4. 结构变更使用 `CommandBuffer`——不得在 query 循环内直接 Create/Destroy/Add/Remove
5. gameplay 状态使用 `Fix64`/`Fix64Vec2`——不得使用 `float`
6. 不得依赖 `Dictionary` 迭代顺序、`System.Random`、或其他不确定性来源

参考实现：`docs/developer-guide/01_ecs_soa_principles.md`

## 2 命名规则

### 2.1 组件与系统

| 类型 | 后缀 | 示例 |
|-----|------|-----|
| 数据组件 | `Cm` 或无后缀名词 | `WorldPositionCm`、`Velocity` |
| 标签组件 | `Tag` | `IsPlayerTag`、`IsDeadTag` |
| 事件组件 | `Event` | `CollisionEvent`、`DamageEvent` |
| 系统 | `System` | `DamageCalculationSystem` |
| Registry | `Registry` | `AttributeRegistry` |

### 2.2 命名原则

*   命名禁止耦合具体业务——不要叫 `MobaHealthSystem`，应叫 `HealthSystem`
*   业务差异通过配置驱动，不通过类名区分
*   Mod 命名使用 PascalCase：`XxxMod`（如 `MobaDemoMod`）

### 2.3 文件位置

| 类型 | 位置 |
|-----|------|
| Core 组件 | 所属子模块的 Components 目录 |
| Core 系统 | 所属子模块的 Systems 目录 |
| Registry | 所属子模块根目录或 Registry 子目录 |
| Mod | `mods/XxxMod/`（仓库根目录下，不在 `src/` 内） |

## 3 SystemGroup 归属

```
SchemaUpdate → InputCollection → PostMovement → AbilityActivation →
EffectProcessing → AttributeCalculation → DeferredTriggerCollection →
Cleanup → EventDispatch → ClearPresentationFlags
```

新增 System 必须明确归属某个 phase，不得游离。每个 System 属于且只属于一个 group。

参考实现：`src/Core/Engine/GameEngine.cs`

## 4 测试标准

### 4.1 基本规范

*   测试遵循 AAA（Arrange/Act/Assert）模式
*   测试类命名：`<Subsystem>Tests`
*   测试方法命名：`<Subject>_<Scenario>_<Expected>`
*   框架：NUnit 4.2.2，断言使用 `Assert.That(actual, Is.EqualTo(expected))`

### 4.2 隔离规则

*   每个测试拥有独立的 `World`——`using var world = World.Create();`——不允许跨测试共享
*   在 `[TearDown]` 或 `finally` 中清理静态 Registry（如 `TagOps.ClearRuleRegistry()`）

### 4.3 禁止项

*   不使用 `Console.WriteLine`——仅在诊断失败时使用 `TestContext.WriteLine`
*   GAS 热路径测试中不使用 LINQ
*   `GameplayEventBus.Events` 通过索引访问（`for` + `events[i]`），不使用 `foreach`

完整测试风格指南：`src/Tests/GasTests/TESTING_STYLE.md`

## 5 Commit 格式

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

## 6 架构挂靠原则

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

## 7 相关文档

*   ECS 开发实践：见 `docs/developer-guide/01_ecs_soa_principles.md`
*   GAS 分层架构：见 `docs/developer-guide/11_gas_layered_architecture.md`
*   Feature 开发工作流：见 [01_feature_development_workflow.md](01_feature_development_workflow.md)
*   AI 辅助开发规范：见 [02_ai_assisted_development.md](02_ai_assisted_development.md)

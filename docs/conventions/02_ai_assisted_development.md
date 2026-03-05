# AI 辅助开发规范

本篇为所有 AI Agent（Cursor、Claude Code 等）在 Ludots 仓库中工作时提供强制性指引。目标是消除幻觉代码（引用不存在的 API）和重复造轮子（忽视已有基础设施）。

人类开发者在 review AI 生成的代码时，也应以本文作为检查基准。

## 1 核心原则：搜索 → 阅读 → 编码 → 自检

```
搜索已有能力 → 阅读相关文档和源码 → 列出复用/新增清单 → 编码 → 验证 API 引用
```

不得跳过前三步直接编码。如果 Agent 在对话中未展示搜索和阅读过程，其产出的代码需要额外审查。

## 2 防幻觉条款

### 2.1 禁止凭空发明 API

AI Agent 生成代码时，以下行为视为严重违规：

*   调用代码库中不存在的方法或类
*   假设某个 Registry 有 `GetById` 方法但实际只有 `TryGet`
*   假设某个组件有某个字段但实际没有
*   使用 NuGet 包中不存在的重载

**规则**：每引用一个非 BCL 的类型或方法，必须先搜索确认其存在。搜索失败则不得使用。

### 2.2 幻觉代码自检

AI Agent 在完成编码后，必须对自己生成的每个 `new` 构造、方法调用、类型引用执行一次存在性搜索。如果发现引用不存在的 API，立即修正，不得留给用户。

### 2.3 设计方案中的 API 验证

设计方案中引用的每一个 API 必须通过以下验证：

*   **类存在性**：搜索确认类/接口定义存在于代码库中
*   **方法签名**：确认方法名、参数类型、返回类型与实际代码一致
*   **注册模式**：确认 Registry 的 `Register` 方法签名和调用时机（schema phase / runtime）
*   **组件布局**：确认 ECS 组件是 blittable struct，字段类型正确

## 3 防重复造轮子条款

### 3.1 禁止创建平行体系

AI Agent 不得在未经搜索的情况下创建以下内容：

*   新的 Registry 类（先确认第 4 节列出的 20+ 个已有 Registry 中是否有可复用的）
*   新的事件系统（先确认 `GameplayEventBus` 和 `TriggerManager` 是否满足需求）
*   新的配置加载机制（先确认 `ConfigPipeline` 是否支持）
*   新的组件基类或接口（先确认已有模式是否足够）

### 3.2 发现阶段不可跳过

在写任何代码之前，AI Agent 必须执行以下操作：

1. **搜索而非猜测**：对任何要引用的类、方法、接口，先用搜索工具确认其存在及签名，不得凭记忆或推测编写调用代码
2. **读文档再动手**：先读 `docs/developer-guide/README.md` 定位相关架构文档，通读后再设计方案
3. **列出复用清单**：在开始编码前，显式列出计划复用的已有类和将要新建的类

完整发现阶段流程见 [01_feature_development_workflow.md](01_feature_development_workflow.md)。

## 4 能力清单速查表

以下是仓库中已有的核心基础设施。新功能开发时优先在此基础上扩展，不要另起炉灶。

### 4.1 Registry 一览

| Registry | 位置 | 用途 |
|----------|------|------|
| `SystemFactoryRegistry` | `src/Core/Engine/` | System 工厂注册，Mod 通过此注册可选系统 |
| `AttributeRegistry` | `src/Core/Gameplay/GAS/Registry/` | 属性名 → ID 映射 |
| `TagRegistry` | `src/Core/Gameplay/GAS/Registry/` | Tag 名 → ID 映射 |
| `AttributeSinkRegistry` | `src/Core/Gameplay/GAS/Bindings/` | 属性 Sink 注册（跨层写入） |
| `EffectTemplateRegistry` | `src/Core/Gameplay/GAS/` | 效果模板 |
| `AbilityDefinitionRegistry` | `src/Core/Gameplay/GAS/` | 技能定义 |
| `OrderTypeRegistry` | `src/Core/Gameplay/GAS/Orders/` | 命令类型 |
| `PerformerDefinitionRegistry` | `src/Core/Presentation/` | 表现定义 |
| `MeshAssetRegistry` | `src/Core/Presentation/` | 网格资产 |
| `ComponentRegistry` | `src/Core/Config/` | 组件 JSON 反序列化 |
| `CameraControllerRegistry` | `src/Core/Gameplay/Camera/` | 相机控制器类型 |
| `LayerRegistry` | `src/Core/Layers/` | 层 ID |
| `BoardIdRegistry` | `src/Core/Map/Board/` | 棋盘 ID |
| `GraphProgramRegistry` | `src/Core/GraphRuntime/` | Graph 程序 |
| `FunctionRegistry` | `src/Core/Scripting/` | 脚本函数 |
| `TriggerDecoratorRegistry` | `src/Core/Scripting/` | Trigger 装饰器 |
| `TaskNodeRegistry` | `src/Core/Gameplay/AI/` | AI 任务节点 |
| `AtomRegistry` | `src/Core/Gameplay/AI/` | AI 世界状态原子 |
| `StringIntRegistry` | `src/Core/Registry/` | 通用字符串-整数双向映射 |

### 4.2 核心管线

| 管线 | 入口 | 架构文档 |
|------|------|---------|
| ConfigPipeline | `ConfigPipeline.MergeGameConfig` | `docs/developer-guide/07_config_pipeline.md` |
| GAS Effect Pipeline | `EffectRequestQueue` → 各 Phase System | `docs/developer-guide/11_gas_layered_architecture.md` |
| Presentation Pipeline | Performer → ResponseChain | `docs/developer-guide/06_presentation_performer.md` |
| Trigger Pipeline | `TriggerManager.OnEvent` | `docs/developer-guide/08_trigger_guide.md` |
| Mod Loading | `ModLoader` → `IMod.OnLoad` | `docs/developer-guide/02_mod_architecture.md` |
| Startup | `GameBootstrapper.InitializeFromBaseDirectory` | `docs/developer-guide/09_startup_entrypoints.md` |

### 4.3 SystemGroup Phase 一览

```
SchemaUpdate → InputCollection → PostMovement → AbilityActivation →
EffectProcessing → AttributeCalculation → DeferredTriggerCollection →
Cleanup → EventDispatch → ClearPresentationFlags
```

新增 System 必须明确归属某个 phase，不得游离。

## 5 相关文档

*   编码标准：见 [00_coding_standards.md](00_coding_standards.md)
*   Feature 开发工作流：见 [01_feature_development_workflow.md](01_feature_development_workflow.md)
*   开发环境与构建：见 [03_environment_setup.md](03_environment_setup.md)
*   架构文档索引：见 `docs/developer-guide/README.md`

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

## 4 任务执行决策规范

Agent 在收到任务后、写第一行代码前，必须依次做出以下三个判断。任何一个判断结果为"否"时，不得继续编码。

### 4.1 判断一：这是不是我该直接做的事

**问自己**：这个任务是业务 feature，还是基建扩展？是在已有管线上做增量，还是需要新建管线？

| 判断结果 | 行动 |
|---------|------|
| 已有管线足以支撑，只需增量 | 继续，进入判断二 |
| 需要新建管线或修改 Core 接口 | **停下来**，向用户说明"这不是一个 feature 任务，而是一个基建任务"，给出基建方案，等用户确认后再动手 |
| 任务描述模糊，无法判断 | **停下来**，向用户提出具体问题，不得基于猜测开工 |

**禁止行为**：闷声把基建变更混在 feature 代码里提交。基建变更必须单独提出、单独 commit。

### 4.2 判断二：我需要复用什么基建

**必须显式列出**复用清单，格式：

```
复用基建：
- Registry: <具体 Registry 名> — 用于 <什么>
- Pipeline: <具体管线名> — 数据从 <哪> 流向 <哪>
- System: <已有 System> — 扩展/组合方式
- Mod: <已有 Mod> — 是否可在其基础上扩展
```

此清单不得为空。如果列不出任何复用项，说明发现阶段没做到位——回到 §3.2 重做。

### 4.3 判断三：基建够不够用

用复用清单逐项检查：已有基建是否完整覆盖需求？如果发现缺口：

| 缺口类型 | 行动 |
|---------|------|
| 缺一个 Registry 方法或字段 | **先补基建**：在已有 Registry 上扩展，单独 commit，再回来做 feature |
| 缺一整条管线 | **停下来**，向用户报告缺口，提出基建方案，不得在 feature 代码里临时造一条 |
| 已有管线接口不匹配 | **停下来**，说明不匹配的具体点，提出重构方案，不得绕过管线自己写 |

**禁止行为**：

*   发现基建不足时静默跳过、用 hack 绕过、或在 feature 代码中内联一个"临时方案"
*   自己闷声重写一个功能近似的 Registry/System 而不先检查已有的

### 4.4 Mod 提取规则

写 feature 时必须评估：这段逻辑是单个 Mod 专用的，还是多个 Mod 可能复用的？

| 情况 | 行动 |
|------|------|
| 逻辑只服务于当前 Mod | 放在当前 Mod 内 |
| 2 个以上 Mod 可能用到同样逻辑 | **提取到 Core**：作为 System/Registry/Sink，通过 `SystemFactoryRegistry` 注册为可选 |
| 逻辑是完整的可独立功能（如 GM 控制台、诊断覆盖层） | **提取为独立 Mod**（如 `GmConsoleMod`、`DiagnosticsOverlayMod`） |
| 不确定 | **停下来问用户**，不要先写死在一个 Mod 里再说"以后再重构" |

**本仓库的正面案例**：

*   `DiagnosticsOverlayMod`：调试 UI 从 RaylibHostLoop 硬编码提取为独立 Mod（Issue #18）
*   `GmConsoleMod`：GM 控制台从散落的调试代码提取为独立 Mod
*   `CoreInputMod`：输入映射从 MobaDemoMod 提取为公共 Mod，多个 Demo Mod 依赖

**本仓库的反面案例**：

*   PR #11 实体外观系统直接写在 Host 层，与已有 FeatureHub/DiagnosticsOverlay 重叠，被关闭

## 5 能力清单速查表（不超过 20 秒即可扫完）

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

## 6 相关文档

*   编码标准：见 [00_coding_standards.md](00_coding_standards.md)
*   Feature 开发工作流：见 [01_feature_development_workflow.md](01_feature_development_workflow.md)
*   开发环境与构建：见 [03_environment_setup.md](03_environment_setup.md)
*   架构文档索引：见 `docs/developer-guide/README.md`

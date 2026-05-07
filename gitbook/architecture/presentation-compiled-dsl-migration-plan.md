# 表现层编译式 DSL 迁移计划

本文定义从当前 performer runtime 路线迁移到 [表现层编译式 DSL 架构](presentation-compiled-dsl-architecture.md) 的正式计划。目标不是“小修 current performer”，而是把现有码拆分为可复用的 compiler/backend 资产，并删除 runtime performer entity 主线。

## 1 迁移原则

- 保留 authoring 面，重写 runtime 语义
- 优先复用 parser、registry、数学与桥接能力
- 明确标记必须删除的 runtime performer 类型
- 所有迁移步骤都要配套测试替换，不允许只迁代码不迁守卫

## 2 当前代码分层判断

当前代码大致可分成 4 类：

1. 可复用为 front-end / compiler 的代码
2. 可复用为 backend math / projection helper 的代码
3. 必须删除的 runtime performer entity 代码
4. 需要迁移重写的测试与 fixture

## 3 代码迁移总表

| 路径 | 处置 | 原因 | 目标去向 |
|------|------|------|----------|
| `src/Core/Presentation/Config/PerformerDefinitionConfigLoader.cs` | 拆分复用 | 目前混合 parse、normalize、runtime 假设 | front-end parser / normalize / semantic validator |
| `src/Core/Presentation/Performers/PerformerDefinition.cs` | 降级为 authoring model | 不能再承载 runtime 语义 | `Compiler/PresentationDslAst.cs` 或 `Compiler/Authoring/` |
| `src/Core/Presentation/Performers/CompiledPerformerBootstrapRegistry.cs` | 重写复用 | 已有“编译 spawn/destroy 规则”雏形 | `Compiler/PresentationOwnerBootstrapCompiler.cs` |
| `src/Core/Presentation/Systems/PresentationBridgeSystem.cs` | 保留增强 | 已有 owner attr/tag change feed | `PresentationBridgeSystem` + route executor 输入 |
| `src/Core/Presentation/Performers/PerformerGroundingUtility.cs` | 提取复用 | 变换/grounding 数学仍有价值 | `Runtime/Transforms/PresentationAnchorTransformUtility.cs` |
| `src/Core/Presentation/Systems/WorldHudToScreenSystem.cs` | 保留 | 属于 screen projection 阶段，不依赖 performer entity | 继续作为 HUD 屏幕投影 |
| `src/Core/Presentation/Performers/PerformerAnimatorStateBuffer.cs` | 重命名复用 | adapter-facing packed state 可保留 | `Runtime/Tick/PresentationAnimatorStateBuffer.cs` |
| `src/Core/Presentation/Systems/PerformerAssetEmitRuntime.cs` | 拆分复用 | 内含 mesh/hud/text typed 发射逻辑，但被 performer runtime 包裹 | `Projection/PresentationMeshProjectionRuntime.cs` 等 |
| `src/Core/Presentation/Performers/PerformerEntityRuntime.cs` | 删除 | runtime performer entity 索引与生命周期中枢 | owner runtime store 取代 |
| `src/Core/Presentation/Systems/PerformerRuntimeSystem.cs` | 删除 | 命令回放 + runtime performer materialize | owner materialize/destroy systems 取代 |
| `src/Core/Presentation/Systems/PerformerBehaviorSystem.cs` | 删除 | definition scan + generic behavior execution | dirty route engine + tick backend 取代 |
| `src/Core/Presentation/Systems/PerformerEmitSystem.cs` | 删除 | definition-driven emit interpreter | typed projection backend 取代 |
| `src/Core/Presentation/Performers/PerformerParamResolver.cs` | 删除 | parent-chain resolve 与 O(chain) lookup 不符合新口径 | dense param page 取代 |
| `src/Core/Presentation/Performers/PerformerFloatParams.cs` | 删除 | 旧 performer param 容器 | presentation param pages 取代 |
| `src/Core/Presentation/Performers/PerformerIntParams.cs` | 删除 | 同上 | presentation param pages 取代 |
| `src/Core/Presentation/Performers/PerformerVectorParams.cs` | 删除 | 同上 | presentation param pages 取代 |
| `src/Core/Presentation/Performers/PerformerState.cs` | 删除 | `1 performer = 1 runtime object` 的核心状态 | `OwnerPresentationRuntime` 取代 |
| `src/Core/Presentation/Performers/PerformerParent.cs` | 删除 | runtime 树关系不再存在 | anchor graph page 取代 |
| `src/Core/Presentation/Performers/PerformerCullState.cs` | 删除 | cull 不再挂在 runtime performer 上 | visible owner set / projection slices |
| `src/Core/Presentation/Performers/PerformerWorldPosition.cs` | 删除 | world transform 不再逐 performer 存储 | anchor page / projection cache |
| `src/Core/Presentation/Performers/PerformerWorldRotation.cs` | 删除 | 同上 | anchor page / projection cache |
| `src/Core/Presentation/Performers/PerformerWorldScale.cs` | 删除 | 同上 | anchor page / projection cache |
| `src/Core/Presentation/Performers/PerformerScopeTagRegistry.cs` | 删除 | scope 不能再是全局 tag registry | artifact-local scope layout |
| `src/Core/Presentation/Performers/WorldHudPerformBehavior.cs` | 删除或下沉 | “behavior” 命名与新主线冲突 | HUD projection helper |

## 4 可复用代码的具体迁移

### 4.1 `PerformerDefinitionConfigLoader`

当前价值：

- 已有 config pipeline 接入
- 已有 alias 清理和 `extends` 展开
- 已有 registry resolve 钩子

正式迁移方式：

- 把文件拆成 `Parse`、`Normalize`、`Resolve`、`Validate`
- 删掉一切 runtime `PerformerRule -> PerformerCommand` 假设
- children 只保留 authoring 语义，lower 交给 compiler

必须删除的内容：

- 任何直接构造 runtime performer tree 的逻辑
- 任何“为了旧 runtime 命令格式做兼容”的字段推断

### 4.2 `CompiledPerformerBootstrapRegistry`

当前价值：

- 已经开始做“定义注册时编译 spawn/destroy 规则”

问题：

- 编译目标仍是 `CreatePerformer` / `DestroyPerformerScope`
- scope 仍依赖 global tag 语义

正式迁移方式：

- 改造成 `PresentationOwnerBootstrapCompiler`
- 输出 `OwnerSpawnProgram` / `OwnerDestroyProgram`
- 不再输出 runtime performer command

### 4.3 `PresentationBridgeSystem`

当前价值：

- 已经从 GAS 组件提取 owner-level attr/tag dirty 信号
- 已有 `PresentationOwnerChangeBuffer`

正式迁移方式：

- 继续作为 owner dirty feed 入口
- 下游接 `PresentationDirtyApplySystem`
- 删除“后面还要去找哪些 performer 受影响”的假设

### 4.4 `PerformerGroundingUtility`

当前价值：

- 变换、grounding、surface 对齐数学

正式迁移方式：

- 提取为 anchor transform utility
- 输入从 `Entity performer` 改为 `owner runtime + anchor recipe + param page`

### 4.5 `PerformerAssetEmitRuntime`

当前价值：

- 内部已经按 asset kind 分支
- HUD/text/mesh 的 request 构造逻辑可复用

问题：

- 当前入口仍要求 `PerformerState`、`PerformerDefinition`、runtime performer entity

正式迁移方式：

- 拆成 typed projection helpers
- 输入改为 `artifact recipe + owner runtime page`
- 删除对 `PerformerEntityRuntime.Resolve*` 的依赖

## 5 必须删除的 runtime performer 主线

以下文件不得进入 compiled DSL 正式主线：

- `src/Core/Presentation/Performers/PerformerEntityRuntime.cs`
- `src/Core/Presentation/Systems/PerformerRuntimeSystem.cs`
- `src/Core/Presentation/Systems/PerformerBehaviorSystem.cs`
- `src/Core/Presentation/Systems/PerformerEmitSystem.cs`
- `src/Core/Presentation/Performers/PerformerParamResolver.cs`
- `src/Core/Presentation/Performers/PerformerState.cs`
- `src/Core/Presentation/Performers/PerformerParent.cs`
- `src/Core/Presentation/Performers/PerformerCullState.cs`
- `src/Core/Presentation/Performers/PerformerWorldPosition.cs`
- `src/Core/Presentation/Performers/PerformerWorldRotation.cs`
- `src/Core/Presentation/Performers/PerformerWorldScale.cs`
- `src/Core/Presentation/Performers/PerformerEmitCache.cs`

删除判定标准：

- 正式主线中无任何 `World.Query(... PerformerState ...)`
- 正式主线中无任何 `GetActiveByOwnerDefinition`
- 正式主线中无任何 `DestroyScope(scopeId)` 全局扫描

## 6 新 runtime 目录建议

建议新增目录：

- `src/Core/Presentation/Compiler/`
- `src/Core/Presentation/Runtime/`
- `src/Core/Presentation/Runtime/Projection/`
- `src/Core/Presentation/Runtime/Tick/`
- `src/Core/Presentation/Runtime/Transforms/`

建议核心文件：

- `Compiler/PresentationDslAst.cs`
- `Compiler/PresentationDslSemanticValidator.cs`
- `Compiler/PresentationArchetypeCompiler.cs`
- `Compiler/PresentationArtifactRegistry.cs`
- `Runtime/OwnerPresentationRuntime.cs`
- `Runtime/OwnerPresentationRuntimeStore.cs`
- `Runtime/PresentationParamPages.cs`
- `Runtime/PresentationAnchorPages.cs`
- `Runtime/PresentationDirtyRouteExecutor.cs`
- `Systems/PresentationOwnerMaterializeSystem.cs`
- `Systems/PresentationDirtyApplySystem.cs`
- `Systems/PresentationProjectionSystem.cs`
- `Systems/PresentationTickSystem.cs`

## 7 测试迁移计划

### 7.1 需要重写的 fixture

以下 fixture 必须从 performer entity 视角改成 owner runtime 视角：

- `BlacksmithPerformerUatTests`
- `AnimatorRuntimeSystemTests`
- scatter / benchmark 夹具

新的 fixture 原则：

- 不暴露 `PerformerEntityRuntime`
- 不暴露 `CountActiveByDefinition`
- 只暴露 owner archetype、recipe count、scope mask、projected batches

### 7.2 需要保留的语义验收

这些验收语义必须保留，但断言目标要改：

- 黑铁匠铺创建后出现 2 workshop + 1 furnace
- working tag 开关只影响 working scope
- durability 变化同时驱动 mesh、HUD bar、text
- day/night 和 region 变化驱动 material/text token 等 route

### 7.3 需要新增的规模守卫

- `PresentationScaleGuardTests.NoRuntimePerformerEntities`
- `PresentationScaleGuardTests.NoDefinitionScanInDirtyPath`
- `PresentationScaleGuardTests.NoGlobalScopeQueryInDeactivate`
- `PresentationScaleGuardTests.TwoHundredThousandRecipes_SteadyStateNoAlloc`

## 8 迁移阶段建议

### M1 Front-End 并行期

- 先保留现有 `performers.json`
- 新 compiler 与旧 runtime 并存
- 用 artifact snapshot tests 验证 lower 结果

### M2 Runtime 替换期

- owner runtime store 和 dirty route engine 上线
- mesh/hud/text 先切到新 projection backend
- 旧 performer runtime 只作为对照验证，不再扩功能

### M3 Legacy 删除期

- 全部 acceptance 和 benchmark 切到 compiled DSL runtime
- 删除 runtime performer 主线文件
- 清理旧 GitBook 页面中的“迁移中”状态

## 9 迁移完成定义

满足以下条件时，迁移才算完成：

- `performers.json` 只代表 authoring DSL
- runtime 中无 `PerformerState` 主线 query
- runtime 中无 `PerformerEntityRuntime`
- HUD/text/mesh 都从 typed recipe backend 投影
- 30K owner / 200K recipe 压测通过
- GitBook 不再把任何 runtime performer entity 页面当成正式主线

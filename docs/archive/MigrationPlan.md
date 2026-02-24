# GraphRuntime 领域无关化迁移蓝图（最小可行路径）

## 目标

- 将现有 `src/Core/Gameplay/GAS/Graph/*` 迁移为：
  - `Core.GraphRuntime`：领域无关运行时（确定性、time-slice、预算、ResetSlice、指令执行）
  - `NodeLibraries.*`：领域节点库（Std/Spatial/GAS），通过 Runtime API 注入使用宿主能力
- 给出最小可行迁移路径（Phase 0/1/2），明确：
  - 每阶段要移动的文件
  - 接口适配点（seams）
  - 回归测试清单

## 必看文档与现有实现定位

- 设计文档
  - [01_GraphRuntime_Core_确定性与TimeSlice.md](file:///c:/AIProjects/Ludots/docs/07_%E5%9F%BA%E7%A1%80%E8%AE%BE%E6%96%BD/01_GraphRuntime_Core_%E7%A1%AE%E5%AE%9A%E6%80%A7%E4%B8%8ETwitterSlice.md)
  - [02_Graph_NodeLibraries_分包与RuntimeAPI注入.md](file:///c:/AIProjects/Ludots/docs/07_%E5%9F%BA%E7%A1%80%E8%AE%BE%E6%96%BD/02_Graph_NodeLibraries_%E5%88%86%E5%8C%85%E4%B8%8ERuntimeAPI%E6%B3%A8%E5%85%A5.md)
- 必看代码（现状）
  - [GAS/Graph 目录](file:///c:/AIProjects/Ludots/src/Core/Gameplay/GAS/Graph/)
  - [InstructionExecutor.cs](file:///c:/AIProjects/Ludots/src/Core/Gameplay/GAS/InstructionExecutor.cs)

## 现状拆分（抽离时的边界）

将当前 `Gameplay/GAS/Graph` 视为 4 类东西的混合体：

1) **运行时内核（应进 Core.GraphRuntime）**
- 指令表示与程序结构：`GraphInstruction` 等
- 程序资产格式与缓存：`GraphProgramBlob`、`GraphProgramRegistry`、`GraphProgramBuffer`
- 执行循环（解释器/VM）：现有 `GraphExecutor`（需要把具体 op 语义外置）

2) **节点语义（应进 NodeLibraries）**
- 当前 `GraphNodeOp` 同时包含：
  - Std（算术/比较/Select/控制流）
  - Spatial（QueryRadius/Filter/Sort/Limit/Agg*）
  - GAS（LoadAttribute/ApplyEffect/ModifyAttr/SendEvent）

3) **宿主适配层（应进 NodeLibraries.GAS* 的 Host 侧）**
- `GasGraphRuntimeApi`：把 Graph 抽象动作接到 ECS/GAS（Position/Tag/Attribute/EffectRequest/EventBus/SpatialQuery）

4) **资产管线与符号绑定（应进 Host 侧，不进入 Core）**
- `GraphProgramLoader.PatchSymbols` 直接调用 `TagRegistry/AttributeRegistry/EffectTemplateIdRegistry` 与 VFS/ModLoader
- 这是 Core 与 GAS Registry 体系的最大耦合点，必须改为“宿主注入 resolver”

---

## Phase 0：安全铺垫（不改语义，先加护栏）

### 目标

- 把“分层边界”变成可验证约束
- 固化回归清单，避免搬家过程中语义漂移

### 迁移内容

- 新增目录骨架（仅结构，不要求一次性搬完）
  - `src/Core/GraphRuntime/`（命名空间建议：`Ludots.Core.GraphRuntime.*`）
  - `src/Core/NodeLibraries/`（命名空间建议：`Ludots.Core.NodeLibraries.*`）
- 新增/扩展测试（建议放在 `src/Tests/GasTests`）
  - 架构守护：`src/Core/GraphRuntime/**.cs` 不得引用 `Ludots.Core.Gameplay.GAS`（grep 级别即可）
- 本阶段不移动任何 Graph 文件

### 验收

- 现有 Graph 相关 tests 全绿
- 架构守护测试可阻止 Core.GraphRuntime 引入 GAS

---

## Phase 1：最小可行迁移（先搬 Core 资产/执行框架，再把 GAS 变成适配层）

### 目标

- 清空（或极薄化）`src/Core/Gameplay/GAS/Graph/`，把代码迁移到：
  - `Core.GraphRuntime`（运行时内核/资产格式）
  - `NodeLibraries.*`（节点与宿主适配）
- 保持现有行为与 tests 不变

### 风险提示（必须锁回归）

- Phase 1 的关键动作是把执行器从 `switch(enum)` 改为 **`IOpHandlerTable` 注入**。这会触碰执行核心路径与所有节点语义映射，改动面大，最容易出现：
  - opcode 到 handler 映射错位（语义串台）
  - 寄存器读写约定变化导致的隐性行为差异
  - 控制流（Jump/JumpIfFalse）相对偏移与 side effect 路径覆盖回退
- 因此 Phase 1 需要把回归测试作为“闸门”，任何失败都视为迁移不可合并状态（必须先修复再继续拆分）。

### 文件迁移清单

#### 1) 迁移到 Core.GraphRuntime（领域无关）

从 `src/Core/Gameplay/GAS/Graph/` 移动到 `src/Core/GraphRuntime/`：

- `GraphProgramBlob.cs`
- `GraphProgramBuffer.cs`
- `GraphProgramRegistry.cs`

拆分并迁移：

- `GraphBytecode.cs`
  - `GraphInstruction`（指令结构）→ `Core.GraphRuntime`
  - 宿主 API 接口（现 `IGraphRuntimeApi`）→ 暂时先归入 `NodeLibraries`（见下），避免 Core 引入 GAS 语义

说明：此阶段不强制引入完整 Graph IR 与版本系统，优先保持现有“编译产物 = Instruction[] + Symbols[]”链路可用。

#### 2) 迁移到 NodeLibraries（含领域语义/宿主适配）

从 `src/Core/Gameplay/GAS/Graph/` 移动到 `src/Core/NodeLibraries/GASGraph/`（命名可调整）：

- `GraphOps.cs`（`GraphNodeOp` / `GraphValueType` / parser）
- `GraphConfig.cs`
- `GraphDiagnostics.cs`
- `GraphValidator.cs`
- `GraphCompiler.cs`
- `GraphTargetList.cs`（若 Phase 1 不拆 SpatialNodes，可先放 GASGraph；Phase 2 再拆）
- `GasGraphRuntimeApi.cs`
- `GraphProgramLoader.cs`

从 `src/Core/Gameplay/GAS/Registry/` 移动到 `src/Core/NodeLibraries/GASGraph/Host/`：

- `GraphIdRegistry.cs`（目前仅被 `GraphProgramLoader` 使用）

删除或延后处理（当前未见引用点）：

- `GraphProgramRef.cs`（若后续确实要作为组件句柄，再在 Host 层复活）

### 关键适配点（Phase 1 必须做，且是“最小改法”）

#### A) Core.GraphRuntime 的执行器不再 switch 领域 enum

现状 `GraphExecutor` 直接 `switch(GraphNodeOp)`，会导致 Core 引入 GAS/Spatial/Std 语义。

Phase 1 的最小改造：

- Core.GraphRuntime 只提供：
  - 解释器循环
  - 寄存器文件与临时 buffer（固定容量、0GC）
- op 语义由 NodeLibraries 注入：
  - 引入 `IOpHandlerTable`（或 `Span<OpHandler>`）做 `opcode -> handler` 映射
  - NodeLibraries.GASGraph 构建 handler 表，覆盖当前全部 `GraphNodeOp`

#### B) 符号绑定（PatchSymbols）仍保留，但必须在 Host 侧

- `GraphProgramBlob` 仍存储 `symbols[]` 与 `program[]`（Imm 为 symbolIndex）
- `GraphProgramLoader` 在 Host 侧把 symbolIndex patch 成 runtime id
- 若要升级 blob version：允许直接 bump 并要求重新编译 graphs.bin；版本不匹配直接失败（不做兼容读取/静默降级）

### 验收

- `Gameplay/GAS/Graph` 目录被清空或仅剩极薄 glue
- 现有 Graph tests 全绿（见下方“Phase 1 必须全绿（现有）”）
- `Core.GraphRuntime` 路径下无任何 GAS 引用（守护测试通过）

---

## Phase 2：目标态（Runtime API 注入 + time-slice/ResetSlice + 预算/熔断 + NodeLibraries 分包）

### 目标

- 对齐两份设计文档的硬约束：
  - 确定性：稳定排序键、去重规则、禁止隐式非确定迭代
  - time-slice：切片不改变语义，ResetSlice 可恢复
  - 预算与熔断：MaxInstructions/MaxLoop/MaxCollectionSize 等
  - Runtime API 注入：Core 只认识接口通道

### 需要新增/演进的 Core.GraphRuntime 能力

- `IRuntimeApiRegistry`：运行时能力定位器（Core 只依赖接口，不依赖实现）
- `GraphExecutionState`：PC/寄存器/临时 buffers（动态状态不序列化，可复用）
- `GraphBudget` 与熔断事件：预算触顶行为可观测且固化（Drop/Fuse 二选一并写入测试）
- `CommandQueue`（或 `IGraphCommandSink`）：外部副作用延迟到提交点，确保 ResetSlice 幂等
- 执行 API 从 `Execute(...)` 升级为 `ExecuteSlice(...) -> GraphSliceResult`

### NodeLibraries 分包（Std / Spatial / GAS）

将 Phase 1 的 `NodeLibraries.GASGraph` 拆分为：

- `NodeLibraries.StdNodes`：算术/比较/控制流/集合基础
- `NodeLibraries.SpatialNodes`：空间查询与目标集合操作（依赖 `ISpatialQueryService`）
- `NodeLibraries.GASNodes`：Tag/Attribute/Effect/Request/Event（依赖 `IGasRuntimeApi` 等接口）

每个 NodeLibrary 需要：

- 注册 NodeTypeId/opcode/handler 的统一入口（Registry）
- 明确声明 Required APIs（缺失直接错误，不 silent fallback）

### 符号解析通用化

把 `PatchSymbols` 的硬编码 registry 调用升级为注入式 resolver：

- Core：只提供 symbol table + operand kind 的机制
- GASNodes：提供 Tag/Attribute/EffectTemplate/EventTag 的 `ISymbolResolver`

### 消除重复实现：Effect 应用逻辑复用

将以下两处“从模板创建 effect 实体/回调组件”等逻辑抽到 GAS 模块的单一实现：

- `InstructionExecutor`（CreateEntityEffect 路径）
- `GasGraphRuntimeApi.ApplyEffectTemplate`

---

## 回归测试清单

### Phase 1 必须全绿（现有）

- [GraphTests.cs](file:///c:/AIProjects/Ludots/src/Tests/GasTests/GraphTests.cs)
  - 编译：`GraphCompilerTests.Compile_BuildsSymbolTable_AndInstructions`
  - Blob：`GraphCompilerTests.Blob_RoundTrip_PreservesGraphNameSymbolsAndProgram`
  - Query/Agg：`GraphExecutorQueryTests.Execute_QueryFilterAggregate_ModifiesNearestTaggedTarget`
- [GraphNodeCoverageTests.cs](file:///c:/AIProjects/Ludots/src/Tests/GasTests/GraphNodeCoverageTests.cs)
  - 控制流/算术/Select/ApplyEffect/SendEvent 覆盖
- [GraphPerfTests.cs](file:///c:/AIProjects/Ludots/src/Tests/GasTests/GraphPerfTests.cs)
  - 热循环执行与当前线程分配统计（关注 0GC 与性能回归）

### Phase 0/2 建议新增

- 架构守护：Core.GraphRuntime 不允许引用 Gameplay.GAS
- 确定性：同一输入在不同 time-slice 切分下输出一致
- ResetSlice 幂等：中断/回滚后不产生重复副作用（通过 command queue 观测）
- 预算/熔断：MaxInstructions/MaxLoop/MaxCollectionSize 触顶行为稳定可观测

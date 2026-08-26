# 图 Codegen 产品化合同

作者继续画同一张蓝图；引擎在装载后可把图指令编成 C# 再跑。语义与解释器同一套，不是第二台图机。进度只认 [图能力唯一入口](graph-capability-status.md)；分层禁区见 [图怎么分层](graph-layering-flow-and-behavior.md)。上手看 [编辑器与 Live Debug](graph-editor-and-live-debug.md) 的 Codegen 面板（本页定合同，面板按切片落地）。

关联：#860（只换执行后端，不引入第二套作者格式）；审计 C23（测试尖峰须升格为正式后端或写明退役）。

---

## 1. 概述

今天仓库里只有 GasTests 尖峰：大约八个整数节点能编成 C#，其余一律拒绝。近期图能力（拼句、文案键、图互调、等回话、状态机/行为树糖、地图变量、放置实体……）都只在解释器上跑。编辑器能画图、能跟解释器调试，但看不到生成代码，也不能对照两种后端。

本页把这条线产品化为正式能力：

1. **作者格式不变**：仍是 `GraphControlFlowDocument` → FrontDoor → 同一份 `GraphInstruction[]`。
2. **执行后端可换**：解释器与 Codegen 共用 `GraphExecutionState` / `IGraphRuntimeApi` / source map / Live Debug 事件合同；禁止平行 opcode 表。
3. **全量 `GraphNodeOp` 覆盖是产品终态**：每个可执行 op 必须有发射策略 + 对拍证据；糖节点先降级再编，不另造糖 opcode。
4. **可视化辅助是一等公民**：编辑器能预览生成 C#、看资格报告、一键对拍、Live Debug 标注当前后端。

未达终态前，缺发射策略的 op **失败关闭**（装载或预览点名），禁止静默回落解释器假装「已经 codegen」。

---

## 2. 结构

```text
作者蓝图（唯一格式）
        │ FrontDoor + 糖降级
L0 GraphInstruction[] + Symbols + SourceMap
        │
        ├─ 解释后端：GasGraphOpHandlerTable.Execute / ExecuteSlice
        │
        └─ Codegen 后端（产品）
              GraphCsharpEmitter（按家族全量）
                    │
              Roslyn → Collectible ALC → GraphGeneratedExecute
                    │
              登记进 GraphProgramRegistry（与解释路径同一 GraphId）

可视化
  Bridge：资格报告 / IR 摘要 / 生成源码 / 对拍结果
  React Codegen 面板：预览 C#、红灯 op、一键对拍
  Live Debug：后端标签 + 同一套 node/pin 事件
```

| 层 | 职责 | 非职责 |
|----|------|--------|
| 作者面 | 蓝图、糖、descriptor | 手写生成代码当资产 |
| L0 IR | 指令与符号真相 | 第二套作者 JSON |
| 解释后端 | 语义先知 / 默认执行 / 对拍金样 | 「Codegen 失败时的静默备胎」（产品模式禁） |
| Codegen 后端 | 同语义加速执行 | 新 opcode、新 Api、旁路 `IGraphRuntimeApi` |
| Bridge / 编辑器 | 预览、资格、对拍、调试标注 | 在前端发明发射规则 |

---

## 3. 详情

### 3.1 合同澄清：不是第二执行器

[图怎么分层](graph-layering-flow-and-behavior.md)「禁止平行 GraphVmOpcode / 第二执行器」的含义收紧为：

- **禁止**：第二套 opcode 枚举、第二套作者格式、绕过 `IGraphRuntimeApi` 的平行改世界路径。
- **允许**：对**同一份** `GraphInstruction[]` 提供可替换的执行后端（解释 / Codegen），语义以解释 handler 为金样，Codegen 必须对拍通过。

#860：「将来只换执行后端，不得再引入第二套作者格式」——本产品化正是该句的落地合同。

### 3.2 语义 SSOT 与发射通则

| 规则 | 要求 |
|------|------|
| 金样 | `GasGraphOpHandlerTable` 各 `Handle*` 的可观察行为（寄存器、Api 调用、失败文案关键字、Yield/Halt 合同） |
| 发射 | 生成方法签名固定为 `void Execute(ref GraphExecutionState state)`（可另有紧路径优化入口，但产品对拍以 state 合同为准） |
| Api | 世界副作用只调用 `state.Api.*`；文字堆只碰 `state.Text`；程序符号只读登记表 |
| 失败 | 与解释器同一失败关闭；禁止截断、禁止缺绑定时空操作 |
| Source map | 生成代码可嵌入 `#line` 或旁路 map；Live Debug 仍按指令下标归因作者节点 |
| 糖 | `FormatText` / `FsmState` / `Bt*` / `While`… 只编**降级后的 L0**；资格报告按降级后 op 集合判定 |

### 3.3 全量覆盖：按家族发射策略（终态目录）

产品终态要求：`GraphOps.cs` 中每个可执行 `GraphNodeOp`（除 `None`）均落入下表之一，并有 ci-gate 对拍。新增 op 的 PR 必须同时交 handler + emitter + 对拍，否则不得标 covered。

| 家族 | 代表 op | 发射策略要点 |
|------|---------|--------------|
| F0 控制纯整数（已有尖峰） | `ConstInt` `AddInt` `Compare*Int` `Jump` `JumpIfFalse` `HaltReturnInt` | 寄存器直写；跳转 → label/goto；**允许回边**（产品取消尖峰「只许向前跳」） |
| F1 标量算术/比较/移动 | `*Float` `MoveInt` `ConstBool/Float` `SelectEntity`… | 同 F0，扩 float/bool/entity 寄存器 |
| F2 正式文字 | `ConstText` `ConcatText` `IntToText` `FloatToText` `SinkPresentationText` | `state.Text` Write/Concat/Get；ConstText 读 Symbols；Sink → `PushPresentationText` |
| F3 文案键 | `LoadTextKey` | `Write(Dst, Api.ResolvePresentationTextKey(Imm))`（Imm 已 patch） |
| F4 属性/标签/配置 | `LoadAttribute` `HasTag` `LoadConfig*` `Read/WriteBlackboard*`… | 直接调对应 Api；Imm 为已 patch id |
| F5 地图变量 / 放置实体 / 入口载荷 | `Read/WriteMapVar*` `LoadPlaced*` `LoadEntryPayload*` | Api + MapId/键；缺索引失败关闭 |
| F6 面板 | `Show/Hide/Create/DestroyPanel` | Api；与 #886 产品面板线独立，但 op 必须可编 |
| F7 查询 / 关系 / 空间 | `Query*` `Relationship*` `Snap*`… | 调 Api；列表流经 TargetList 槽；容量策略与解释一致 |
| F8 效果 / 生成 / 内建 | `ApplyEffect*` `SpawnTemplate` `InvokeBuiltin`… | 只经 Api/队列；禁止生成代码直写 World |
| F9 调用与挂起 | `Call` `Return` `Yield` `AwaitCallback` `InvokeScript` `InvokeGraph` `StoreArg*` `DispatchMapEvent` | **必须**服从 `ExecuteSlice` 游标合同：Yield/Await 写 Suspended 并 return；Call/Invoke 推栈；禁止生成「一口气跑完」绕过切片 |
| F10 生命周期 / 其它 400+ | `BeginLifecycleTransaction`… | 与解释同一 Api 边界 |

**覆盖门禁（SSOT）**

- 登记表：`assets/GAS/graph_node_op_coverage.registry.json` 增字段 `codegenStatus`: `pending` \| `covered` \| `exempt`（仅 `None` 可 exempt）。
- 测试：每个 `covered` op 至少一条「解释 vs Codegen」对拍（可与画廊 vignette IR 同源）。
- Generator：`EveryExecutableOp_HasCodegenStrategy` 失败关闭。

### 3.4 运行时产品宿主

从测试尖峰升格到 Core（建议命名，实现时以仓库为准）：

| 组件 | 职责 |
|------|------|
| `GraphCsharpEmitter` | 全量家族发射；未知 op → 诊断列表 |
| `GraphCodegenCompilerHost` | Roslyn + Collectible ALC；成功才替换入口；失败保留旧入口或装载失败（见模式） |
| `GraphExecutionBackend` | `Interpret` \| `Codegen` \| `Parity`（仅工具/CI） |
| `GraphProgramRegistration` | 可挂 `GeneratedExecute`；`Execute`/`ExecuteSlice` 优先走生成入口 |

**装载模式（数据驱动，禁硬编码静默）**

| 模式 | 行为 |
|------|------|
| `interpret` | 只解释（默认兼容） |
| `codegen` | 必须编过；失败 → 图装载失败关闭 |
| `codegen-prefer` | 能编则编；**个别图**因显式 allowlist 回退解释时必须打点名诊断进报告（不得静默）——产品默认不推荐；旗舰应用 `codegen` |

全局/按图策略落在引擎配置或 mod `game.json` 键（路径实现时登记 config_catalog，本页不发明平行 schema 文件名以外的第二套）。

### 3.5 可视化工具（产品）

#### Bridge API

| 方法 | 用途 |
|------|------|
| `POST /api/graph/{modId}/{graphId}/codegen/preview` | 编译作者图 → 返回资格报告 + 生成 C# 源码 + 诊断（不激活 ALC 亦可） |
| `POST /api/graph/{modId}/{graphId}/codegen/parity` | 在沙箱跑解释 vs Codegen（固定预算），返回差分 |
| `GET /api/graph/codegen/coverage` | 投影 registry 的 `codegenStatus` 汇总 |

资格报告字段（最少）：`eligible`、`unsupportedOps[]`（op + 指令下标 + 作者 nodeId）、`yieldPoints`、`backendRecommended`。

#### 编辑器 Codegen 面板（`/gas-graphs`）

与 Live Debug 并列的一栏：

1. **资格红绿灯**：整图绿/红；红灯列出节点（点名跳转画布）。
2. **生成源码预览**：只读 Monaco/等宽；一键复制；与当前保存版本绑定。
3. **一键对拍**：调用 parity；差分高亮寄存器/出口文本/sink。
4. **后端徽章**：Live Debug 标题显示 `Interpret` / `Codegen`；事件流不因后端分叉。

校验接口在现有 `instructionCount` 之外，可附带 `codegen` 摘要（可选字段，旧客户端忽略）。

#### AgentBridge

`ludots.graph.debug` 增加只读字段 `executionBackend`；不新增第二套 trace 协议。

### 3.6 交付切片（仍是同一产品终态）

实现按切片合入，但**合同终态是全覆盖**；切片只排工期，不缩小终态范围。

| 切片 | 交付 |
|------|------|
| CG-0 | 合同进 gitbook；coverage 字段；尖峰迁 Core 骨架；编辑器预览壳（可只对 F0 出码） |
| CG-1 | F0+F1 全量 + 回边循环；ci 对拍 |
| CG-2 | F2+F3 文字/文案键 + sink 对拍 |
| CG-3 | F4+F5 属性/地图/放置/载荷 |
| CG-4 | F9 调用与挂起（切片语义） |
| CG-5 | F7 查询关系 + F8 效果生成 + F6 面板 |
| CG-6 | 覆盖门禁全绿；旗舰图 `codegen` 模式；尖峰测试目录降为薄委托或删除重复 |

每切片必须：emitter + 对拍 + Bridge/面板可用字段不回退 + 更新本页「已覆盖家族」一句（进度仍只改 capability-status）。

### 3.7 与现有尖峰的关系

| 现状 | 产品化后 |
|------|----------|
| `Ludots.Tests.Gas.Graph.Codegen` | 迁入 Core（或 `Ludots.Graph.Codegen` 程序集），Tests 只留对拍 |
| `LinearIntGraphCsharpEmitter` 白名单拒绝对外 | 改为全量表驱动；未知 op 诊断 |
| 禁止向后跳 | **取消**；循环/While 降级图必须可编 |
| 无编辑器 | 必须有预览/对拍面板 |

---

## 4. 场景

1. 作者画完「守卫倒下了」拼句图，打开 Codegen 面板：绿灯，右侧能看到生成的 C#；点对拍，字幕出口与解释器一致。
2. 作者图上有 `AwaitCallback`，CG-4 未完成前预览红灯点名该节点；装载若强制 `codegen` 模式则失败并写明缺家族，不会上线后才炸。
3. 热更一张纯计算 FuncLib：Codegen 编译成功后替换 ALC 入口；编译失败则保留上一版生成体，并在面板打失败诊断（不静默改走解释）。
4. QA 打开覆盖页：看到每个 GraphNodeOp 的 `codegenStatus`；任一 `pending` 不得宣称「Codegen 产品完成」。
5. 夜袭旗舰在 `codegen` 模式下整图装载成功，Live Debug 徽章显示 Codegen，进节点事件仍能对上作者节点名。

---

## 5. 边界

- 不引入第二套作者格式或平行 opcode。
- 不在生成代码里直接操作 Arch World / 跳过 Api。
- 产品 `codegen` 模式禁止「编不过就偷偷解释」。
- 不把 Roslyn 引用塞进需要禁用分析器的游戏热路径程序集而不做程序集边界评审（实现时单独过 S14）。
- 面板线 #886、分层物理化 S14 不与本页抢进度；但面板 **op** 仍须在终态可编。
- 本页不替代 [图正式文字](graph-formal-text.md) / [TextKey](graph-textkey.md) 语义合同；只规定如何发射。

---

## 6. UAT

```gherkin
Feature: 蓝图能编成代码并看得见

  Scenario: 作者预览生成代码
    Given 我打开蓝图编辑器并保存了一张只含已覆盖家族的图
    When 我打开 Codegen 面板并点预览
    Then 我看到绿灯
    And 我能读到与这张图对应的 C# 源码
    And 源码里没有我没画过的节点名字被瞎编出来

  Scenario: 未覆盖节点会红灯点名
    Given 当前产品切片尚未覆盖「等回话」节点
    And 我的图上有一个等回话节点
    When 我打开 Codegen 面板
    Then 我看到红灯
    And 列表点名那个等回话节点
    And 强制 Codegen 装载时游戏拒绝装这张图并说明原因

  Scenario: 对拍一致才算过
    Given 一张拼句上字幕的图在解释器下字幕是「守卫倒下了」
    When 我对同一张图跑 Codegen 对拍
    Then 字幕出口同样是「守卫倒下了」
    And 对拍报告没有寄存器或出口差分

  Scenario: Live Debug 知道现在跑的是哪边
    Given 图以 Codegen 后端挂载并开启 Live Debug
    When 图跑过一个作者节点
    Then 调试面板标明后端是 Codegen
    And 我仍能看到该作者节点被点亮

  Scenario: 全量覆盖门禁
    Given 发布清单声称 Codegen 产品完成
    When 我查看覆盖登记
    Then 每个可执行图节点都是 covered
    And 没有 pending 项被静默跳过
```

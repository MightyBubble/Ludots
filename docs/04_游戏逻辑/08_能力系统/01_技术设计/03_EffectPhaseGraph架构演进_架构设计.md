---
文档类型: 架构设计
创建日期: 2026-02-07
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v1.1
适用范围: 游戏逻辑 - 能力系统(GAS) - Effect Phase Graph 架构演进
状态: 已落地（部分结构待重命名）
---

# Effect Phase Graph 架构演进

# 1 背景与动机

## 1.1 改造前的问题

原 Effect 系统的生命周期回调基于 `EffectCallbackComponent`（静态 effectId 字段）和 `InstructionExecutor`（指令缓冲执行器），存在以下瓶颈：

| 问题 | 影响 |
|---|---|
| 回调粒度粗：只有 OnApply / OnPeriod / OnExpire / OnRemove 四个固定 hook | 无法在提案阶段预计算、无法拆分命中检测与实际结算 |
| 回调能力弱：只能触发子 Effect，不能做自定义公式或条件分支 | 伤害反弹、护盾抵消等复杂场景必须 hardcode |
| 配置与执行耦合：callback 字段直接绑定 effectId | 同一 Graph 程序无法被不同 Effect 参数化复用 |
| 两套执行路径并存：InstructionExecutor 与 GraphExecutor 职责重叠 | 维护成本翻倍，bug 容易互相遮掩 |

## 1.2 设计目标

- **一切皆 Graph**：所有动态行为（公式、条件、触发子效果）统一为 Graph 程序
- **8 阶段 × 3 槽位**：细化生命周期为 8 个 Phase，每个 Phase 支持 Pre / Main / Post 三段式执行
- **配置驱动**：Graph 程序通过 ConfigParams 参数化，同一程序可在不同 EffectTemplate 间复用
- **Blackboard 即时读写**：Graph 内直接操作 ECS 组件，无中间缓冲
- **零 GC**：所有新增结构使用 `unsafe struct` + `fixed` 数组，热路径无堆分配

# 2 核心设计

## 2.1 Phase 模型

```
OnPropose → (ResponseChain) → OnCalculate → OnResolve → OnHit → OnApply → OnPeriod → OnExpire → OnRemove
    0                             1              2          3        4          5          6          7
```

```csharp
public enum EffectPhaseId : byte   // 8 个阶段
{
    OnPropose = 0,   // 提案，ResponseChain 之前
    OnCalculate = 1, // ResponseChain 结算后，计算最终值
    OnResolve = 2,   // 目标搜索（空间查询）
    OnHit = 3,       // 逐目标命中验证（闪避/护盾/免疫）
    OnApply = 4,     // 施加 Modifier
    OnPeriod = 5,    // 持续效果周期 tick
    OnExpire = 6,    // 自然过期
    OnRemove = 7,    // 强制移除
}

public enum PhaseSlot : byte       // 3 个槽位（借鉴 Mod Trigger 框架）
{
    Pre = 0,   // InsertBefore — 用户自定义 Graph
    Main = 1,  // AnchorCommand — 预设行为 Graph
    Post = 2,  // InsertAfter — 用户自定义 Graph
}
```

## 2.2 数据结构

### EffectBehaviorTemplate（替代 EffectCallbackComponent）

```csharp
public unsafe struct EffectBehaviorTemplate
{
    public const int MAX_STEPS = 16;
    public int StepCount;
    public fixed byte StepPhases[MAX_STEPS];     // EffectPhaseId
    public fixed byte StepSlots[MAX_STEPS];      // PhaseSlot (Pre/Post)
    public fixed int StepGraphIds[MAX_STEPS];    // GraphProgramId
    public byte SkipMainFlags;                   // bit N = skip Main for phase N
}
```

### EffectConfigParams（Graph 参数化）

```csharp
public unsafe struct EffectConfigParams
{
    public const int MAX_PARAMS = 16;
    public int Count;
    public fixed int Keys[MAX_PARAMS];
    public fixed byte Types[MAX_PARAMS];       // Float / Int / EffectTemplateId
    public fixed int IntValues[MAX_PARAMS];
    public fixed float FloatValues[MAX_PARAMS];
}
```

### PresetBehaviorRegistry（预设类型 → Main Graph 映射）

```csharp
public unsafe struct PresetBehaviorDescriptor
{
    public fixed int MainGraphIds[8]; // 每个 Phase 的 Main GraphProgramId
}
```

## 2.3 执行流程

```
EffectPhaseExecutor.ExecutePhase(phase, behavior, presetType):
    ① Pre  = behavior.GetGraphId(phase, Pre)   → 执行用户 Graph
    ② Main = presets.GetMainGraphId(preset, phase) → 执行预设 Graph（除非 SkipMain）
    ③ Post = behavior.GetGraphId(phase, Post)  → 执行用户 Graph
```

所有 Graph 共享同一组寄存器（F/I/B/E），Blackboard 写入即时生效，前序 Graph 的输出可被后序 Graph 读取。

## 2.4 新增 Graph Op（16 个）

| 分类 | Op | 编码 | 说明 |
|---|---|---|---|
| **数学** | SubFloat | 22 | 减法 |
| | DivFloat | 23 | 除法（除零→0） |
| | MinFloat | 24 | 取较小值 |
| | MaxFloat | 25 | 取较大值 |
| | ClampFloat | 26 | 钳位 clamp(val, min, max) |
| | AbsFloat | 27 | 绝对值 |
| | NegFloat | 28 | 取反 |
| **Blackboard** | ReadBlackboardFloat | 300 | 即时读 BB float |
| | ReadBlackboardInt | 301 | 即时读 BB int |
| | ReadBlackboardEntity | 302 | 即时读 BB entity |
| | WriteBlackboardFloat | 303 | 即时写 BB float |
| | WriteBlackboardInt | 304 | 即时写 BB int |
| | WriteBlackboardEntity | 305 | 即时写 BB entity |
| **Config** | LoadConfigFloat | 310 | 读 EffectTemplate 配置参数 (float) |
| | LoadConfigInt | 311 | 读 EffectTemplate 配置参数 (int) |
| | LoadConfigEffectId | 312 | 读 EffectTemplate 配置参数 (effectId) |

## 2.5 配置 Schema（JSON）

```json
{
  "id": "Effect.FireImpact",
  "presetType": "None",
  "durationType": "Instant",
  "phaseGraphs": {
    "onPropose": { "pre": "Graph.MarkProposed" },
    "onApply": {
      "pre": "Graph.ComputeDamage",
      "post": "Graph.AccumulateDamage"
    },
    "onExpire": { "pre": "Graph.ComputeReflect" }
  },
  "configParams": {
    "baseDamage": { "type": "float", "value": 20.0 },
    "dmgMultiplier": { "type": "float", "value": 1.5 },
    "childEffect": { "type": "effectTemplate", "value": "Effect.BurnTick" }
  }
}
```

# 3 改动清单

## 3.1 新增文件（5 个）

| 文件 | 说明 |
|---|---|
| `GAS/EffectPhaseId.cs` | EffectPhaseId(8) + PhaseSlot(3) 枚举 |
| `GAS/Components/EffectBehaviorTemplate.cs` | 用户 Phase Graph 绑定 + SkipMainFlags |
| `GAS/Components/EffectConfigParams.cs` | 零 GC 参数表，Graph 通过 LoadConfig* 读取 |
| `GAS/PresetBehaviorRegistry.cs` | PresetType → 各 Phase 的 Main GraphProgramId 映射 |
| `GAS/Systems/EffectPhaseExecutor.cs` | Pre/Main/Post 三段式统一执行器 |

## 3.2 删除文件（2 个）

| 文件 | 原因 |
|---|---|
| `GAS/Components/EffectCallbackComponent.cs` | 被 EffectBehaviorTemplate 完全替代 |
| `GAS/InstructionExecutor.cs` | 被 GraphExecutor + EffectPhaseExecutor 完全替代 |

## 3.3 扩展文件（12 个）

| 文件 | 改动 |
|---|---|
| `GraphOps.cs` | +16 个 GraphNodeOp 枚举值 |
| `GraphVmLimits.cs` | HandlerTableSize 256→512 |
| `GasGraphOpHandlerTable.cs` | +16 个 Handler 方法实现 |
| `IGraphRuntimeApi.cs` | +9 个接口方法（BB×6 + Config×3） |
| `GasGraphRuntimeApi.cs` | 实现 BB 即时读写 + Config 上下文管理 |
| `EffectTemplateRegistry.cs` | EffectTemplateData 增加 BehaviorTemplate + ConfigParams 字段 |
| `EffectTemplateConfig.cs` | JSON schema 增加 PhaseGraphs + ConfigParams |
| `EffectTemplateLoader.cs` | 解析新配置字段，旧字段输出弃用警告 |
| `ResponseChainComponents.cs` | ResponseChainListener 增加 ResponseGraphIds[] |
| `TargetResolverFanOutHelper.cs` | 拆分为 ResolveTargets(OnResolve) + ValidateAndCollect(OnHit) |
| `EffectProposalProcessingSystem.cs` | 集成 OnPropose / OnCalculate Phase Graph 执行 |
| `EffectApplicationSystem.cs` | 集成 OnResolve / OnHit / OnApply Phase Graph 执行 |
| `EffectLifetimeSystem.cs` | 集成 OnPeriod / OnExpire / OnRemove Phase Graph 执行 |

## 3.4 测试文件调整（6 个）

清理所有对 `EffectCallbackComponent` 的引用，调整 test fixture 以匹配新 API。

# 4 典型游戏场景支持

| 场景 | 实现方式 |
|---|---|
| **SC2 兴奋剂** | OnApply.Pre: 读 Config(hpCost, atkBonus)，修改属性 |
| **游戏王效果连锁** | OnPropose: 各效果独立入队；ResponseChain: LIFO 结算 |
| **伤害反弹 50%** | OnApply.Post: 写 BB(accumDamage)；OnExpire.Pre: 读 BB × 0.5 → 触发反弹效果 |
| **护盾抵消** | OnHit.Pre: 读目标 BB(shieldHP)，clamp 伤害，写 BB 更新护盾值 |
| **可配置 DOT** | Config(tickDamage=5, tickCount=3)，OnPeriod.Main 读 Config 施加伤害 |
| **完全自定义效果** | SkipMain + Pre 自定义公式 Graph，预设行为完全被覆盖 |

# 5 性能基准

## 5.1 测试环境

- CPU: 测试机（CI/开发机）
- Runtime: .NET 8.0, NUnit 4.2.2
- 配置: Release-equivalent (no debugger attached)

## 5.2 压力测试结果

| 测试 | 规模 | 总耗时 | 单次延迟 | GC Alloc | GC 回收 |
|---|---|---|---|---|---|
| PhaseExecutor 批量 | 500 实体 × 8 Phase × 100 轮 = **40 万次** | 651ms | **1.63μs** | 40B | Gen0/1/2=0 |
| Math Ops 链 | 10 指令 × **1 万轮** | 3.4ms | **0.34μs** | 40B | - |
| BB 读写批量 | 1000 实体 × **100 轮** = 10 万次 | 100ms | **1.00μs** | 40B | Gen0=0 |
| 奥术齐射(原有) | 2000 目标 × 5 帧 = **1 万 roots** | 117ms | 11.96μs/root | 3.3MB | Gen0=0 |
| SC2 EMP(原有) | 10000 roots × 5 帧 | 11.4ms | **1.14μs/root** | 0B | Gen0=0 |

**结论**：新增架构层 Phase Graph 执行开销 < 2μs/次，零 GC，不影响既有热路径。

## 5.3 测试覆盖

| 文件 | 类型 | 测试数 | 覆盖范围 |
|---|---|---|---|
| `EffectPhaseArchitectureTests.cs` | 单元测试 | **31** | BehaviorTemplate(5), ConfigParams(5), PresetRegistry(2), PhaseExecutor(3), MathOps(8), BBOps(5), ConfigOps(3) |
| `EffectPhaseStressTests.cs` | 压力测试 | **3** | PhaseExecutor 吞吐量, Math 链零 GC, BB 批量零 GC |
| `MudPhaseGraphDemoTests.cs` | MUD 场景 | **3** | 完整生命周期, SkipMain 覆盖, Config 参数化复用 |

**全部 172/173 通过**（1 个失败为先前已有的架构守卫测试，与本次改动无关）。

# 6 迁移指南

## 6.1 旧 → 新映射

| 旧机制 | 新机制 |
|---|---|
| `EffectCallbackComponent.OnApplyEffectId` | `EffectBehaviorTemplate` + OnApply.Post Graph（内含 ApplyEffectTemplate 节点） |
| `EffectCallbackComponent.OnPeriodEffectId` | OnPeriod.Main Graph（内含 ApplyEffectTemplate 节点） |
| `EffectCallbackComponent.OnExpireEffectId` | OnExpire.Post Graph |
| `EffectCallbackComponent.OnRemoveEffectId` | OnRemove.Post Graph |
| `InstructionExecutor` | 删除，由 `GasGraphOpHandlerTable.Execute` 替代 |
| 硬编码伤害公式 | Graph 程序 + ConfigParams 参数化 |

## 6.2 JSON 配置迁移

旧格式（弃用，加载时输出警告）：
```json
{ "onApplyEffect": "Effect.BurnTick", "onPeriodEffect": "Effect.BurnTick" }
```

新格式：
```json
{
  "phaseGraphs": {
    "onApply": { "post": "Graph.TriggerBurnOnApply" },
    "onPeriod": { "pre": "Graph.TriggerBurnOnPeriod" }
  },
  "configParams": {
    "childEffect": { "type": "effectTemplate", "value": "Effect.BurnTick" }
  }
}
```

# 7 架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                    EffectTemplate (JSON)                          │
│  phaseGraphs: { onApply: { pre, post }, onExpire: { pre } }     │
│  configParams: { baseDmg: 20, multiplier: 1.5 }                 │
└──────────────────────┬──────────────────────────────────────────┘
                       │ compile
                       ▼
┌──────────────────────────────────────────────────────────────────┐
│              EffectTemplateData (runtime)                         │
│  BehaviorTemplate: EffectBehaviorTemplate (Pre/Post graph IDs)   │
│  ConfigParams: EffectConfigParams (key-value table)              │
│  PresetType → PresetBehaviorRegistry (Main graph IDs)            │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────────────────┐
│                   EffectPhaseExecutor                             │
│                                                                  │
│  for each Phase in lifecycle:                                    │
│    ┌─── Pre Graph  (user-defined) ───┐                           │
│    │  LoadConfig*, ReadBB*, WriteBB*  │                           │
│    └─────────────────────────────────┘                           │
│    ┌─── Main Graph (preset-defined) ──┐  ← SkipMain 可跳过      │
│    │  预设行为 (e.g. ApplyForce2D)    │                           │
│    └─────────────────────────────────┘                           │
│    ┌─── Post Graph (user-defined) ───┐                           │
│    │  ApplyEffectTemplate, SendEvent  │                           │
│    └─────────────────────────────────┘                           │
└──────────────────────────────────────────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────────────────┐
│  Graph VM (GasGraphOpHandlerTable)                               │
│  寄存器: F[32] I[32] B[32] E[32]                                 │
│  指令集: 原有 25 + 新增 16 = 41 个 Op                             │
│  BB 即时读写 │ Config 参数读取 │ 数学运算 │ 效果触发               │
└──────────────────────────────────────────────────────────────────┘
```

# 8 总结

本次重构以 **"一切皆 Graph"** 为核心原则，将 Effect 生命周期从 4 个固定回调扩展为 8 阶段 × 3 槽位的可编程管线。删除了 2 个遗留文件，新增 5 个核心组件，扩展 12 个既有文件，新增 16 个 Graph Op，编写 37 个测试用例。所有改动零 GC、微秒级执行，与既有系统完全兼容。

---

> SSOT: 本文档记录 Phase Graph 架构的首次落地。后续设计演进见下方第 9 节。  
> 代码入口: `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs`

# 9 架构对齐说明（2026-02-08）

本文档记录的是 Phase Graph 架构的**首次落地状态**。后续设计已在以下方面做出进一步演进，尚未全部落地：

| 本文描述 | 最新设计 | 参考文档 |
|---|---|---|
| `EffectBehaviorTemplate` | 重命名为 `EffectPhaseGraphBindings` | `06_Effect生命周期Phase_架构设计.md` 第 5 节 |
| `EffectConfigParams.MAX_PARAMS = 16` | 扩容为 32 | `05_Effect参数架构_架构设计.md` 第 2.3 节 |
| `PresetBehaviorRegistry` 独立注册 | 整合进 `PresetTypeDefinition.PhaseHandlers` | `04_Effect类型体系_架构设计.md` 第 3.4 节 |
| `presetType: "None"` | 每个 template 必须声明真实 PresetType | `03_配置结构/01_EffectPresetType定义_配置结构.md` |
| JSON `durationType` 字段 | 改为 `lifetime` | `03_配置结构/02_EffectTemplate配置_配置结构.md` 第 7 节 |
| 无 CallerParams 机制 | `EffectRequest.CallerParams` + `MergeFrom` | `05_Effect参数架构_架构设计.md` 第 3 节 |
| 无 PhaseListener | `EffectPhaseListenerBuffer` 反应式监听 | `07_EffectPhaseListener_架构设计.md` |
| 无 `_ep.*` 保留键 | `EffectParamKeys` 按组件注入 | `05_Effect参数架构_架构设计.md` 第 2.1 节 |

完整改造项见 `11_Effect改造清单_架构设计.md`。

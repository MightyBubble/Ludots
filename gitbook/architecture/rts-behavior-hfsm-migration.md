# Tech Spec — RTS 前线行为回归正路：用既有 HFSM 基建驱动，补极小 caster 透传

**关联**：issue #1536、分支 codex/issue-1536-actionloop-graph-brains、PR #1542
**状态**：已完成的 v1 是"图行为用手写 BranchBool + 自建 GraphActionBrainHostSystem"。本 spec 是把行为迁到**仓库既有的 HFSM 基建**（HfsmWorld + GraphProgramHfsmHost + AI/hfsm.json + GAS/action_lib.json），**仅补一个真实的 gap**，不新造第二套行为宿主、不新增平行 ECS 组件系统。

---

## 1. 为什么回归正路

现状 v1（可运行、已对拍）：
- 行为是两张 Script 图（`rts.frontline.attack` / `rts.frontline.transport`），内部分支用手写 `BranchBool`（写在实体黑板 `Brain.*` 键上）完成状态流转。
- 由新写的 `GraphActionBrainHostSystem`（按 `GraphActionBrain` 组件扫描实体）驱动，它自己管理 per-entity 槽位（BrainPool）+ 订单胶水（`Order.*` 黑板键）。

问题：**这套"手写状态机 + 自建 per-entity 池"是在重复实现仓库本来就有的 HFSM 能力**——`HfsmWorld`（按 agent 驱动状态机的池）+ `GraphProgramHfsmHost`（每个状态可跑 onEnter/onTick/onExit 子图、转移可带 condition 子图）+ `AI/hfsm.json`（声明式 HFSM 作者面）+ `GAS/action_lib.json`（host=Hfsm 的行为图登记）。哨兵 showcase 已验证这套能飞，方向必须是"用这套"，而不是继续手写。

## 2. 既有基建能力（复用，绝不重造）

| 能力 | 载体 | 状态（实探） |
|---|---|---|
| HFSM 定义作者面（compound/leaf、defaultChild、transitions、onEnter/onTick/onExit、condition） | `assets/AI/hfsm.json` + `GraphBehaviorDefinitionLoader` → `GraphBehaviorCatalog.RequireHfsm` | ✅ 已有，哨兵在用 |
| 每个状态挂生命周期子图 | HFSM 定义里 `onEnter/onTick/onExit` 指向 **ActionLib 名字** → `GraphActionCatalog.RequireAction(name, GraphActionHost.Hfsm)` | ✅ 已有，须 host=Hfsm |
| 转移可带守卫图 | HFSM `condition` → `ConditGraphId` → `EvalCondition` | ✅ 已有 |
| 按 agent 驱动状态机池 | `HfsmWorld`（AddAgent / TickAll / GetLeafState） | ✅ 已有，哨兵在用 |
| 指令级为各 Agent 提供 world/api | `GraphProgramHfsmHost(_programs, world, api)` | ✅ 已有，**但见 §4 的 gap** |
| Bridge 编辑器（mod 源 hfsm） | `/api/ai/hfsm` 支持 `source=core\|modId`，读 `mod.RootPath/assets/AI/hfsm.json` | ✅ 已有 |

**明确不新造**：
- ❌ 不新写"entity-aware"的第二个 HFSM 运行时 —— `HfsmWorld` 就是按 agent 驱动，只需让每个 agent 能对应实体。
- ❌ 不把 `GraphActionBrainHostSystem.BrainPool` 整套搬进 HFSM —— `GraphProgramHfsmHost` 每次运行都清寄存器并严格 halt，不需要常驻帧；每 agent 只需绑定 caster。
- ❌ 不用 `FsmState` 糖做 per-entity —— 它是 per-map 变量 + `Entity.Null` caster，用于"同图所有单位共享"的场景，和"每实体要自己的订单"语义不匹配（当前把状态放**实体黑板**是被既有能力逼出来的正确解，保留）。

## 3. 目标结构（已实现 — 真走 HFSM，driver 是通用系统而非手写状态机）

```
mods/showcases/rts_multiplayer_frontline/RtsMultiplayerFrontlineMod/assets/
  AI/hfsm.json           # 已实现: hfsm.rts.transport / hfsm.rts.attack（全新 id，绝不覆盖 Core 全域）
  GAS/action_lib.json   # 已实现: 21 个 host:"Hfsm" 生命周期+condition 行为图登记
  GAS/graphs.json       # 改: 原 attack/transport 已拆成各状态 onEnter/onTick/onExit 小图 + 转移 guard 图
  Entities/templates.json # 改: GraphActionBrain.Script -> HfsmId（ScriptKey 两存兼容，遗留 host 测试迁制）
```

驱动（已实现于 `src/Core/Gameplay/GraphBrains/HfsmBrainHostSystem.cs`，**通用**）：
- 扫 `GraphActionBrain + OrderBuffer + PlayerOwner + 黑板` 实体；**每个实体一个 `HfsmWorld`(capacity:1) + `GraphProgramHfsmHost`**（因 HFSM 运行时无 RemoveAgent；实体消亡则整槽扫走）。
- `SetAgentCasters([entity])` + `AddAgent(host)` + 每 tick 写 `Order.*` 胶水 → `TickAll(host)`。
- **不自己写状态、不复制 BraintPool、不启用 FsmState**；状态只活在 HFSM 栈，各状态全部由 action_lib 小图完成。
- budget：`GraphProgramHfsmHost` 构造可选默认 64，前线驱动传 128（standoff 路由图 85 条指令），向后兼容哨兵。

## 4. 唯一真实的 gap：`GraphProgramHfsmHost` 不透传实体 caster

**实探证据**：`src/Core/Gameplay/AI/Fsm/GraphProgramHfsmHost.cs`：
- `EvalCondition(int agentIndex, int conditionGraphId)` / `RunAction(int agentIndex, int actionGraphId)` 把 **agentIndex 丢弃**（只当标记），从不据此查实体。
- `ExecuteHalt(...)` 调 `GraphExecutor.ExecuteResolvedRegisteredScriptSlice(_programs, program, ..., budgetSteps:64, _world, api:_api)` —— **关键：`caster` 形参未传，默认 `Entity.Null`**。
- 而行为图入口是 `self0 = LoadCaster()` 再 `ReadBlackboardInt(source:self0, "Order.ActiveTypeId")`；`Entity.Null` 时 `ReadBlackboardInt → TryReadBlackboardInt → world.Get<BlackboardIntBuffer>(Entity.Null)` 抛 `MissingBlackboard`（`GasGraphRuntimeApi`）。

**哨兵为何能跑**：它**没有订单**，只 `LatchStimulus`，从不用实体黑板，所以 `Entity.Null` 从没访问任何组件 → 从没炸。

**修复（极小、向后兼容）**：
```csharp
// GraphProgramHfsmHost 增加面向"要实体的 agent"的能力
private Entity[] _agentCasters = Array.Empty<Entity>();
public void SetAgentCasters(ReadOnlySpan<Entity> casters) { … _agentCasters = casters.ToArray(); }
public void RunAction(int agent, int g) => ExecuteHalt(g, "状态机生命周期", caster: SafeCaster(agent));
public bool EvalCondition(int agent, int g) => ExecuteHalt(g, "状态机条件", caster: SafeCaster(agent)).ReturnInt != 0;
private Entity SafeCaster(int agent) => (uint)agent < _agentCasters.Length ? _agentCasters[agent] : Entity.Null;
```
`ExecuteHalt` 增加可选 `caster` 参数并透传给 `ExecuteResolvedRegisteredScriptSlice(caster: caster)`。**默认仍 Entity.Null**，完全向后兼容 showcase（不设 caster 时行为不变）。

**不必动的**：
- `HfsmWorld` 若存在"动态增删 agent"需求（RTS 生灭），**需要确认** `HfsmWorld` 无释放；但可在宿主层用"每个实体一个 HfsmWorld（capacity 小）"或"活动集合再分配"绕开，先不做 HfsmWorld 结构性改造（如需再议）。

## 5. 决策要关（本 spec 待 act 前定）

1. **攻击和运矿是否都迁**：对抗审计建议**同批迁**，避免"两套写法"裂缝；但也接受"先运矿示范、攻击后…"并在能力页登记两套并存。→ 倾向**同批迁**，工作量本就不大（纯配置拆分）。
2. **GraphActionBrain 组件保留吗**：若 HFSM 驱动按 `hfsm.id` 识别实体，可把 `GraphActionBrain.Script` 改成 `GraphActionBrain.HfsmId`（或保留 Script 字段但节点从 action_lib 取）。→ 需要一次组件伪接口决策，见 issue。
3. **夹具加载 mod assets**：`GraphBrainFrontlineEquivalenceTests` 目前只手编 mod graphs.json、不加载 hfsm/action_lib。→ 需新增"过真实 ConfigPipeline 加载 mod AI/hfsm + action_lib"的路径（扩 `GraphRegistryTestBootstrap`）。这是 P1-3（线上）也是**必须**，且是对拍的真正门（playable 用真实引擎、等价用真实加载）。
4. **39 预算**：HFSM 每状态生命周期图别越 32 预算；最好用 action_lib 的纯度检查（会跑）。原单图已顶满预算——拆成每状态小图本来就应该更小、更安全。

## 6. 验收（对拍要**两条都绿**）

- **等价**：`GraphBrainFrontlineEquivalenceTests`（新增加载 mod hfsm + action_lib 路径）、且行为同现状 —— 运矿的"采集→装→返→卸"，只在码头 +20；攻击的"追→近距→开火""死目标停机"全部保持。
- **真实买单**：`RtsMultiplayerFrontlinePlayableAcceptanceTests` 用真实 GameEngine，断言"离开起点/到矿点/loading 不记入账/到码头才记＋"依然过。
- **新增**：一个"实体绑定生命周期"的最小测试（现在 `FsmRuntimeTests` 全是寄存器桩，覆盖不到 caster 与实体黑板）。
- **零分配**：稳态 idle tick 无 `GC.AllocatedBytesForCurrentThread` 增长（对被删的 `StableIdleTicks_AreAllocationFree` 的对等，并给 brain 宿主补）。

## 7. 边界 / 不做

- 不新开平行 ECS 行为系统；复用 `HfsmWorld` / `GraphProgramHfsmHost` / `AI/hfsm.json` / `GAS/action_lib.json`。
- 不启用 `FsmState` 糖（per-map 语义）当 per-entity。per-entity 状态继续存在实体黑板 `Brain.*`（既有正确做法）。
- 不引入新配置文件格式；全部沿用既有（hfsm.json / action_lib.json / graphs.json）。
- Core 的 `assets/config_catalog.json` **不需要加** `AI/hfsm.json`、也不需要动 Core 的 hfsm —— 走 mod source 已有支持（见 §2）。

---

## 附：为什么"不是又造新代码"（对 PI 疑问的直答）★
- `HfsmWorld` + `GraphProgramHfsmHost` + onEnter/tick/exit + condition + action_lib + bridge —— **每一样都已在仓库存在且哨兵真跑过**，不是骗人。
- 唯一缺的是 `GraphProgramHfsmHost` 在**需要真实实体（订单/黑板）**时，把 `caster` 透传下去（现写死 `Entity.Null`）。这极小、向后兼容、是"给既有轮子补该有的轴"，**不是**新写第二个 HFSM 运行时时、更不是复制 BraintPool 或再造 per-entity 池。

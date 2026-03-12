# Graph Op 速查表

> 来源：`src/Core/NodeLibraries/GASGraph/GraphOps.cs`

所有 Graph 程序在 Effect Phase 内执行，无法做结构变更（创建/删除实体、挂载组件）。结构变更必须通过 `RuntimeEntitySpawnQueue` 或 `CommandBuffer` 在 Phase 外处理。

---

## 常量与控制流

| Op | 代码 | 用途 |
|----|------|------|
| `ConstBool` | 1 | B[dst] = 立即值 |
| `ConstInt` | 2 | I[dst] = 立即值 |
| `ConstFloat` | 3 | F[dst] = 立即值 |
| `Jump` | 6 | 无条件跳转 |
| `JumpIfFalse` | 7 | B[cond]==0 时跳转 |

## 实体加载

| Op | 代码 | 用途 |
|----|------|------|
| `LoadCaster` | 4 | E[dst] = 施法者（AbilityExec 的 owner） |
| `LoadExplicitTarget` | 5 | E[dst] = 显式目标 |
| `LoadContextSource` | 320 | E[dst] = EffectContext.Source |
| `LoadContextTarget` | 321 | E[dst] = EffectContext.Target |
| `LoadContextTargetContext` | 322 | E[dst] = EffectContext.TargetContext（AoE 中心等） |
| `SelectEntity` | 40 | 从 TargetList 中选取 |

## 属性操作

| Op | 代码 | 用途 |
|----|------|------|
| `LoadAttribute` | 10 | F[dst] = entity.Attribute[key] |
| `LoadSelfAttribute` | 330 | F[dst] = Caster.Attribute[key]（无 EffectContext 时用，如派生属性图） |
| `WriteSelfAttribute` | 331 | Caster.Attribute[key] = F[src]（直接 SetCurrent，绕过 Modifier 聚合） |
| `ModifyAttributeAdd` | 210 | entity.Attribute[key] += F[delta]（走标准 Modifier 路径） |

## 浮点运算

| Op | 代码 | 用途 |
|----|------|------|
| `AddFloat` | 20 | F[dst] = F[a] + F[b] |
| `MulFloat` | 21 | F[dst] = F[a] * F[b] |
| `SubFloat` | 22 | F[dst] = F[a] - F[b] |
| `DivFloat` | 23 | F[dst] = F[a] / F[b]（除零→0） |
| `MinFloat` | 24 | F[dst] = min(F[a], F[b]) |
| `MaxFloat` | 25 | F[dst] = max(F[a], F[b]) |
| `ClampFloat` | 26 | F[dst] = clamp(F[a], F[b], F[c]) |
| `AbsFloat` | 27 | F[dst] = |F[a]| |
| `NegFloat` | 28 | F[dst] = -F[a] |
| `CompareGtFloat` | 30 | B[dst] = F[a] > F[b] |

## 整型运算

| Op | 代码 | 用途 |
|----|------|------|
| `AddInt` | 29 | I[dst] = I[a] + I[b] |
| `CompareLtInt` | 31 | B[dst] = I[a] < I[b] |
| `CompareEqInt` | 32 | B[dst] = I[a] == I[b] |

## Tag 查询

| Op | 代码 | 用途 |
|----|------|------|
| `HasTag` | 33 | B[dst] = entity.HasTag(Imm) |

## Blackboard 读写

| Op | 代码 | 用途 |
|----|------|------|
| `ReadBlackboardFloat` | 300 | F[dst] = entity.BB[keyId] |
| `ReadBlackboardInt` | 301 | I[dst] = entity.BB[keyId] |
| `ReadBlackboardEntity` | 302 | E[dst] = entity.BB[keyId] |
| `WriteBlackboardFloat` | 303 | entity.BB[keyId] = F[src] |
| `WriteBlackboardInt` | 304 | entity.BB[keyId] = I[src] |
| `WriteBlackboardEntity` | 305 | entity.BB[keyId] = E[src] |

标准 Blackboard Key（伤害管线）：
- `DamageAmount` (float) — OnCalculate 写入，OnApply 读取
- `DamageType` (int) — 分类（物理/魔法/真实）
- `IsTrueDamage` (int) — 1=跳过减伤
- `FinalDamage` (float) — 减伤后的最终伤害
- `MitigatedAmount` (float) — 被吸收量（护盾/减伤统计）

## 配置参数读取

| Op | 代码 | 用途 |
|----|------|------|
| `LoadConfigFloat` | 310 | F[dst] = EffectTemplate.ConfigParams[keyId] |
| `LoadConfigInt` | 311 | I[dst] = EffectTemplate.ConfigParams[keyId] |
| `LoadConfigEffectId` | 312 | I[dst] = EffectTemplate.ConfigParams[keyId]（effectTemplateId） |

## 空间查询

| Op | 代码 | 用途 |
|----|------|------|
| `QueryRadius` | 100 | TargetList = 以 TargetPos 为中心、半径 Imm(cm) 内的实体 |
| `QueryCone` | 104 | TargetList = 锥形区域 |
| `QueryRectangle` | 105 | TargetList = 矩形区域 |
| `QueryLine` | 106 | TargetList = 线形区域 |
| `QueryFilterTagAll` | 101 | 过滤：必须含有所有指定 Tag |
| `QueryFilterRelationship` | 113 | 过滤：Enemy/Ally/Self |
| `QueryFilterNotEntity` | 111 | 过滤：排除指定实体 |
| `QueryFilterLayer` | 112 | 过滤：按层 |
| `QuerySortStable` | 102 | 稳定排序 |
| `QueryLimit` | 103 | 取前 N 个 |
| `AggCount` | 120 | I[dst] = TargetList.Count |
| `AggMinByDistance` | 121 | E[dst] = 最近目标 |
| `TargetListGet` | 123 | E[dst] = TargetList[I[a]] |

六边形空间查询（Hex 地图专用）：
- `QueryHexRange` (130)、`QueryHexRing` (131)、`QueryHexNeighbors` (132)

## 效果与事件

| Op | 代码 | 用途 |
|----|------|------|
| `ApplyEffectTemplate` | 200 | 对固定目标施加指定 Effect |
| `FanOutApplyEffect` | 201 | 对 TargetList 所有成员施加 Effect |
| `ApplyEffectDynamic` | 202 | source=Caster, target=E[A], templateId=I[B] |
| `FanOutApplyEffectDynamic` | 203 | source=Caster, TargetList, templateId=I[A] |
| `SendEvent` | 220 | 发布事件（TriggerManager） |

---

## 已知缺口

| 缺口 | 优先级 | 说明 |
|------|--------|------|
| `QueryRing`（环形，排除内圆） | P2 | 需求来自 D2 ring_aoe |
| Displacement 碰撞回调 | P2 | 需求来自 U2 wall_slam；DisplacementRuntimeSystem 需发出碰撞信号 |
| ~~`AbilityActivationRequireTags`~~ | ~~P1~~ | ✅ **已有** — 对应 `AbilityActivationBlockTags.RequiredAll`；详见 gap_analysis.md §1.1 |
| ContextGroup 评分机制 | P1 | N 系列 context_scored 全部依赖；评分器注册/调用接口待定 |
| 双击输入（DoubleTap） | P2 | `InputTriggerType.DoubleTap` 已废弃，需 SelectionSystem 实现 |

---

## EffectContext 约定

```
E[0] / EffectContext.Source        施法者
E[1] / EffectContext.Target        主目标
E[2] / EffectContext.TargetContext 附加上下文（AoE 中心、链式原始目标等）
```

## Effect Phase 执行顺序

```
OnPropose → OnCalculate → OnResolve → OnHit → OnApply → OnPeriod → OnExpire → OnRemove
```

每个 Phase 内部：Pre → Main → Post → Listeners（按 priority 升序）

参考：`src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs`

# Entity Lifecycle 原子 Op 与组合架构（设计）

> **状态**：实现中（`cursor/entity-morph-core-ea2f`），跟踪 GitHub issue [#494](https://github.com/MightyBubble/Ludots/issues/494)  
> **SSOT**：本文件为实体结构替换唯一架构文档；`entity-morph.md` 已废弃。

## 1 问题陈述

P0 Morph 将「结构替换事务」与「业务继承策略」揉进单一 `morph_profiles.json` DSL（placement、stableId、inherit.*、effects.mode…）。每增加一种 deploy 变体，就倾向于新增 profile 开关或 enum，违反：

- 禁止重复造轮子（与 `CreateUnit` / GAS handler 平行管线）
- 禁止跨越职责（placement 校验、effect 清理、selection 重接本可独立）
- 数据驱动 SSOT（应组合现有 effect/graph，而非二级配置语言）

## 2 目标分层

```text
Layer 0  Atomic ops        单一结构操作，无业务命名
Layer 1  Transaction       有序执行 + rollback（薄）
Layer 2  Composition       Effect 链 / Graph program（Mod 可改）
Layer 3  Preset           DeployConsumeSource 等封装名（给人抄）
```

**判断标准（开工前必过）**：新变体应新增 **graph 节点连线 / effect 步骤**，而不是给 profile / preset 加 enum 或开关。

## 3 Layer 0 — 原子 Op 清单（草案）

| Op | BuiltinHandler / Queue | 职责 |
|----|------------------------|------|
| `MaterializeTemplate` | 复用 spawn 物化 + performer/map | 按 templateId 创建实体 |
| `ConsumeEntity` | 新或 presentation lifecycle 包装 | 标记/销毁源实体 |
| `TransferStableId` | 新 | 源 → 目标 presentation id |
| `CopyAttributeSlice` | 新或扩展 `ApplyModifiers` | 显式 attribute 列表 + Base/Current |
| `CopyIdentityComponents` | 新 | PlayerOwner / Team（registry 扩展） |
| `ClearActiveEffects` | 新 | 清空目标 `ActiveEffectContainer`（现 Morph `StripAll`） |
| `RewireSelection` | 新或暴露 Morph 内部 | selection 源 → 目标 |
| `CopyMapOwnership` | `RuntimeEntityMapOwnershipSupport` | 已有 |

坐标：**不在任何 op profile 内**；SSOT 为 `EffectTargetPointResolver` + ability propose 阶段 placement validation（独立基建）。

## 4 Layer 1 — 事务壳

`RuntimeEntityLifecycleTransaction`（名称待定）：

- 输入：有序 op 列表 + `EffectContext` + rollback 策略
- 执行：`EffectProcessing` 阶段同步或专用 queue（与 spawn/morph queue 收敛）
- 失败：按已执行步骤逆序 rollback（与现 Morph rollback 同构）
- **不解析** inherit.mode / tags.mode 等 DSL

P0 Morph system 迁移目标：瘦身为 transaction executor，或并入 unified lifecycle queue。

## 5 Layer 2 — 组合

**Deploy consume source** 示例（effect 链，非 profile）：

```text
OnApply:
  1. MaterializeTemplate @ EffectTargetPoint
  2. CopyIdentityComponents (Source → Target)
  3. CopyAttributeSlice (Health, Current)
  4. ClearActiveEffects (Target)
  5. TransferStableId (Source → Target)
  6. RewireSelection (Source → Target)
  7. ConsumeEntity (Source)
```

Mod 可通过 graph 调整顺序、增删步骤；Core 不新增 `inherit.effects.mode`。

## 6 Layer 3 — Preset

| Preset | 用途 |
|--------|------|
| `DeployConsumeSource` | 预编译上述链（或 graph 名） |
| `MorphInPlace` | AtSource + 不 Consume 的变体 |

`presetType: Morph` **deprecated** → 具名 lifecycle preset 或 graph entry。

## 7 迁移计划（无向后兼容）

| 阶段 | 动作 |
|------|------|
| M1 | 实现 Layer 0 ops + transaction executor | 进行中（`DeployConsumeSource` preset 内联；独立 atomic handler 待拆） |
| M2 | `DeployConsumeSource` preset 替换 `morph.rts.deploy_consume_source` | 完成 |
| M3 | 删除 Morph profile DSL、Morph preset、平行管线 | 完成 |

## 8 非目标

- placement 地形/阻挡校验（ability propose 基建，另 issue）
- Presentation `EntityMorphed` 事件（可选 follow-up）

## 9 参考实现

- `src/Core/Gameplay/Lifecycle/` — Layer 1 transaction executor + `DeployConsumeSource` preset
- `presetType: DeployConsumeSource` + `lifecycleDeploy` block in `effects.json`

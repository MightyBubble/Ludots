---
name: ludots-gas-composition-gate
description: Mandatory self-review gate before Ludots GAS, entity lifecycle, or effect-preset work. Enforces atomic-op composition over declarative profile DSL. Use when starting any issue, feature, or refactor touching BuiltinHandlers, effect presets, graph ops, spawn/morph/lifecycle, or new gameplay config schemas.
---

# Ludots GAS Composition Gate

**写第一行代码前必须完成本 skill。** 与 `gitbook/contributing/ai-assisted-development.md` §4 并列，不替代 §4。

## Load References

1. `references/composition-judgment-standard.md` — 硬性判断标准
2. `references/self-review-checklist.md` — 自审清单（必须逐项填写）
3. `references/layer-model.md` — Layer 0–3 分层
4. `gitbook/architecture/entity-lifecycle-atomic-ops.md` — 当前设计 SSOT（entity lifecycle 相关任务）

## Workflow

### Step 0 — 适用范围判定

若任务涉及以下任一项，**必须**跑完整 gate；否则可跳过并一句说明：

- 新增/扩展 `BuiltinHandlerId`、`EffectPresetType`
- 新增 JSON profile / catalog schema（如 `*_profiles.json`）
- `RuntimeEntitySpawn*` / `RuntimeEntityMorph*` / 实体结构替换
- Graph op 或 effect 链编排
- `inherit.*`、`placement`、`destroySource` 类声明式开关

### Step 1 — 判断标准（硬性）

回答 `references/composition-judgment-standard.md` 中的核心问题：

> **新变体是新增 graph 节点 / effect 步骤，还是新增 profile enum / preset 开关？**

- 若答案是后者 → **停止编码**，改为设计 atomic op + composition，或开重构 issue。
- 若无法回答 → **停止**，先补发现阶段。

### Step 2 — 自审清单

复制 `references/self-review-checklist.md` 模板，填写后附在 issue 评论、PR 描述或 `artifacts/gas-composition-gate.md`。

**未附完整自审表，不得提交实现 PR。**

### Step 3 — 复用 / 新增清单

与 `ai-assisted-development.md` §4.2 合并输出：

| 类型 | 项 |
|------|-----|
| 复用 | 已有 handler、resolver、queue、registry |
| 新增 Layer 0 op | 仅当单一职责且无法由现有 op 组合 |
| 新增 Layer 1 | 仅 transaction / rollback |
| 新增 Layer 2 | effect 链或 graph（Mod 可改部分） |
| 禁止 | 新 profile DSL、平行加载器、inherit.mode 枚举 |

### Step 4 — 输出

产出 `artifacts/gas-composition-gate.md`，包含：

- 任务摘要
- 判断标准结论（通过 / 不通过 / 需重构）
- 填写的自审清单
- 复用/新增表
- 若不通过：建议 issue 标题与下一步

## Red Flags — 立即不通过

- 「给 morph profile 加一个 mode」
- 「新 preset 内嵌 5+ 继承开关」
- 「再建一条与 spawn 平行的物化管线」
- 「placement 校验写进 morph/profile」
- 「无法列出 atomic op 边界」

## Related

- Issue [#494](https://github.com/MightyBubble/Ludots/issues/494) — Entity Lifecycle 原子 op 重构（设计 SSOT）
- Issue #490 / PR #491 — Morph P0 spike（迁移源，非扩展目标）
- `gitbook/architecture/entity-morph.md` — 现行文档（将随重构 supersede）

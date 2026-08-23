# GAS Composition Gate - Self Review

- **Task / Issue**: Entity Attachment transaction and direct-operation atomicity repair (#1064)
- **Date**: 2026-08-23
- **Agent / Author**: Codex

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A** - 修复既有 attachment atomic op 与 Layer 1 transaction rollback。

结论: PASS

一句话理由: 本次只补齐既有 attach/detach 原子操作的校验、快照和回滚，不新增 profile、inherit mode、placement enum 或平行管线。

## 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Attach/Detach preflight and state snapshot | Layer 0 | AttachmentOps |
| GAS structural rollback of nav membership | Layer 1 | EffectPhaseSideEffectTransaction |
| Existing effect composition | Layer 2 | Existing StageAttach / StageDetach |

## 3. Reuse list

- Handlers: existing attachment handlers and AttachmentOps
- Queues / Systems: existing CommandBuffer, PoseAuthorityArbiter, MassNavigationMembership
- Resolvers / Registries: none added
- Existing presets / graphs: unchanged

## 4. New Layer 0 ops

N/A. The repair composes existing atomic operations and adds no new gameplay operation.

## 5. Transaction boundary

Attach/detach must restore relation buffers, pose components, navigation membership components, and pending authority transitions when any later mutation fails.

## 6. Config SSOT

Behavior remains in existing Core attachment operations and GAS transaction code. No JSON schema added.

## 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加静默 fallback

## 8. Next variant test

下一个 Mod 变体将修改 graph 连线或 effect 步骤，不改 Core enum。

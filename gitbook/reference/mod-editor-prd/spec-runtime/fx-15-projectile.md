# fx-19 runtime spec · 弹道

> 引擎实现任务书。第一性需求见 [fx-18 PRD](../prd/fx-15-projectile.md)；现状见 [reference](../reference/fx-15-projectile.md)。

## 1. 概述
弹道发射合同：原点与方向解析、ProjectileState 组装、经生成队列入世界。

## 2. 设计
- HandleCreateProjectile：发射原点取源实体 WorldPositionCm；目标点与方向按上下文与保留参数解析；Direction 模式无可解析方向直接抛错。
- 组装 ProjectileState（速度/射程/弧高/命中策略/碰撞参数/子效果 id/源与目标）经 RuntimeEntitySpawnRequest 入队；事务内 StageSpawnRequest，非事务队列满抛错。
- 保留参数 `_ep.projectileSpeed/Range/ArcHeight/impactEffectId` 的覆盖通道保持；Legacy 双枚举报错文案保持"was removed"。

## 3. 精确语义与不变量
- 弹道实体只经生成队列产生，效果处理器不直改世界。
- speed<=0 或源实体死亡时静默跳过（现状语义保持并写入诊断文档）。
- 命中历史容量限定 maxHitCount 上界；贯穿弹至多记录容量内个命中。

## 4. 迁移与治理
现状即基线。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[fx-18 PRD](../prd/fx-15-projectile.md) · [reference](../reference/fx-15-projectile.md)

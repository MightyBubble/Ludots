---
文档类型: 测试用例
创建日期: 2026-02-09
最后更新: 2026-02-09
维护人: Claude
文档版本: v1.0
适用范围: GAS - MOBA品类扩展验证 (MobaDemoMod 扩展)
状态: 已实现
---

# MOBA 品类扩展验证测试用例

## 1 测试总览

在 GPT 原有 Q/W/E/R 四技能基础上新增 3 个效果扩展，补充 PeriodicSearch 友方光环、GrantedTags 减速/沉默。

## 2 场景列表

### M6: 友方治疗光环 (PeriodicSearch Friendly)

**测试目标**: 琴女式光环 = PeriodicSearch(Circle, Friendly, r=700) + Heal(+3 HP/tick)

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 激活 FriendlyHeal 光环效果 | PeriodicSearch(After, 6000 ticks, period=60) 创建 |
| 2 | 友方英雄在范围内等待 60 ticks | 被施加 FriendlyHeal.Tick (Heal +3 HP) |
| 3 | 范围外友方不受影响 | 超出 700cm 半径的友方 HP 不变 |
| 4 | 敌方不受影响 | Hostile 关系的实体不被 Friendly 过滤命中 |

### M7: 减速效果 + GrantedTags (Debuff.Slow)

**测试目标**: Buff 效果同时修改属性和授予标签

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 对目标施加 Debuff.Slow | MoveSpeed -30, 授予 Status.Slowed |
| 2 | 验证属性 | MoveSpeed 降低 30 |
| 3 | 验证标签 | Status.Slowed 存在于目标 |
| 4 | 等待 120 ticks 效果过期 | MoveSpeed 恢复, Status.Slowed 移除 |

### M8: 沉默效果 + GrantedTags (Debuff.Silence)

**测试目标**: 纯标签授予效果（无属性修改，只授予 Status.Silenced）

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 对目标施加 Debuff.Silence | GrantedTags=[Status.Silenced], duration=90 |
| 2 | 验证标签 | Status.Silenced 存在 |
| 3 | 如果与 BlockedAny 配合 | 被 Status.Silenced 阻止的技能无法激活 |
| 4 | 90 ticks 后 | Status.Silenced 自动移除 |

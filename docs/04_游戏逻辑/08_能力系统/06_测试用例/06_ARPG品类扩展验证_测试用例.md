---
文档类型: 测试用例
创建日期: 2026-02-09
最后更新: 2026-02-09
维护人: Claude
文档版本: v1.0
适用范围: GAS - ARPG品类扩展验证 (ArpgDemoMod 扩展)
状态: 已实现
---

# ARPG 品类扩展验证测试用例

## 1 测试总览

在 GPT 原有 FireArrow/HealPotion/SummonWolf/Stun 基础上新增 3 个扩展，补充 Effect Stack(Poison 3 层/Bleed 5 层)、ConfigParams、GrantedTags(Bleeding/Armored)。

## 2 场景列表

### A5: 毒液叠加 (Poison DoT + Stack RefreshDuration)

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 对敌人施加 Poison | Stack=1, Duration=60, Health -2/period |
| 2 | 再次施加 Poison | Stack=2, Duration 刷新到 60 (RefreshDuration) |
| 3 | 第 3 次施加 | Stack=3, 达到 limit |
| 4 | 第 4 次施加 | RejectNew, Stack 保持 3 |
| 5 | 每个周期(10 ticks)伤害 | Health -= 2 * stackCount (如果按 stack 叠加伤害) |

### A6: 流血堆叠 (Bleed DoT + Stack AddDuration + GrantedTags)

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 施加 Bleed (3105) | Stack=1, Duration=90, Period=15, Health -4/period, GrantedTags=[Status.Bleeding] |
| 2 | 再次施加 | Stack=2, Duration 增加 (AddDuration), Status.Bleeding 维持 |
| 3 | 叠加到 5 层 | Stack=5, limit 达到 |
| 4 | 第 6 次 | RemoveOldest: 移除最旧 1 层, 加新 1 层, Stack=5 |
| 5 | 效果全部过期 | Status.Bleeding 标签移除 |

### A7: 铁皮 (IronSkin Buff + GrantedTags)

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 施放 IronSkin (3106) | Buff(After, 300 ticks): Armor+20, GrantedTags=[Status.Armored] |
| 2 | 验证属性和标签 | Armor 增加 20, Status.Armored 存在 |
| 3 | 300 ticks 后 | Armor 恢复, Status.Armored 移除 |

## 3 扩展 ConfigParams (FireArrow)

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| damage_base | float | 10.0 | 基础伤害值 |
| skill_level | int | 1 | 技能等级 |

CallerParams 可在技能执行时覆盖这些值，实现技能等级影响伤害的效果。

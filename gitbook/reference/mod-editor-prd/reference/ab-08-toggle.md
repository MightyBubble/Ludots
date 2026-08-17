# ab-08 reference · Toggle 技能

> 现状参考。第一性需求见 [ab-08 PRD](../prd/ab-08-toggle.md)；配置说明见 [ab-08 配置说明](../config/ab-08-toggle.md)。

## 1. 现状快照

- 开启 ActivateToggle：激活时间轴 Finished 终态触发，时点在 FinalizeCurrent 与 CastFinished 之前；幂等（tag 已在即返回）；容量预检（效果请求队列缺失/余量不足抛专门错误，含所需与可用数）→ AddTag → 对 ≤4 个 activeEffects 逐个发布无限时长 EffectRequest（RootId=0、Source=Target=自身）。
- 关闭 DeactivateToggle：先确保表现事件容量（有关断时间轴预留 2 CastStarted，否则 1 CastFinished + 终态）→ RemoveTag → 有 DeactivateExecSpec 创建 IsToggleDeactivating 新实例（推进换用 DeactivateExecSpec），无时间轴瞬时完成。
- 关闭分支在激活门之前：已开技能再激活即关，不判 blockTags（含再激活冷却）。
- activeEffects 清除：依赖效果被打上 toggle tag 身份后由 EffectLifetimeSystem 过期清理，关断逻辑不逐个撤销。
- 加载：toggleSpec.toggleTag 必填（旧名 tag 专门报错）、activeEffects ≤4 且须已注册、deactivateExec 按时间轴同规编译。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 开启（容量先行/幂等/发效果） | src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs:1501-1556 |
| 触发时点（终态前容量预检） | AbilityExecSystem.cs:564-577 |
| 关闭（容量/摘 tag/关断时间轴） | AbilityExecSystem.cs:1562-1640 |
| 关断实例推进换轴 | AbilityExecSystem.cs:483-487 |
| 关闭先于门 | AbilityExecSystem.cs:215-231 |
| toggleSpec 编译 | src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs:203-207、493-545 |
| 真实实例 | mods/showcases/champion_skill_sandbox/.../abilities.json（Garen.Courage、Jayce.Transform.*） |

**相关文档**：[ab-08 PRD](../prd/ab-08-toggle.md) · [ab-07 reference](ab-07-form-sets.md)

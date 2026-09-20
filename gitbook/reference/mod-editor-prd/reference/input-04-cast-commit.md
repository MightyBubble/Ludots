# input-04 reference · 施法提交档案

> 现状参考。第一性需求见 [input-04 PRD](../prd/input-04-cast-commit.md)；配置说明见 [input-04 配置说明](../config/input-04-cast-commit.md)。

## 1. 现状快照

- 档案形状：`profiles[].id` / `onActivate[]`（op 序列）/ `frameActions{actionId → op 序列}`；op = `{op: pushFrame|popFrame|submitOrder, payload{槽位键→值源(cursorWorld|framePointer)}, contextProfileId}`；loader 拒收三种字段以外的键（无 FSM schema）。
- 锁形状：`locks[]{scope: global|template|formSet|slot, key, castCommitId}`。
- 消费：技能槽激活按 op 序列执行；帧内动作由 `CastCommitProfileRegistry.TryExecuteFrameAction` 拦截（仅压帧在顶期间）；玩家偏好 `ResolveCastCommit` 按锁 > perSlot > perFormSet > perTemplate > global 解析，被锁作用域 `TrySetPreference` 返回 false。
- 根资产两文件均为空（`profiles: []` / `locks: []`）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 档案/op 形状（三字段封死） | src/Core/Input/Interaction/CastCommitProfile.cs:140-170 |
| 锁形状 | src/Core/Input/Interaction/ClientCastPreference.cs:46-63 |
| 帧内动作拦截 | src/Core/Input/Interaction/CastCommitProfileRegistry.cs:124 |
| 偏好五级解析 | ClientCastPreference.cs:97-119 |
| 被锁作用域拒绝 | ClientCastPreference.cs:128-161 |
| 根资产 | assets/Input/cast_commit_profiles.json、assets/Input/cast_commit_locks.json |

**相关文档**：[input-04 PRD](../prd/input-04-cast-commit.md) · [input-03 reference](input-03-interaction-context.md)

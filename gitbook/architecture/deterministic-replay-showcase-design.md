# deterministic_replay showcase 设计

> 状态：已实现，待运行验收（真机四节点证据待网络窗口恢复后按 ludots-showcase-design 闸门 6/7 采集）。

## 一句话与目标用户

同一段操作，重放出来的世界和当时一模一样——确定性是本体，用户看到的是「录制终点 digest == 回放终点 digest」的绿灯。

## 主循环

- **谁改变世界**：玩家 [Nudge hero] 注入真实权威 Command 动作（RTS 移动单），英雄移动。
- **录制**：[Start record] 起点打 checkpoint，每 fixed step 捕获一帧 AuthoritativeFrame（ReplayRecorder）。
- **回放**：[Play replay] 从录制 checkpoint restore 后逐帧 QueueReplayActions，世界沿同一条演化轨迹重走。
- **惊喜时刻**：回放中再点 [Nudge hero]——实时输入被隔离拒绝，重放轨迹分毫不差。

## 消融对照

确定性重放 vs 直接读档跳终点：读档只证明「终点状态在」，重放证明「每一 tick 都一致」（digest 对比 + 逐帧步进可见演化）。

## 解释层

- HUD：录制帧数 / 当前 tick / 回放索引 / 录制终点 digest / 当前 digest / MATCH 绿灯；
- 输入隔离状态行：回放中实时输入的拒绝记录；
- 归档行：帧数与磁盘路径。

## 旋钮

| 旋钮 | 演示什么 |
|------|----------|
| Nudge hero（录制中） | 权威输入被逐帧捕获 |
| Play replay / Pause / Step one frame | 逐 tick 演化可见、暂停可查 |
| Nudge hero（回放中） | 输入隔离——回放不被实时输入污染 |
| Save archive / Load latest archive | 录制跨会话冷加载重放 |

## 专项闸门（Replay）

- 权威输入管线（ReplayRecorder/ReplayArchive/ReplayArchiveCodec/ReplayPlayer/FrozenInputActionReader 隔离），无测试回调旁路；
- 终点 digest 一致性由面板绿灯 + 验收脚本双重证明（中间 checkpoint 对比待真机验收补录）；
- 证据四节点（录制终点/回放中间/回放终点/比较结果 + 资产路径版本）待真机补录后本 showcase 方宣告可玩交付完成。

## 交付边界

入口：preset `deterministic_replay_showcase_raylib`（selectors: $deterministic_replay_showcase + $agent_bridge）。

## 来源

#1337（massnav 结构变更缺陷，PR #1339）排查中发现的**潜在死锁本身**——#1339 只消除了「裸组件 Add 重排代理索引」这个触发面，死锁条件仍然在。

## 问题

移动代理在窄路 + 相向环境单位让行时 settle 在距当前 waypoint 51cm 处；而 `MassNavigationRouteExecutionSink.AdvanceWaypointCursor` 的推进阈值 **小于 settle 半径**——单位永远进不了推进圈，游标不动，订单最终被 stall 超时排空（表象：`completed=true` 但没走到目标，`Final=(1134,-4)` vs 目标 `(18000,0)`）。

#1339 实测链：让行动力学改变 → settle 在 51cm → 游标死锁。任何让单位提前 settle 的原因（不只是索引重排）都可能触发同一死锁。

## What to build

- 裁决推进阈值与 settle 半径的关系（阈值 ≥ settle 半径？或 settle 态下 waypoint 推进走特殊路径？）
- 回归测试：制造「单位被迫 settle 在 waypoint 推进圈外」的场景，断言订单不被超时排空（能恢复推进或至少显式失败而非假完成）
- 顺带审视「completed=true 但未达目标」的订单完成语义是否该带位置断言

## Acceptance criteria

- [ ] 阈值关系裁决并回填
- [ ] settle-在圈外场景回归测试绿
- [ ] 订单完成语义带位置断言或显式放弃原因

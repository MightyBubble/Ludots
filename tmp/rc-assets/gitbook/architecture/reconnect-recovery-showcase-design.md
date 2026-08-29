# reconnect_recovery showcase 设计

> 状态：已实现，待运行验收；且**联机专项未验收**——当前为单机模拟（如实标注），真实网络故障注入通道待后续落地。

## 一句话与目标用户

断线不是重置：权威侧继续走、客户端冻结、重连从权威 checkpoint 续上——恢复来源的权威性是本体。

## 主循环

- **谁改变世界**：玩家 [Nudge hero]（活线）/ 断线后权威模拟自身继续 tick。
- **双侧时间线**：authority tick 与 client tick 并行显示；[Disconnect] 后两线分叉、差值可见；[Reconnect] 后从权威 checkpoint 恢复，两线重新并走。
- **惊喜时刻**：重连瞬间面板明示「权威在断线期间多走了 N tick，客户端从权威点续上，不是本地幻觉」。

## 消融对照

权威恢复 vs 本地幻觉：恢复来源行明示 `authoritative checkpoint` + digest；若只是本地状态还在（幻觉），authority/client 时间线对不上。

## 解释层

- 双时间线（authority/client tick）+ 断线差值高亮；
- checkpoint digest / 当前 digest / 恢复来源常显；
- 故障注入拒绝消息原文（expected/actual 序列错误）。

## 旋钮

| 旋钮 | 演示什么 |
|------|----------|
| Disconnect / Reconnect 时机 | 任意时刻断都续得上 |
| Inject missing frame | 缺帧被真 Validate 拒绝（ReplayArchive 帧校验） |
| Inject duplicate frame | 重复序列被拒 |
| Inject stale frame | 过期序列被拒 |

## 专项闸门（联机）— 现状如实

- 单机模拟：断线=客户端视图冻结 + 权威继续；重连=WorldRestoreService 从 checkpoint 恢复 ✓；
- 真实网络故障注入、跨进程权威/客户端分离：**未验收**，面板与本文档均明示「联机专项未验收」；
- 三类帧故障注入走真实 ReplayArchive.Validate()（构造 20 帧流 → 注入 → 校验异常原文入面板），非伪造文案。

## 交付边界

入口：preset `reconnect_recovery_showcase_raylib`（selectors: $reconnect_recovery_showcase + $agent_bridge）。

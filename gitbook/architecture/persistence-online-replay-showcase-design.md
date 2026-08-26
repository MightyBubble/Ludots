# 持久化 / Replay / 联机追回 Showcase（历史复合条目）

> **已退役（Epic #1204 / #1208）。**  
> 原 `persistence_online_replay` 把存档、确定性回放、断线恢复塞进同一场景，违反「一 showcase 一能力」。  
> 能力已拆到三份新设计，请改走：

| 能力 | 设计 | 验收 | Showcase |
|------|------|------|----------|
| 存档读档 | [save-load-showcase-design.md](save-load-showcase-design.md) | [save-load.feature](../acceptance/save-load.feature) | `save_load` |
| 确定性回放 | [deterministic-replay-showcase-design.md](deterministic-replay-showcase-design.md) | [deterministic-replay.feature](../acceptance/deterministic-replay.feature) | `deterministic_replay` |
| 断线恢复（单机模拟） | [reconnect-recovery-showcase-design.md](reconnect-recovery-showcase-design.md) | [reconnect-recovery.feature](../acceptance/reconnect-recovery.feature) | `reconnect_recovery` |

历史手工验收资产：`artifacts/archive/persistence-online-replay/`。  
旧 Gherkin：`gitbook/acceptance/archive/persistence-online-replay.feature`。

本文仅作索引入口，不再描述可启动场景。

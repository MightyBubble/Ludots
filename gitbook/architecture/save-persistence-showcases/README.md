# 存档 · 回放 · 联机

本页是门户一级入口。把「进度能留下来」「同一段操作能复现」「断线后能接上」三件事放在同一棵导航下，方便新作者一次找齐。

## 你能在这里看到什么

| 能力 | 人话 | 可玩 Showcase | 设计说明 |
|------|------|---------------|----------|
| 存档读档 | 挪开 → 存一档 → 再挪远 → 读档弹回 | `save_load` | [存档读档 Showcase](../save-load-showcase-design.md) |
| 确定性回放 | 录一段，再播一遍，世界一样 | `deterministic_replay` | [确定性回放 Showcase](../deterministic-replay-showcase-design.md) |
| 断线恢复 | 断线不是重开；单机模拟追回 | `reconnect_recovery` | [断线恢复 Showcase](../reconnect-recovery-showcase-design.md) |

通用存档合同（槽位、检查点、落盘口径）见 [通用存档系统](../save-system.md)。  
历史复合条目（已退役）见 [持久化、Replay 与联机追回 Showcase（历史）](../persistence-online-replay-showcase-design.md)。

## 怎么启动

```text
scripts/run-mod-launcher.cmd cli launch save_load_showcase --adapter raylib
scripts/run-mod-launcher.cmd cli launch deterministic_replay_showcase --adapter raylib
scripts/run-mod-launcher.cmd cli launch reconnect_recovery_showcase --adapter raylib
```

画廊与验收证据分别走 Showcase 画廊 / 测试与验收页；注册表条目 `save_load`、`deterministic_replay`、`reconnect_recovery`。

## 边界（先读再玩）

- 存档 UI 必须走正式 `SavePanelMod`，不另造面板。
- 回放资产走 `ISaveStorage`（如 `replays/showcase.ldreplay`），禁止私有路径飞线。
- 断线恢复页眉固定写：**「单机模拟断线（联机专项未验收）」**——本页「联机」指断线追回能力面，不是已验收的多人联机。
- 旧复合 Lab `persistence_online_replay` 已退役；请勿再引用其启动命令。

## 门户资产现状

- 三份 Showcase 设计说明与验收 feature 已挂 SUMMARY / 注册表。
- 验收目录现有静态截图；**正式录屏（`play.mp4`）尚未入库**——补齐后挂到 `showcase.registry.json` 的 `video` 字段与对应 `artifacts/evidence/`。

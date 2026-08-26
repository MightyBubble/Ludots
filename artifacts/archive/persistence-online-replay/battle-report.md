# Persistence / Replay / Online Recovery 历史复合条目战报（已退役）

> Epic #1208：本资产仅作历史索引。有效能力由 `save_load` / `deterministic_replay` / `reconnect_recovery` 承接。

## 场景

同一检查点分别用于存档恢复、Replay 播放和断线恢复演示；权威帧要求序号连续、tick 不回退、动作 id 排序唯一。

## 实机结果（历史）

- 启动入口曾为 `preset:persistence_online_replay_showcase_raylib`（已摘除）。
- Checkpoint → Save / 冷启动 Restore / Record→Replay / Disconnect→Reconnect 等观测见当时日志。
- 回放终点曾出现 digest mismatch；缺帧/乱序被正式校验拒绝。

## 证据索引（本目录）

- `artifacts/archive/persistence-online-replay/persistence-first-viewport.png`
- `artifacts/archive/persistence-online-replay/persistence-reconnect-success.png`
- `artifacts/archive/persistence-online-replay/persistence-rejection.png`
- `artifacts/archive/persistence-online-replay/trace.jsonl`
- `artifacts/archive/persistence-online-replay/path.mmd`

新验收资产：

- `artifacts/acceptance/save-load/`
- `artifacts/acceptance/deterministic-replay/`
- `artifacts/acceptance/reconnect-recovery/`

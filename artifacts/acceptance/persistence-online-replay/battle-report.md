# Persistence / Replay / Online Recovery 实机战报

## 场景

同一检查点分别用于存档恢复、Replay 播放和断线恢复演示；权威帧要求序号连续、tick 不回退、动作 id 排序唯一。

## 实机结果

- 启动入口：scripts/run-mod-launcher.cmd cli launch "preset:persistence_online_replay_showcase_raylib"。
- Agent Bridge 判活：pumpCount 从 2301 增长到 2351；session.info 确认地图 persistence_online_replay，Mod 列表含 PersistenceOnlineReplayShowcaseMod 与 AgentBridgeMod。
- 首屏：1280x720 下主面板 bounds 为 x=760,y=20,w=500,h=680；内容区为可滚动容器，按钮和 HUD 均在同一真实 UI 树中。
- Checkpoint -> Save：面板显示真实 tick 738、摘要 016F00F34746 和落盘槽位；实际文件为 %LOCALAPPDATA%/Ludots/persistence-online-replay/saves/manual/showcase.ldsave。
- 冷启动 -> Restore：关闭目标进程后从同一 preset 再启动，点击 Restore，面板显示“从磁盘恢复”与原检查点 tick 738，随后 tick 继续增长。
- Record -> Stop：生成 40 个 authoritative frames、schema 1 的 replay 资产；实际文件为 %LOCALAPPDATA%/Ludots/persistence-online-replay/replays/showcase.ldreplay。
- Replay -> Pause：面板显示 recovery source: replay、replay result: playing，暂停状态明确写出“live input remains rejected”。
- Delete frame -> Replay：正式校验拒绝缺帧，界面显示 expected 20, actual 21，并停止当前回放，不静默继续。
- Disconnect：tick 停在 756（两次观察均为 756），pacemaker 切换为 TurnBasedPacemaker；Reconnect 从 checkpoint tick 738 恢复，随后 tick 从 765 增长到 779，pacemaker 恢复为 RealtimePacemaker。没有内存检查点时 Reconnect 明确显示 `Rejected: Reconnect has no authoritative checkpoint.`，不会静默重置。
- ReplayArchiveTests：3 passed；持久化回归覆盖基础存档、容器校验、损坏 world blob 拒绝。

## 证据索引

- `src/Core/Persistence/AuthoritativeFrame.cs`
- `src/Core/Persistence/ReplayArchiveCodec.cs`
- `src/Tests/PersistenceTests/ReplayArchiveTests.cs`
- `artifacts/agent-bridge/shots/persistence-dedicated-map.png`
- `artifacts/agent-bridge/shots/persistence-layout-fixed.png`
- `artifacts/acceptance/persistence-online-replay/persistence-first-viewport.png`
- `artifacts/acceptance/persistence-online-replay/persistence-reconnect-success.png`
- `artifacts/acceptance/persistence-online-replay/trace.jsonl`

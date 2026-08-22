# Persistence / Replay / Online Recovery 战报

## 场景

同一检查点分别用于存档恢复、Replay 播放和联机追回；权威帧要求序号连续、tick 不回退、动作 id 排序唯一。

## 结果

- `ReplayArchiveTests`：3 passed。
- 持久化回归：基础存档、容器校验、损坏 world blob 拒绝均覆盖。
- 已知失败：恢复后连续运行摘要仍受现有引擎状态重建差异影响，尚未宣称联机端到端通过。

## 证据

- `src/Core/Persistence/AuthoritativeFrame.cs`
- `src/Core/Persistence/ReplayArchiveCodec.cs`
- `src/Tests/PersistenceTests/ReplayArchiveTests.cs`

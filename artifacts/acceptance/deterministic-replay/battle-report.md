# deterministic_replay showcase 真机验收报告（#1206）

- 进程：Ludots.App.Web.exe（无头 Web adapter，preset deterministic_replay_showcase_raylib）
- mods：LudotsCoreMod, DeterministicReplayShowcaseMod, AgentBridgeMod；map=deterministic_replay
- 桥判活：health ok=true，pump 持续增长；全部操作经 bridge ui.click 驱动

## 四节点证据（Replay 专项闸门）

| 节点 | 结果 |
|------|------|
| N1 录制 | 297 帧密度完美（tick 严格 +1），含 5 次随机 nudge 权威动作 |
| N2 回放 | 297/297 帧全部消费 |
| N3 终点比对 | **MATCH — digest C47EB695CD1E == recorded end** |
| N4 复验 | 第二轮（先挪后录+首尾 nudge）228 帧 **再 MATCH**；第三轮回放中注入实时输入被隔离，重放仍 MATCH |

多轮 MATCH：同进程三种模式（标准录制/先挪后录/回放中隔离注入）全部一致。

## 展示层三态诚实化验证

- 未播时灰色 Pending「press [Play replay] to run the proof」
- 一致时绿色 MATCH 明示 reproduced the recorded end state
- 不一致时琥珀 MISMATCH 明示挂 #1311（P0 修复前的真机行为）

## 已知边界（如实）

- 冷启动归档回放的终点 digest 对比未闭合：归档未持久化录制终点 digest，冷加载后面板无 MATCH 判决（回放本身成功：228/228 帧消费）。修复方向记录在 #1360。
- 修复批次：#1358（P0 测量假象）、#1372（P1 三源）、#1381（P1b formatter 覆盖 + 相位对齐 + row-diff）、digest 排除扩展（本批）。

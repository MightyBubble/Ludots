# 战报 · RTS 多人前线三进程验收（final8 全绿）

- 运行：`20260824T_kimi_final8` · status = **passed**
- 进程：DedicatedServer + client-a（side 0，胜方）+ client-b（side 1，败方）
- 结局：**SideOneVictory**（side 0 胜）@ committedTick 2138
- 阶段：Connecting → Ready → Gathering → Training → Advancing → Engaging → WaitingForOutcome（全部 passed）

## Timeline

| Tick | 事件 |
|---|---|
| 0→2 | 双客户端 Connecting/Ready 通过：全量快照 + 本地玩家绑定 + 开图水晶场就绪 |
| 2→869 | Gathering：四轮 Gather 命令全部 TerminalCompleted，采矿车 8:1 出征 (13800,15400) → (12200,15000)，水晶 40 → 120 |
| 869→1355 | Training：双方 TrainInfantry+QueueTrainInfantry 准入通过，两名训练步兵出生（服务器权威 tick 1343/1584 对齐），步兵 2 → 4，水晶耗尽至 0 |
| 1352→1415 | Advancing：MoveToMeeting 后双方编队抵达汇合屏障（A 汇合点 (14700,15000) / B (15300,15000)），视野互见敌方步兵 |
| 1412→1643 | Engaging：B 军 AttackEnemyInfantry 反击 A 步兵 4:1（100 → 80 血）；A 军 MoveToSiege(1541) 后 AttackEnemyCore(1637) 围攻败方核心 11:1 (17000,15000)（1000 → 40 血） |
| 2138 | Outcome：败方核心摧毁（completedLosingCoreCount=0），胜方 3 步兵驻守残骸 (17000,15000)，比赛 Completed |

## Outcome

- 核心血量：side0 = 1000 / side1 = 40（服务器权威确认 [1000, 0]）
- 存活步兵：side0 = 3 / side1 = 2
- 相机终局锚定败方核心残骸 (17000,15000)

## Summary Stats

| 指标 | client-a (side0 胜) | client-b (side1 败) |
|---|---|---|
| 水晶 初始/采集/训练后 | 40 / 120 / 0 | 40 / 120 / 0 |
| 步兵 初始/训练后 | 2 / 4 | 2 / 4 |
| 命令准入 | {"Gather": 4, "TrainInfantry": 1, "QueueTrainInfantry": 1, "MoveToMeeting": 1, "MoveToSiege": 1, "AttackEnemyCore": 1} | {"Gather": 4, "TrainInfantry": 1, "QueueTrainInfantry": 1, "MoveToMeeting": 1, "AttackEnemyInfantry": 1} |
| 敌方核心入视野 | True | False |

## 证据链

- 世界证据：四里程碑 × 双客户端（ready / advancing / engaging / completed）
- 截图：`screens/client-{a,b}/frontline_00[1-4]_*.png` + typed-visual-counts 像素证据
- 帧缓冲：`framebuffer-pixel-evidence/` 8 组 request/result
- 命令准入流水与阶段转换：`trace.jsonl`；测试路径图：`path.mmd`

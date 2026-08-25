# 持久化、Replay 与断线恢复 showcase 设计

状态：可玩交付完成（联机专项仍标记为单机等价故障注入；本次实机回放终点比较显示 mismatch，界面明确暴露差异，未伪报确定性通过）。
## 一句话与目标用户
让新玩家亲眼看到：断线、回放或读档后，战局从同一个检查点继续，并得到同一个结果。

目标用户是第一次接触 Ludots 的游戏开发者和联机玩法设计者；他们不需要先读序列化代码，就能判断系统是否值得采用。

## 主循环
主演示是一场持续推进的 Persistence RTS 训练战局，入口地图为 `persistence_online_replay`。玩家在右侧面板建立检查点、落盘、录制权威输入、播放、暂停、逐帧和重置回放，并观察 tick、检查点摘要、回放帧数和恢复来源实时变化。

惊喜时刻发生在玩家主动断线：权威更新立刻冻结；已有检查点时，重连会从该检查点恢复并继续推进。没有检查点时，重连会明确拒绝，不会自动重置或假装成功。本场景是单机等价故障注入，用来展示恢复合同，不冒充真实网络端到端。

## 消融对照
同一战局提供两个按钮：

- 完整协议：检查点 + 严格递增权威帧，丢帧或乱序立即拒绝并显示原因。
- 消融模式：删除一帧或交换两帧，恢复流程拒绝提交，当前战局仍保持在断线前状态。

对照的重点不是报错文字，而是玩家能看到“完整协议能继续，破坏输入不能悄悄继续”。

## 解释层
HUD 只显示真实领域状态：当前 tick、最近检查点 tick、已记录权威帧数、已应用回放帧数、恢复来源、当前 checkpoint digest、回放终点 world digest 比较结果。回放期间会明确显示实时输入被隔离；完整回放终点显示 digest matches，删除或交换帧显示具体拒绝原因。颜色用于区分运行中、恢复中、已一致和已拒绝；右下角固定图例解释四种状态。

## 旋钮清单
本 showcase 使用离散运行时控件，而不是虚构尚未接入的网络参数。

| 旋钮 | 范围 | 演示什么 |
|---|---:|---|
| 回放运行状态 | 播放 / 暂停 | 暂停后实时输入仍被隔离 |
| 回放推进方式 | 连续 / 单步 | 逐帧检查权威输入的应用位置 |
| 回放位置 | 当前 / 重置到起点 | 从同一检查点重新开始比较 |
| 连接状态 | 在线 / 断线 / 重连 | 断线冻结与检查点恢复的边界 |
| 输入完整性 | 完整 / 删除一帧 / 交换两帧 | 缺帧或乱序时明确拒绝，而不是静默放过 |

## 场景结构
主演示：RTS 训练战局，左侧是战场，顶部是 tick 与摘要，右侧是存档、Replay、断线恢复控制。

子场景：

1. 存档后继续：保存检查点，继续下单，恢复后比较摘要。
2. Replay 追帧：从检查点开始逐帧播放，支持暂停和单步。
3. 断线恢复：模拟断线和从最近检查点重连。
4. 完整性消融：删除一帧或交换两帧，观察明确拒绝。

首屏引导：“先点 Checkpoint，再尝试 Save、Record 或 Disconnect；这是单进程断线模拟，不是真实网络；注意 tick、恢复来源和 digest 比较如何变化。”

## 门户资产
门户封面使用真实目标进程的首屏截图，展示操作按钮、HUD 与故障入口。验收资产来自同一个 preset 和真实运行过程：

- 设计文档：`gitbook/architecture/persistence-online-replay-showcase-design.md`
- UAT：`gitbook/acceptance/persistence-online-replay.feature`
- 战报：`artifacts/acceptance/persistence-online-replay/battle-report.md`
- 轨迹：`artifacts/acceptance/persistence-online-replay/trace.jsonl`
- 路径图：`artifacts/acceptance/persistence-online-replay/path.mmd`
- 首屏截图：`artifacts/acceptance/persistence-online-replay/persistence-first-viewport.png`
- 断线恢复截图：`artifacts/acceptance/persistence-online-replay/persistence-reconnect-success.png`
- 消融拒绝截图：`artifacts/acceptance/persistence-online-replay/persistence-rejection.png`

预览页和 HUD 读取同一份检查点/帧 DTO；不复制第二份演示数据。

## 反向 API 审计
| 需要的接口 | 归属 | 状态 |
|---|---|---|
| 固定步完成后签发检查点 | Core `CheckpointCoordinator` | 本次交付 |
| 权威帧连续性、tick 单调性和动作排序校验 | Core `AuthoritativeFrameStream` | 本次交付 |
| Replay 容器完整性与上下文校验 | Core `ReplayArchiveCodec` | 本次交付 |
| 重连传输批次、丢帧、重复帧拒绝 | Online adapter | 后续接入；领域帧合同已就绪 |
| 将回放帧注入正式 `AuthoritativeInput` 快照 | Core `FrozenInputActionReader` + `AuthoritativeInputSnapshotSystem` | 本次交付；已走固定步输入管线 |
| 将追回帧接入真实 Online adapter | Online adapter | 后续接入；本 showcase 使用明确的断线/重连等价故障注入，不能宣称网络端到端 |
| 引擎域恢复失败后的原子回滚 | Core `GameEngine.RestoreWorldSnapshot` | 后续债务；当前坏 world blob 在导入前拒绝 |

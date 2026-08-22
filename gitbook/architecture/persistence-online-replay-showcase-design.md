# 持久化、Replay 与联机追回 showcase 设计
## 一句话与目标用户
让新玩家亲眼看到：断线、回放或读档后，战局从同一个检查点继续，并得到同一个结果。

目标用户是第一次接触 Ludots 的游戏开发者和联机玩法设计者；他们不需要先读序列化代码，就能判断系统是否值得采用。

## 主循环
主演示是一场 60 秒 RTS 训练战局。玩家给两支部队下移动和攻击订单，固定步推进在屏幕上显示 tick、单位位置和战局摘要。每隔可调间隔生成一次真实固定步完成后的检查点，同时把冻结后的权威帧交给 Replay 和联机追回路径。

惊喜时刻发生在玩家主动断线：画面冻结在检查点，连接恢复后先加载检查点，再逐帧追回断线期间的权威订单；单位位置和世界摘要追上服务器后，战局继续推进。整个过程显示“来源：联机追回”“追回帧数”和最终摘要。

## 消融对照
同一战局提供两个按钮：

- 完整协议：检查点 + 严格递增权威帧，丢帧或乱序立即拒绝并显示原因。
- 消融模式：删除一帧或交换两帧，恢复流程拒绝提交，当前战局仍保持在断线前状态。

对照的重点不是报错文字，而是玩家能看到“完整协议能继续，破坏输入不能悄悄继续”。

## 解释层
HUD 只显示真实领域状态：当前 tick、最近检查点 tick、已记录权威帧数、恢复来源、当前 world digest、连续运行与恢复结果是否一致。颜色用于区分运行中、追回中、已一致和已拒绝；右下角固定图例解释四种状态。

## 旋钮清单
| 旋钮 | 范围 | 演示什么 |
|---|---:|---|
| 检查点间隔 | 1–20 个固定步 | 检查点越密，追回距离越短 |
| 模拟发送延迟 | 0–500 ms | 网络延迟只改变追回等待，不改变权威结果 |
| 断线持续时间 | 1–12 秒 | 追回更多连续帧时，进度和摘要如何变化 |
| Replay 播放速度 | 0.25x–4x | 慢放查看每个权威订单，快放观察整体一致 |
| 输入完整性 | 完整 / 删除一帧 / 乱序一帧 | 明确看到拒绝原因，而不是静默放过 |

## 场景结构
主演示：RTS 训练战局，左侧是战场，顶部是 tick 与摘要，右侧是存档、Replay、断线追回控制。

子场景：

1. 存档后继续：保存检查点，继续下单，恢复后比较摘要。
2. Replay 追帧：从检查点开始逐帧播放，支持暂停和单步。
3. 联机追回：模拟断线、延迟、重连和帧校验。
4. 完整性消融：删除、重复、乱序帧，观察明确拒绝。

首屏引导：“先给部队下两个订单，再点断线追回；注意检查点 tick、追回帧数和摘要是否回到一致。”

## 门户资产
门户封面取“追回完成且两个摘要变为一致”的截图，而不是静态全景。验收资产使用真实运行时配置和测试输出生成：

- 设计文档：`gitbook/architecture/persistence-online-replay-showcase-design.md`
- UAT：`gitbook/acceptance/persistence-online-replay.feature`
- 战报：`artifacts/acceptance/persistence-online-replay/battle-report.md`
- 轨迹：`artifacts/acceptance/persistence-online-replay/trace.jsonl`
- 路径图：`artifacts/acceptance/persistence-online-replay/path.mmd`

预览页和 HUD 读取同一份检查点/帧 DTO；不复制第二份演示数据。

## 反向 API 审计
| 需要的接口 | 归属 | 状态 |
|---|---|---|
| 固定步完成后签发检查点 | Core `CheckpointCoordinator` | 本次交付 |
| 权威帧连续性、tick 单调性和动作排序校验 | Core `AuthoritativeFrameStream` | 本次交付 |
| Replay 容器完整性与上下文校验 | Core `ReplayArchiveCodec` | 本次交付 |
| 重连传输批次、丢帧、重复帧拒绝 | Online adapter | 后续接入；领域帧合同已就绪 |
| 将追回帧注入正式 `AuthoritativeInput` / `OrderQueue` | Core input/order adapter | 后续接入；禁止 showcase 自造管线 |
| 引擎域恢复失败后的原子回滚 | Core `GameEngine.RestoreWorldSnapshot` | 后续债务；当前坏 world blob 在导入前拒绝 |

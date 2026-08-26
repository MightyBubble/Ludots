# 断线恢复战报（单机模拟）

> 页眉合同：单机模拟断线（联机专项未验收）

## 节点

- 检查点 tick=1
- 断线后权威推进到 tick=3
- 权威恢复后客户端追到 tick=3，digest 保持权威事实，不倒回检查点
- 恢复来源：authority live tick=3 digest=D396732FC5E4 sinceCheckpoint=1
- 时间线：重连补齐：客户端从 1 追到 3（权威事实）

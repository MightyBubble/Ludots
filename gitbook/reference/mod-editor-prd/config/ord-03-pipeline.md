# ord-03 配置说明 · 订单流水

> 配置写法与行为。第一性需求见 [ord-03 PRD](../prd/ord-03-pipeline.md)；编辑器需求见 [UXD](../uxd/ord-03-pipeline.md)；现状见 [reference](../reference/ord-03-pipeline.md)。

## 1. 示例配置

订单流水无专属表，容量全部声明在 `game.json` 的 `gasRuntimeCapacity` 段（真实值见 [事实与取值表](../facts.md)）：

```json
{ "gasRuntimeCapacity": {
    "orderQueueCapacity": 4096,
    "responseChainOrderQueueCapacity": 4096,
    "orderAdmissionResultCapacity": 8192,
    "orderAdmissionRejectionCapacity": 4096,
    "orderTerminalResultCapacity": 4096 } }
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `orderQueueCapacity` | 主订单队列（环形）深度；超深度提交被拒入拒绝区 |
| `responseChainOrderQueueCapacity` | 响应链专用队列深度（链单不与玩家单混队） |
| `orderAdmissionResultCapacity` | 准入结果区深度；须 ≥ 主队列深度 ×2（代码交叉校验） |
| `orderAdmissionRejectionCapacity` | 独立拒绝区深度；须 ≥ 主队列深度；两区全满进终端故障 |
| `orderTerminalResultCapacity` | 终态快照账本深度；溢出按快照代际覆盖 |

## 3. 文件结构

`assets/game.json` `gasRuntimeCapacity` 段（DeepObject 合并，见 cfg-06）；仅容量可配，流水行为本身无开关。

## 4. 运行时加载效果

容量在引擎构造期定容（缓冲固定大小、零运行期扩容）；主队列与链队列共用同一准入结果缓冲。逻辑步内时序：换代 → 实体摄入 → 摄入收尾 → 步收尾，缺步即错误。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 准入结果区 < 队列 ×2、拒绝区 < 队列 | 启动失败（交叉校验） |
| 队列容量满 | 提交被拒，写拒绝区（带原因码） |
| 拒绝区也满 | 进入终端故障 |
| 终态非法（完成带原因/失败无原因） | 拒绝终态化并报错 |

## 6. 实例

- 根基线：`assets/game.json` `gasRuntimeCapacity` 段（数值以 [事实与取值表](../facts.md) 为准）

**相关文档**：[ord-03 PRD](../prd/ord-03-pipeline.md) · [cfg-06 配置说明](cfg-06-game-config.md) · [ord-02 配置说明](ord-02-rules.md)

# rt-02 配置说明 · 预算与容量

> 配置写法与行为。第一性需求见 [rt-02 PRD](../prd/rt-02-budgets.md)；编辑器需求见 [UXD](../uxd/rt-02-budgets.md)；现状见 [reference](../reference/rt-02-budgets.md)。

## 1. 示例配置

预算机制无独立配置表；作者能配的是**容量**——`game.json` 的 `gasRuntimeCapacity` 块（片段，全量见事实页）：

```json
{
  "gasRuntimeCapacity": {
    "effectFanOutCommandCapacity": 16384,
    "orderQueueCapacity": 4096,
    "effectRequestQueueCapacity": 4096
  }
}
```

## 2. 作者可配什么与在哪配

| 数字 | 在哪 | 管什么 |
|---|---|---|
| 各类容量 | `game.json` `gasRuntimeCapacity`（见事实页全表） | 缓冲/队列/快照的碗有多大；**单根记账表容量=effectFanOutCommandCapacity** |
| 单根创建上限 | 引擎常量（见事实页 MAX_CREATES_PER_ROOT） | 一个根效果单帧最多创建多少个效果——**不是**表容量，两数职责不同（治理项 R1） |
| 窗口步数/响应数、帧处理 pass、各类组件槽位 | 引擎常量（见事实页 GasConstants 全清单） | 单窗预算、帧预算、单效果槽位——代码合同，无配置面 |

规则：容量改大=碗变大（更多并发的根/事件/请求）；单根上限管的是级联爆炸半径——mod 造出"效果生效果"链时它先拦。

## 3. 文件结构

`assets/game.json`（引擎基线，mod 可深合并覆盖个别容量字段；见 cfg-06）。预算计数与单根表是纯运行时结构，无文件。

## 4. 运行时加载效果

容量在引擎装配期读取并构造各缓冲与单根记账表；交叉约束（订单准入结果 ≥ 队列×2 等）启动期校验。每帧：复位系统清零 → 系统消费/记账 → 报告系统发布非零指标到诊断通道。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 单根达上限 | 该次创建被拒、计数器 +1，游戏继续 |
| 各队列/容器超容量 | 拒绝或丢弃并按类计数（进诊断），不崩溃 |
| 事务检查点重入/回滚超限/失效检查点 | 抛错带错误码 |
| 订单溢出计数回退 | 发布诊断错误 |

## 6. 实例

- 容量基线：`assets/game.json`、全量见 [事实与取值表](../facts.md)
- 单根上限：引擎常量 MAX_CREATES_PER_ROOT（见事实页）

**相关文档**：[rt-02 PRD](../prd/rt-02-budgets.md) · [cfg-06 配置说明](cfg-06-game-config.md) · [rt-03 配置说明](rt-03-diagnostics.md)

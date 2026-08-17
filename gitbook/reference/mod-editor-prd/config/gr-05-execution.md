# gr-08 配置说明 · 执行模型

> 配置写法与行为。第一性需求见 [gr-06 PRD](../prd/gr-05-execution.md)；编辑器需求见 [UXD](../uxd/gr-05-execution.md)；现状见 [reference](../reference/gr-05-execution.md)。

## 1. 示例配置

作者只做两件事：在 Script 图里写 `Yield` 节点，并把图挂到允许挂起的宿主。真实组合（`assets/GAS/graphs.json` + `assets/GAS/action_lib.json`）：

```json
{ "id": "drinkYield", "op": "Yield" }
{ "name": "script.drinkUntilFull", "graph": "Graph.Script.DrinkUntilFull", "kind": "Script", "host": "Script" }
```

## 2. 字段与行为

| 写法 | 这样配会产生什么效果 |
|---|---|
| 图含 `Yield` | 只能由允许挂起的宿主（BT 叶、Script）经切片执行；进 FuncLib 会被纯度校验拒（gr-08） |
| 挂到 Hfsm / Level 宿主 | 装载期做可达挂起校验，图内可达 Yield 即拒 |
| `InvokeScript` 跨图调用 | 目标必须是 Script；被调子图含挂起直接拒；深度计入硬上限 |
| 关卡脚本 | 步数预算更小且禁止挂起（挂点差异@@gr7@@） |

预算与深度常量（单执行指令数、调用栈、跨图深度）不是配置——是引擎硬上限（[事实与取值表](../facts.md) 相关容量项与 gr-02 reference）。

## 3. 文件结构

无独立配置文件：执行语义由图内容（是否含 Yield）与挂接宿主共同决定。

## 4. 运行时加载效果

ActionLib 装载时按宿主政策做挂起可达校验（gr-09）；FuncLib 装载时做纯度闭包校验（gr-08）；执行期两种入口——run-to-halt 与切片，游标状态机见 gr-03。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| run-to-halt 入口遇 Yield | 执行失败（提示走切片执行） |
| 预算耗尽 | run-to-halt 抛出；切片持久化为挂起态，下帧续跑 |
| 跨图深度超限 | 执行失败 |
| 指令指针越界 | 按越界处理，不静默 |

## 6. 实例

- 挂起全集：`assets/GAS/graphs.json` 的 `Graph.Script.DrinkUntilFull`（While+Yield+Call/Return）
- 无挂起 BT 叶：同文件 `Graph.BT.Leaf.*` 系列

**相关文档**：[gr-06 PRD](../prd/gr-05-execution.md) · [gr-08 配置说明](gr-06-funclib.md) · [gr-09 配置说明](gr-07-actionlib.md)

# gr-07 配置说明 · 动作库 ActionLib

> 配置写法与行为。第一性需求见 [gr-07 PRD](../prd/gr-07-actionlib.md)；编辑器需求见 [UXD](../uxd/gr-07-actionlib.md)；现状见 [reference](../reference/gr-07-actionlib.md)。

## 1. 示例配置

引擎默认 `assets/GAS/action_lib.json`（共 7 条，节选）：

```json
[
  { "name": "bt.attack",            "graph": "Graph.BT.Leaf.Attack",      "kind": "Script" },
  { "name": "hfsm.combat.onTick",   "graph": "Graph.HFSM.Combat.OnTick",  "kind": "Script" },
  { "name": "script.drinkUntilFull","graph": "Graph.Script.DrinkUntilFull","kind": "Script" }
]
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `name` | 动作名，跨 mod 合并键；不得与 func_lib 撞名 |
| `graph` / `kind` | 指向已注册图；kind 必须 `Script` |

资产中性（#1542 起）：条目不声明消费方。谁挂接（BT 叶 / HFSM 生命周期 / 脚本挂接点）由行为侧引用决定，`host` 字段已删除。挂起（gr-05）约束由消费方在绑定时校验：可挂起的图挂到不可挂起的宿主上，绑定期拒绝。

## 3. 文件结构

`assets/GAS/action_lib.json`（目录登记条目，同 name 合并）；引用许可序在 graphs、func_lib 之后（gr-01 第 4 节）。

## 4. 运行时加载效果

装载时逐条校验：图已注册、kind 一致、撞名检查（含 func_lib）；通过后目录生效，供 BT/HFSM/脚本挂接点按名取用（地图级反应式关卡流走 TriggerGraph 挂载，不经动作库）。Yield 可达性不在装载期检查，由各消费方绑定时自校验。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| `kind` 非 Script | 装载失败 |
| 与 func_lib 撞名 | 装载失败 |
| 引用未注册图 / kind 不一致 | 装载失败 |
| 消费方绑定时违反自身挂起政策 | 绑定失败，指明动作与图 |

## 6. 实例

- 引擎默认：`assets/GAS/action_lib.json`（7 条：3 BT / 3 HFSM / 1 Script）
- 挂起动作样本：`script.drinkUntilFull`（gr-05 第 1 节）

**相关文档**：[gr-07 PRD](../prd/gr-07-actionlib.md) · [gr-05 配置说明](gr-05-execution.md) · [gr-08 配置说明](gr-08-mount-points.md)

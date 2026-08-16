# gr-06 配置说明 · 动作库 ActionLib

> 配置写法与行为。第一性需求见 [gr-06 PRD](../prd/gr-07-actionlib.md)；编辑器需求见 [UXD](../uxd/gr-07-actionlib.md)；现状见 [reference](../reference/gr-07-actionlib.md)。

## 1. 示例配置

引擎默认 `assets/GAS/action_lib.json` 四宿主各一（共 11 条，节选）：

```json
[
  { "name": "bt.attack",           "graph": "Graph.BT.Leaf.Attack",     "kind": "Script", "host": "BehaviorTree" },
  { "name": "hfsm.combat.onTick",  "graph": "Graph.HFSM.Combat.OnTick", "kind": "Script", "host": "Hfsm" },
  { "name": "level.phaseAdvance",  "graph": "Graph.Level.PhaseAdvance", "kind": "Script", "host": "Level" },
  { "name": "script.drinkUntilFull","graph": "Graph.Script.DrinkUntilFull","kind": "Script","host": "Script" }
]
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `name` | 动作名，跨 mod 合并键；不得与 func_lib 撞名 |
| `graph` / `kind` | 指向已注册图；kind 必须 `Script` |
| `host` | 四值之一：`BehaviorTree`（可挂起）/ `Hfsm`（不可）/ `Level`（不可）/ `Script`（可挂起） |

宿主与挂起（gr-04）：host 填 Hfsm 或 Level 时，图内任何可达 Yield 都在装载期拒绝；要挂起就挂 BT 或 Script。

## 3. 文件结构

`assets/GAS/action_lib.json`（目录登记条目，同 name 合并）；引用许可序在 graphs、func_lib 之后（gr-00 第 4 节）。

## 4. 运行时加载效果

装载时逐条校验：图已注册、kind 一致、host 合法、撞名检查、宿主政策下的挂起可达校验；通过后目录生效，供 BT/HFSM/关卡/脚本挂接点按名取用。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| `kind` 非 Script | 装载失败 |
| `host` 缺省或非四值 | 装载失败 |
| Hfsm/Level 图内可达 Yield | 装载失败，指明动作与图 |
| 与 func_lib 撞名 | 装载失败 |
| 引用未注册图 / kind 不一致 | 装载失败 |

## 6. 实例

- 引擎默认：`assets/GAS/action_lib.json`（11 条：5 BT / 4 Hfsm / 1 Level / 1 Script，计数见事实页目录节）
- 挂起动作样本：`script.drinkUntilFull`（gr-04 第 1 节）

**相关文档**：[gr-06 PRD](../prd/gr-07-actionlib.md) · [gr-04 配置说明](gr-05-execution.md) · [gr-07 配置说明](gr-08-mount-points.md)

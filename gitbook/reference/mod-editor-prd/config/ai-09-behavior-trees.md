# ai-09 配置说明 · 行为树

> 配置写法与行为。第一性需求见 [ai-09 PRD](../prd/ai-09-behavior-trees.md)；编辑器需求见 [UXD](../uxd/ai-09-behavior-trees.md)；现状见 [reference](../reference/ai-09-behavior-trees.md)。

## 1. 示例配置

引擎默认（主仓 `assets/AI/behavior_trees.json` 现状唯一树，节选）：

```json
[
  {
    "id": "bt.patrolChaseAttack",
    "root": "root",
    "nodes": [
      { "id": "root",   "kind": "Selector", "children": ["engage", "patrol"] },
      { "id": "engage", "kind": "Sequence", "children": ["seeEnemy", "engageSelect"] },
      { "id": "patrol", "kind": "Action",   "leaf": "ScriptSlice", "action": "bt.patrol" },
      { "id": "seeEnemy","kind": "Condition","leaf": "ScriptSlice", "action": "bt.seeEnemy" },
      { "id": "attack", "kind": "Action",   "leaf": "ScriptSlice", "action": "bt.attack" }
    ]
  }
]
```

（完整树另有 engageSelect/chase/inRange 等 9 节点；无 action 的叶可写 `"leaf": "AlwaysSuccess"` 等。）

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| id / root | 树名与根节点 id；root 必须能在 nodes 找到 |
| nodes[].kind | Sequence/Selector/Condition/Action 四值，**区分大小写**（I2） |
| nodes[].children | 子节点 id 数组（组合节点）；加载时禁多父、禁不可达 |
| nodes[].leaf | None/AlwaysSuccess/AlwaysFailure/HoldRunning/ScriptSlice 五值，区分大小写 |
| nodes[].action | ActionLib 名；仅 ScriptSlice 叶可写，编译期 Require(BehaviorTree) |

叶语义：Condition 的 ScriptSlice 必须 halt（ReturnInt≠0=Success）；Action 的 ScriptSlice 可 Yield 跨波续跑。

## 3. 文件结构

`assets/AI/behavior_trees.json`（ArrayById）。**本表有 schema**（behavior_trees.schema.json，随资产分发）但 schema 不参与流水线校验——结构提示靠编辑器/工具自取。

## 4. 运行时加载效果

GraphBehaviorDefinitionLoader 逐树解析：校验 id 去重与枚举严格匹配，PackTree BFS 打包为 SoA 节点数组；ScriptSlice 的 action 逐一进 GraphActionCatalog.Require(name, BehaviorTree)。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| root 缺失 / nodes 空 | 启动失败 |
| 节点 id 重复 | 启动失败 |
| kind/leaf 大小写或拼写错 | 启动失败（Enum.TryParse 严格） |
| 多父 / 不可达节点 | 启动失败 |
| action 挂非 ScriptSlice 叶 | 启动失败：action is only valid on ScriptSlice leaves |
| action 名未注册 | 启动失败（ActionLib Require） |

## 6. 实例

- 引擎默认：`assets/AI/behavior_trees.json`（bt.patrolChaseAttack，9 节点）
- 驱动侧真实例：`mods/showcases/capability_standard/CapabilityStandardBehaviorTreeArenaMod/Runtime/BehaviorTreeArenaRuntime.cs:129-131`（RestartAllThinking + TickAll 32）

**相关文档**：[ai-09 PRD](../prd/ai-09-behavior-trees.md) · [ai-10 配置说明](ai-10-hfsm.md) · [ai-01 配置说明](ai-01-utility-overview.md)

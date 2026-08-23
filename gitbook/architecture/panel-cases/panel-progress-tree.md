#### 案 24：panel.progress.tree —— 进度节点树（交互模态）

> 状态：⛔ 装载即拒 G5（gesture/modal 锚点）——运行链路：G8、G9、#1015、节点树展示依赖 G12（列表型引脚）。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "list", "rows": [["✓ 铁器", ""], ["✓ 弩机", ""], ["▶ 攻城", "37%"]]}
```

```jsonc
{
  "id": "panel.progress.tree",
  "graph": "Graph.Progress.Tree",             // 节点解锁态+前置边输出（前置=图内关系读）
  "pins": [ { "name": "nodeCount", "key": "tree.nodeCount", "mode": "realtime", "default": 0 },
            { "name": "unlocked",  "key": "tree.unlocked",  "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "node.inspect", "control": "tree.nodes", "gesture": "click", "payload": { "node": "Int" } } ],
  "intents": [ { "event": "node.inspect", "intent": "tree.inspect", "args": { "node": "$payload.node" },
                 "playerSource": "seat", "actorSource": "none" } ]
}
```

```jsonc
// 值图 Graph.Progress.Tree（kind: Query）
{
  "id": "Graph.Progress.Tree", "kind": "Query", "entry": "nodeCount",
  "nodes": [
    { "id": "nodeCount", "op": "LoadSelfAttribute", "attribute": "Tree.NodeCount" },
    { "id": "unlocked",  "op": "LoadSelfAttribute", "attribute": "Tree.Unlocked" }
  ],
  "controlEdges": [
    { "from": "nodeCount", "fromPort": "next", "to": "unlocked" }
  ],
  "valueEdges": [],
  "outputs": [
    { "id": "nodeCount", "destination": "Summary", "type": "Int", "source": "nodeCount", "key": "tree.nodeCount" },
    { "id": "unlocked",  "destination": "Summary", "type": "Int", "source": "unlocked",  "key": "tree.unlocked" }
  ]
}
```

```text
modal.center（G5）┌───────────────────────────────┐
                  │  ◉──◯──◯                    │ ◉已解锁 ◯可解锁
                  │      └──◉──◯                │
                  └───────────────────────────────┘
```

30 秒预期：点节点看详情，解锁态随图更新。依赖：G5、G8、G9、#1015。

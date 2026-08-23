#### 案 31：panel.collection.book —— 图鉴背包（交互模态）

> 状态：⛔ 装载即拒 G5（gesture/modal 锚点）——运行链路：G8、G9、#1015、条目网格依赖 G12（列表型引脚）。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "stat", "items": ["已收录 12", "共 40"]}
```

```jsonc
{
  "id": "panel.collection.book",
  "graph": "Graph.Collection.Book",           // 收集进度+分页条目输出
  "pins": [ { "name": "collected", "key": "book.collected", "mode": "realtime", "default": 0 },
            { "name": "total",     "key": "book.total",     "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "book.flip", "control": "btn.page", "gesture": "click", "payload": { "page": "Int" } } ],
  "intents": [ { "event": "book.flip", "intent": "ui.bookPage", "args": { "page": "$payload.page" },
                 "playerSource": "seat", "actorSource": "none" } ]
}
```

```jsonc
// 值图 Graph.Collection.Book（kind: Query）
{
  "id": "Graph.Collection.Book", "kind": "Query", "entry": "collected",
  "nodes": [
    { "id": "collected", "op": "LoadSelfAttribute", "attribute": "Book.Collected" },
    { "id": "total",     "op": "ConstInt", "intValue": 40 }
  ],
  "controlEdges": [
    { "from": "collected", "fromPort": "next", "to": "total" }
  ],
  "valueEdges": [],
  "outputs": [
    { "id": "collected", "destination": "Summary", "type": "Int", "source": "collected", "key": "book.collected" },
    { "id": "total",     "destination": "Summary", "type": "Int", "source": "total",     "key": "book.total" }
  ]
}
```

```text
modal.center（G5）┌─────────────────────────────────────┐
                  │ 图鉴 12/40  ▦▦▦▦▦▦▦▦▦▦▦▦░░░░░░░░  │ 条目网格=图中收集集合
                  │ 【◀ 上一页】【下一页 ▶】             │
                  └─────────────────────────────────────┘
                  （目标视觉——G12 列表型引脚后可达；今日 pins 仅驱动标量计数）
```

30 秒预期：翻页浏览图鉴，收集进度随获取增长。依赖：G5、G8、G9、#1015。

至此目录 35 类全部立案（前四案 + 本批 31 案），逐组过核完成，目录行可作为 #841 种子行与 #840 对照物。

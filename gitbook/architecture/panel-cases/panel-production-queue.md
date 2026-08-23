#### 案 25：panel.production.queue —— 生产队列（#1012 验收场景）

> 状态：⛔ 装载即拒 G3（global scope）+ G6（pins 非空）；运行链路：G8（事件载荷值）、#1015、集合形态见 G12（#1012 验收场景）。聚合/百分比=图内节点（已落地），无 G2。

```jsonc
{
  "id": "panel.production.queue",
  "graph": "Graph.Production.Queue",          // 队列项+进度输出（G2：百分比 Float 图输出）
  "pins": [ { "name": "progressPercent", "key": "queue.progressPercent", "mode": "realtime", "default": 0 },
            { "name": "queueCount", "key": "queue.count", "mode": "realtime", "default": 0 },
            { "name": "queueCap", "key": "queue.cap", "mode": "realtime", "default": 5 } ],
  "events": [ { "eventId": "queue.push", "control": "btn.enqueue", "gesture": "click" },
              { "eventId": "queue.cancel", "control": "btn.cancel", "gesture": "click" } ],
  "intents": [ { "event": "queue.push", "intent": "production.enqueue", "args": {}, "playerSource": "seat", "actorSource": "commandSource.primary" },
               { "event": "queue.cancel", "intent": "production.cancel", "args": {}, "playerSource": "seat", "actorSource": "commandSource.primary" } ]
}
```

```text
screen.bottomRight 上方 ┌───────────────────────────────┐
                        │ ▓▓▓▓░░ 66% ▍弩手▍弩手▍▢       │ 进度=progressPercent 回读；满员置灰=皮读 queueCap 自比
                        │ 【+1】【取消】                 │
                        └───────────────────────────────┘
```

30 秒预期：点+1 弩手入队进度走，点取消出队。依赖：G2、G3、#1012（验收场本体）。

### 分组六 · 单位操作（案 26–28）

#### 案 1：panel.time.elapsed —— 时间流逝（纯展示走表）

> 状态：🟢 今日可装载——纯展示，图输出 realtime 回读，字段全过白名单（新形状不写 scope）。
>
> ⚠️ **基建依赖**：引擎时钟仅有 `time.scale_permille` 缩放与 EntityLocalClock.LocalStep 计数，无“流逝分钟/昼夜相位”推进系统——`Clock.ElapsedMin`/`Clock.DayPhase` 属性无底层系统。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "stat", "items": ["☀", "12:34"]}
```

```jsonc
{
  "id": "panel.time.elapsed",
  "graph": "Graph.Time.Elapsed",              // 时钟图输出 elapsedMin/dayPhase
  "pins": [
    { "name": "elapsedMin", "key": "clock.elapsedMin", "mode": "realtime", "default": 0 },
    { "name": "dayPhase",   "key": "clock.dayPhase",   "mode": "realtime", "default": 1 }
  ]
  // 无 events/intents——纯展示；昼夜图标换肤=皮读 dayPhase 自行决定
}
```

```jsonc
// 值图 Graph.Time.Elapsed（kind: Query）
{
  "id": "Graph.Time.Elapsed", "kind": "Query", "entry": "elapsedMin",
  "nodes": [
    { "id": "elapsedMin", "op": "LoadSelfAttribute", "attribute": "Clock.ElapsedMin" },
    { "id": "dayPhase",   "op": "LoadSelfAttribute", "attribute": "Clock.DayPhase" }
  ],
  "controlEdges": [
    { "from": "elapsedMin", "fromPort": "next", "to": "dayPhase" }
  ],
  "valueEdges": [],
  "outputs": [
    { "id": "elapsedMin", "destination": "Summary", "type": "Float", "source": "elapsedMin", "key": "clock.elapsedMin" },
    { "id": "dayPhase",   "destination": "Summary", "type": "Int",   "source": "dayPhase",   "key": "clock.dayPhase" }
  ]
}
```

```text
screen.topRight（信息聚合左侧）┌──────────────┐
                              │ ☀ 12:34      │  dayPhase=2 换 ☾（皮层换肤）
                              └──────────────┘
```

30 秒预期：表走字、昼夜图标随 dayPhase 切换。依赖：G3（global scope 语义）。

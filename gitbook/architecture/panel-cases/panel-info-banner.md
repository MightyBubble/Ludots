#### 案 4：panel.info.banner —— 全局信息横幅（纯展示）

> 状态：🟢 今日可装载——纯展示；显隐由 TriggerGraph 听游戏事件驱动（现有 op，非 UI 事件不涉 G10）。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "text", "text": "⚠ 敌军逼近北门"}
```

```jsonc
{
  "id": "panel.info.banner",
  "graph": "Graph.Info.Banner",               // banner.current=文案 id；文案查表=图内节点
  "pins": [
    { "name": "bannerText",  "key": "banner.current", "mode": "realtime", "default": 0 },
    { "name": "bannerLevel", "key": "banner.level",   "mode": "realtime", "default": 0 }
  ]
}
```

```text
screen.topCenter ┌───────────────────────────────────┐
                 │ ⚠ 敌军逼近北门（level=2 红底）     │  平静时 HidePanel、事件时 ShowPanel（图驱动）
                 └───────────────────────────────────┘
```

30 秒预期：敌军进区横幅弹红字，威胁解除消失。依赖：G3（global scope 语义）。

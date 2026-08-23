## 案 C：设置 `panel.settings` —— 模态浮层 · 连续手势 · 全局副作用

> ⛔ **装载即拒**：`scope`（G3）、`gesture:"change"` 不在现有手势表（G8）、modal.center 锚点（G5）。运行链路缺口：actorSource none 值域（G9，运行时拒）、图消费 UI 事件接线（G10）、事件分发（#1015）。
>
> ⚠️ **基建依赖**：`Settings.Volume` 无底层系统——无设置/音量运行时，滑条回读属性无稳定出口（持久化亦属存档域未定）。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "text", "text": "音量 80%"}
```

### C1 玩家旅程
玩家点右上角常驻【⚙】→ 模态浮层弹出（其余输入挂起）；拖音量滑条 → 音量实时变化；点【✕】或浮层外 → 关闭，输入恢复。点【退出到主菜单】→ 二次确认后退出。

### C2 完整配置

```jsonc
{
  "id": "panel.settings",
  "graph": "Graph.Settings",
  "pins": [
    { "name": "volume", "key": "settings.volume", "mode": "realtime", "default": 0.8 }   // 滑条位置=volume 回读
  ],
  "events": [
    { "eventId": "settings.volume", "control": "slider.volume", "gesture": "change",
      "payload": { "value": "Float" } },
    { "eventId": "settings.exit", "control": "btn.exit", "gesture": "click" },
    { "eventId": "settings.close", "control": "btn.close", "gesture": "click" }
  ],
  "intents": [
    { "event": "settings.volume", "intent": "settings.setVolume",
      "args": { "value": "$payload.value" }, "playerSource": "seat", "actorSource": "none" },
    { "event": "settings.exit", "intent": "game.exitToMenu",
      "args": {}, "playerSource": "seat", "actorSource": "none" }
    // settings.close 无意图——纯 UI 事件，显隐编排层消费（见 C5）
  ]
}
```

```jsonc
// 值图 Graph.Settings（kind: Query）
{
  "id": "Graph.Settings", "kind": "Query", "entry": "volume",
  "nodes": [
    { "id": "volume", "op": "LoadSelfAttribute", "attribute": "Settings.Volume" }
  ],
  "controlEdges": [],
  "valueEdges": [],
  "outputs": [
    { "id": "volume", "destination": "Summary", "type": "Float", "source": "volume", "key": "settings.volume" }
  ]
}
```

### C3 线框

```text
模态锚点 modal.center（G5）   ┌──────────────────────┐ zOrder 500（浮层最高层）
                              │ 设置            【✕】 │
                              │ 音量 ▁▂▃▅▆▇ 【━━●━】 │ ← gesture:change 连续派发
                              │ 【退出到主菜单】       │
                              └──────────────────────┘
   背景输入挂起（模态语义）；【⚙】入口按钮属 subsystem.entries（2.9），非本面板
```

### C4 数据流与事件链
- 音量：滑条 change → settings.volume 事件（连续）→ 意图 settings.setVolume → 设置系统落值 → `settings.volume` 图输出 → 面板 realtime 回读（滑条跟随）。与案 B 同构，差别在 gesture=change 高频派发——**意图层须做合流**（admission 对连续意图只取末值，#1015 设计点）。
- 退出：click → game.exitToMenu 意图（无载荷）→ 确认流程属玩法域。

### C5 显隐编排（本案的灵魂）
默认隐藏：装载图只 `CreatePanel(panelAnchor: "modal.center")` **不 Show**。
- 开：⚙ 入口的事件 `sub.open{sub:settings}` 走图（TriggerGraph 监听 UI 事件）调 `ShowPanel("panel.settings")` + 输入焦点捕获。
- 关：`settings.close` 事件被同一张图消费 → `HidePanel` + 释放焦点。**纯 UI 事件不经意图层**——无 order 副作用的关闭动作在编排层终结。

### C6 验收（Gherkin）

```gherkin
Scenario: 模态开合由图编排
  Given panel.settings 已创建
  And 未显示
  When ⚙ 入口事件出现（G10 接线后）
  Then 编排图调 ShowPanel
  And 输入焦点被浮层捕获
  When 玩家点击【✕】
  Then 编排图调 HidePanel
  And 焦点释放

Scenario: 连续意图合流
  Given 浮层打开
  And 音量滑条存在
  When 玩家连续拖动产生 10 个 change 事件
  Then 仅一条 settings.setVolume 意图携带末值入队

Scenario: 模态锚点未落地即拒
  Given G5 未实现
  And 配置使用 modal.center
  When 装载
  Then 装载失败，不静默降级为角落面板
```

30 秒人验：⚙ 开浮层→拖滑条声音变→✕ 关闭恢复。

### C7 依赖与边界
依赖：G5（模态锚点，#840 前置）、G8（change 手势+载荷值）、G9（actorSource none）、G10（开/关编排接线）、#1015。不做：热键 Esc 关闭（输入域）；设置持久化（存档域，仅回读已存值）。

---

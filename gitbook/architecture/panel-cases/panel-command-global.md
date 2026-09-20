## 案 D：全局指令 `panel.command.global` —— 零变量纯命令 · G6 缺口样板

> ⛔ **装载即拒**：`scope`（G3）、`pins: []`（G6）。运行链路缺口：actorSource none（G9）、互斥编排接线（G10）、事件分发（#1015）。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "chips", "chips": ["全选", "集结", "停止", "撤退"], "on": -1}
```

### D1 玩家旅程
玩家框选一队兵后点底栏【集结】→ 进入指定目标模式，光标变化；点【全选】→ 场上己方作战单位全亮。按钮按下即命令，面板自身无任何状态显示。

### D2 完整配置

```jsonc
{
  "id": "panel.command.global",
  "graph": "Graph.CommandGlobal",     // 纯命令面板：图为空壳（G6 放开前 pins ≥1 约束在）
  "pins": [],
  "events": [
    { "eventId": "army.selectAll", "control": "btn.selectAll", "gesture": "click" },
    { "eventId": "army.rally",     "control": "btn.rally",     "gesture": "click" },
    { "eventId": "army.stop",      "control": "btn.stop",      "gesture": "click" },
    { "eventId": "army.retreat",   "control": "btn.retreat",   "gesture": "click" }
  ],
  "intents": [
    { "event": "army.selectAll", "intent": "selection.allArmy", "args": {},
      "playerSource": "seat", "actorSource": "commandSource.primary" },
    { "event": "army.rally", "intent": "army.setRally", "args": {},
      "playerSource": "seat", "actorSource": "commandSource.primary" },
    { "event": "army.stop", "intent": "army.stopAll", "args": {},
      "playerSource": "seat", "actorSource": "commandSource.primary" },
    { "event": "army.retreat", "intent": "army.retreat", "args": {},
      "playerSource": "seat", "actorSource": "commandSource.primary" }
  ]
}
```

### D3 线框

```text
screen.bottomCenter ┌────────────────────────────────┐
                    │ 【全选】【集结】【停止】【撤退】 │  无状态显示：按钮永远不高亮
                    └────────────────────────────────┘  （可用性态=灰，由皮读意图
                                                          admission 状态渲染，非模板变量）
```

### D4 事件链
与案 B 同构（click → 意图 → admission → order）。特点：**四事件零载荷零变量**——载荷/变量/binds 全部缺席，最小事件面板。`args: {}` 合法（无参意图）。

### D5 显隐编排
常驻。特殊编排需求：**指挥模式互斥**——当玩家进入其他命令模式（如建筑放置）时，指挥图调 `HidePanel`；退出模式恢复 Show。仍由图驱动，面板不自理。

### D6 验收（Gherkin）

```gherkin
Scenario: 零变量命令面板装载（G6 后）
  Given G6 已放开零变量约束
  When 装载本模板
  Then 装载通过
  And 无假变量被塞入

Scenario: 命令直达意图
  Given 玩家已框选一队兵
  When 玩家点击【集结】
  Then army.setRally 意图入队，光标进入指定目标模式
```

30 秒人验：选兵→点集结→光标变指定态；点全选→全军亮。边界：G6 落地前 `variables: []` 装载即抛并提示"纯命令面板需 G6"。

### D7 依赖与边界
依赖：G6（零变量）、G9、G10（互斥编排）、#1015、G3。不做：按钮冷却是皮层自由实现（读 admission 状态），模板不承诺。

---

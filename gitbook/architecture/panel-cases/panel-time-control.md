## 案 B：时间控制 `panel.time.control` —— 交互全链 · 事件/意图/回读闭环

> ⛔ **装载即拒**：`scope`（G3）。另运行链路缺口：args 字面常量（意图解析现仅认 $payload. 前缀，G8）、事件分发链路（#1015）。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "chips", "chips": ["⏸", "1x", "2x", "3x"], "on": 1}
```

### B1 玩家旅程
游戏进行中，玩家点【3x】→ 游戏明显加速，【3x】按钮进入高亮态；再点【⏸】→ 游戏停，暂停键高亮。全程面板不知道"游戏速度"是什么——它只发事件、收回读。

### B2 完整配置

```jsonc
{
  "id": "panel.time.control",
  "graph": "Graph.Clock",
  "pins": [
    { "name": "speed", "key": "clock.speed", "mode": "realtime", "default": 1 }   // 回读：当前速度挡
  ],
  "events": [                                                    // 四挡四事件（装载器拒重复 eventId）
    { "eventId": "speed.set.pause", "control": "btn.pause",  "gesture": "click" },
    { "eventId": "speed.set.1",     "control": "btn.speed1", "gesture": "click" },
    { "eventId": "speed.set.2",     "control": "btn.speed2", "gesture": "click" },
    { "eventId": "speed.set.3",     "control": "btn.speed3", "gesture": "click" }
  ],
  "intents": [
    { "event": "speed.set.pause", "intent": "game.setSpeed", "args": { "speed": "0" }, "playerSource": "seat", "actorSource": "commandSource.primary" },
    { "event": "speed.set.1",     "intent": "game.setSpeed", "args": { "speed": "1" }, "playerSource": "seat", "actorSource": "commandSource.primary" },
    { "event": "speed.set.2",     "intent": "game.setSpeed", "args": { "speed": "2" }, "playerSource": "seat", "actorSource": "commandSource.primary" },
    { "event": "speed.set.3",     "intent": "game.setSpeed", "args": { "speed": "3" }, "playerSource": "seat", "actorSource": "commandSource.primary" }
  ]
}
```

```jsonc
// 值图 Graph.Clock（kind: Query）
{
  "id": "Graph.Clock", "kind": "Query", "entry": "speed",
  "nodes": [
    { "id": "speed", "op": "LoadSelfAttribute", "attribute": "Clock.Speed" }
  ],
  "controlEdges": [],
  "valueEdges": [],
  "outputs": [
    { "id": "speed", "destination": "Summary", "type": "Float", "source": "speed", "key": "clock.speed" }
  ]
}
```

### B3 线框

```text
screen.topRight 时间条右侧 ┌───────────────────┐
                           │ 【⏸】【1x】【2x】【3x】│  speed=0..3，皮层高亮 bind 到 lbl.speed
                           └───────────────────┘
```

### B4 事件-意图链（UI 永不直构 Order）

```text
点击 btn.speed3 ──gesture:click──> 事件 speed.set.3（挡位经 intent args 常量携带，G8）
      │（皮侧只声明，不实现）
      ▼
PanelEventBus（声明层校验：eventId/控件/手势/载荷类型四对四）
      ▼
IntentMap：speed.set.3 → game.setSpeed，args={speed:"3"}（字面常量定挡，语义规则 G8）
      │ 归因：playerSource=seat（座位 2 点的就是座位 2 的）
      ▼
意图 admission 中间层（预算/合法性裁决——UI 不碰）
      ▼ 通过                              ▼ 拒绝
Order(game.setSpeed,3)                拒绝回执(reason)→面板显示
      ▼
时钟系统执行 → GraphOutput clock.speed=3
      ▼
面板 realtime 回读 → 【3x】高亮
```

要点：**回读闭环**——高亮不是点击直接置位，而是意图落地后经 `clock.speed` 图输出回流。暂停键点两下、网络延迟、admission 拒绝，面板状态永远与游戏真值一致。

### B5 显隐编排
常驻（同案 A）：装载图 CreatePanel + ShowPanel。暂停菜单打开时可选择 HidePanel（由暂停菜单的图决定，本面板不自理）。

### B6 验收（Gherkin）

```gherkin
Scenario: 点挡加速且高亮回读
  Given 游戏以 1x 运行
  And 面板可见
  When 玩家点击【3x】
  Then game.setSpeed 意图以 args{speed:3} 入队并被 admission 放行
  And clock.speed 输出变为 3
  And 【3x】进入高亮态

Scenario: 拒绝时状态不漂移
  Given admission 预算为 0
  When 玩家点击【2x】
  Then 意图被拒
  And clock.speed 不变
  And 无按钮状态变化

Scenario: 载荷校验 fail-closed
  Given 事件携带未声明字段
  When 事件分发
  Then 事件层拒绝并点名字段
```

30 秒人验：点 3x 游戏明显加速+高亮；点暂停画面静止。

### B7 依赖与边界
依赖：G3、G8（args 常量）、#1015（事件分发→意图→admission 链路本体）。不做：热键绑定（输入系统域，面板只管点击）；变速过渡动画（皮层）。

---

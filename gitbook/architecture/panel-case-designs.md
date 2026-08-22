# 面板典型案例全设计——四个样板间的完整设计案

本页是 [面板目录设计](panel-catalog-designs.md) 的第一批**全设计案**：从分组一挑选四个最典型 case（覆盖纯展示、交互全链、模态浮层、零变量命令四种形态，踩中 G3/G5/G6 全部缺口），每案含：玩家旅程、完整配置、线框、数据流、事件-意图链、显隐编排、验收断言、依赖与边界。本批同时作为后续 31 类的设计范式模板。

**状态分级契约（双审计订正）**：每案头部标状态——🟢 今日可用（过装载器且运行链路完整）/ 🔴 目标态（**配置今日即可装载通过**——装载器只验结构；缺口在运行链路：意图解析值域、手势载荷机制、事件接线等，标注缺口编号）/ ⛔ 装载即拒（字段级：scope 未在白名单、pins 空数组等）。判断依据：拿配置碰 `PanelTemplateLoader` 白名单（结构）+ 运行链路清单（意图解析 args 仅认 $payload. 前缀、actorSource 值域现仅 commandSource.primary、事件分发/图接线依赖 G10）。

**通用约定**（不再每案重复）：实例参数四级链（op > 模板 > game.json > default）；皮与主题正交（`panelSkin` × `panelTheme`），本页线框均为内容语义，视觉由主题决定；所有 JSON fail-closed——未知字段/未知读嘴/未知事件手势装载期即抛。

---

## 案 A：玩家信息聚合 `panel.player.aggregate` —— 纯展示 · global scope · 活状态中转

> ⛔ **装载即拒**：`scope: "global"` 不在装载器白名单（G3）——unknown field 'scope'。

### A1 玩家旅程
开局即见顶栏左角资源条；造一队兵花掉 200 金——金币数字掉、人口 8→10；再花光时人口变红提示超限。全程零交互，纯被动读取。

### A2 完整配置

```jsonc
{
  "id": "panel.player.aggregate",
  "graph": "Graph.Economy.Aggregate",           // 唯一数据源：经济聚合图（心跳拍重算）
  "pins": [
    { "name": "gold",    "key": "economy.gold", "mode": "realtime", "default": 0 },
    { "name": "popUsed", "key": "pop.used",     "mode": "realtime", "default": 0 },
    { "name": "popCap",  "key": "pop.cap",      "mode": "realtime", "default": 20 }
  ]
  // 无 events / 无 intents —— 纯展示面板零交互面
}
```

### A3 线框

```text
screen.topLeft ┌──────────────────────────────┐  panelZOrder 100（常驻最底）
               │ ⛁ 1,240     👥 8/20          │  超 popCap 时皮层把 👥 行标红
               └──────────────────────────────┘  （皮读双变量自行比较，模板不做逻辑）
```

### A4 数据流（溯源到图）

```text
经济系统(实体属性) ──┐
人口系统(集合计数) ──┼→ Graph.Economy.Aggregate（Derived 图，心跳拍重算）
地图变量(储备)    ──┘        │ graphOutputKey: economy.gold / pop.used / pop.cap
                             ▼
                 PanelProjectionReader（读图输出 store，realtime 拍刷新）
                             ▼
                 PanelVariableSet{gold,popUsed,popCap} ──binds──> 控件 lbl.*
```

要点：面板**不直读**地图变量/实体——一切活状态经图输出中转（完成核"数字溯源到图"）；图心跳拍重算，面板帧扫取值，两级节流天然存在。

### A5 显隐编排
常驻面板：地图装载图（TriggerGraph）`CreatePanel(panelAnchor: "screen.topLeft")` 后**不调** `ShowPanel` 之外的任何显隐逻辑——开局默认隐藏由激活商店语义决定（`IsVisible` 缺省 false），装载图随即 `ShowPanel("panel.player.aggregate")` 常亮。整局无再隐需求。

### A6 验收（Gherkin）

```gherkin
Scenario: 资源条随经济同帧刷新
  Given Full-HUD 验收场启动
  And panel.player.aggregate 可见
  When 玩家花费 200 金生产一队兵
  Then gold 变量减少 200
  And popUsed 增加
  And 与图输出 economy.gold 同帧

Scenario: 溯源失败即拒
  Given 模板引用未注册的 graphOutputKey
  When 装载地图
  Then 装载失败并点名该 key（无 0 兜底）
```

30 秒人验：打开即见资源条；按 U 造兵 → 金掉/人口涨肉眼可见。

### A7 依赖与边界
依赖：G3（scope=global，#1012）。明确不做：货币动画/溢出滚动（皮层自由实现，模板不承诺）；不读敌方经济（图输出仅己方 scope）。

---

## 案 B：时间控制 `panel.time.control` —— 交互全链 · 事件/意图/回读闭环

> ⛔ **装载即拒**：`scope`（G3）。另运行链路缺口：args 字面常量（意图解析现仅认 $payload. 前缀，G8）、事件分发链路（#1015）。

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

## 案 C：设置 `panel.settings` —— 模态浮层 · 连续手势 · 全局副作用

> ⛔ **装载即拒**：`scope`（G3）、`gesture:"change"` 不在现有手势表（G8）、modal.center 锚点（G5）。运行链路缺口：actorSource none 值域（G9，运行时拒）、图消费 UI 事件接线（G10）、事件分发（#1015）。

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

## 案 D：全局指令 `panel.command.global` —— 零变量纯命令 · G6 缺口样板

> ⛔ **装载即拒**：`scope`（G3）、`pins: []`（G6）。运行链路缺口：actorSource none（G9）、互斥编排接线（G10）、事件分发（#1015）。

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

## 四案覆盖矩阵

| 维度 | A 聚合 | B 时间控制 | C 设置 | D 全局指令 |
|---|---|---|---|---|
| scope=global (G3) | ✅ | ✅ | ✅ | ✅ |
| 变量/binds | 有+realtime | 有（回读） | 有（回读） | **无（G6）** |
| events/intents | 无 | click+载荷 | **change 连续+合流** | click 无载荷 |
| 显隐 | 常驻 | 常驻 | **模态编排（开/关图）** | 互斥编排 |
| 锚点 | topLeft | topRight | **modal.center（G5）** | bottomCenter |
| 手势载荷/args 常量（G8） | — | ✅ | ✅(change) | — |
| actorSource none（G9） | — | — | ✅ | ✅ |
| 图消费 UI 事件（G10） | — | — | ✅(开/关编排) | ✅(互斥编排) |
| 溯源形态 | 图聚合输出 | 图输出回读闭环 | 图输出+连续意图 | 纯意图 |

---

## 五、其余案例设计（31 案）

> 状态标记随页首契约（双审计订正版）：🟢 配置可装载且链路完整；🔴 配置今日可装载通过、缺口在运行链路（意图/手势/接线，逐案点名）；⛔ 装载即拒（字段级）。列表/集合内容型案（实体列表/日志/编队/图鉴等）依赖**列表型引脚**（图集合输出→面板条目渲染的形状，缺口 G12）——在案内点名。

#### 案 1：panel.time.elapsed —— 时间流逝（纯展示走表）

> 状态：🟢 今日可装载——纯展示，图输出 realtime 回读，字段全过白名单（新形状不写 scope）。

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

```text
screen.topRight（信息聚合左侧）┌──────────────┐
                              │ ☀ 12:34      │  dayPhase=2 换 ☾（皮层换肤）
                              └──────────────┘
```

30 秒预期：表走字、昼夜图标随 dayPhase 切换。依赖：G3（global scope 语义）。

#### 案 2：panel.date.cycle —— 日期（纯展示）

> 状态：🟢 今日可装载——纯展示；年/季/月查表为图内节点。

```jsonc
{
  "id": "panel.date.cycle",
  "graph": "Graph.Time.Date",                 // 时钟输出 dayIndex；年/季由图内 TableLookup 换算
  "pins": [
    { "name": "dayIndex", "key": "clock.dayIndex", "mode": "realtime", "default": 1 }
  ]
}
```

```text
紧贴时间条右侧 ┌──────────────────┐
               │ 第 3 年 · 春 · 7 │   年/季=dayIndex 查周期表（图内节点）
               └──────────────────┘
```

30 秒预期：过夜日期 +1、季节图标换。依赖：G3（global scope 语义）。

#### 案 3：panel.tabs.global —— 全局功能 tab（交互路由）

> 状态：🔴 目标态——拒因：G8（$payload 引用语义）+ G9（actorSource none）+ #1015（意图链路本体）。

```jsonc
{
  "id": "panel.tabs.global",
  "graph": "Graph.UI.Tabs",                   // 回读 ui.activeTab 高亮
  "pins": [ { "name": "activeTab", "key": "ui.activeTab", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "tab.switch", "control": "tab.bar", "gesture": "click", "payload": { "tab": "Int" } } ],
  "intents": [ { "event": "tab.switch", "intent": "ui.switchTab", "args": { "tab": "$payload.tab" },
                 "playerSource": "seat", "actorSource": "none" } ]
}
```

```text
screen.topLeft（信息聚合下方）┌────────────────────────────┐
                             │【信息】【科技】【外交】【生产】│ ← activeTab 高亮
                             └────────────────────────────┘
```

30 秒预期：点科技→右侧切科技面板，再点外交切走。依赖：G3、G8、G9、#1015、子系统面板本体（2.9）。

#### 案 4：panel.info.banner —— 全局信息横幅（纯展示）

> 状态：🟢 今日可装载——纯展示；显隐由 TriggerGraph 听游戏事件驱动（现有 op，非 UI 事件不涉 G10）。

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

#### 案 5：panel.subsystem.entries —— 子系统入口（交互路由）

> 状态：🔴 目标态——拒因：G8（$payload）+ G9（actorSource none）+ #1015。

```jsonc
{
  "id": "panel.subsystem.entries",
  "graph": "Graph.UI.Subsystems",             // tech.unread 等未读角标输出
  "pins": [ { "name": "techUnread", "key": "tech.unread", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "sub.open", "control": "bar.subsystems", "gesture": "click", "payload": { "sub": "Int" } } ],
  "intents": [ { "event": "sub.open", "intent": "ui.openSubsystem", "args": { "sub": "$payload.sub" },
                 "playerSource": "seat", "actorSource": "none" } ]
}
```

```text
screen.bottomRight 竖条 ┌────┐
                        │【🔬3】│ ← 角标=techUnread（超 9 显示 9+）
                        │【🛠】│
                        └────┘
```

30 秒预期：科技完成角标+1，点开面板清零。依赖：G3、G8、G9、#1015、路由机制（#29）。

### 分组二 · 地图/空间指示（案 6–12）

#### 案 6：panel.minimap —— 小地图（纯展示覆盖层）

> 状态：🟢 今日可装载——纯展示；图内聚合实体位置/阵营输出图层。

```jsonc
{
  "id": "panel.minimap",
  "graph": "Graph.Map.Minimap",               // 图内聚合实体位置+阵营 → 输出图层数据
  "pins": [ { "name": "layer", "key": "minimap.layer", "mode": "realtime", "default": 0 } ]
  // 覆盖层画布：皮按 layer 渲染；缺数据=空图层（default 合同）
}
```

```text
screen.bottomLeft ┌──────────┐
                  │ ▦▦  ▦▦▦  │  己方蓝点/敌方红点（皮分层渲染）
                  └──────────┘
```

30 秒预期：兵移动蓝点同步动。依赖：无。

#### 案 7：panel.relation.indicator —— 关系图指示（纯展示）

> 状态：🟢 今日可装载——纯展示；关系边=图内读取。

```jsonc
{
  "id": "panel.relation.indicator",
  "graph": "Graph.Relation.Indicator",        // 图内读关系边（同盟/贸易/敌对）+ 强度
  "pins": [
    { "name": "edge",     "key": "relation.edge",     "mode": "realtime", "default": 0 },
    { "name": "strength", "key": "relation.strength", "mode": "realtime", "default": 0 }
  ]
}
```

```text
世界锚点（关系边两端）┌──── 虚线 ────┐
   城市A ─················· 城市B     同盟蓝线/敌对红线，线宽随 strength（皮层渲染）
```

30 秒预期：结盟后两城间出蓝线，断交变红。依赖：无。

#### 案 8：panel.selection.marker —— 选中标记（纯展示）

> 状态：🟢 今日可装载——纯展示；选中集合=图内读取。

```jsonc
{
  "id": "panel.selection.marker",
  "graph": "Graph.Selection.Marker",          // 图内读 selection 集合 → 计数/形态
  "pins": [
    { "name": "selectedCount", "key": "selection.count", "mode": "realtime", "default": 0 },
    { "name": "selectionKind", "key": "selection.kind", "mode": "realtime", "default": 0 }
  ]
}
```

```text
世界锚点（选中单位脚下）┌───────────┐
                        │ ◉ 光圈      │ 单选=光圈；框选=三角阵（皮按 kind 画）
                        └───────────┘
```

30 秒预期：点选出光圈、框选变多选标记。依赖：无。

#### 案 9：panel.offscreen.indicator —— 屏外指示（纯展示）

> 状态：🟢 今日可装载——纯展示；屏外投影=图内节点。

```jsonc
{
  "id": "panel.offscreen.indicator",
  "graph": "Graph.Map.Offscreen",             // 图内投影屏外目标方向/距离
  "pins": [
    { "name": "dir",  "key": "offscreen.dir",  "mode": "realtime", "default": 0 },
    { "name": "dist", "key": "offscreen.dist", "mode": "realtime", "default": 0 }
  ]
}
```

```text
屏幕边缘（世界锚点投影）┌──────────────────────────────┐
                        │        ↗ 敌军编队 120m       │ 箭头贴边旋转指向
                        └──────────────────────────────┘
```

30 秒预期：敌军移出屏幕，边缘箭头指向其方位。依赖：无。

#### 案 10：panel.scene.tint —— 场景染色（全屏效果非框）

> 状态：🟢（装载+链路完整）——归类为全屏效果非框：渲染走皮层全屏覆盖（screen.full 锚点语义待 G5 浮层锚点一并定），非四角框面板。

```jsonc
{
  "id": "panel.scene.tint",
  "graph": "Graph.Scene.Tint",                // 输出整屏染色色/透明度（夜幕/中毒/低血）
  "pins": [
    { "name": "tintColor", "key": "scene.tint.color", "mode": "realtime", "default": 0 },
    { "name": "tintAlpha", "key": "scene.tint.alpha", "mode": "realtime", "default": 0 }
  ]
}
```

```text
全屏覆盖（非框）┌───────────────────────────────────────────┐
               │ ░░ 入夜整屏叠蓝黑（alpha 渐入渐出）░░     │
               └───────────────────────────────────────────┘
```

30 秒预期：入夜场景渐染夜色，天明淡出。依赖：无。

#### 案 11：panel.region.indicator —— 区域指示（纯展示）

> 状态：🟢 今日可装载——纯展示；区域归属/威胁=图内读取。

```jsonc
{
  "id": "panel.region.indicator",
  "graph": "Graph.Map.Region",                // 图内读区域归属/威胁等级；区域名查表=图内节点
  "pins": [
    { "name": "regionId",    "key": "region.id",    "mode": "realtime", "default": 0 },
    { "name": "regionLevel", "key": "region.level", "mode": "realtime", "default": 0 }
  ]
}
```

```text
世界锚点（区域边界）┌────────────────────────┐
                    │ ⚠ 危险区（红框）       │ 威胁级决定框色
                    └────────────────────────┘
```

30 秒预期：进危险区边界泛红框，离开消失。依赖：无。

#### 案 12：panel.road.indicator —— 路网指示（纯展示）

> 状态：🟢 今日可装载——纯展示；路网状态=图内读取。

```jsonc
{
  "id": "panel.road.indicator",
  "graph": "Graph.Map.Road",                  // 图内读路网状态（通畅/拥堵/损毁）
  "pins": [
    { "name": "roadState", "key": "road.state", "mode": "realtime", "default": 0 }
  ]
}
```

```text
世界锚点（路径上）┌──────────────────────────┐
                  │ ═══ 通畅 ═══   拥堵=黄闪   │ 行军路线高亮，状态决定颜色
                  └──────────────────────────┘
```

30 秒预期：点选行军路线路网高亮，拥堵变黄。依赖：无。

### 分组三 · 选择与实体（案 13–18）

#### 案 13：panel.entity.list —— 实体列表（交互选中）

> 状态：🔴 目标态——拒因：G8（$payload）+ #1015（意图链路）。

```jsonc
{
  "id": "panel.entity.list",
  "graph": "Graph.Entity.List",               // 图内过滤+排序集合 → 行数据
  "pins": [ { "name": "rowCount", "key": "list.rowCount", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "entity.pick", "control": "list.entities", "gesture": "click", "payload": { "row": "Int" } } ],
  "intents": [ { "event": "entity.pick", "intent": "selection.setTarget", "args": { "row": "$payload.row" },
                 "playerSource": "seat", "actorSource": "commandSource.primary" } ]
}
```

```text
screen.leftCenter ┌──────────────────────┐
                  │ ▸ 步兵班 A   8/8     │ 点击行→选中对应实体（行→实体映射在图内）
                  │   弓手班 B   5/6     │
                  └──────────────────────┘
```

30 秒预期：点列表行对应单位被选中并高亮。依赖：G8、#1015。

#### 案 14：panel.entity.aggregate —— 实体信息聚合（纯展示）

> 状态：🟢 今日可装载——纯展示，图内 LoadSelfAttribute 聚合（总合同实体链路）。

```jsonc
{
  "id": "panel.entity.aggregate",
  "graph": "Graph.Entity.Aggregate",          // 图内 LoadSelfAttribute 聚合 hp/mp/等级
  "pins": [
    { "name": "hp",    "key": "unit.hp",    "mode": "realtime", "default": 100 },
    { "name": "mp",    "key": "unit.mp",    "mode": "realtime", "default": 0 },
    { "name": "level", "key": "unit.level", "mode": "realtime", "default": 1 }
  ]
}
```

```text
screen.rightCenter ┌────────────────────┐
                   │ 圣骑士 Lv.6        │ scope=self（CreatePanel source 边传选中实体）
                   │ ▓▓▓▓▓▓░░ 78/100 HP │
                   └────────────────────┘
```

30 秒预期：切换选中单位详情随 scope 换内容。依赖：无。

#### 案 15：panel.entity.relation —— 实体关系（纯展示）

> 状态：🔴（配置可装载）——计数/标量 pin 可用；条目内容依赖 G12（列表型引脚）；纯展示；关系边=图内读取。

```jsonc
{
  "id": "panel.entity.relation",
  "graph": "Graph.Entity.Relation",           // 图内读 self 关系边（所属/同盟/敌对）
  "pins": [
    { "name": "relationCount", "key": "relation.count", "mode": "realtime", "default": 0 },
    { "name": "relationKind",  "key": "relation.kind",  "mode": "realtime", "default": 0 }
  ]
}
```

```text
screen.rightCenter（聚合下方）┌─────────────────────┐
                             │ 所属：王国 A         │
                             │ 同盟：公会 B·敌对：C │
                             └─────────────────────┘
```

30 秒预期：选中单位显示所属/同盟/敌对清单。依赖：无。

#### 案 16：panel.collection.aggregate —— 集合聚合（纯展示）

> 状态：🔴（配置可装载）——计数/标量 pin 可用；条目内容依赖 G12（列表型引脚）；纯展示；聚合=图内节点（集合行为图本体随 #1012，缺数据落 default）。

```jsonc
{
  "id": "panel.collection.aggregate",
  "graph": "Graph.Collection.Aggregate",      // 图内聚合集合计数/均值（#1012 集合行为主战场）
  "pins": [
    { "name": "count", "key": "collection.count", "mode": "realtime", "default": 0 },
    { "name": "avgHp", "key": "collection.avgHp", "mode": "realtime", "default": 0 },
    { "name": "cap",   "key": "collection.cap",   "mode": "realtime", "default": 0 }
  ]
}
```

```text
screen.topLeft（tab 下方）┌───────────────────┐
                          │ 部队 24/30 均血 82% │
                          └───────────────────┘
```

30 秒预期：部队增减数字同帧变化。依赖：无。

#### 案 17：panel.context.route —— 选中路由（机制，不产配置）

> 状态：机制说明——非模板不产配置、无装载面；落地依赖 G10 + #1015。

```jsonc
// 无配置——机制：选中实体 → 路由表决定挂载哪组面板 → 图编排显隐/置换
// 不新建面板类型：由 CreatePanel 图 op 的 source 值边传选中 scope 驱动各面板
```

```text
选中步兵 ─route→ 案14 聚合 + 案26 状态条 + 案27 技能
选中建筑 ─route→ 案14 聚合 + 案28 装备 + 案11 区域
切换选中 ───────→ 旧组 HidePanel / 新组 ShowPanel（图编排，面板不自理）
```

30 秒预期：选中不同类型实体右侧面板组整体置换。依赖：G10、#1015。

#### 案 18：panel.linked.entities —— 关联实体集（纯展示）

> 状态：🔴（配置可装载）——计数 pin 今日可用；成员列表展示依赖 G12（列表型引脚），点击跳转依赖事件声明+#1015。

```jsonc
{
  "id": "panel.linked.entities",
  "graph": "Graph.Entity.Linked",             // 图内沿关系边收集关联实体（小队/编队成员）
  "pins": [ { "name": "linkedCount", "key": "linked.count", "mode": "realtime", "default": 0 } ]
}
```

```text
screen.leftCenter（列表下方）┌──────────────────────┐
                            │ 关联：A 班·B 班        │ 点击跳转属案17 路由机制
                            └──────────────────────┘
```

30 秒预期：选编队队长，关联成员清单列出。依赖：无。

### 分组四 · 信息流（案 19–21）

#### 案 19：panel.events.feed —— 事件面板（纯展示）

> 状态：🔴（配置可装载）——计数/标量 pin 可用；条目内容依赖 G12（列表型引脚）；纯展示；滚动/展开=皮层手势不声明意图。

```jsonc
{
  "id": "panel.events.feed",
  "graph": "Graph.Events.Feed",               // 图内取最近 N 条事件（新→旧）
  "pins": [ { "name": "feedCount", "key": "events.feedCount", "mode": "realtime", "default": 0 } ]
}
```

```text
screen.topRight 下方 ┌─────────────────────────┐
                     │ ⚔ 遭遇战开始 08:12      │ 新事件顶入，超 N 条溢出
                     │ ⚒ 铁矿 +50     08:11   │
                     └─────────────────────────┘
```

30 秒预期：战斗打响事件顶入首行。依赖：无。

#### 案 20：panel.events.entry —— 日志入口（交互）

> 状态：🔴（配置可装载）——运行链路缺口：#1015（意图链路）；args 为空不涉 G8。

```jsonc
{
  "id": "panel.events.entry",
  "graph": "Graph.Events.Entry",              // 未读日志数输出
  "pins": [ { "name": "unread", "key": "events.unread", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "log.open", "control": "btn.log", "gesture": "click" } ],
  "intents": [ { "event": "log.open", "intent": "ui.openLog", "args": {}, "playerSource": "seat", "actorSource": "none" } ]
}
```

```text
screen.bottomRight（子系统入口上方）┌──────┐
                                   │【📜3】│ ← unread 角标；点击开案21 日志
                                   └──────┘
```

30 秒预期：战斗后角标+1，点开日志清零。依赖：G8、G9、#1015、日志面板本体（案21）。

#### 案 21：panel.events.log —— 事件日志（交互模态）

> 状态：🔴 目标态——拒因：G5（modal.center）+ G8（$payload）+ G9 + G10（close 编排）+ #1015。

```jsonc
{
  "id": "panel.events.log",
  "graph": "Graph.Events.Log",                // 全量事件分页输出；过滤=图内节点
  "pins": [ { "name": "pageCount", "key": "events.pageCount", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "log.filter", "control": "tab.filter", "gesture": "click", "payload": { "kind": "Int" } },
              { "eventId": "log.close",  "control": "btn.close",  "gesture": "click" } ],
  "intents": [ { "event": "log.filter", "intent": "ui.setLogFilter", "args": { "kind": "$payload.kind" },
                 "playerSource": "seat", "actorSource": "none" } ]
  // log.close 无意图——纯 UI 事件，编排图消费（G10）
}
```

```text
modal.center（G5）┌──────────────────────────────────┐
                  │ 日志 【全部】【战斗】【✕】        │
                  │ ⚔ 遭遇战 08:12 · ⚒ 铁矿+50 08:11 │
                  └──────────────────────────────────┘
```

30 秒预期：开日志按类型过滤，✕ 关闭。依赖：G5、G8、G9、G10、#1015。

### 分组五 · 编队生产任务（案 22–25）

#### 案 22：panel.formation.info —— 编队信息（纯展示）

> 状态：🔴（配置可装载）——计数/标量 pin 可用；条目内容依赖 G12（列表型引脚）；纯展示；编队聚合=图内节点。

```jsonc
{
  "id": "panel.formation.info",
  "graph": "Graph.Formation.Info",            // 图内聚合编队成员属性 → 阵型/均速/士气
  "pins": [
    { "name": "formationKind", "key": "formation.kind", "mode": "realtime", "default": 0 },
    { "name": "avgSpeed",      "key": "formation.avgSpeed", "mode": "realtime", "default": 0 }
  ]
}
```

```text
screen.bottomLeft 上方 ┌─────────────────────────┐
                       │ 楔形阵 均速 3.2 士气 80  │ 阵型名查表=图内节点
                       └─────────────────────────┘
```

30 秒预期：切换阵型信息条同步更新。依赖：无。

#### 案 23：panel.quests —— 任务面板（交互）

> 状态：🔴 目标态——拒因：G8（$payload）+ G9 + #1015。

```jsonc
{
  "id": "panel.quests",
  "graph": "Graph.Quests",                    // 任务列表+进度输出
  "pins": [ { "name": "questCount", "key": "quests.count", "mode": "realtime", "default": 0 },
            { "name": "activeIndex", "key": "quests.active", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "quest.track", "control": "btn.track", "gesture": "click", "payload": { "slot": "Int" } } ],
  "intents": [ { "event": "quest.track", "intent": "quest.setTracked", "args": { "slot": "$payload.slot" },
                 "playerSource": "seat", "actorSource": "none" } ]
}
```

```text
screen.rightCenter ┌──────────────────────────┐
                   │ ☑ 守住北门 2/3 【追踪】    │ 进度=图内聚合；追踪高亮回读
                   │ ☐ 招募 10 兵 0/10【追踪】  │
                   └──────────────────────────┘
```

30 秒预期：点追踪切换引导目标。依赖：G8、G9、#1015。

#### 案 24：panel.progress.tree —— 进度节点树（交互模态）

> 状态：🔴 目标态——拒因：G5（modal.center）+ G8 + G9 + #1015。

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

```text
modal.center（G5）┌───────────────────────────────┐
                  │  ◉──◯──◯                    │ ◉已解锁 ◯可解锁
                  │      └──◉──◯                │
                  └───────────────────────────────┘
```

30 秒预期：点节点看详情，解锁态随图更新。依赖：G5、G8、G9、#1015。

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

#### 案 26：panel.unit.status —— 状态条（纯展示）

> 状态：🟢 今日可装载——纯展示；图内 LoadSelfAttribute 聚合。

```jsonc
{
  "id": "panel.unit.status",
  "graph": "Graph.Unit.Status",               // 图内 LoadSelfAttribute → hp/hpMax/mp
  "pins": [
    { "name": "hp",    "key": "unit.hp",    "mode": "realtime", "default": 100 },
    { "name": "hpMax", "key": "unit.hpMax", "mode": "realtime", "default": 100 },
    { "name": "mp",    "key": "unit.mp",    "mode": "realtime", "default": 0 }
  ]
}
```

```text
世界锚点（单位头顶）┌───────────────┐
                    │ ▓▓▓▓▓░░ 78  │ 血条溢出屏外自动收起（皮读 hp/hpMax 自比）
                    └───────────────┘
```

30 秒预期：单位挨打血条变短。依赖：无。

#### 案 27：panel.abilities —— 技能指令（#1015 主战场）

> 状态：🔴 目标态——#1015 主战场；拒因：G8（$payload）+ #1015（admission 拒绝回执→按钮态）。

```jsonc
{
  "id": "panel.abilities",
  "graph": "Graph.Unit.Abilities",            // 技能冷却/可用态输出
  "pins": [ { "name": "cooldown", "key": "ability.cooldown", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "ability.cast", "control": "bar.abilities", "gesture": "click", "payload": { "slot": "Int" } } ],
  "intents": [ { "event": "ability.cast", "intent": "unit.castAbility", "args": { "slot": "$payload.slot" },
                 "playerSource": "seat", "actorSource": "commandSource.primary" } ]
}
```

```text
screen.bottomCenter ┌────────────────────────────────────┐
                    │ 【⚔】【🛡】【✨】【💥】              │ 冷却=cooldown 回读置灰转圈
                    └────────────────────────────────────┘
```

30 秒预期：点技能释放、冷却转圈、预算拒绝回执置灰。依赖：G8、#1015。

#### 案 28：panel.loadout —— 物品装备（交互）

> 状态：🔴 目标态——拒因：G8（$payload）+ #1015。

```jsonc
{
  "id": "panel.loadout",
  "graph": "Graph.Unit.Loadout",              // 装备槽位+物品属性输出
  "pins": [ { "name": "slotCount", "key": "loadout.slotCount", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "loadout.equip", "control": "grid.loadout", "gesture": "click", "payload": { "slot": "Int" } } ],
  "intents": [ { "event": "loadout.equip", "intent": "unit.equipSlot", "args": { "slot": "$payload.slot" },
                 "playerSource": "seat", "actorSource": "commandSource.primary" } ]
}
```

```text
screen.rightCenter（聚合下方）┌────────────────────────┐
                             │ 武器 圣剑 +12  │ 点击槽位=装备/卸下（携带物清单在图内）
                             │ 防具 板甲 +8   │
                             └────────────────────────┘
```

30 秒预期：点槽位换装备，属性回读更新。依赖：G8、#1015。

### 分组七 · 其他（案 29–31）

#### 案 29：panel.view.filter —— 视图过滤器（交互）

> 状态：🔴 目标态——拒因：G8（$payload）+ G9 + #1015。

```jsonc
{
  "id": "panel.view.filter",
  "graph": "Graph.View.Filter",               // 当前过滤条件回读
  "pins": [ { "name": "filterState", "key": "view.filterState", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "filter.toggle", "control": "bar.filter", "gesture": "click", "payload": { "flag": "Int" } } ],
  "intents": [ { "event": "filter.toggle", "intent": "view.toggleFilter", "args": { "flag": "$payload.flag" },
                 "playerSource": "seat", "actorSource": "none" } ]
}
```

```text
screen.topLeft（tab 下方）┌─────────────────────────────┐
                          │【兵】【建】【资源】【敌方】    │ 高亮=filterState 回读
                          └─────────────────────────────┘
```

30 秒预期：点"敌方"隐藏敌方单位，再点恢复。依赖：G8、G9、#1015。

#### 案 30：panel.extra.text —— 额外文本（纯展示）

> 状态：🟢 今日可装载——纯展示；snapshot 手动刷新（教程阶段切换时作者主动 Refresh）。

```jsonc
{
  "id": "panel.extra.text",
  "graph": "Graph.Extra.Text",                // 任意文本 id 输出；文案查表=图内节点
  "pins": [ { "name": "textId", "key": "extra.textId", "mode": "snapshot", "default": 0 } ]
}
```

```text
screen.bottomLeft ┌─────────────────────────────┐
                  │ 版本 0.9.2 · 教程：按 U 造兵  │ 锚点由实例 op 覆盖（水印/提示通用）
                  └─────────────────────────────┘
```

30 秒预期：教程阶段切换提示文字更新。依赖：无。

#### 案 31：panel.collection.book —— 图鉴背包（交互模态）

> 状态：🔴 目标态——拒因：G5（modal.center）+ G8 + G9 + #1015。

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

```text
modal.center（G5）┌─────────────────────────────────────┐
                  │ 图鉴 12/40  ▦▦▦▦▦▦▦▦▦▦▦▦░░░░░░░░  │ 条目网格=图中收集集合
                  │ 【◀ 上一页】【下一页 ▶】             │
                  └─────────────────────────────────────┘
```

30 秒预期：翻页浏览图鉴，收集进度随获取增长。依赖：G5、G8、G9、#1015。

至此目录 35 类全部立案（前四案 + 本批 31 案），逐组过核完成，目录行可作为 #841 种子行与 #840 对照物。

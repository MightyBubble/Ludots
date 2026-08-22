# 面板典型案例全设计——四个样板间的完整设计案

本页是 [面板目录设计](panel-catalog-designs.md) 的第一批**全设计案**：从分组一挑选四个最典型 case（覆盖纯展示、交互全链、模态浮层、零变量命令四种形态，踩中 G3/G5/G6 全部缺口），每案含：玩家旅程、完整配置、线框、数据流、事件-意图链、显隐编排、验收断言、依赖与边界。本批同时作为后续 31 类的设计范式模板。

**状态分级契约（审稿产物）**：每案头部标装载状态——🟢 今日可装载（过 PanelTemplateLoader 即真）/ 🔴 目标态（依赖缺口，**今日装载会被拒**，是验收目标不是可抄配置）。判断依据只有一条：拿配置去碰 `PanelTemplateLoader` 的字段白名单与校验规则。目标态被拒的原因在案内逐条点名（哪个字段、哪条校验），缺口编号进 §1.5。

**通用约定**（不再每案重复）：实例参数四级链（op > 模板 > game.json > default）；皮与主题正交（`panelSkin` × `panelTheme`），本页线框均为内容语义，视觉由主题决定；所有 JSON fail-closed——未知字段/未知读嘴/未知事件手势装载期即抛。

---

## 案 A：玩家信息聚合 `panel.player.aggregate` —— 纯展示 · global scope · 活状态中转

> 🔴 **目标态**：`scope` 字段今日不在装载器白名单（G3）。落地前本配置装载即拒——拒因：unknown field 'scope'。

### A1 玩家旅程
开局即见顶栏左角资源条；造一队兵花掉 200 金——金币数字掉、人口 8→10；再花光时人口变红提示超限。全程零交互，纯被动读取。

### A2 完整配置

```jsonc
{
  "id": "panel.player.aggregate",
  "scope": "global",                          // G3：无 scope 实体，地图级唯一实例
  "variables": [
    { "name": "gold", "kind": "Int", "realtime": true,
      "source": { "sourceKind": "GraphOutput", "graphOutputKey": "economy.gold" } },
    { "name": "popUsed", "kind": "Int", "realtime": true,
      "source": { "sourceKind": "GraphOutput", "graphOutputKey": "pop.used" } },
    { "name": "popCap", "kind": "Int", "realtime": true,
      "source": { "sourceKind": "GraphOutput", "graphOutputKey": "pop.cap" } }
  ],
  "binds": [
    { "control": "lbl.gold", "variable": "gold" },
    { "control": "lbl.popUsed", "variable": "popUsed" },
    { "control": "lbl.popCap", "variable": "popCap" }
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
                 PanelProjectionReader(GraphOutput 读嘴, realtime 帧扫)
                             ▼
                 PanelVariableSet{gold,popUsed,popCap} ──binds──> 控件 lbl.*
```

要点：面板**不直读**地图变量/实体——一切活状态经图输出中转（完成核"数字溯源到图"）；图心跳拍重算，面板帧扫取值，两级节流天然存在。

### A5 显隐编排
常驻面板：地图装载图（TriggerGraph）`CreatePanel(panelAnchor: "screen.topLeft")` 后**不调** `ShowPanel` 之外的任何显隐逻辑——开局默认隐藏由激活商店语义决定（`IsVisible` 缺省 false），装载图随即 `ShowPanel("panel.player.aggregate")` 常亮。整局无再隐需求。

### A6 验收（Gherkin）

```gherkin
Scenario: 资源条随经济同帧刷新
  Given Full-HUD 验收场启动且 panel.player.aggregate 可见
  When 玩家花费 200 金生产一队兵
  Then gold 变量减少 200 且 popUsed 增加，与图输出 economy.gold 同帧

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

> 🔴 **目标态**：拒因=① `scope`（G3）② intents 字段名用 `event:` 非 `eventId:`（本文已按真实 loader 修正）③ args 常量语义（{"speed":"3"}）未定义——引用 `$payload.x` 与字面常量的区分规则属 G8；④ 手势载荷值来源（按钮怎么把自己的挡位塞进事件）同属 G8。

### B1 玩家旅程
游戏进行中，玩家点【3x】→ 游戏明显加速，【3x】按钮进入高亮态；再点【⏸】→ 游戏停，暂停键高亮。全程面板不知道"游戏速度"是什么——它只发事件、收回读。

### B2 完整配置

```jsonc
{
  "id": "panel.time.control",
  "scope": "global",
  "variables": [
    { "name": "speed", "kind": "Int", "realtime": true,          // 回读：当前速度挡
      "source": { "sourceKind": "GraphOutput", "graphOutputKey": "clock.speed" } }
  ],
  "binds": [
    { "control": "lbl.speed", "variable": "speed" }               // 高亮态由皮按 speed 值决定
  ],
  "events": [                                                     // 四挡四事件（装载器拒重复 eventId）
    { "eventId": "speed.set.pause", "control": "btn.pause", "gesture": "click" },
    { "eventId": "speed.set.1", "control": "btn.speed1", "gesture": "click" },
    { "eventId": "speed.set.2", "control": "btn.speed2", "gesture": "click" },
    { "eventId": "speed.set.3", "control": "btn.speed3", "gesture": "click" }
  ],
  "intents": [
    { "event": "speed.set.pause", "intent": "game.setSpeed", "args": { "speed": "0" }, "playerSource": "seat", "actorSource": "commandSource.primary" },
    { "event": "speed.set.1", "intent": "game.setSpeed", "args": { "speed": "1" }, "playerSource": "seat", "actorSource": "commandSource.primary" },
    { "event": "speed.set.2", "intent": "game.setSpeed", "args": { "speed": "2" }, "playerSource": "seat", "actorSource": "commandSource.primary" },
    { "event": "speed.set.3", "intent": "game.setSpeed", "args": { "speed": "3" }, "playerSource": "seat", "actorSource": "commandSource.primary" }
  ]
  // 装载器 intents 的事件字段名是 event:（非 eventId:）；四挡四独立事件各自映射，args 字面常量定挡位（语义规则 G8）
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
  Given 游戏以 1x 运行且面板可见
  When 玩家点击【3x】
  Then game.setSpeed 意图以 args{speed:3} 入队并被 admission 放行
  And clock.speed 输出变为 3 且【3x】进入高亮态

Scenario: 拒绝时状态不漂移
  Given admission 预算为 0
  When 玩家点击【2x】
  Then 意图被拒、clock.speed 不变、无按钮状态变化

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

> 🔴 **目标态**：拒因=① `scope`（G3）② `actorSource:"none"` 值域（G9，解析器现拒）③ `gesture:"change"` 连续手势不在现有手势表（G8）④ C5 的"图消费 UI 事件→调 ShowPanel"整条接线不存在——触发器事件词典无 UI 域（G10，本页初稿"#1014 现有"系高估，审稿纠正）；⑤ modal.center 锚点（G5）。

### C1 玩家旅程
玩家点右上角常驻【⚙】→ 模态浮层弹出（其余输入挂起）；拖音量滑条 → 音量实时变化；点【✕】或浮层外 → 关闭，输入恢复。点【退出到主菜单】→ 二次确认后退出。

### C2 完整配置

```jsonc
{
  "id": "panel.settings",
  "scope": "global",
  "variables": [
    { "name": "volume", "kind": "Float", "realtime": true,
      "source": { "sourceKind": "GraphOutput", "graphOutputKey": "settings.volume" } }
  ],
  "binds": [
    { "control": "slider.volume", "variable": "volume" }          // 滑条位置=volume 回读
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
      "args": {}, "playerSource": "seat", "actorSource": "none" }  // actorSource none → G9
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
  Given panel.settings 已创建且未显示
  When ⚙ 入口事件出现（G10 接线后）
  Then 编排图调 ShowPanel 且输入焦点被浮层捕获
  When 玩家点击【✕】
  Then 编排图调 HidePanel 且焦点释放

Scenario: 连续意图合流
  Given 浮层打开且音量滑条存在
  When 玩家连续拖动产生 10 个 change 事件
  Then 仅一条 settings.setVolume 意图携带末值入队

Scenario: 模态锚点未落地即拒
  Given G5 未实现且配置使用 modal.center
  When 装载
  Then 装载失败，不静默降级为角落面板
```

30 秒人验：⚙ 开浮层→拖滑条声音变→✕ 关闭恢复。

### C7 依赖与边界
依赖：G5（模态锚点，#840 前置）、G8（change 手势+载荷值）、G9（actorSource none）、G10（开/关编排接线）、#1015。不做：热键 Esc 关闭（输入域）；设置持久化（存档域，仅回读已存值）。

---

## 案 D：全局指令 `panel.command.global` —— 零变量纯命令 · G6 缺口样板

> 🔴 **目标态**：拒因=① `scope`（G3）② `variables:[]`（G6）③ `actorSource:"none"`（G9）。D5 的模式互斥编排同样依赖 G10。

### D1 玩家旅程
玩家框选一队兵后点底栏【集结】→ 进入指定目标模式，光标变化；点【全选】→ 场上己方作战单位全亮。按钮按下即命令，面板自身无任何状态显示。

### D2 完整配置

```jsonc
{
  "id": "panel.command.global",
  "scope": "global",
  "variables": [],                        // G6：现行装载器要求 ≥1 变量，纯命令面板需放开为 0
  "binds": [],
  "events": [
    { "eventId": "army.selectAll", "control": "btn.selectAll", "gesture": "click" },
    { "eventId": "army.rally", "control": "btn.rally", "gesture": "click" },
    { "eventId": "army.stop", "control": "btn.stop", "gesture": "click" },
    { "eventId": "army.retreat", "control": "btn.retreat", "gesture": "click" }
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
  Then 装载通过且无假变量被塞入

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

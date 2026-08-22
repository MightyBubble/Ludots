# 面板目录设计——配置形状总合同与分组线框

本页是 PANEL-6（#841 目录即数据）与 PANEL-7（#840 验收场）的**前置设计 SSOT**：先给全部面板类型定死配置形状与线框，再逐组登记、逐组过核。每组过完即锁组；本页产物直接成为 #841 的种子行与 #840 的对照物。

## 用法

1. 每个面板类型 = **一条目录行 + 一份配置草案 + 一张线框 + 一句 30 秒预期**；
2. 设计发现的基建缺口回填对应子单（票号写在依赖栏），不在本页开实现；
3. 归类修正（某行其实不是框、是机制或全屏效果）发生在本页，目录行数允许微调。

## 一、共同骨架（总合同）

### 1.1 一个面板类型的全部组成

```text
面板类型 = assets/Panels/panel_templates.json 里的一个条目（下述模板骨架）
         + PanelThemes 目录（#841）里的一行（登记与完成核字段）
         + 本页的一条线框与 30 秒预期
实例参数 = 图 op（CreatePanel: panelType/panelAnchor/panelSkin/panelZOrder）
         > 模板 skin 字段 > game.json panelSkin/panelTheme > default
```

### 1.2 模板骨架（真实 schema 的全字段注释版，非发明）

```jsonc
{
  "id": "panel.<域>.<名>",            // 顶点前缀 panel.，点分命名空间
  "skin": "markup",                    // 可选：本模板默认皮（实例 op 可覆盖）
  "scope": "item",                     // item | collection | global —— 行为侧（§1.4）
  "variables": [                       // 至少一条；kind 目前 Float|Int（缺口 G1: Bool|String）
    { "name": "gold", "kind": "Int", "realtime": true,
      "source": {                      // 五路读嘴，fail-closed：
        "sourceKind": "TableLookup",   //   六路读嘴（真实枚举名，fail-closed）：
                                       //   SingleAttribute / DerivedAttribute / AggregateProjection /
                                       //   GraphOutput / TableLookup / AttributeBase
        "lookupTable": "economy",      //   GraphOutput / TableLookup
        "lookupField": "goldPerTick",
        "keyAttribute": "TeamId" } } ],
  "binds": [                           // 控件 ↔ 变量（皮侧按 control id 寻址）
    { "control": "lbl.gold", "variable": "gold" } ],
  "events": [                          // 0 编码事件（#1013 已落地）
    { "eventId": "speed.set", "control": "btn.speed2", "gesture": "click",
      "payload": { "speed": "Int" } } ],   // 载荷类型已有 String|Int|Float|Bool
  "intents": [                         // 事件 → 玩法意图（面板永不直构 Order）
    { "eventId": "speed.set", "intent": "game.setSpeed",
      "args": { "speed": "$payload.speed" },
      "playerSource": "seat", "actorSource": "commandSource.primary" } ]
}
```

### 1.2.1 realtime 判定规则（总合同条款）

`realtime: true` = 值本身会动，帧扫重算（`PanelRealtimeRefreshSystem`）。按读嘴定死：

| 读嘴 | realtime | 理由 |
|---|---|---|
| SingleAttribute（活属性：血/蓝/资源） | ✅ | 战斗中真变 |
| GraphOutput（活输出：时钟/经济聚合/未读数） | ✅ | 图每拍重算 |
| AggregateProjection（聚合投影） | ✅ | 随集合变化——#1012 集合面板的现成地基 |
| AttributeBase | ❌（buff 改基数时显式 Refresh） | 基础值通常静止 |
| TableLookup | ❌ | 查的是启动装载的静态表，结果不动；key 动态换行属例外，走显式 Refresh，不帧扫 |
| DerivedAttribute | 随派生源 | 派生图绑定写入 AttributeBuffer |

配套建模纪律：**活游戏状态（gold/人口/时钟）一律 GraphOutput 中转**——查表只放静态数据（税率/周期/文案表）。这同时满足完成核"数字溯源到图"。地图变量（MapVariableStore）不在五路读嘴内：全局状态经图输出中转是刻意设计；若实践中"每项全局状态开一张图"爆炸，再议第六读嘴（G7 候选，暂不立项）。

### 1.3 实例参数（四级链，已落地）

`panelAnchor`（screen.topLeft/topRight/bottomLeft/bottomRight）· `panelSkin`（default/markup/compose/reactive/web）· `panelZOrder`（缺省 100）· `panelTheme`（game.json 全局；模板/op 级为缺口 G4）。

### 1.4 行为侧骨架（设计位；#1012 落地）

```jsonc
"scope": "item"  |  "collection"  |  "global"
// ⚠ 设计位字段：装载器 RootFields 白名单今日无 "scope"——出现即抛。G3 落地时增补，
//   落地前案例文档凡用此字段均标 🔴 目标态，不得当作今日可装载配置抄写。
// item:       一物一面（现状：scope=hero 实体）
// collection: 一集一面——框选集合变化时同一模板切形态，模板 id 不变，
//             变量读嘴允许集合聚合（缺口 G2: 聚合读嘴 sourceKind，如 AggSum 队列项）
// global:     全局面板（无 scope 实体；地图变量/查表为源）——缺口 G3: scope=global 的
//             实例语义（当前 Instantiate 要求 scope 实体）
```

### 1.5 缺口登记（设计即排期）

| # | 缺口 | 归属 |
|---|---|---|
| G1 | 变量 kind 扩展 Bool/String（载荷已有，变量侧缺） | #1010 尾巴 |
| G2 | 集合聚合读嘴（AggSum/Avg/Count 队列项） | #1012 |
| G3 | scope=global 实例语义 | #1012 |
| G4 | panelTheme 模板/op 级覆盖 | #1011 后续 |
| G5 | 浮层/模态锚点（设置、子系统详情） | #840 前置小片 |
| G8 | 手势载荷值来源（按钮→事件携带定值的机制）与 intent args 常量语义（"$payload.x" 引用与字面常量混排） | #1015 |
| G9 | actorSource 值域扩展（"none"——设置类无实体归因意图；解析器现拒） | #1015 |
| G10 | TriggerGraph 消费 UI 事件的接线（编排层入口：UI 事件→图触发；当前触发器事件词典无 UI 域） | #1013/#1030 交界 |

---

## 二、分组一：全局 HUD（9 类）

约定：线框用锚点+尺寸语义描述；`【】`标交互控件（事件源）。

### 2.1 玩家信息聚合 `panel.player.aggregate`

```jsonc
{ "id": "panel.player.aggregate", "scope": "global",
  "variables": [
    { "name": "gold", "kind": "Int", "realtime": true, "source": { "sourceKind": "GraphOutput", "graphOutputKey": "economy.gold" } },
    { "name": "popUsed", "kind": "Int", "realtime": true, "source": { "sourceKind": "GraphOutput", "graphOutputKey": "pop.used" } },
    { "name": "popCap", "kind": "Int", "realtime": true, "source": { "sourceKind": "GraphOutput", "graphOutputKey": "pop.cap" } } ],
  "binds": [ { "control": "lbl.gold", "variable": "gold" }, { "control": "lbl.pop", "variable": "popUsed" } ] }
// 注：gold/pop 是活状态 → GraphOutput 中转（§1.2.1 纪律）；economy 查表放静态（如
// 每级人口上限 popCapByLevel），面板侧不标 realtime。初稿曾把活状态建模成查表+
// realtime——用户审稿揪出的反例，规则因此入合同。
```

```text
screen.topLeft ┌────────────────────────────┐
               │ ⛁ 1,240   👥 8/20   ⚡ 65 │
               └────────────────────────────┘
```
30 秒预期：顶栏左角资源条；花 200 金造兵 → 金数字掉、人口 +1，同帧刷新。
依赖：G3（global scope）。现有读嘴即可，G1/G2 不需要。

### 2.2 时间流逝 `panel.time.elapsed`

```jsonc
{ "id": "panel.time.elapsed", "scope": "global",
  "variables": [
    { "name": "elapsedMin", "kind": "Float", "realtime": true, "source": { "sourceKind": "GraphOutput", "graphOutputKey": "clock.elapsedMin" } },
    { "name": "dayPhase", "kind": "Int", "realtime": true, "source": { "sourceKind": "GraphOutput", "graphOutputKey": "clock.dayPhase" } } ],
  "binds": [ { "control": "lbl.clock", "variable": "elapsedMin" } ] }
```

```text
screen.topRight 区、信息聚合左侧 ┌──────────┐
                                 │ ☀ 12:34  │   （dayPhase 驱动日夜图标换肤）
                                 └──────────┘
```
30 秒预期：表走字、昼夜图标随 dayPhase 切换。
依赖：G3；无交互。

### 2.3 日期 `panel.date.cycle`

```jsonc
{ "id": "panel.date.cycle", "scope": "global",
  "variables": [
    { "name": "dayIndex", "kind": "Int", "realtime": true, "source": { "sourceKind": "GraphOutput", "graphOutputKey": "clock.dayIndex" } } ],
  "binds": [ { "control": "lbl.date", "variable": "dayIndex" } ] }
```

```text
紧贴时间条右侧 ┌────────────────┐
               │ 第 3 年 · 春 · 7 │   （年/季由 dayIndex 查表）
               └────────────────┘
```
30 秒预期：过夜后日期 +1，季节图标换。
依赖：G3；TableLookup 周期表（现有读嘴）。

### 2.4 时间控制 `panel.time.control`

```jsonc
{ "id": "panel.time.control", "scope": "global",
  "variables": [
    { "name": "speed", "kind": "Int", "realtime": true, "source": { "sourceKind": "GraphOutput", "graphOutputKey": "clock.speed" } } ],
  "binds": [ { "control": "lbl.speed", "variable": "speed" } ],
  "events": [
    { "eventId": "speed.set", "control": "btn.speed", "gesture": "click", "payload": { "speed": "Int" } } ],
  "intents": [
    { "eventId": "speed.set", "intent": "game.setSpeed",
      "args": { "speed": "$payload.speed" }, "playerSource": "seat", "actorSource": "commandSource.primary" } ] }
```

```text
时间条右侧 ┌───────────────────┐
           │ 【⏸】【1x】【2x】【3x】│   ← 点击切挡，当前挡高亮（speed 变量回读）
           └───────────────────┘
```
30 秒预期：点 3x 游戏明显加速，按钮高亮跟随。
依赖：G3 + **#1015 交互回调**（事件/意图声明已落地，回调链路待建）。

### 2.5 全局指令 `panel.command.global`

```jsonc
{ "id": "panel.command.global", "scope": "global",
  "variables": [],
  "events": [
    { "eventId": "army.selectAll", "control": "btn.selectAll", "gesture": "click" },
    { "eventId": "army.rally", "control": "btn.rally", "gesture": "click" } ],
  "intents": [
    { "eventId": "army.selectAll", "intent": "selection.all", "args": {}, "playerSource": "seat", "actorSource": "commandSource.primary" },
    { "eventId": "army.rally", "intent": "army.setRally", "args": {}, "playerSource": "seat", "actorSource": "commandSource.primary" } ] }
```
注：variables 至少一条的现行约束（模板骨架）对纯命令面板需放开为 0 条（记 **G6**）。
```text
底栏中央 ┌──────────────────────────────┐
         │ 【全选】【集结】【停止】【撤退】 │
         └──────────────────────────────┘
```
30 秒预期：点全选 → 场上己方单位全亮；点集结 → 进入指定目标状态。
依赖：G6 + #1015。

### 2.6 全局功能 tab `panel.tabs.global`

```jsonc
{ "id": "panel.tabs.global", "scope": "global",
  "variables": [
    { "name": "activeTab", "kind": "Int", "realtime": true, "source": { "sourceKind": "GraphOutput", "graphOutputKey": "ui.activeTab" } } ],
  "events": [
    { "eventId": "tab.switch", "control": "tab.bar", "gesture": "click", "payload": { "tab": "Int" } } ],
  "intents": [
    { "eventId": "tab.switch", "intent": "ui.switchTab", "args": { "tab": "$payload.tab" }, "playerSource": "seat", "actorSource": "none" } ] }
```

```text
左上（信息聚合下方）┌──────────────────────────┐
                    │【信息】【科技】【外交】【生产】│  ← activeTab 高亮
                    └──────────────────────────┘
                     点击切换右侧主区域内容（子系统面板路由）
```
30 秒预期：点科技 → 右侧内容区切成科技面板，再点外交切走。
依赖：G3 + #1015 + 子系统面板本体（2.9）。

### 2.7 全局信息横幅 `panel.info.banner`

```jsonc
{ "id": "panel.info.banner", "scope": "global",
  "variables": [
    { "name": "bannerText", "kind": "Int", "realtime": true, "source": { "sourceKind": "GraphOutput", "graphOutputKey": "banner.current" } },   // G1: 应为 String（查表文案 id 兜底）
    { "name": "bannerLevel", "kind": "Int", "realtime": true, "source": { "sourceKind": "GraphOutput", "graphOutputKey": "banner.level" } } ],
  "binds": [ { "control": "lbl.banner", "variable": "bannerText" } ] }
```

```text
screen.topCenter ┌────────────────────────────────┐
                 │ ⚠ 敌军逼近北门（level=2 红底）  │   显隐由图：HidePanel 于平静、
                 └────────────────────────────────┘   ShowPanel 于事件（#1014 现有）
```
30 秒预期：敌军进区 → 横幅弹出红字；威胁解除 → 消失。
依赖：G3；G1（String 变量）让文案直读，否则查表 id。显隐图 op 现有。

### 2.8 设置 `panel.settings`

```jsonc
{ "id": "panel.settings", "scope": "global",
  "variables": [
    { "name": "volume", "kind": "Float", "realtime": true, "source": { "sourceKind": "GraphOutput", "graphOutputKey": "settings.volume" } } ],
  "events": [
    { "eventId": "settings.volume", "control": "slider.volume", "gesture": "change", "payload": { "value": "Float" } },
    { "eventId": "settings.exit", "control": "btn.exit", "gesture": "click" } ],
  "intents": [
    { "eventId": "settings.volume", "intent": "settings.setVolume", "args": { "value": "$payload.value" }, "playerSource": "seat", "actorSource": "none" },
    { "eventId": "settings.exit", "intent": "game.exitToMenu", "args": {}, "playerSource": "seat", "actorSource": "none" } ] }
```

```text
模态浮层（G5 锚点） ┌──────────────────────┐
                    │ 设置            【✕】 │
                    │ 音量 ▁▁▂▃▅▆▇ 【滑条】│
                    │ 【退出到主菜单】       │
                    └──────────────────────┘
                     入口齿轮常驻右上；点开模态，其余输入挂起
```
30 秒预期：齿轮点开浮层拖音量即生效；点外部或 ✕ 关闭。
依赖：G5（模态锚点）+ #1015（gesture=change 的滑条输入）。

### 2.9 子系统入口 `panel.subsystem.entries`

```jsonc
{ "id": "panel.subsystem.entries", "scope": "global",
  "variables": [
    { "name": "techUnread", "kind": "Int", "realtime": true, "source": { "sourceKind": "GraphOutput", "graphOutputKey": "tech.unread" } } ],
  "events": [
    { "eventId": "sub.open", "control": "bar.subsystems", "gesture": "click", "payload": { "sub": "Int" } } ],
  "intents": [
    { "eventId": "sub.open", "intent": "ui.openSubsystem", "args": { "sub": "$payload.sub" }, "playerSource": "seat", "actorSource": "none" } ] }
```

```text
右下角竖条 ┌───┐
           │【🔬3】│ ← 角标=techUnread 未读数（超 9 显示 9+）
           │【🛠】 │
           │【📜】 │
           └───┘   点击 = 路由打开对应子系统面板（与 2.6 tab 联动）
```
30 秒预期：科技完成 → 角标 +1 闪动；点开科技面板角标清零。
依赖：G3 + #1015；与路由机制（原 #29）联动。

---

## 三、分组二~七（待逐组过）

| 组 | 类数 | 状态 |
|---|---|---|
| 二 地图/空间指示（小地图/关系图/选中/屏外/染色/区域/路网） | 7 | 待设计 |
| 三 选择与实体（列表/信息/关系/集合聚合/路由/关联集） | 6 | 待设计（含 #1012 集合行为主战场） |
| 四 信息流（事件面板/日志入口/日志） | 3 | 待设计 |
| 五 编队生产任务（编队/任务/进度树/生产队列） | 4 | 待设计（生产队列= #1012 验收场景） |
| 六 单位操作（状态条/技能指令/物品装备） | 3 | 待设计（技能条=#1015 主战场） |
| 七 其他（视图过滤器/额外文本/图鉴背包） | 3 | 待设计 |

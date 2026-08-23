## 案 A：玩家信息聚合 `panel.player.aggregate` —— 纯展示 · global scope · 活状态中转

> ⛔ **装载即拒**：`scope: "global"` 不在装载器白名单（G3）——unknown field 'scope'。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "stat", "items": ["⛁ 1,240", "👥 8/20"]}
```

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

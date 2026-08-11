# UI 面板作者形态：四种表面共用「变量 → 计算图 → 投影」

## 1. 概述

作者第一性原则：

1. **像 Shader Graph**：一张计算图画布，右边一个带**多类型引脚**的 Panel 汇入（你原型里的 PanelNode）  
2. **引脚 = 面板变量**；文案 `{hp}` 引用引脚名；多种 Float/Text/… 可在同一张图里汇入  
3. **Compose / Markup / Reactive / Ludots Web UI** 只是四种**表面投影语言**，共享同一套引脚与图  
4. **编辑器糖 ≠ 新 VM**：Panel 汇入节点落盘为 `outputs[]` / bindings，**不是** `GraphNodeOp.Panel` / `GraphKind.Presentation`

本页是作者形态的文档 SSOT；交互样板在编辑器路由 `/ui-panel-authoring`（`Ludots.Editor.React`）。计划 Epic：[#858](https://github.com/MightyBubble/Ludots/issues/858)。

## 2. 结构

```text
Template
  variables[]          ← 作者声明：hp / oreTotal / …
  surfaceKind          ← Compose | Markup | Reactive | WebUI
  defaultGraphId       ← 计算图引用
        │
        ▼
DataGraph (L1 Query/Derived)
  steps… → outputs[]   ← output.id / key 对齐 variable.id
        │
        ▼
Binding
  variableId → sourceKind + ref
  sourceKind ∈ singleAttribute | derivedAttribute | aggregateProjection | graphOutput
        │
        ▼
Surface projection（四种原生形态，见 §3）
        │
        ▼
Instance = templateId + scope（多开；Router 另册）
```

| 层 | 作者看见什么 | 不是什么 |
|----|--------------|----------|
| Panel 汇入（多引脚） | Shader 式输出节点；引脚=变量 | 运行时 Graph 操作码 |
| DataGraph | 一张图多分支、多类型出口 | DOM / 每帧 UI 循环 |
| Binding / outputs[] | 引脚 ← 图节点 / Summary key | 手写跨实体求和 |
| Surface | 四种语言各自的原生写法 | 第二套绑定 DSL |

### 和「PanelNode 多引脚」原型的对齐

```text
编辑器（你期待的样子）
  [选中集合]→[取实体]┬→[读血量]──→ Panel.hp (Float)
                    ├→[读击杀]──→ Panel.lastKill (Text)
                    └→[读状态]──→ Panel.curState (Text)

落盘 / 运行（合同）
  outputs: [
    { id: "hp", key: "panel.entity_info.hp", … },
    { id: "lastKill", … },
    { id: "curState", … }
  ]
  → GraphOutputValueStore / Attribute
  → Reactive TState 或 WPK fields[] 等母语投影
```

作者始终面对**一张图 + 一个多引脚 Panel**；引擎不增加 Presentation Graph 宇宙。

## 3. 详情

### 3.1 变量（四种表面共用）

```json
{
  "variableId": "hp",
  "valueKind": "Float",
  "label": "血量"
}
```

模板文案里的 `{hp}` **只引用 `variableId`**，不引用图节点 id。

### 3.2 计算图

- Kind：`Query`（跨实体/集合摘要）或 `Derived`（单实体派生）  
- 终点：`outputs[]`，`key` / `id` 与 `variableId` 对齐（或 Binding 显式映射）  
- 编辑器中心画布展示步骤链；**汇入条是「变量槽」**，不是 UI 控件树

### 3.3 Binding

| sourceKind | 适用 | 读口 |
|------------|------|------|
| `singleAttribute` | 单实体属性 | `AttributeBuffer` |
| `derivedAttribute` | 单实体派生 | `AttributeBuffer` 派生槽 |
| `aggregateProjection` / `graphOutput` | 图投影 | `GraphOutputValueStore` Summary key |

缺 key / 缺属性：**失败关闭**，禁止静默 0。

### 3.4 四种表面的原生投影形态

同一套 `variables[]` + Binding，投影如下（编辑器右侧实时对照）：

| 表面 | 原生变量落点 | 原生读数方式 |
|------|--------------|--------------|
| **Reactive** | `ReactivePage<TState>` 的 `TState` 字段 | 投影写入 `TState` → `Ui.Text(state.Hp)` |
| **Compose** | 控制器字段 + 重建 | 同左，无细粒度订阅 |
| **Markup** | code-behind 字段；HTML 只做布局与 `ui-click` | 引擎**无** `{{var}}` 绑定语法；展示值由 code-behind 填入或整页替换 |
| **Web UI** | WPK descriptor `fields[]` | `sourceKind` + `attributeId` / `graphOutputKey` → DataPlane topic |

要点：**不要为了「统一」再发明第五种绑定语言。** 统一的是变量表与图；表面各自保持母语。

### 3.5 与假 PanelNode 的边界

| 允许 | 禁止 |
|------|------|
| 画布右侧「变量槽」汇入图输出 | `GraphNodeOp.Panel` / Presentation Kind |
| 模板预览显示 `{hp}` | 在图指令里拼 DOM 文案 |
| WebUI field 对齐 variableId | showcase 内遍历实体求和 |

## 4. 场景

1. **实体信息卡**：变量 `hp` / `lastKill` / `curState`；选中实体后 — `LoadAttribute`(血量)、`ReadBlackboard`(上次击杀)、`ReadGameplayTag`→`LookupTagDisplayText`(状态文案)；Reactive `TState` 三字段同构。  
   > Tag 快捷 L0（`SelectTagInMask` / `LookupTagDisplayToken`）见运行时线 #868；编辑器仍是作者糖。仍欠：Text BB、表资产装载、表面 token→文案。禁止 Attribute 假冒。  
2. **资源总览条**：变量 `oreTotal` / `crystalTotal`；Query 聚合 → Summary；WebUI `aggregateProjection`。  
3. **切换表面**：同一模板把 `surfaceKind` 从 Reactive 换成 WebUI，变量与图不变，仅右侧投影形态变。  
4. **试玩 / 配置**：编辑器工作区含「试玩」（玩家情景）与「配置」（导出 `ludots.ui.panel_template/v1` JSON，样例见 `Ludots.Editor.React/public/samples/panel_templates.json`）。

## 5. 边界

- 本页定义**作者形态**；Template/Instance/Router **合同**见 [`ui-panel-template-instance-router.md`](ui-panel-template-instance-router.md)（ADR [#880](https://github.com/MightyBubble/Ludots/issues/880)）；运行时落地见 #858 后续切片 
- Markup 不扩展为表达式语言；需要绑定走 code-behind 或换 Reactive/WebUI  
- 图编辑真机仍走 GAS 图工具链；本编辑器中心画布是作者形态样板，可与真编译器对接但不得平行 VM

## 6. UAT

```gherkin
Feature: 四种表面共用面板变量与计算图
  作为面板作者
  我想先声明变量再用图算满它们
  并在 Compose / Markup / Reactive / WebUI 间切换表面而不重做算数

  Scenario: 变量声明独立于表面语言
    Given 模板声明了变量 hp
    When 我将表面从 Reactive 切换为 WebUI
    Then 变量表仍只含 hp
    And 计算图输出映射不必重写

  Scenario: 图终点是变量槽而不是 Panel 节点
    Given 一张为该模板供数的 Query 图
    When 我在作者画布查看终点
    Then 我看到的是变量槽 hp
    And 看不到 GraphKind.Presentation 或 PanelNode 操作码

  Scenario: 各表面投影保持母语
    Given 同一变量 oreTotal 已绑定 graphOutputKey
    When 我查看 Reactive 投影
    Then 我看到 TState 字段 OreTotal
    When 我查看 WebUI 投影
    Then 我看到 fields[].sourceKind 为 aggregateProjection
    And Markup 投影不假装存在引擎级 {{oreTotal}} 绑定

  Scenario: 试玩里玩家看到信息卡随情景变化
    Given 我打开实体信息卡模板的试玩工作区
    When 我点「刚击杀敌军」
    Then 信息卡显示上一次击杀对象与更新后的血量
    And 旁白说明数字来自变量表而非界面自编

  Scenario: 导出作者配置可给运行时消费
    Given 我打开配置工作区
    When 我下载当前模板 JSON
    Then 文件含 schema ludots.ui.panel_template/v1
    And 含 variables / bindings / outputs / surfaceKind
    And 不含 GraphNodeOp.Panel
```

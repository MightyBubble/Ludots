# UIP-0：Template / Instance / Router 合同

> **ADR 正本挂载点**：GitHub issue [#880](https://github.com/MightyBubble/Ludots/issues/880)（Epic [#858](https://github.com/MightyBubble/Ludots/issues/858)；落地后债地图 [#886](https://github.com/MightyBubble/Ludots/issues/886)）。  
> 本页是仓库可读锚点；**勿**在 `docs/adr/` 另堆平行 AAC 式文件。若 #880 正文尚未粘贴，以本页与 issue 对账后以 issue 为计划 SSOT。  
> 作者形态（四种表面 / 变量引脚）见 [`ui-panel-authoring-form.md`](ui-panel-authoring-form.md)。静态讨论原型：`docs/prototypes/ui-panel-template-instance-prototype.html`。

## 1. 概述

为通用 UI 面板建立 **Template（模板）/ Instance（实例）/ Router（上下文路由）** 合同，使后续实现按同一挂载真相落地，禁止 showcase 私写第二套「挂面板」规则。

产品一句话：面板先有**可复用的模板**，再按**选中上下文**挂出若干**实例**；数字一律走现有 L1 计算图投影读口，不在界面里私算。

硬约束：

- 复用 [#848](https://github.com/MightyBubble/Ludots/issues/848) 图分层：L0 同一 VM；L1 用 `Query` / `Derived`；**禁止** `GraphKind.Presentation`、`GraphNodeOp.Panel`、第二套 Graph VM。
- 表面（Compose / Reactive / Markup / WebUI / Performer）只投影，不算合计。
- 本 ADR **只写合同**，不实现 Template 运行时、不写通用查表、不写 TagDisplay、不写热重载。

## 2. 结构

```text
玩法真相（Entity / Attribute / Tag / Collection / GAS）
        │
        ▼
L1 计算图投影（Query / Derived → AttributeBuffer / GraphOutputValueStore）
        │
        ▼
统一读口（PanelProjection / 世界表面黑板）
        │
        ▼
Template + Instance + Router
        │
        ▼
Surface（Compose / Reactive / Markup / WebUI / Performer）
```

| 层 | 职责 | 非职责 |
|----|------|--------|
| Template | 布局/样式/变量与绑定口/默认 graph 引用；schema `ludots.ui.panel_template/v1` | 不持实体、不算合计、不决定「此刻挂几份」 |
| Instance | `templateId + scope`、生命周期、可多开；`binds[]` 只声明读口 | 不写玩法真相、不在实例内遍历实体求和 |
| Router | 选中上下文 → 挂载/卸载哪些实例 | 不手写每面板业务公式、不做 DOM |
| DataGraph | L1 Query/Derived | 不做 DOM、不用 Script+Yield 当 UI 帧循环 |
| Surface | 把已投影变量画出来；点击发正式指令 | 不私建跨实体合计；不算图双向写回 |

Router 本身**不是**可见面板（看板第 29 项是机制），而是「当前上下文 → 实例挂载表」的单一决策点。

## 3. 详情

### 3.1 Template

- **templateId** 稳定、全局唯一，例如 `panel.player_aggregate`、`panel.production_queue`、`panel.minimap`。
- 声明：`variables[]`、`bindings` / `binds[]`、`surfaceKind`、`defaultGraphId`、可选布局/文案模板（如 `{oreTotal}` 只引用 `variableId`）。
- Mod 可覆盖同名模板的 `defaultGraphId`；缺图 **失败即炸**。
- 编辑器「Panel 多引脚汇入」落盘为 `outputs[]` / bindings，**不是**运行时 `GraphNodeOp.Panel`。

### 3.2 Instance

- **默认 instance id**：`templateId + scopeKey`（实现可用稳定拼接，如 `panel.production_queue#building:7`）。
- **singleton: true**：全局只允许一份（典型：小地图）；id 仍绑定固定 scope（通常 `global`），Router 不得因选中变化再挂第二份。
- **scope 模型**（至少覆盖；字面量合同）：

  | scope 形态 | 含义 | 典型面板 |
  |------------|------|----------|
  | `global` | 无实体归属的全局槽 | 小地图、部分 HUD 壳 |
  | `faction:self` | 本地玩家势力 | 玩家信息聚合 / 资源条 |
  | `entity:{id}` | 单一实体 | 实体信息、技能栏 |
  | `building:{id}` | 建筑（生产者） | 生产队列 |

- 同模板多实例：多选两座兵营 → 两个 `panel.production_queue` 实例，scope 分别为 `building:A` / `building:B`，互不覆盖。
- **绑定**：实例（或模板缺省 + 实例覆盖）只声明 `binds[]` → `PanelProjectionReader`（或等价统一读口）解析 `singleAttribute` / `derivedAttribute` / `aggregateProjection` / `graphOutput`；缺 key / 缺属性 **失败即炸**，禁止静默 0。
- **禁止**实例内、showcase 胶水内遍历实体求和。

### 3.3 Router

- 输入：选中上下文（空选 / 单选 / 多选；单位 / 建筑 / 生产者集合等可观测事实）。
- 输出：目标实例集合（templateId + scope + singleton 约束）。
- 行为：挂载缺失实例、卸载不再需要的实例；切换选中时玩法属性**不被 UI 改写**。
- 路由表由配置/Mod 声明，禁止在每个 showcase 里复制一套 if-else 挂载真相。

### 3.4 与计算图 / 投影的接线（复用，不平行）

| 面板读数类型 | GraphKind | 物化位置 | binding sourceKind |
|--------------|-----------|----------|-------------------|
| 单实体属性 | （无图） | `AttributeBuffer` | `singleAttribute` |
| 单实体派生 | **Derived** | `AttributeBuffer` 派生槽 | `derivedAttribute` |
| 跨实体合计/筛选摘要 | **Query** + `GraphReturnWriter` | `GraphOutputValueStore` Summary key | `aggregateProjection` / `graphOutput` |

**禁止**：`GraphKind.Presentation`；`GraphNodeOp.Panel`；新建第二套 Graph VM；用 Script+Yield 当每帧 UI 读数。

同一 Summary / Attribute 可供 Panel 与 Performer（世界表面）共用读口，不另写公式。

### 3.5 与现有资源条 MVP（手挂表面）的过渡

现状（[#875](https://github.com/MightyBubble/Ludots/pull/875) / showcase `ui_player_aggregate_graph_mvp`，见 [#886](https://github.com/MightyBubble/Ludots/issues/886)）：

- 读数路径已正确：Query → `GraphOutputValueStore` → `PanelProjectionReader`。
- **挂载路径仍是过渡态**：showcase 运行时**手挂表面**（在 Mod Runtime 里直接驱动 HUD），尚未走 Template 注册 → Instance 生命周期 → Router 决策。

合同要求：

1. UIP-0 合同生效后，后续实现切片必须把该 MVP **改为**「模板 `panel.player_aggregate` + scope `faction:self` 的实例挂载」（可由常驻 Router 规则或等价单一挂载表驱动）。
2. **不得**长期并存两套挂载真相（手挂 Runtime + 正式 Instance 表各算各的）。
3. 过渡期允许：读口继续用现有 `PanelProjectionReader`；只替换「谁决定表面出现/消失」这一层。
4. showcase 卫生（双真相/硬编码）归 [#882](https://github.com/MightyBubble/Ludots/issues/882)；本 ADR 只钉挂载合同方向，不在此实现改造。

### 3.6 非目标（本 ADR 不展开）

- Template / Router 运行时完整实现（后续 UIP 切片）
- 通用查表 ops（[#876](https://github.com/MightyBubble/Ludots/issues/876) / [#881](https://github.com/MightyBubble/Ludots/issues/881)）
- TagDisplay 专线清理（[#877](https://github.com/MightyBubble/Ludots/issues/877)）
- 热重载正式化（PR #874 DEFERRED）
- 35 类面板全量实现

## 4. 场景

1. **空选中**：玩家点空白 → Router 只保留玩家聚合（`faction:self`）与小地图（`global` singleton）；实体信息 / 技能栏 / 生产队列卸载。
2. **多选两座兵营**：同一生产队列模板出现两个 scope 不同的实例；队列读数各自对应自己的建筑。
3. **切换选中**：从单位 A 改选单位 B → Router 卸载 `entity:A` 相关实例、挂载 `entity:B`；血量等玩法属性不被 UI 写回。
4. **资源条过渡**：指挥玩家仍看顶栏合计；实现侧从「showcase 手挂表面」收敛为「`panel.player_aggregate` 实例」，数字仍来自同一 GraphOutput，不平行第二套求和。

## 5. 边界

- 不做完整 CSSOM / 假 `@media` / `float`/`fixed` 兼容。
- 面板点击只发正式指令，不算图双向写回。
- Mod 可覆盖模板默认 graph id；缺图 / 缺 key **失败即炸**。
- AttributeBinding / TagBinding 保留为薄适配；复杂聚合必须上 Query/Derived。
- 不批准 Core 预置业务映射表；查表另册。
- 不批准 `ReloadConfigs(GAS)` 当正式热应用。
- 本页不替代 [#858](https://github.com/MightyBubble/Ludots/issues/858) Epic 正文；落地后债地图见 [#886](https://github.com/MightyBubble/Ludots/issues/886)。

## 6. UAT

```gherkin
Feature: 模板与实例让多选面板互不打架
  作为一个指挥玩家
  我想多选几座兵营时每座都有自己的生产队列
  以便分别看各自造到哪一步

  Scenario: 多选两座兵营出现两份队列
    Given 生产队列模板已经注册好
    When 我多选兵营 A 和兵营 B
    Then 画面上同时出现两份生产队列
    And 一份对应兵营 A、一份对应兵营 B
    And 两份的数字不会互相盖掉

Feature: 空选中只留全局信息
  作为一个指挥玩家
  我想点空白时收起单位面板
  以便专心看地图和资源

  Scenario: 点空白卸掉实体面板
    Given 我刚才选中了一个单位并看到实体信息
    When 我点地图空白处
    Then 实体信息和技能栏消失
    And 顶栏玩家资源与小地图还在

Feature: 换选中不会改玩法数值
  作为一个指挥玩家
  我想切换选中单位时只是换看谁
  而不是界面偷偷改单位属性

  Scenario: 切换选中只换实例不改属性
    Given 单位 A 的血量是玩法里的真实值
    When 我改选单位 B 再选回 A
    Then 我看到的是 A 的投影读数
    And A 的玩法血量没有被界面改写

Feature: 资源条从手挂迁到模板实例
  作为一个后续实现者
  我想按同一挂载合同收编现有资源条 MVP
  以免两套挂面板方式长期并存

  Scenario: UIP-0 落地后资源条走实例挂载
    Given 资源条 MVP 已在 main 且数字来自 GraphOutput
    And 当前展示仍由 showcase 手挂表面驱动
    When 实现侧按本 ADR 挂载 panel.player_aggregate 实例
    Then 挂载决策只来自 Template/Instance/Router（或等价单一挂载表）
    And 不再保留平行的手挂挂载真相
    And 顶栏数字仍等于同一 Summary 投影

Feature: 作者不会造第二套图宇宙
  作为一个面板作者
  我想用现有计算图给面板供数
  而不要再学一套 Presentation Graph

  Scenario: 合同禁止平行 Presentation Graph
    Given 我阅读本 ADR
    Then 合同禁止 GraphKind.Presentation
    And 合同禁止 GraphNodeOp.Panel
    And 合同禁止第二套 Graph VM
    And 面板变量仍通过 Projection 读口绑定

  Scenario: 缺图或缺 key 必须失败
    Given 某模板声明了绑定到不存在的 Summary key
    When 运行时解析该实例绑定
    Then 加载或解析失败并明确报错
    And 界面不会静默显示 0 假装正常
```

# 读表 / 查表 / 表数据聚合图节点族设计（UI Panel）

## 1. 概述

目标是为 UI Panel 的图作者提供一组严格分层的节点族，覆盖：

```text
选中实体 -> ReadGameplayTag(State.*) -> LookupTagDisplayText -> Panel.curState
```

以及更通用的：

```text
key/tag/entity/template id -> TableLookup -> display text token / 数值 / entity / 多字段摘要
```

核心结论：

- L0 Graph VM 继续只运行数值、bool、entity、target list；不新增字符串寄存器。
- Text 进入面板的方式是 `PresentationTextCatalog` token id 进入 `GraphOutputValueStore`，surface/projection 层按 locale 格式化成最终字符串。
- `ReadGameplayTag` 是新的纯读原子 op；`LookupTagDisplayText` 是作者糖，运行时产物必须命名为 token id。
- 通用表查询拆成 `ResolveTableRow` + typed field read，支持多字段聚合时复用 row handle，避免每个字段重复查 key。
- L0 只新增必须的 GraphNodeOp，不新增 `GraphKind.Presentation`、不新增 `GraphNodeOp.Panel`、不把 Attribute 当 Tag/BB/Text/Table 的替身。

对照 #858：本设计复用 `Query/Derived -> GraphOutputValueStore -> Panel binds[]`，服务 Template/Instance/Router，不创建 Presentation Graph VM。  
对照 #848：复用同一 VM 与 `GraphKind.Query/Derived` 分层，查表 op 归类为 Pure；Query 真引脚/outputs 仍是计算图投影的 SSOT。  
对照 `gitbook/architecture/ui-panel-authoring-form.md`：作者看见 `ReadGameplayTag`、`LookupTagDisplayText`、Panel Text 引脚；落盘不是 Panel opcode，而是 outputs/bindings。

## 2. 结构

```text
Mod Config
  Presentation/text_tokens.json
  Presentation/text_locales.json
  GraphTables/tag_display_tables.json
  GraphTables/lookup_tables.json
        |
        v
ConfigPipeline
        |
        +--> PresentationTextCatalog          (既有文案 SSOT)
        +--> TagRegistry / AttributeRegistry  (既有 gameplay id SSOT)
        +--> GraphLookupTableRegistry         (新增，只读 SoA 表索引)
        |
        v
GraphProgramConfigLoader / GraphProgramAuthoringFrontDoor / GraphControlFlowCompiler / GraphProgramSymbolPatcher
        |
        v
L0 Graph VM (GraphKind.Query | Derived)
  ReadGameplayTag -> Int tagId
  LookupTagDisplayToken -> Int tokenId
  ResolveTableRow -> Int rowHandle
  TableReadFloat/Int/Entity/Token/Tag -> typed register
        |
        v
GraphReturnWriter
        |
        v
GraphOutputValueStore
  Bool / Int / Float / Entity / TargetList
  + TextToken semantic over Int (recommended as GraphOutputValueKind.TextToken)
        |
        v
Panel Projection
  variable.valueKind = Text
  tokenId -> PresentationTextFormatter(locale) -> string
```

职责边界：

| 层 | 负责 | 不负责 |
| --- | --- | --- |
| GameplayTag / TagOps | 状态真相、Effective tag 语义 | 玩家文案 |
| GraphLookupTableRegistry | 已解析 id 的只读表索引 | ECS 组件读写、locale 格式化 |
| Graph VM | 0Alloc 读取 tag/table/value id | 字符串拼接、DOM、fallback |
| GraphOutputValueStore | scope owner + key 的 typed projection | 文案 SSOT |
| Panel surface | Text token 格式化与展示 | 玩法状态推导 |

## 3. 详情

### 3.1 现有代码对照

已确认可复用能力：

- `GraphNodeOp` 已有 `HasTag`、`ReadBlackboardFloat/Int/Entity`、`LoadAttribute`、`GraphOutput` 相关 Query/Agg op；没有 `ReadGameplayTag`、`LookupTagDisplayText`、通用 `TableLookup`。
- `IGraphRuntimeApi` 已承载 tag、attribute、blackboard、entity query、relationship 的只读接口，新增读表接口应挂这里。
- `GameplayTagContainer` 是固定 256 bit；`TagRegistry` 限定 tag id 1..255；适合 dense tag display lookup。
- Blackboard 当前只有 Float/Int/Entity buffer；没有 Text BB。不能用 `BlackboardInt` 的裸 int 偷偷表示 Text，必须有语义合同。
- `AttributeBuffer` 是 float 属性 SoA；Text/Tag/表查询不得塞进 Attribute。
- `GraphOutputValueStore` 当前只存 Bool/Int/Float/Entity；Text 输出需要 token 语义，不能直接存 string。
- `EntityCollectionStore` 是实体集合 SoA，不是通用表数据仓库。
- `PresentationTextCatalog`、`PresentationTextCatalogLoader`、`PresentationTextFormatter` 已是文案 token/locale SSOT。
- 各领域 `DefinitionRegistry` 多为领域专用，没有可直接复用的通用 typed lookup table；新增表 registry 必须复用 `StringIntRegistry`、`ConfigPipeline`，不能各节点自建字典。

### 3.2 L0 vs L1 拆分

| 层 | 名称 | 形态 | 原因 |
| --- | --- | --- | --- |
| L1 作者节点 | `ReadGameplayTag` | 画布节点 | 作者关心 State.* 语义，不关心 bitset mask |
| L0 op | `ReadGameplayTag` 或更精确 `SelectGameplayTag` | `GraphNodeOp` | VM 需要一个可执行纯 op |
| L1 作者节点 | `LookupTagDisplayText` | 画布节点 | Panel 引脚是 Text |
| L0 op | `LookupTagDisplayToken` | `GraphNodeOp` | VM 只返回 token id，不返回 string |
| L1 作者节点 | `TableLookup` | 组合节点 | 作者按 key 查字段 |
| L0 op | `ResolveTableRow` + `TableRead*` | 多个 `GraphNodeOp` | 多字段聚合复用 row，保持 typed register |

### 3.3 ValueType / OutputKind 决策

不新增 `GraphValueType.Text`。原因：

- VM 热路径是寄存器 span；string 寄存器会引入托管引用、生命周期与分配问题。
- UI 文案已有 `PresentationTextCatalog` SSOT；graph 不应拥有 locale。
- Attribute/BB 都不是 Text 的承载层。

建议新增或明确：

| 类型 | 是否新增 | 说明 |
| --- | --- | --- |
| `GraphValueType.Text` | 否 | 禁止 L0 字符串寄存器 |
| `GraphValueType.TableRow` | 否 | row handle 用 Int；field op 用 table/field schema 校验 |
| `GraphOutputValueKind.TextToken` | 是，推荐 | 底层存 int，但 view kind 明确为 Text token |
| Panel `valueKind: Text` | 复用作者模型 | source 必须是 token semantic 或 projection formatter |

如果第一刀不改 `GraphOutputValueKind`，可临时用 `Int + binding.semantic = presentationToken`，但实现 issue 应优先新增 `TextToken`，避免裸 int 在 projection 层被误读。

### 3.4 `ReadGameplayTag`

语义：

```text
tagId = Select exactly one effective tag from entity tags within authored domain/table mask
```

输入：

- Entity register。
- `tagDomain` 或 `table` 符号；P0 推荐复用 tag display table 的 mask，减少重复配置。
- Cardinality policy：默认 `RequireOne`。

输出：

- Int register：tag id。
- 可选 Bool register：found，仅 `AllowNone` 策略可启用。

策略：

| 策略 | 行为 |
| --- | --- |
| `RequireOne` | 0 个或多个匹配 tag 直接抛错；EntityInfoCard 的 `State.*` 默认用它 |
| `AllowNone` | 0 个返回 found=false；多个仍抛错 |
| `LowestId` | 取最低 id，仅调试或明确排序域；UI Panel 默认不允许 |

实现挂点：

- `IGraphRuntimeApi.SelectGameplayTag(Entity entity, int tagDomainId, TagSelectionPolicy policy, out int tagId, out int matchCount)`。
- `GasGraphRuntimeApi` 必须与 `HasTag` 同源，尊重 staged side-effect 视图与 `TagOps` Effective 语义。
- 无 `GameplayTagContainer`、entity dead、domain 空 mask、ambiguous state 都失败关闭。

为什么 `HasTag` 不够：

- `HasTag(State.Moving)` 是谓词；`ReadGameplayTag(State.*)` 是“从互斥族选出当前状态”的读取。
- 用 N 个 `HasTag` + branch 会把作者图变成手写 if-else，也无法统一处理“0/多个状态”的合同错误。

### 3.5 `LookupTagDisplayText` / `LookupTagDisplayToken`

作者节点名：`LookupTagDisplayText`。  
运行时 op 名：`LookupTagDisplayToken`。

语义：

```text
tokenId = TagDisplayTable[tableId].TokenByTagId[tagId]
```

输入/输出：

- 输入：Int tagId。
- Immediate：table id。
- 输出：Int tokenId，或 `GraphOutputValueKind.TextToken` summary。

失败关闭：

- `tagId == 0` 且未通过显式 `AllowNone` 分支处理：抛错。
- table 没有该 tag 行：抛错。
- token 未注册或 locale 缺模板：加载期优先失败；运行期仍保留断言。
- 禁止 fallback 到 `TagRegistry.GetName(tagId)`、英文硬编码、空串。

### 3.6 通用 TableLookup

为 display text、数值、多字段聚合提供一套通用表基建，而不是给每个面板造私有 dictionary。

#### 3.6.1 表资产形状

建议路径：

- `GraphTables/tag_display_tables.json`：tag 专用表，key 固定为 tag。
- `GraphTables/lookup_tables.json`：通用 typed table。

通用表示例：

```json
[
  {
    "id": "entity.rank.display",
    "keyKind": "Int",
    "columns": [
      { "id": "displayToken", "kind": "TextToken" },
      { "id": "sortWeight", "kind": "Int" },
      { "id": "powerScale", "kind": "Float" }
    ],
    "rows": [
      { "key": 1, "displayToken": "rank.recruit", "sortWeight": 10, "powerScale": 1.0 },
      { "key": 2, "displayToken": "rank.veteran", "sortWeight": 20, "powerScale": 1.2 }
    ]
  }
]
```

规则：

- `columns[].kind` 必须是 `Bool | Int | Float | EntityTemplate | Tag | TextToken` 中的显式类型。
- `TextToken` 列只存 token id；loader 校验 token 与 locale 覆盖。
- `Tag` 列只存 tag id；loader 经 `TagRegistry` 解析。
- `EntityTemplate` 列只存 template key id；loader 经既有 `EntityTemplateKeyRegistry` 解析。
- 重复 row key、缺列、列类型不匹配、未知 token/tag/template 全部加载失败。

#### 3.6.2 L0 op 清单

最小通用 op：

| L0 op | 输入 | 输出 | 说明 |
| --- | --- | --- | --- |
| `ResolveTableRow` | key Int, table id | Int rowHandle | 一次 key 查找，多字段复用 |
| `TableReadInt` | rowHandle, field id | Int | 读 Int/Tag/TextToken/EntityTemplate 这类 id |
| `TableReadFloat` | rowHandle, field id | Float | 读数值 |
| `TableReadEntity` | rowHandle, field id | Entity | 仅用于明确注册的 entity 引用表；P0 可不开放 |
| `LookupTagDisplayToken` | tagId, table id | Int tokenId | tag display 快捷 op；内部等价 dense tag table |

不建议 P0 做：

- `TableReadString`。
- `TableAggregateTextList`。
- `TableLookupAny` 返回动态 union。
- `TableRow` 新寄存器 bank。

#### 3.6.3 查表索引结构

Tag display 表：

```text
tableSlot -> mask GameplayTagContainer
tableSlot -> tokenByTagId[256]
```

通用 int key 表：

```text
tableSlot -> rowByDenseKey?      // 当 key 域可 dense 化
tableSlot -> open-address index  // key -> rowIndex，构建期分配，运行期只读
rowHandle = globalRowBase + rowIndex
fieldId -> tableSlot + columnIndex + kind
column values:
  intColumns[][]
  floatColumns[][]
  entityColumns[][]
```

字符串 key 只允许加载期解析为 int id；运行时不接收 string key。

### 3.7 GraphOutput 与 Panel Text

`EntityInfoCard.curState` 建议 outputs：

```json
{
  "id": "curState",
  "destination": "Summary",
  "type": "TextToken",
  "source": "stateToken",
  "key": "panel.entity_info.curState"
}
```

如果 `TextToken` 尚未实现，临时过渡：

```json
{
  "id": "curState",
  "destination": "Summary",
  "type": "Int",
  "source": "stateToken",
  "key": "panel.entity_info.curState",
  "semantic": "presentationToken"
}
```

Panel binding：

```json
{
  "variableId": "curState",
  "valueKind": "Text",
  "sourceKind": "graphOutput",
  "graphOutputKey": "panel.entity_info.curState",
  "sourceValueKind": "TextToken"
}
```

Projection：

- Reactive：projection 可输出 `int CurStateTokenId` 与/或格式化后的 `string CurState`；格式化发生在 UI 层。
- WebUI：payload 可发 token id + args，由 WebUI text contract 渲染；或服务器 projection 格式化 snapshot。
- Compose/Markup：由 code-behind 读取 token id 后调用 `PresentationTextFormatter`。

### 3.8 Blackboard Text 的处理

当前 Blackboard 只有 Float/Int/Entity。设计结论：

- 本节点族不新增 raw Text BB。
- 不允许把 `ReadBlackboardInt` 的返回值在作者层无标注地当 Text。
- 如需“BB 存文案”，应另开 `BlackboardTextToken` 语义能力：仍存 token id，但 key registry 声明 value kind，GraphOutput/Panel binding 能验证类型。
- EntityInfoCard 的 `lastKill` Text 更推荐存 `BlackboardEntity`，再由 projection 查实体显示名/insight token；不要把最终字符串写进 ECS 热路径。

### 3.9 FrontDoor / ControlFlow / Patch / Runtime 接线

必须改动的基建点（实现期）：

1. `GraphNodeOp`
   - 新增 `ReadGameplayTag` 或 `SelectGameplayTag`。
   - 新增 `LookupTagDisplayToken`。
   - 新增 `ResolveTableRow`、`TableReadInt`、`TableReadFloat`；`TableReadEntity` 可 L1。

2. `GasGraphOpHandlerTable`
   - 全部注册为 Pure。
   - handler 只访问 span / readonly registry；不分配。

3. `IGraphRuntimeApi`
   - 增加 tag domain selection 和 table lookup 接口。
   - 默认实现 throw，保持现有缺基建 fail-fast 风格。

4. `GasGraphRuntimeApi`
   - 注入 `GraphLookupTableRegistry`。
   - `ReadGameplayTag` 与现有 `HasTag` 同源。

5. `GraphProgramAuthoringFrontDoor` / `GraphControlFlowCompiler`
   - 作者字段：`table`、`field`、`tagDomain` 或复用 table mask。
   - ControlFlow 输出 type 映射：tag/token/rowHandle 都是 Int；float field 是 Float。

6. `GraphProgramSymbolPatcher` / `IGraphSymbolResolver`
   - 新增 `ResolveGraphLookupTable`、`ResolveGraphLookupField` 或 combined field symbol。

7. `GraphOutputTypes` / `GraphOutputValueStore`
   - 推荐新增 `TextToken` kind，底层复用 int 列。

8. Panel binding / WPK
   - Text variable 必须验证 source 是 `TextToken` 或显式 formatter，不接受裸 Float/Attribute。

## 4. 场景

### 4.1 EntityInfoCard 当前状态

```text
LoadExplicitTarget
  -> ReadGameplayTag(table = entity.state.display, policy = RequireOne)
  -> LookupTagDisplayText(table = entity.state.display)
  -> GraphOutput curState(TextToken)
  -> Panel.curState(Text)
```

玩家选中实体时：

- 实体只有 `State.Moving`：面板显示本 locale 的“移动中”。
- 实体没有任何 `State.*`：图执行失败；作者必须给实体初始化状态或改为显式 `AllowNone` 分支。
- 实体同时有 `State.Idle` 与 `State.Stunned`：图执行失败，暴露玩法状态互斥性破坏。

### 4.2 按 rank key 查显示名和数值

```text
ReadBlackboardInt(entity.rank)
  -> ResolveTableRow(entity.rank.display)
  -> TableReadInt(displayToken)
  -> TableReadFloat(powerScale)
  -> Panel.rankName(Text), Panel.powerScale(Float)
```

同一 rowHandle 读两个字段，避免重复 key lookup。

### 4.3 跨实体聚合后展示 bucket 文案

```text
QueryFromCollection(selected)
  -> QueryFilterTagAny(State.Stunned)
  -> AggCount
  -> GraphOutput stunnedCount(Int)

ConstTag(State.Stunned)
  -> LookupTagDisplayToken(entity.state.display)
  -> GraphOutput stunnedLabel(TextToken)
```

数字聚合复用现有 Query/Agg；显示文案只查 token。

### 4.4 多实例 Panel

两个 EntityInfoCard 实例分别绑定不同 selected entity：

- 每个 scope owner 写自己的 `panel.entity_info.curState` Summary。
- 若两个实例绑定同一实体，应由 Router/Projection 共用 owner+key，避免重复执行。
- 如果业务要求每实例独立 owner，则成本线性增长，但每实例状态查表仍是 O(1)。

## 5. 边界

允许：

- Query/Derived 图内读 tag id、读 rowHandle、读 typed field。
- GraphOutput 输出 Int/Float/Entity/TextToken。
- Surface 在投影边界格式化 token。
- Mod 通过表资产扩展新 tag 文案或新 rank 字段。

禁止：

- 新增 `GraphKind.Presentation`。
- 新增 `GraphNodeOp.Panel`。
- Graph handler 返回/拼接/缓存 string。
- Attribute 假扮 Tag、BB、Text、TableLookup。
- `TagRegistry.GetName` 作为玩家文案 fallback。
- 缺表行、缺 token、缺 locale 时返回空串/Unknown/0。
- 在 ECS 热路径动态添加 Text BB 组件。
- 在 showcase/Web 层手写跨实体求和绕过 Query/Agg。

未纳入本切片：

- raw dynamic string 数据流。
- 完整本地化富文本参数系统扩展。
- 通用 SQL/JSONPath 式查询。
- 表热重载增量 patch；P0 采用加载期 freeze。

## 6. UAT（Cucumber）

```gherkin
Feature: EntityInfoCard 状态文案来自 GameplayTag 查表
  作为玩家
  我想在实体信息卡看到当前状态的本地化文案
  以便理解选中实体正在做什么

  Scenario: 选中实体的 State tag 被映射为 Text 引脚
    Given 实体带有唯一有效 tag "State.Moving"
    And 表 "entity.state.display" 将 "State.Moving" 映射到 token "entity.state.moving"
    And 当前 locale 注册了 token "entity.state.moving"
    When EntityInfoCard 图执行 ReadGameplayTag 和 LookupTagDisplayText
    Then GraphOutput key "panel.entity_info.curState" 写入 TextToken
    And Panel.curState 显示该 locale 的状态文案
    And AttributeBuffer 中没有用于承载该 Text 的假属性

  Scenario: 状态族缺失时失败关闭
    Given EntityInfoCard 使用 RequireOne 策略读取 "State.*"
    And 选中实体没有任何 "State.*" tag
    When 图执行 ReadGameplayTag
    Then 执行失败并报告缺失状态域
    And 面板不显示空字符串
    And 面板不显示 Unknown fallback

  Scenario: 状态族冲突时失败关闭
    Given EntityInfoCard 使用 RequireOne 策略读取 "State.*"
    And 选中实体同时带有 "State.Idle" 和 "State.Stunned"
    When 图执行 ReadGameplayTag
    Then 执行失败并报告状态域歧义
    And 作者能定位到冲突 tag id

Feature: 作者按 key 查表输出多字段
  作为面板作者
  我想用一次 row lookup 读取显示名和数值字段
  以便避免为同一 key 重复建查表节点

  Scenario: rank key 查出 TextToken 与 Float
    Given 表 "entity.rank.display" 有 key 2
    And key 2 的 displayToken 为 "rank.veteran"
    And key 2 的 powerScale 为 1.2
    When 作者连接 ResolveTableRow 到 TableReadTextToken 和 TableReadFloat
    Then Panel.rankName 接收到 TextToken
    And Panel.powerScale 接收到 Float
    And 图 VM 没有 string 寄存器

  Scenario: 缺表行时不静默降级
    Given 表 "entity.rank.display" 不存在 key 99
    When 图执行 ResolveTableRow(key=99)
    Then 执行失败并报告 table id 与 key
    And 不返回 rowHandle 0 继续读默认行

Feature: 跨实体聚合仍复用 Query/Agg
  作为作者
  我想按 tag 聚合实体数量并显示 bucket 标题
  以便在 UI Panel 中展示状态统计

  Scenario: 状态统计数字来自 Query/Agg，标题来自 token 表
    Given 选中集合中有 3 个 State.Stunned 实体
    When Query 图执行 QueryFilterTagAny 和 AggCount
    Then stunnedCount Summary 为 3
    And stunnedLabel Summary 是 State.Stunned 的 TextToken
    And Web/Showcase 层没有手写遍历实体求和
```

## 7. 性能专章

### 7.1 热路径分配预算

| 路径 | 分配预算 |
| --- | --- |
| `ReadGameplayTag` handler | 0 alloc |
| `LookupTagDisplayToken` handler | 0 alloc |
| `ResolveTableRow` / `TableRead*` handler | 0 alloc |
| Query/Agg existing ops | 继续使用 caller-owned spans / stackalloc |
| `GraphReturnWriter` 写 Summary | 0 alloc，沿用 store SoA |
| Surface token 格式化 | 允许 UI 边界分配；不算 ECS/Graph 热路径 |

### 7.2 查表索引结构

- Tag display：`int[256] tokenByTagId`，O(1)，无 hash。
- 通用 int key：优先 dense direct index；非 dense 用构建期分配的 open addressing `int[] keys/rows`，运行期只读。
- field：`fieldId -> tableSlot + columnIndex + kind`，读取前校验 rowHandle 属于 table。
- Text token：始终是 int；真正 string 由 `PresentationTextCatalog` 按 locale 解析。

### 7.3 是否可缓存

可缓存：

- table registry 是加载期构建后的只读缓存。
- `ResolveTableRow` 的 rowHandle 可在同一 graph 中复用。
- Panel projection 可按 `(localeId, tokenId, outputRevision)` 缓存格式化 string。

不可缓存：

- Graph handler 内不可缓存 locale string。
- 不在实体组件上缓存最终 UI 文案。
- 不为缺失行缓存 fallback。

### 7.4 与 Graph 执行频率的关系

- `EntityInfoCard` 应随 selection/tag dirty/绑定刷新执行，不随浏览器 paint 或 DOM render 重复执行。
- Query 聚合仍按 #848 的 Query 物化节奏执行：simulation/projection phase，而不是 UI 帧内手写循环。
- Derived 只写 AttributeBuffer 派生槽；Text token 不走 Derived attribute，走 Query Summary 或面板 projection。
- 如果单实体 panel 只读一个状态 tag，单次成本是一次 tag bitset 选择 + 一次数组查表。

### 7.5 多实例 Panel 代价

- N 个不同 scope 的 panel：N 次 graph materialization，成本线性。
- 同一 scope 多表面：应共享 `GraphOutputValueStore(owner,key)`，projection 多读，不重复执行图。
- 表大小对实例数无乘法影响；lookup registry 全局只读。
- 多实例下最贵路径仍是 QueryAll/Filter/Agg，不是 tag display lookup；应避免每个 panel 都 QueryAllMapEntities。

## 8. 复用 / 新增清单

### 8.1 复用

- `GraphKind.Query` / `GraphKind.Derived`
- `GraphNodeOp` / `GasGraphOpHandlerTable`
- `GraphProgramAuthoringFrontDoor` / `GraphControlFlowCompiler` / `GraphProgramConfigLoader` / `GraphProgramSymbolPatcher`
- `IGraphRuntimeApi` / `GasGraphRuntimeApi`
- `TagRegistry` / `GameplayTagContainer` / `TagOps`
- `ReadBlackboardFloat/Int/Entity`
- `LoadAttribute`
- Query/Agg ops：`QueryFromCollection`、`QueryFilterTagAny/None`、`AggCount`、`AggSumAttribute`
- `GraphReturnWriter`
- `GraphOutputValueStore`
- `EntityCollectionStore` 仅用于实体集合，不用于表数据
- `PresentationTextCatalog` / `PresentationTextFormatter`
- `StringIntRegistry`
- `ConfigPipeline`

### 8.2 必须新开 GraphNodeOp

P0 必须：

1. `ReadGameplayTag` 或 `SelectGameplayTag`
2. `LookupTagDisplayToken`
3. `ResolveTableRow`
4. `TableReadInt`
5. `TableReadFloat`

P1 可选：

6. `TableReadEntity`
7. `TableReadBool`（也可由 `TableReadInt + Compare` 覆盖）

不新增：

- `TableReadString`
- `LookupTagDisplayString`
- `GraphNodeOp.Panel`

### 8.3 必须新开非 op 基建

- `GraphLookupTableRegistry`
- `GraphLookupTableLoader`
- `IGraphSymbolResolver.ResolveGraphLookupTable`
- `IGraphSymbolResolver.ResolveGraphLookupField`
- `GraphOutputValueKind.TextToken`（推荐）
- Panel binding 的 TextToken validation

## 9. 建议的 GitHub issue 标题

`feat(graph): add ReadGameplayTag and typed TableLookup tokens for UI Panel text bindings`

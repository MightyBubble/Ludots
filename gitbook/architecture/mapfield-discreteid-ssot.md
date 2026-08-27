# MapField 离散归属（discreteId）SSOT

本文是「格子区域」标准配置与运行时 API 的唯一正文。作者速查见 [MapField 作者手册](mapfield-howto.md)；栅格存储底座见 [Core Field2D](core-field2d.md)。

## 1. 概述

引擎只认：**层 key**、**格长 / chunk**、**regionId（int）**、**区域明文 key（string）**。  
没有「国家 / 省 / 市」类型，也没有行政区父子合同。

玩家或玩法问「这块地属于谁」，答案永远是：在**某一个** discreteId 层上，某世界坐标落在哪个 regionKey。需要另一套口径（例如省），就另开一层、另做一次点查——**不要**把两层叠进同一张玩法图当「国+省双合同」，更不要用 `ChildOf` / `hierarchies.json` 焊行政区树。

国界与省界在几何上怎么对齐，属于**离线烘焙**：用 `tools/` 脚本一次生成栅格资产。Core 只装载与点查，不参与对齐。

## 2. 结构

```text
声明层     Fields/layers.json          → FieldLayerRegistry
笔画资产   Fields/cells/<layerKey>.json → FieldCellsConfigLoader → ChunkedField2D<int>
地图启用   Maps/<mapId>.json Fields.Layers → FieldSessionStore
物化       FieldRegionMaterializer     → RegionEntityIndex + 区域实体
跟踪       FieldTrackedCm.layer        → FieldRegionMembershipSystem
可选分组   Fields/hierarchies.json     → RegionHierarchyBuilder（仅玩法分区，非行政区）
投影       FieldDiscreteVisualProjector → GlobalFieldVisualKind.DiscreteOwnership
探针       ludots.field.*
```

可选并存的**独立** field（例如 fog + 一层 ownership）合法。  
**禁止**把「国家 ownership + 省级 ownership」同时挂进同一玩法 showcase，当作行政区 SSOT。

## 3. 详情

### 3.1 层声明：`Fields/layers.json`

| 字段 | 合同 |
|------|------|
| `id` | 层 key；地图 `Fields.Layers` 与 `FieldTrackedCm.layer` 引用它 |
| `kind` | 归属层固定 `discreteId` |
| `cellSizeCm` / `chunkSizeCells` | 世界 cm ↔ 格 / chunk |
| `default` | 未绘制格的值；归属层用 `0`（无区域） |
| `writerDomain` | 写域标识（配置合并 / 权限语义） |
| `maxRegionIds` | 区域名表容量（含可编号区域上限） |
| `persistent` | 是否参与持久化策略 |

Core + Mod 片段经 ConfigPipeline `ArrayById` 合并。装载入口：`FieldLayerConfig` / `FieldLayerConfigLoader`。

### 3.2 笔画：`Fields/cells/<layerKey>.json`

| 版本 | 形状 | 用途 |
|------|------|------|
| `schemaVersion: 1` | `cells: [[x,y,regionId], ...]` | 小图 / 既有资产 |
| `schemaVersion: 2` | `rects: [[x0,y0,x1,y1,regionId], ...]`，可选 `points`；**禁止**再写 `cells` | 大陆级作者默认 |

公共字段：

- `layer`：必须等于文件名层 key。
- `regions`：非空明文 key 列表；片段内按 **Ordinal** 排序后，`regionId = 排序下标 + 1`（`0` 保留「无区域」）。**不是** JSON 书写顺序的 `i+1`。
- 多 Mod 片段合并时：全体 key 再取 **Ordinal 并集排序** 得到最终 id；笔画按明文 key 认领；同一格被两个 key 争抢则装载失败（点名双方）。

装载把笔画 `FillRect` / `Set` 进 `ChunkedField2D<int>`，禁止先展开成千万级中间数组。FieldEditor / 存档写出走 `FieldRectCodec`（行 RLE + 纵向合并）。

实现：`src/Core/Fields/Config/FieldCellsConfigLoader.cs`。

### 3.3 地图启用与会话

`Maps/<mapId>.json`：

```json
{
  "Id": "example_map",
  "Fields": {
    "Layers": ["ownership.example"]
  }
}
```

装载链（`GameEngine.CreateFieldsForSession`）：

1. `FieldSessionStore.Create(registry, mapConfig.Fields.Layers, cellsLoader)`
2. `FieldRegionMaterializer.Materialize` → `session.RegionIndex`
3. 若有 `Fields/hierarchies.json`，`RegionHierarchyBuilder.Build` → `session.RegionGroups`

未在 `Fields.Layers` 列出的层**不会**进入本图会话，即使 Mod 声明了该层。

### 3.4 跟踪、过境、查询 API

| API | 职责 |
|-----|------|
| `FieldTrackedCm { layer }` | 实体跟哪一层；一实体一跟踪层 |
| `RegionMembershipCm` | 当前 layerId + regionId（系统维护） |
| `FieldRegionMembershipSystem` | 格变化时差分过境；发 `FieldRegionEntered` / `FieldRegionExited` |
| `RegionEntityIndex` | `(layerId, regionId) →` 区域实体，O(1) |
| `FieldRegionQueries.TryIsInFieldRegion` | 问实体是否在某层某明文 key |
| `DiscreteIdFieldLayerData.Field.Get(cell)` | 裸点查 regionId |
| `DiscreteIdFieldLayerData.Regions` | id ↔ 明文 key |

TriggerGraph `filters.region` 用**明文 key**，不用 int id。

### 3.5 视觉投影

`FieldDiscreteVisualProjector` 读 discreteId 层，发布 `GlobalFieldVisualKind.DiscreteOwnership`。  
名字是历史通用投影 kind，**不是**「行政区专用」。玩法若要国界色贴花，用 Decal；可用地图 Tag（如 `Raylib.FieldOverlays:Off`）关掉调试格网叠层。

有玩法 `hierarchies.json` 时，投影可 remap 到祖先；行政区**不要**依赖这条链路。

### 3.6 Agent bridge

| Tool | 作用 |
|------|------|
| `ludots.field.layers` | 列层 |
| `ludots.field.cell` | 点查 regionId / key；有玩法层级时附链 |
| `ludots.field.hierarchy` | 玩法祖先链 |
| `ludots.field.writeCell` | 运行时改格（不回写资产） |

### 3.7 离线烘焙（对齐归属）

| 允许 | 禁止 |
|------|------|
| `tools/` 脚本读矢量 / 栅格，写出 `Fields/cells/*.json` 与贴花 PNG | 在 Core 增加「国包含省」类型、组件或系统 |
| 烘焙期做国↔省几何对齐、冲突消解、投影与缩图 | 运行时用 `ChildOf` / GAS 名单 / `hierarchies.json` 表达行政区 |
| 产物进 data-only Mod 资产 | 为行政区抬 `GasConstants.MAX_CHILDREN_BUFFER_CAPACITY` |

现有国家层再生：`tools/east_asia_borders/rasterize_countries_to_field.py`、`export_country_decal_png.py`。  
省级层有独立 showcase `field_east_asia_admin`；与国家层的对齐若需要，**另写离线脚本**，不要叠进陆海玩法图。

### 3.8 `hierarchies.json` 边界

仅服务玩法分区分组（如小区 → 中组 → 大组，见 `field_hierarchy_query`）。  
省市县：**国家一层、省一层，各自点查**；禁止省焊国家实体下；禁止为名单容量发明中间「片区」。

## 4. 场景

| 场景 | 正确做法 |
|------|----------|
| 只关心国界过境 | 玩法图 `Fields.Layers` 只挂国家层；实体 `FieldTrackedCm` 跟该层 |
| 只关心省级投影 | 独立地图 / showcase 挂省级层（`field_east_asia_admin`） |
| 同一世界坐标既要国又要省 | 两次点查两个**独立**会话或工具链；对齐在烘焙脚本里完成，不进 Core |
| 雾与归属同图 | fog 层 + **一层** ownership；合法的多 field，不是行政多层 |
| 玩法「中区包含小区」 | `hierarchies.json` + `TryResolveChain`；与行政区无关 |

## 5. 边界

- Core Field2D / MapField **不**包含 Presentation / Raylib / 贴花类型。
- 热路径：点查与 membership 差分；禁止热路径结构变更与内存飞线；调用方提供 span，不返回 LINQ 分配。
- `DiscreteOwnership` ≠ 必须显示行政马赛克；展示合同由玩法选择 Decal / 关 overlay。
- 作者 `regions` 书写顺序**不**决定 id；只认 Ordinal。
- 一实体同时只跟踪一层；不要指望一个 `FieldTrackedCm` 报两国界口径。

## 6. UAT（玩家 / 作者视角）

```gherkin
Feature: 格子区域只认一层归属合同

  Scenario: 玩法图只挂一层国家归属
    Given 作者打开陆海国界演示图的配置
    Then Fields.Layers 里只有国家层
    And 没有省级层挂在同一张玩法图上

  Scenario: 单位过境只报跟踪层
    Given 玩家进入挂了国家层的棋盘
    And 黄块跟踪国家层
    When 黄块从中国走进韩国
    Then 过境面板报的是国家变化
    And 系统不会因为「少挂了省」而失败或静默补一层

  Scenario: 省界是另一份独立展示
    Given 作者要看省级示意
    When 启动省级归属 showcase
    Then 棋盘上出现省级投影
    And 这份资产不要求同时装载国家层才能工作

  Scenario: 对齐归属不进引擎核心
    Given 需要国家栅格与省栅格在几何上对齐
    When 作者运行离线烘焙脚本
    Then 产物写入 Mod 的 Fields/cells 资产
    And Core 源码中不出现国家包含省的类型或系统
```

## 7. 索引

| 角色 | 路径 |
|------|------|
| 本 SSOT | `gitbook/architecture/mapfield-discreteid-ssot.md` |
| 作者速查 | `gitbook/architecture/mapfield-howto.md` |
| 存储底座 | `gitbook/architecture/core-field2d.md` |
| Field Editor | `gitbook/architecture/field-editor.md` |
| 装载 | `src/Core/Fields/Config/`、`FieldSessionStore.cs`、`GameEngine.CreateFieldsForSession` |
| 运行时 | `src/Core/Gameplay/FieldRegions/` |
| 组件 | `src/Core/Components/FieldRegionComponents.cs` |
| 国家烘焙 | `tools/east_asia_borders/` |
| 标准 showcase | `field_layer_table`、`field_editor_paint`、`field_hierarchy_query`、`field_jing_yang_transit`、`field_east_asia_country`、`field_east_asia_admin`、`east_asia_borders_land_sea`（仅国家层） |

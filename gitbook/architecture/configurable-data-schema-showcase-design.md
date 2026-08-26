# 可配置数据结构 Showcase 设计

状态：**可玩交付完成（进程内验收已通过；Agent Bridge 实机截图可按 preset 补采）**。底层配置与面板投影能力随 Core 一并交付；本页定义并约束 Showcase 切片 `configurable-data-schema-showcase`。

## 一句话与目标用户

让新作者启动后，亲手改一条单位数据，立刻看见校验、面板绑定和换肤结果，而不是去读测试里的内嵌 JSON。

目标用户是第一次接触 Ludots 数据配置的内容作者和面板作者；他们不需要写 C#，也不应被迫手搓整份 JSON。

## 当前真相（禁止误报）

| 层 | 状态 | 证据 |
|---|---|---|
| Core 能力（schema / record / Data pin） | 已实现，适合作为能力 PR 合并 | PR #1216：`DataSchemaCatalog`、`DataSchemaRegistry`、`PanelProjectionReader` Data source、单元测试 |
| 真实作者资产 | Showcase 已交付非空示例 | `ConfigurableDataSchemaSharedMod/assets/Data/data_schemas.json` 与 `data_records.json` |
| Showcase Mod / preset / 启动入口 | 已交付 | `showcase.registry.json`：`configurable_data_schema` / `_native` / `_web`；preset `configurable_data_schema_*_raylib` |
| Agent Bridge 实机交互证据 | 领域工具已挂；实机截图按 preset 采证 | `ludots.dataschema.state` / `ludots.dataschema.author`；`artifacts/acceptance/configurable-data-schema-showcase/` |
| 作者编辑器 | MVP 已交付（宿主在 Showcase 工作台） | 四层 Schema/Record/Binding/Preview；写回 `DataSchemaModAssetWriter`；详见 [data-schema-authoring-workbench-design.md](data-schema-authoring-workbench-design.md) |

本 Showcase 做成“数据结构工作台”展示，而不是静态单位信息面板。动态轴是：

> 作者修改 schema 或 record → 校验结果变化 → 面板绑定结果实时变化。

## 主循环（约 60 秒）

1. 进入工作台，看到一个 `unit.scout` 示例。
2. 左侧显示 `struct`、嵌套 `position`、`tags` 数组和 `rarity` enum。
3. 作者修改 `position.x` 或 enum 值。
4. 右侧面板立即显示结构化数据变化。
5. 切换 Native / Web skin，数据和绑定不变。
6. 故意输入错误 enum 或删除必填字段。
7. 界面显示精确字段路径，保存按钮禁用。
8. 修复后重新校验并导出作者资产。

首屏引导文案建议：“先改 Scout 的坐标或稀有度，再看右侧面板；故意填错时，保存必须停住并指出字段路径。”

## 消融对照

| A | B |
|---|---|
| Graph source：只显示图输出 | Data source：同一面板显示嵌套 struct、数组和 enum |
| 单一 source | Graph / Data 混用；切换 skin 不改变数据来源 |

对照重点不是换皮视觉差异，而是玩家能看见：**同一套投影数据在换皮后仍然成立；换数据源才会改变面板内容。**

## 解释层

至少显示真实领域状态，不伪造第二份演示数据：

- 当前 schema 名称
- 当前 record 名称
- 当前绑定路径，例如 `position.x`
- 当前值及其类型
- 数组长度
- enum 当前名称和数值
- 校验状态：通过 / 错误数量 / 第一处错误路径

颜色只用于区分通过、警告、拒绝；右下角固定图例解释三种状态。

## 旋钮清单

| 旋钮 | 作用 | 演示什么 |
|---|---|---|
| Schema 选择 | 比较不同数据结构 | 同一工作台可切换多种 schema |
| Record 选择 | 比较同一 schema 的不同实例 | `unit.scout` 与其它 record 差异可见 |
| Binding path | 观察根节点、嵌套字段和数组项 | 路径选择器禁止手写错误路径 |
| Source mode | Graph / Data / Mixed | 消融对照 |
| Skin / backend | Native / Web | 渲染正交于数据 |
| Invalid case | 缺字段、错 enum、错类型、未知路径 | 失败可见，保存禁用，不静默回退 |

## 场景结构

主演示：数据结构工作台。左侧是示例 schema/record 与可改字段，右侧是真实 `PanelVariableSet` 预览与诊断。

子场景：

1. 合法编辑：改 `position.x` / `rarity`，右侧立即更新。
2. 消融对照：Graph only → Data only → Mixed。
3. 换肤：Native ↔ Web，绑定与数值不变。
4. 校验失败：缺必填、错 enum、错类型、未知路径；保存禁用并显示第一处错误路径。
5. 修复并导出：校验通过后写出目标 Mod 的 `Data/data_schemas.json`、`Data/data_records.json` 与面板模板。

## 门户资产（切片交付时必须齐）

| 资产 | 路径约定 |
|---|---|
| 设计文档 | `gitbook/architecture/configurable-data-schema-showcase-design.md`（本页） |
| UAT | `gitbook/acceptance/configurable-data-schema-showcase.feature` |
| Showcase Mod | `mods/showcases/configurable_data_schema/...`（计划） |
| 注册入口 | `showcase.registry.json` 增加真实 id / preset / binding |
| 启动 preset | launcher 可启动的 Raylib / Web 对照入口 |
| 非空示例资产 | Mod 内 `Data/data_schemas.json`、`Data/data_records.json`、面板模板 |
| 战报 / 轨迹 / 路径图 | `artifacts/acceptance/configurable-data-schema-showcase/` |
| Agent Bridge 实机截图 | 首屏、改值后、校验失败、换肤对照 |

预览页与面板读取同一份 schema/record；禁止复制第二份演示 JSON。

## 反向 API 审计

| 需要的接口 | 归属 | 状态 |
|---|---|---|
| Schema / Record 加载与严格校验 | Core `DataSchemaCatalog` / `DataSchemaRegistry` / `DataSchemaConfigLoader` | Core PR #1216 |
| ConfigPipeline `ArrayById` 合并 | Core ConfigPipeline | Core PR #1216 |
| 面板 Data pin（record + path） | Core `PanelProjectionReader` / `DataSchemaPanelProjectionSource` | Core PR #1216 |
| Graph / Data 混合 pin | Core `PanelTemplate` / `PanelVariableSet` | Core PR #1216 |
| Native / Web skin 只消费变量集 | Panel skins | 已有；Showcase 需对照入口 |
| Showcase Mod、preset、registry | Showcase 切片 | **已实现** |
| 工作台内临时改值与校验反馈 | Showcase 交互层 + Core `DataSchemaProjectionSession` | **已实现** |
| 写回 Mod 资产 | Showcase 导出到 acceptance/exported | **已实现（导出目录）**；正式作者写回见编辑器切片 |
| 隔离 preview session | Core `DataSchemaProjectionSession` | **已实现** |

## 交付边界与完成判据

本页是设计 SSOT，不是实现证明。

- Core PR #1216：只宣称配置加载、结构校验、路径投影与 Graph/Data 混合面板能力；**不宣称 Showcase 与作者编辑器完成**。
- Showcase PR `configurable-data-schema-showcase`：真实 Mod、registry、preset、非空示例、Native/Web 对照、Agent Bridge 实机 UAT 与截图齐备后，才可把本页状态改为“可玩交付完成”。
- 作者编辑器 PR `data-schema-authoring-workbench`：独立切片，见专页；不得混进 Showcase PR 冒充已完成。

```gherkin
Feature: 作者能在工作台里看见数据结构如何驱动面板

  Scenario: 修改 Scout 坐标后右侧面板立刻变化
    Given 我从数据结构工作台入口启动
    And 我看到 unit.scout 示例与右侧面板
    When 我把 position.x 改成另一个合法值
    Then 右侧面板显示同一个新的坐标
    And 解释层显示当前 schema、record、绑定路径、值与类型

  Scenario: 换肤不改变数据来源
    Given 面板同时绑了图输出和数据字段
    When 我在 Native 与 Web skin 之间切换
    Then 绑定路径和数值保持不变
    And 只有渲染外观改变

  Scenario: 非法数据会停住保存并指出字段路径
    Given 我正在编辑 unit.scout
    When 我填入未知的 rarity 或删掉必填字段
    Then 界面显示第一处错误路径
    And 保存按钮不可用
    And 系统不会静默写回旧值
```

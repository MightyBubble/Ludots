# 可配置数据结构作者编辑器设计

状态：**MVP 已交付（宿主在 Showcase 工作台内）**。四层 Schema / Record / Binding / Preview 可操作；校验失败禁用保存；通过 `DataSchemaModAssetWriter` 写回目标 Mod 的 `Data/*.json` 与 `Panels/panel_templates.json`。完整独立 capability 提取与 EntityRef 实体选择器增强仍可后续加深。

## 1. 概述

内容作者需要一份“懂 schema 的写作工作台”，而不是万能 JSON 文本框。

编辑器帮助作者：

1. 定义 struct / enum 与字段约束；
2. 按 schema 填写 record，不手填整数 enum；
3. 用树状路径把字段绑到面板；
4. 在真实面板预览里看见结果，失败时看见精确诊断。

“应用”优先定义为：校验并写回目标 Mod 的 `Data/data_schemas.json`、`Data/data_records.json` 和面板模板；正式运行时仍通过 `ConfigPipeline` 启动加载。需要实时预览时，使用隔离的 preview session，不直接替换正式运行中的 immutable registry。

目标用户：面板与内容作者。他们会改数据形状、改实例、改绑定，但不应被要求理解 ECS 布局或手写错误路径。

## 2. 结构

四层，单页工作台内分区，不拆成互不相通的工具：

| 层 | 职责 | 非职责 |
|---|---|---|
| Schema Designer | 新建 struct/enum，加字段，选类型，设 required，调整嵌套 | 不直接改运行中 ECS 组件 |
| Record Editor | 按 schema 生成表单；struct 子面板；array 增删排序；enum 下拉；EntityRef 走已有实体选择器 | 不提供绕过 schema 的自由 JSON 主路径 |
| Panel Binding Editor | 选 Graph / Data source；选 record；树状路径绑定；允许 Graph/Data pin 混用 | 禁止作者手写错误路径作为唯一绑定方式 |
| Preview and Diagnostics | 左侧编辑、右侧真实 `PanelVariableSet` 预览；Native/Web 同投影；校验错误带 schema/record/field/path | 不静默回退、不伪造第二份预览数据 |

宿主复用边界：

- 可复用 LiveSkillWorkbench 的 WebUI / DataPlane 宿主与连接方式；
- **不得**复用其面向 GAS graph / effect 的领域模型；
- 新增的是作者模型与编辑器 UI，不把动态 schema 变成 ECS component。

复用基建清单：

- `ConfigPipeline`
- `DataSchemaCatalog`
- `DataSchemaRegistry`
- `PanelTemplateLoader`
- `PanelProjectionReader`
- 已有实体选择器（EntityRef）
- LiveSkillWorkbench 的宿主/连接骨架（仅宿主）

## 3. 详情

### 3.1 Schema Designer

- 新建 `struct` / `enum`。
- 添加字段；类型可选 primitive、struct、enum、array。
- 设定 `required`。
- 拖拽调整嵌套层级；循环引用、未知引用、重复 enum 值在保存前拒绝。
- 输出形状与 Core 合同一致：写入 `data_schemas.json` 的 ArrayById 条目。

### 3.2 Record Editor

- 根据当前 schema 自动生成表单。
- struct 展开为子面板。
- array 支持添加、删除、排序。
- enum 使用名称下拉，不让作者手填整数；显示对应稳定数值只作解释。
- EntityRef 使用已有实体选择器。
- 未知字段、缺必填、错类型、未知 enum 立即进入诊断列表。

### 3.3 Panel Binding Editor

- source 选择 Graph 或 Data。
- Data source 选择 record。
- 用树状路径选择器绑定字段（根、嵌套、数组项）。
- 禁止把“手写任意路径”当作默认成功路径；未知路径在预检阶段失败。
- 同一模板允许 Graph pin 与 Data pin 混用。

### 3.4 Preview and Diagnostics

- 左侧作者编辑，右侧真实 `PanelVariableSet` 预览。
- 同一套投影数据切换 Native / Web skin。
- 诊断至少包含：schema、record、field、path、错误类别、错误数量、第一处错误路径。
- 保存失败必须可见；保存按钮在存在阻塞错误时禁用。
- preview session 与正式 immutable registry 隔离；确认写回后才通过目标 Mod 资产进入下次正式加载。

### 3.5 写回合同

目标文件（相对目标 Mod 根）：

- `Data/data_schemas.json`
- `Data/data_records.json`
- 面板模板（既有 Panel 模板路径与装载合同，不另造平行格式）

写回顺序：校验 → 生成写回计划 → 原子写盘（失败则整次拒绝）→ 诊断区报告结果。不做静默部分写回。

## 4. 场景

### 场景 A：从零定义 Scout

作者新建 `point` struct、`rarity` enum、`unit` struct，再创建 `unit.scout` record，把 `position.x` 绑到面板。右侧预览显示 Scout 坐标；导出后冷启动仍能读到同一路径。

### 场景 B：改实例不改形状

作者只改 `unit.scout` 的 `tags` 数组与 `rarity`，schema 不变。预览立即更新数组长度与 enum 名称/数值。

### 场景 C：混绑与换肤

作者在同一面板放一个 Graph pin 和一个 Data pin，切换 Native/Web。数据来源与数值不变，只有外观变。

### 场景 D：失败可见

作者删掉必填 `name`，或把 `rarity` 改成未知名，或绑定不存在的路径。诊断列出精确路径，保存禁用；修复前不得写盘。

## 5. 边界

- 不支持 map、union、递归 struct、动态 ECS component。
- enum 必须显式稳定整数值，不能依赖声明顺序。
- registry 适合配置/面板数据源；热路径 SoA 编译不在本切片范围。
- Native skin 仍只接受其既有数值合同；结构化节点由支持节点读取的 skin 消费。
- 不把动态 schema 物化为 ECS 组件布局。
- 不引入平行配置装载器或第二份预览真相源。
- 不复用 LiveSkillWorkbench 的 GAS 领域模型与热应用语义来“顺便”改数据 schema。
- Showcase 切片可以先用预置非法 case 旋钮演示校验；完整 Schema/Record/Binding 编辑 UI 属于本切片。

## 6. UAT

正式 Cucumber 见 `gitbook/acceptance/data-schema-authoring-workbench.feature`。摘要：

```gherkin
Feature: 作者用懂 schema 的工作台改数据和面板绑定

  Scenario: 按 schema 生成的表单能改嵌套字段
    Given 我打开数据结构作者工作台并选中 unit.scout
    When 我在表单里修改 position.x 并选择 rarity
    Then 右侧预览显示新的坐标和稀有度名称
    And 解释层显示绑定路径、类型、enum 数值

  Scenario: 非法编辑不能保存
    Given 我正在编辑一条必填字段齐全的 record
    When 我删除必填字段或选择未知 enum
    Then 诊断区显示 schema、record 与字段路径
    And 保存不可用
    And 目标 Mod 资产文件内容不变

  Scenario: 路径选择器阻止错误绑定
    Given 我在面板绑定编辑器中选择 Data source 与 unit.scout
    When 我从树中选择 position.x
    Then 绑定成功且预览可读到该路径
    And 我不需要手写路径字符串也能完成绑定

  Scenario: 预览换肤不改数据
    Given 同一面板混用了 Graph pin 与 Data pin
    When 我切换 Native 与 Web 预览
    Then 投影数据保持不变
```

## 切片建议

| PR | 内容 | 完成判据 |
|---|---|---|
| #1216 Core | 配置、校验、投影 | 单元/配置测试通过；不宣称 Showcase/编辑器完成 |
| `configurable-data-schema-showcase` | 可玩工作台展示 | registry、preset、非空资产、实机 UAT/截图 |
| `data-schema-authoring-workbench`（本页） | 四层作者编辑器 | 表单编辑、路径绑定、诊断、写回 Mod、Cucumber UAT |

实现顺序：先 Showcase 让新作者看见主循环，再本编辑器替换手写 JSON。

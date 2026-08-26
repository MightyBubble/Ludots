# Configurable Data Schema and Panel Projection

## 1. 概述

内容作者只编辑 JSON，就能定义并加载自定义数据结构。第一版支持基础值、嵌套 `struct`、同质数组和显式数值 `enum`。数据作为不可变 registry 树存在，不生成动态 C# 类型，也不改变 ECS 组件布局。

## 2. 结构

- `DataSchemaCatalog`：定义 struct/enum 及类型引用。
- `DataSchemaRegistry`：加载 records，严格校验并按路径读取节点。
- `PanelProjectionReader`：按 pin 的 source 选择 Graph 或 Data source。
- `PanelVariableSet`：同时提供旧的数值读取和结构化 `JsonNode` 读取。
- Native/Web skin：只消费变量集，不访问 Graph store 或 Data registry。

## 3. 详情

配置入口是 `assets/Data/data_schemas.json` 和 `assets/Data/data_records.json`，由 `ConfigPipeline` 按 `ArrayById` 合并。未知字段、未知引用、循环 struct、重复 enum 值、数组超限和嵌套超限都会拒绝整次加载。

面板 pin 的 `source` 可以是 `graph` 或 `data`。数据 pin 声明 `record` 与点号路径，例如 `position.x`。Graph pin 保留原有 key/default 合同；数据 pin 缺失路径直接报告 panel、pin、record 和 path，不静默使用默认值。

## 4. 场景

### 内容作者

作者新增一个 `unit` struct、`rarity` enum 和 `unit.scout` record，不写 C#；启动后面板可展示 `position.x`、名字和数组内容。

### 运行时

Graph 与 Data pin 可以出现在同一个模板里。替换 skin 或渲染后端只改变消费方式，不改变 schema 或 projection。

## 5. 边界

- 不支持 map、union、递归 struct 和动态 ECS component。
- enum 必须显式声明稳定整数值，不能依赖声明顺序。
- registry 适合作为配置/面板数据源；若进入 ECS 热路径，应另行编译为 SoA/索引数据，不能逐对象分配。
- Native skin 仍只接受数值 pin；结构化 pin 由支持 `GetNode`/`GetValue` 的 skin 消费。

## 6. UAT

```gherkin
Feature: Load custom data without C# types

  Scenario: Display nested configured data in a panel
    Given a JSON schema declares a unit with a nested position struct, tags array, and rarity enum
    And a JSON record named "unit.scout" contains that shape
    When the engine loads the configuration
    Then the panel can read "unit.scout" path "position.x"
    And the panel receives the configured array and enum values

  Scenario: Keep graph and data projection independent from skin
    Given one panel template has graph pins and data pins
    When a native or web skin renders the panel
    Then both sources are read through the same variable set
    And changing the skin does not change the loaded data

  Scenario: Reject an invalid data path
    Given a panel data pin points to a missing path
    When the panel evaluates
    Then evaluation fails with the panel, pin, record, and path in the error
    And no empty value is rendered silently
```

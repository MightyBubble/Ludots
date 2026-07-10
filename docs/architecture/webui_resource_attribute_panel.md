# WebUI Resource Attribute Panel（WPK-2）

Resource 是 Attribute / Graph 输出的展示视图，不是平行资源系统。本层在 WPK-1 Panel Kit manifest 之上提供 descriptor 与 DataPlane topic 合同，供资源栏等面板统一展示单体、派生与跨实体聚合数值。

深度实现：`src/Libraries/Ludots.WebUI.PanelKit/`（`WebUiResourceAttribute*`）。组合仍走 [WebUI Panel Kit Manifest](webui_panel_kit_manifest.md)；订阅仍走 [WebUI DataPlane](webui_dataplane_architecture.md)。

## 1. 概述

WPK-2 建立 Resource / Attribute 聚合面板的最小合同：

- Mod 作者用 JSON descriptor 声明展示字段、分组、单位/token、排序与 value source 类型。
- 同一 descriptor 可同时声明单实体 attribute 与玩家级聚合投影字段。
- DataPlane topic payload 固定包含 `owner`、`descriptor`、`revision`、`values`。
- 跨实体合计只读 `GraphOutputValueStore`（或等价 Core 投影），禁止在 Web / showcase 手写求和。
- 缺 attribute、graph output、descriptor、token 时 fail-fast，错误含具体 id。

## 2. 结构

```text
Resource attribute descriptor JSON
    -> WebUiResourceAttributeDescriptorLoader（结构 + 引用校验）
        -> WebUiResourceAttributeDescriptor（fields）
            -> WebUiResourceAttributeTopicProducer（IWebUiTopicProducer）
                -> payload: owner / descriptor / revision / values
            -> WPK-1 panel manifest.topic 引用同一 DataPlane topic
```

| 字段 | 含义 |
|------|------|
| `descriptorId` | 描述符稳定 id |
| `fieldId` | 字段稳定 id；同一 descriptor 内唯一 |
| `groupId` | 展示分组 id |
| `displayTokenId` / `unitTokenId` | 文案/单位 token；必须已注册 |
| `sortOrder` | 展示排序 |
| `sourceKind` | `singleAttribute` / `derivedAttribute` / `aggregateProjection` |
| `attributeId` | 单体或派生 attribute 名（前两种 source 必填） |
| `graphOutputKey` | Graph 输出键（聚合 source 必填） |

## 3. 详情

### 3.1 复用

- WPK-1：`WebUiPanelKitManifest` / topic / profile / layout / `UiSurfaceHost` 绑定。
- GAS：`AttributeBuffer`、`AttributeRegistry`、`AttributeAggregatorSystem`、`AttributeDerivedGraphBinding`（派生值已写入 buffer 后面板只读）。
- Graph：`GraphOutputValueStore` / `GraphOutputValueView` 作为跨实体合计投影 SSOT。
- DataPlane：`IWebUiTopicProducer`、`WebUiDataPlaneRuntime.IsTopicRegistered`。

### 3.2 新增

- `WebUiResourceAttributeDescriptor` + loader/validator + sample catalog。
- `WebUiResourceAttributeTopicProducer` + snapshot payload 合同。
- Sample：`Samples/sample_resource_attribute_descriptor.json`（通用 id，无游戏硬编码资源名）。

### 3.3 Value source

| sourceKind | 读哪里 | 谁负责计算 |
|------------|--------|------------|
| `singleAttribute` | owner 的 `AttributeBuffer` | 玩法写入 / 聚合系统 |
| `derivedAttribute` | owner 的 `AttributeBuffer`（派生结果槽） | `AttributeAggregatorSystem` + derived graph |
| `aggregateProjection` | owner 上的 `GraphOutputValueStore` 键 | GAS graph query/aggregation / GraphReturnWriter |

### 3.4 Fail-fast

未知 `attributeId` / `displayTokenId` / `unitTokenId` / `graphOutputKey`（若提供注册表）/ 缺失 graph output / 缺失 AttributeBuffer 槽，一律抛 `InvalidOperationException`，消息含具体 id。禁止空串、Unknown、默认 0 静默放过。

### 3.5 Browser RTS showcase 切换点

`BrowserRtsProductionShowcaseDataPlane.BuildResourceChips` 仍含 flavor switch 与 `ReadTeamAttributeTotal` 手写合计，属于待迁移债务。正式切换应：

1. 为 showcase 提供 resource attribute descriptor（字段引用真实 attribute / graph output id）。
2. 用 graph/aggregation 把玩家级合计写入 `GraphOutputValueStore`。
3. 注册 `WebUiResourceAttributeTopicProducer`，并在 WPK-1 manifest 的 resource panel `topic` 上引用它。
4. 删除 flavor 资源名列表与 showcase 内跨实体求和。

本 issue 先落地可复用 descriptor/topic 层；showcase 全量切换可作为后续切片。

## 4. 场景

- C&C：电力、资金、人口 — 单体建筑 attribute + 玩家级 graph 合计同 descriptor。
- 星际/帝国：矿、气、木、食物 — 字段 id 来自 mod 配置，不进通用 PanelKit。
- 群星/CK3：国家资源、月收入 — 聚合字段只读 graph output。

## 5. 边界

- 不创建 `ResourceStore`。
- 不在 Web 或 showcase 通用层手写跨实体求和。
- 不把具体游戏资源名写进 PanelKit / DataPlane 通用代码。
- 不新增平行 panel manifest / host / DataPlane。
- 不修改 GAS graph op / `*_profiles.json` / effect preset / entity lifecycle（本切片只读已有投影）。

## 6. UAT

```gherkin
Feature: 资源栏聚合显示
  Scenario: 同一描述符同时展示单体与玩家合计
    Given resource panel descriptor 声明了单体 attribute 字段和聚合投影字段
    And 对应的 attribute、graph output、display/unit token 都已注册
    When DataPlane 发布资源 attribute topic revision
    Then payload 包含 owner、descriptor、revision 与 values
    And values 同时含单实体数值与玩家级合计
    And 合计来自 GraphOutputValueStore 投影而不是浏览器遍历实体

  Scenario: 缺引用时失败
    Given descriptor 引用了未注册的 attribute 或 token 或缺失的 graph output
    When 加载 descriptor 或生产 topic snapshot
    Then 操作失败
    And 错误信息包含缺失的具体 id

  Scenario: 浏览器只订阅 manifest 声明的 topic
    Given WPK-1 panel manifest 的资源栏 topic 指向 resource attribute producer
    When 绑定 panel kit surface
    Then 浏览器订阅列表恰好等于 manifest 声明的 topic
```

## 源码与测试

- 库：`src/Libraries/Ludots.WebUI.PanelKit/WebUiResourceAttribute*.cs`
- Sample：`src/Libraries/Ludots.WebUI.PanelKit/Samples/sample_resource_attribute_descriptor.json`
- 测试：`src/Tests/WebUiPanelKitTests/WebUiResourceAttributePanelTests.cs`

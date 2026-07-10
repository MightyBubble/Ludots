# WebUI Panel Kit Manifest（WPK-1）

正式组合合同：下游 mod 用配置声明有哪些面板、挂在哪个 surface、订阅哪个 topic、使用哪个 profile / layout。本层只描述面板组合与引用，不承载玩法真相。

深度实现：`src/Libraries/Ludots.WebUI.PanelKit/`。Surface 所有权仍以 [UI 渲染控制与 Surface 所有权](../../gitbook/architecture/ui-rendering-and-surface-ownership.md) 为准；数据订阅仍以 [WebUI DataPlane](webui_dataplane_architecture.md) 为准。

## 1. 概述

WPK-1 建立 WebUI Panel Kit 的最小面板 manifest：

- Mod 作者用 JSON 声明面板列表与引用 id。
- 加载期校验 topic / profile / layout / surface region / density / input capability / visible condition；缺失即 fail-fast，错误信息包含具体 id。
- 绑定到已有 `UiSurfaceHost`，不新建 Web runtime、host、DataPlane 或本地 UI 真相。
- 浏览器侧订阅列表只来自 manifest 声明的 topic，不猜测实体、资源或命令来源。

## 2. 结构

```text
Mod JSON manifest
    -> WebUiPanelKitManifestLoader（结构 + 引用校验）
        -> WebUiPanelKitManifest（panel 列表 + DeclaredTopics）
            -> WebUiPanelKitSurfaceBinder -> IUiSurfaceHost（每 panel 一张租约）
            -> BrowserSubscriptionTopics（仅 manifest 声明的 topic）
```

| 字段 | 含义 |
|------|------|
| `panelId` | 面板稳定 id；同一 manifest 内唯一 |
| `panelType` | 面板类型标签（如 `resource-bar` / `command-deck` / `objective`），不含具体游戏语义 |
| `surfaceRegionId` | surface 区域引用 |
| `surfaceSegment` / `surfacePriority` | 映射到 `UiSurfaceSegment` 与租约优先级 |
| `anchor` | 布局锚点 id |
| `visibleConditionId` | 可见条件引用 |
| `topic` | DataPlane topic；必须已注册 |
| `profileId` | 展示 profile 引用 |
| `layoutId` | 布局引用 |
| `densityId` | 密度引用 |
| `inputCapabilityId` | 输入能力引用 |

## 3. 详情

### 3.1 复用

- `IUiSurfaceHost` / `UiSurfaceContribution` / `UIRoot`：唯一 UI 组合入口。
- `WebUiDataPlaneRuntime.IsTopicRegistered` / `GetRegisteredTopics`：加载期 topic 存在性校验。
- 不复用 `SelectionRuntime`（已退役）；命令相关面板后续只引用显式 entity / collection / control view topic。

### 3.2 新增

- `Ludots.WebUI.PanelKit`：manifest 合同、id registry、loader、surface binder、sample catalog。
- Sample：`Samples/sample_panel_kit_manifest.json`（资源栏、命令栏、目标、生产概览、通知、科技树面板，通用 id，无游戏硬编码名）。
- TechTree / Progression 展示合同见 [WebUI TechTree / Progression Panel (WPK-9)](webui_techtree_progression_panel.md)。
- 跨类型独立 showcase 见 [WebUI Panel Kit Showcase Family (WPK-10)](webui_panel_kit_showcase_family.md)。

### 3.3 Fail-fast

未知 `topic` / `profileId` / `layoutId` / `surfaceRegionId` / 重复 `panelId` 一律抛 `InvalidOperationException`，消息含具体 id。禁止空串、Unknown、默认猜测。

## 4. 场景

- RTS：底部命令栏、右侧生产概览、左上资源栏 — 同一 host，不同 region / topic / profile。
- 4X：顶部资源栏、左侧任务、右侧舰队面板 — 换 profile / topic，不换组合合同。
- 大战略：人物 / 国家 / 军队概览复用同一 manifest 形状，只换引用 id。

## 5. 边界

- 不做 Resource / CommandDeck / Quest 业务逻辑；只做装配合同。
- Resource / Attribute 展示合同见 [WebUI Resource Attribute Panel (WPK-2)](webui_resource_attribute_panel.md)。
- 不创建平行 Web host / DataPlane / 本地 UI 真相。
- 不把 CK3、群星、C&C、AoE、StarCraft 的资源名 / 单位名 / 游戏名写进通用 panel kit。
- 不恢复 `SelectionRuntime`；玩家文案可说“当前实体”，代码只能是显式 entity / view / command source / control view。

## 6. UAT

```gherkin
Feature: 面板组合合同
  Scenario: 新玩家打开 showcase 时多个面板稳定挂载
    Given mod 声明了资源栏、命令栏、目标、生产概览、通知和科技树面板
    And 对应的 topic、profile、layout、surface region 都已注册
    When 游戏加载 WebUI surface 并绑定 panel kit manifest
    Then 这些面板都挂在同一个 UI Host 中
    And 它们按 manifest 的区域与优先级显示
    And 浏览器订阅列表恰好等于 manifest 声明的 topic

  Scenario: 缺引用时加载失败
    Given mod 声明了一个面板但 topic 或 profile 或 layout 或 surface region 未注册
    When 加载 panel kit manifest
    Then 加载失败
    And 错误信息包含缺失的具体 id

  Scenario: 重复面板 id 被拒绝
    Given mod 在同一 manifest 里写了两个相同的 panelId
    When 加载 panel kit manifest
    Then 加载失败并报告重复的 panelId
```

## 源码与测试

- 库：`src/Libraries/Ludots.WebUI.PanelKit/`
- 测试：`src/Tests/WebUiPanelKitTests/WebUiPanelKitManifestTests.cs`
- Sample：`src/Libraries/Ludots.WebUI.PanelKit/Samples/sample_panel_kit_manifest.json`

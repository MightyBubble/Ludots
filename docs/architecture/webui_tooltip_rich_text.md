# WebUI Tooltip + Rich Text（WPK-5）

Tooltip 是轻量信息面板投影，不是新信息系统。实体、技能等 tooltip 复用 `EntityInsightProfile`、`PresentationTextCatalog` 与 ability presentation token；Web payload 只传结构化 rich-text blocks/runs，禁止 HTML 字符串与英文 fallback。

深度实现：`src/Libraries/Ludots.WebUI.PanelKit/`（`WebUiRichText*`、`WebUiTooltip*`）与 `src/Core/Gameplay/GAS/Config/AbilityPresentationTextValidator.cs`。组合仍走 [WebUI Panel Kit Manifest](webui_panel_kit_manifest.md)；订阅仍走 [WebUI DataPlane](webui_dataplane_architecture.md)；实体真相仍走 [Entity Insight Panel](entity_insight_panel_architecture.md)。

## 1. 概述

WPK-5 建立 Tooltip / 富文本 token 合同：

- 可复用 rich-text：blocks → runs，role 含 text / emphasis / token / icon / value / state。
- Tooltip descriptor 声明 target、profile、template、locale、anchor、sections；DataPlane payload 含 target、profile、template、locale、revision、sections、state flags、anchor。
- Entity tooltip 复用 `EntityInsightProfile`（title/body/badges/stats/actions token），不新增平行 TooltipProfile。
- Ability 文案可声明 `displayNameToken` / `hintTextToken` / `modeHintTokens`；loader 编译 token 字段，`AbilityPresentationTextValidator` 校验 token + locale 覆盖。
- 缺 token、locale、profile、template、unknown run role 时 fail-fast，错误含具体 id。禁止 `Unknown`、`Ability#123`、空串、HTML、英文硬编码 fallback 进入可复用 tooltip payload。

## 2. 结构

```text
PresentationTextCatalog / EntityInsightProfile / AbilityPresentationConfig(tokens)
    -> WebUiTooltipDescriptorLoader（结构 + 引用校验）
        -> WebUiTooltipDescriptor（sections → rich-text blocks/runs）
            -> WebUiTooltipTopicProducer（IWebUiTopicProducer）
                -> payload: target / profile / template / locale / revision / anchor / stateFlags / sections
            -> WPK-1 panel manifest.topic 可引用同一 DataPlane topic
```

| 字段 | 含义 |
|------|------|
| `descriptorId` | 描述符稳定 id |
| `targetKind` | `entityInsight` / `ability` |
| `profileId` | EntityInsightProfile id（实体）或展示 profile 引用；实体路径必须能解析到 Insight profile |
| `templateId` | tooltip 模板引用 |
| `localeId` | 本地化 locale；token 必须有该 locale 模板 |
| `anchor` | 锚点 id（如 cursor） |
| `sections[].blocks[].runs[]` | 结构化富文本；role + 对应内容通道 |

## 3. 详情

### 3.1 复用

- WPK-1：`WebUiPanelKitManifest` / topic / profile / layout / `UiSurfaceHost`。
- EntityInfo：`EntityInsightProfile` / `EntityInsightProfileLoader` / `EntityInsightTextResolver` / `PresentationTextCatalog`。
- Ability：`AbilityPresentationConfig` + `AbilityExecLoader` 编译 `displayNameToken` / `hintTextToken` / `modeHintTokens`。
- DataPlane：`IWebUiTopicProducer`、`WebUiDataPlaneRuntime.IsTopicRegistered`。

### 3.2 新增

- `WebUiRichTextRun` / `WebUiRichTextBlock` + role 枚举与 HTML 拒绝守卫。
- `WebUiTooltipDescriptor` + loader/validator + sample catalog + topic producer。
- `AbilityPresentationTextValidator`：token/locale 覆盖 fail-fast；GameEngine 在加载 PresentationTextCatalog 后对已声明 token 的 ability 做校验。
- Sample：`Samples/sample_tooltip_descriptor.json`（通用 id，无游戏硬编码名）。

### 3.3 Entity 边界

Entity tooltip 的 `profileId` 必须是已注册的 `EntityInsightProfile` id。投影 DTO（`WebUiTooltipEntityInsightProjection`）只搬运 Insight 已有的 token 引用，不拥有第二套实体真相。禁止新建 `TooltipProfile` 平行体系。

### 3.4 Ability token 迁移

| 状态 | 行为 |
|------|------|
| 声明了 `displayNameToken` / `hintTextToken` / `modeHintTokens` | 必须通过 token + locale 校验；缺失即失败 |
| 仍只有最终字符串 `displayName` / `hintText` / `modeHints` | 编译仍允许（存量 showcase 债务）；**token 校验路径拒绝**把它们当作已 token 化；不得静默当 fallback |

剩余迁移点（本切片不批量改 showcase JSON）：

- `mods/showcases/**/assets/GAS/abilities.json` 中大量最终字符串 presentation。
- `AbilityPresentationConfig.ResolveDisplayName` / `ResolveHintText` 仍接受 fallback 参数（遗留 UI 路径）；Web tooltip 不得调用它们拼 payload。
- UxPrototype 等 showcase 本地 `TooltipTitle`/`TooltipBody` 字符串气泡，不属于可复用 PanelKit 合同。

### 3.5 Fail-fast

未知 token / locale / profile / template / run role / 缺失 locale 模板 / HTML 文本 / `Unknown` / `Ability#N`，一律抛 `InvalidOperationException`（或 AggregateException 汇总），消息含具体 id。

## 4. 场景

- 玩家悬停单位头像：看到本地化名称、定位、属性、可用动作（来自 EntityInsight 投影）。
- 玩家悬停技能格：看到消耗、冷却、阻塞原因与目标说明（ability token + state/value runs）。
- 玩家悬停科技节点：后续可用同一 rich-text / tooltip descriptor 形状，换 profile/token，不新建信息系统。

## 5. 边界

- 不做浏览器侧实体查询；tooltip 数据来自 Core / DataPlane 投影。
- 富文本是语义 runs，不是 HTML 片段。
- 不新建平行 TooltipProfile / 玩法真相 store。
- 不把具体游戏名、单位名、技能名写进 PanelKit 通用代码。
- 不修改 GAS graph op / `*_profiles.json` / effect preset / entity lifecycle（本切片只投影与校验）。

## 6. UAT

```gherkin
Feature: Tooltip 本地化和富文本
  Scenario: 技能说明 token 缺失时拒绝启动
    Given 一个技能配置引用了不存在的说明 token
    When 地图加载技能配置并校验 PresentationText 覆盖
    Then 启动失败并指出缺失 token
    And 玩家不会看到空 tooltip、Unknown 或英文兜底

  Scenario: 悬停单位看到结构化本地化说明
    Given 单位绑定了 EntityInsightProfile 且 token/locale 齐全
    And tooltip descriptor 的 profileId 指向该 Insight profile
    When 玩家把指针停在单位头像上
    Then DataPlane 发布含 target、profile、template、locale、revision、sections 的 tooltip snapshot
    And sections 是 blocks/runs，不是 HTML 字符串

  Scenario: 缺 profile 或未知 run role 时失败
    Given tooltip descriptor 引用了未注册的 profile、template、locale、token 或未知 run role
    When 加载 descriptor 或生产 topic snapshot
    Then 操作失败
    And 错误信息包含缺失的具体 id
```

## 源码与测试

- 库：`src/Libraries/Ludots.WebUI.PanelKit/WebUiRichText*.cs`、`WebUiTooltip*.cs`
- Ability：`src/Core/Gameplay/GAS/Config/AbilityPresentationTextValidator.cs`、`AbilityExecLoader` token 字段
- Sample：`src/Libraries/Ludots.WebUI.PanelKit/Samples/sample_tooltip_descriptor.json`
- 测试：`src/Tests/WebUiPanelKitTests/WebUiTooltipPanelTests.cs`

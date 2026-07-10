# WebUI Notification Panel（WPK-7）

游戏消息 / 提示 / 告警的独立 SSOT 与 WebUI 面板投影。Notification 不混进 Quest、NarrativeFrontend 或 showcase 临时 toast 状态。

深度实现：`src/Libraries/Ludots.WebUI.DataPlane/Notification*.cs`。面板组合仍以 [WebUI Panel Kit Manifest (WPK-1)](webui_panel_kit_manifest.md) 为准；文案 token / locale 校验复用 [WebUI Tooltip + Rich Text (WPK-5)](webui_tooltip_rich_text.md) 合同钩子；命令触发复用 `WebUiCommandRouter`。

## 1. 概述

WPK-7 建立可复用的 Notification 运行时与面板投影：

- 独立 `NotificationRuntime`：接收领域事件投影成消息（id / category / severity / ttl / priority / dedupe key / actions / text token）。
- 独立 DataPlane topic：发布有序 snapshot + revision；Web 只渲染 snapshot，不自己推断事件历史。
- 独立 profile：`profile.notification.generic`，支持 toast stack / event feed / warning banner / log review 等 panel kind。
- 文案必须是 PresentationText token；缺 token 或 locale 覆盖 fail-fast，禁止明文兜底。
- 至少一个 action 通过 `INotificationActionRegistry` 映射到已注册 WebUI command；未知 action fail-fast。

## 2. 结构

```text
Domain event projection
    -> NotificationRuntime (SSOT: messages + revision + dedupe/ttl)
        -> NotificationWebUiTopicProducer (DataPlane topic)
            -> NotificationWebSnapshot (profile / panelKind / locale / revision / notifications[])
                -> Panel Kit manifest panelType=notification 订阅该 topic
        -> INotificationActionRegistry -> WebUiCommandRouter command name
```

| 构件 | 职责 |
|------|------|
| `NotificationMessage` | id、category、severity、textToken、dedupe、priority、ttl、actions |
| `NotificationPanelProfile` | panel kind、severity/category 过滤、maxVisible、locale |
| `INotificationTextValidator` | WPK-5 token + locale 覆盖校验 |
| `INotificationActionRegistry` | actionId → 已注册 commandName |
| `NotificationRuntime` | 发布 / 去重 / TTL / 排序快照 / action 解析 |
| `NotificationWebUiTopicProducer` | 投影 JSON snapshot |
| `WebUiNotificationPanelDescriptors` | Panel Kit 稳定 panelType / profile / sample topic / sample action |

## 3. 详情

### 3.1 复用

- WPK-1：`panelType=notification`、`profile.notification.generic`、manifest topic、`UiSurfaceHost`
- WPK-5：`PresentationText` token 存在性 + locale 模板钩子（与 tooltip 同一 fail-fast 口径）
- DataPlane：`IWebUiTopicProducer`、`WebUiOutboundPacket`、LatestWins
- Command：`WebUiCommandRouter.Register(commandName, handler)`；notification action 只存 command 名引用

### 3.2 新增

- DataPlane：`NotificationContracts`、`NotificationPanelProfile`、`NotificationTextValidator`、`NotificationActionRegistry`、`NotificationRuntime`、`NotificationWebUiTopicProducer`
- PanelKit：`WebUiNotificationPanelDescriptors`；sample manifest 增加 `hud.notification`
- Sample topic：`panel-kit.sample.notification`
- Sample action：`action.notification.open-panel` → command `notification.openPanel`

### 3.3 Snapshot 形状

- `profileId`、`panelKind`、`localeId`、`revision`
- `notifications[]`：`id`、`categoryId`、`severity`、`textTokenId`、`dedupeKey`、`priority`、`ttlSeconds`、`createdAtSeconds`、`actions[]`
- `actions[]`：`actionId`、`commandName`、可选 `labelTokenId`、可选 `payload`

排序稳定：priority 降序 → severity 降序 → createdAt 升序 → id 升序；再按 profile `maxVisible` 截断。

### 3.4 Fail-fast

- 未知 / 未注册 text token
- 声明 locale 但缺 locale 模板（或未配置 WPK-5 locale 钩子）
- 未知 notification action id
- 空 id / 空 category / 非法 severity / 负 TTL
- 已过期消息不允许 publish

禁止空串兜底、Unknown、静默跳过坏数据、从 NarrativeFrontend / Quest / showcase toast 私有状态读真相。

## 4. 场景

- C&C：基地受攻击、建造完成、电力不足 — 同一 runtime，换 category / severity。
- 群星：研究完成、舰队抵达、外交事件 — 点击 action 打开对应面板（registered command）。
- CK3：角色事件、战争进展、决议可用 — event feed / warning banner profile，不换 SSOT。

## 5. 边界

- Notification 可以展示 Quest 事件投影，但不拥有 Quest 状态。
- 不替代 combat log 或 debug log。
- 不依赖 NarrativeFrontend NotificationStack。
- 不依赖 showcase toast 私有布尔状态。
- 不把具体游戏名 / 科技名写进可复用 PanelKit / DataPlane 代码。

## 6. UAT

```gherkin
Feature: 游戏通知
  Scenario: 科技完成后玩家收到通知
    Given 玩家正在研究一个科技
    When 科技完成事件进入 Notification runtime
    Then 通知面板显示本地化完成消息
    And 点击通知可以打开对应科技面板或定位来源
    And 消息不会由 Quest 或 NarrativeFrontend 私有状态维护

  Scenario: 缺文案 token 时拒绝发布
    Given 一条通知引用了未注册的 text token
    When 领域事件尝试写入 Notification runtime
    Then 发布失败
    And 错误信息包含缺失的具体 token id
    And 玩家不会看到空通知或英文兜底

  Scenario: 未知 action 被拒绝
    Given 一条通知声明了未注册的 action
    When 写入 Notification runtime 或解析点击
    Then 操作失败
    And 错误信息包含未知 action id
```

## 源码与测试

- Runtime / producer：`src/Libraries/Ludots.WebUI.DataPlane/Notification*.cs`
- PanelKit 描述符：`src/Libraries/Ludots.WebUI.PanelKit/WebUiNotificationPanelDescriptors.cs`
- Sample：`Samples/sample_panel_kit_manifest.json`（含 `hud.notification`）
- 测试：`src/Tests/WebUiDataPlaneTests/NotificationWebUiTopicProducerTests.cs`
- PanelKit 回归：`src/Tests/WebUiPanelKitTests/WebUiPanelKitManifestTests.cs`

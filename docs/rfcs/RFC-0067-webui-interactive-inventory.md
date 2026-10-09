# RFC-0067 WebUI 可点控件进入 AgentBridge 查询空间

Status: Proposed（对应 [#1493](https://github.com/MightyBubble/Ludots/issues/1493)；是 [RFC-0066](RFC-0066-agent-debug-bridge.md) §7「browser canvas 后续切片」的合同）

## 1. 概述

`ludots.ui.query` / `ludots.ui.tree` / `ludots.ui.click` 只能看见 UiScene 里的节点。WebUI 面板在 UiScene 里只是一块 `canvas`（例如 `X28.MainMenu-browser-surface`）。面板里「新游戏 / 载入游戏 / 设置」这些按钮活在浏览器 DOM 里，查询空间看不到，`ui.click` 也到不了浏览器输入口。

已经能用的两条路：

- `ludots.screenshot` 能取证
- `ludots.input.raw` 能经宿主真实输入管线点进面板（#1493 已复现：同一坐标 `input.raw` 能进选关页，`ui.click` 返回 `handled:false`）

缺的是**稳定 elementId + 矩形**，让验收脚本不用看像素、也不用手写坐标。

本 RFC 的结论：

1. **面板自己上报可点控件。** Web 应用把当前可见、可点的控件清单（稳定 id、角色、文案、矩形、可见性）发回宿主；查询工具把这份清单并进现有 `ui.tree` / `ui.query`；`ui.click` 改走 `UIRoot.HandleInput`，和玩家点这块 canvas 同一条口。
2. **不要内嵌 Chrome DevTools / CDP。** 那是给人看页面的调试器，不是自动化合同；而且绑死 CEF，Ultralight 与 UE5 BLUI 走不通。
3. **不要把整棵 DOM 拉进 Core。** RFC-0066 已禁止；`IBrowserMessageBridge.ExecuteScriptAsync` 也不是查询 API。

现成 Inspector（`src/Tools/Ludots.Inspector.React`）已经吃 `ui.tree`。清单投影进去之后，人用的面板不用另做一套「内置网页调试器」。

## 2. 结构

```text
Web 应用（Panel Kit / 主菜单等 Ludots 拥有的页面）
    用 getBoundingClientRect 收集可点控件
    -> window.ludotsDataplane 发 Control 包（kind = interactive-inventory）
        -> IWebUiDataTransport / IBrowserMessageBridge（只当传输）
            -> 宿主侧 InteractiveInventoryStore（挂在对应 browser canvas 上）
                -> UiScene 查询覆盖层（不进 Yoga 布局、不参与绘制）
                    -> ludots.ui.tree / ui.query / FindByElementId
                    -> ludots.ui.click
                        -> UIRoot.HandleInput（Pointer Down/Up，带 Left 键）
                            -> IUiCanvasInputSink（BrowserSurfaceCanvasContent.HandleInput）
                                -> IBrowserSurface.SendInputAsync
```

| 层 | 职责 | 不做什么 |
|----|------|----------|
| Web 应用 | 上报自己画出来的可点控件 | 不把 React fiber / 整棵 DOM 当合同 |
| DataPlane 传输 | 把 Control 包从页面送到宿主 | 不把清单变成玩法 topic，不建平行 host |
| `InteractiveInventoryStore` | 按 canvas `elementId` 存最新一份清单 + revision + drop | 不拥有像素、不拥有玩法真相 |
| UiScene 查询覆盖层 | `QuerySelectorAll` / `FindByElementId` / `ui.tree` 合并覆盖层节点 | 不把覆盖层节点塞进布局树 |
| `ludots.ui.click` | 解析 elementId 或坐标后走 `UIRoot.HandleInput` | 不再只 `scene.Dispatch`（那条路到不了 canvas sink） |

切片：

| 切片 | 内容 | 独立可验收 |
|------|------|------------|
| A | `ui.click` 改为 `UIRoot.HandleInput` + 显式 Left 键 Down/Up | 裸坐标点 WebUI canvas，面板真的响应；`handled` 跟 canvas sink 一致 |
| B | inbound Control `interactive-inventory` 合同 + 宿主 store | 未上报时 canvas 节点带 `browserInventory: "unpublished"`，禁止静默去扒 DOM |
| C | 查询覆盖层并进 `ui.tree` / `ui.query`；按 elementId 点击取覆盖层矩形中心 | `selector:"button"` 能命中已上报的面板按钮 |
| D | Panel Kit 共享前端助手，Ludots 拥有的 WebUI 面板必须上报 | 各 showcase 不再各写一套采集 |

## 3. 详情

### 3.1 复用清单

| 需求 | 复用 |
|------|------|
| 查询 / 选择器 | `UiScene.QuerySelectorAll` / `FindByElementId` / `HitTest`（`src/Libraries/Ludots.UI/Runtime/UiScene.cs`） |
| 玩家点 canvas 的输入口 | `UIRoot.HandleInput` → `IUiCanvasInputSink.HandleInput`（`src/Libraries/Ludots.UI/UIRoot.cs`） |
| 浏览器指针转发 | `BrowserSurfaceCanvasContent.HandleInput` → `IBrowserSurface.SendInputAsync`（`src/Libraries/Ludots.UI.Browser/BrowserSurfaceCanvasContent.cs`） |
| 页面 ↔ 宿主传输 | `window.ludotsDataplane` + `IBrowserMessageBridge`；UE5 BLUI 已承诺同一 facade（`docs/architecture/webui_dataplane_architecture.md`） |
| 页面 → 宿主包形 | 已有 `WebUiPacketKind.Control` + `WebUiControlEnvelope`（`src/Libraries/Ludots.WebUI.DataPlane/WebUiDataPackets.cs`） |
| canvas 定位 | `IUiBrowserCanvasContent` / `UiNode.CanvasContent` |
| 人用调试壳 | 现有 Inspector，不新增工具名 |
| 窗口层对照路径 | 现有 `ludots.input.raw`（`SyntheticInputDevice`），继续留给「验证真实键鼠」 |

### 3.2 为什么不内嵌 DevTools / CDP

| 选项 | 问题 |
|------|------|
| 在 CEF 里开 Chrome DevTools | 给人看的；AgentBridge 要的是稳定 elementId，不是 Styles/Network 面板 |
| 用 CDP `DOM.getDocument` / `DOM.querySelector` | 合同绑在 Chromium；Ultralight 没有同形 CDP；UE5 BLUI 也不该把 DevTools 协议送进 Core |
| 用 `ExecuteScriptAsync("document.querySelectorAll(...)")` 现场扒 DOM | 选择器跟 React 生成 class 走，没有稳定 id；RFC-0066 禁止把 Browser DOM 拉进 Core；未上报时静默扒 DOM 等于 fallback |

`IBrowserMessageBridge.ExecuteScriptAsync` 可以继续给适配器装 facade、做一次性诊断。它不是 `ui.query` 的实现。

### 3.3 点击为什么现在是瞎的

`UiClickTool`（`src/Libraries/Ludots.AgentBridge/Tools/UiTools.cs`）对命中节点发 `UiPointerEvent` Down/Up/Click，走 `UiScene.Dispatch`。

`UIRoot.HandleInput` 才会先问 `IUiCanvasInputSink`。浏览器面板的点击只在这条口上变成 `BrowserPointerEvent`。

所以 #1493 里同一坐标：

- `ui.click` → 命中 canvas → 没有 Markup 点击处理器 → `handled:false`，画面不变
- `input.raw` → 宿主轮询 → `UIRoot.HandleInput` → canvas sink → 进选关页

切片 A 只改点击入口，不改工具名。坐标点击 WebUI 面板应开始工作。语义发现仍要切片 B/C。

`ui.click` 仍然是 UI 层工具：走 `UIRoot.HandleInput`，不改走 `input.raw`。后者还会进世界指针路由，不该用来点一个已知的 UI 控件。

### 3.4 清单合同

页面按 surface 上报一份快照，Latest-Wins。字段：

```json
{
  "schemaVersion": 1,
  "surfaceElementId": "X28.MainMenu-browser-surface",
  "revision": 12,
  "viewport": { "w": 1920, "h": 1080 },
  "elements": [
    {
      "elementId": "main-menu-new-game",
      "tag": "button",
      "role": "button",
      "text": "新游戏",
      "classes": ["menu-item"],
      "visible": true,
      "enabled": true,
      "localRect": { "x": 640, "y": 320, "w": 240, "h": 48 }
    }
  ],
  "dropped": 0
}
```

约束：

- `elementId` 由 Ludots 页面赋予（`data-ludots-id` 或已有控件 id），禁止用框架生成的临时 id。
- `localRect` 是浏览器视口坐标（`getBoundingClientRect`）。宿主用 canvas `GetContentRect` 与 `BrowserViewport` 的现有缩放，转成与 `UiNode.LayoutRect` 同一套屏幕坐标。禁止页面自己猜窗口偏移。
- 只报**当前可见且可交互**的控件（button / link / input / 带 click 角色的节点）。装饰、隐藏、`pointer-events:none` 不上报。
- 容量显式封顶（建议 256；超出写入 `dropped`，禁止静默截断不记账）。
- `revision` 每次布局或可见性变化递增；查询覆盖层只保留最新一份。

未收到清单、或 `surfaceElementId` 对不上任何 canvas：

- canvas 节点带 `browserInventory: "unpublished"`（或 `"stale"`，若上一份 revision 超时——超时阈值实现时再定，缺省则只有 unpublished）
- `ui.query` 不会假装扫到 DOM 里的 `button`
- 禁止 CEF 脚本兜底

### 3.5 查询覆盖层（不进布局树）

覆盖层节点只出现在：

- `ui.tree` 里对应 canvas 的 `children`（序列化视图）
- `QuerySelectorAll` / `FindByElementId`

它们**不是** Yoga 子节点，不绘制，不参与 `UiScene.HitTest` 的布局命中。真实命中仍以 canvas + alpha / bounds 为准。`ui.click(elementId)` 用覆盖层矩形中心换成屏幕坐标，再交给 `UIRoot.HandleInput`。

若把这些节点做成真子节点，Yoga 会重排 canvas，绘制也会画到幽灵控件上。

### 3.6 AgentBridge 工具名

不新增 `ludots.ui.browser.*`。#1493 要的是 WebUI 面板和 Markup 面板用同一套 `ui.query` / `ui.click`。

`ui.tree` 在 browser canvas 上补：

- `canvasContent`（已有）
- `browserInventory`: `published` / `unpublished`
- `inventoryRevision`（已发布时）

### 3.7 谁必须上报

Ludots 拥有、走 Panel Kit / DataPlane 的 WebUI 面板：切片 D 之后必须上报，缺清单视为面板合同失败（加载期或首帧后显式错误，不静默）。

任意 URL、非 Ludots 页面（`BrowserUiShowcaseMod` 那种）：允许 `unpublished`。自动化继续用 `input.raw` + `screenshot`。

## 4. 场景

### 4.1 主菜单点「新游戏」

QA 脚本对挂着浏览器主菜单的宿主：

1. `ludots.ui.query { selector: "button" }` 拿到 `main-menu-new-game` 和屏幕矩形
2. `ludots.ui.click { elementId: "main-menu-new-game" }`
3. 下一帧 `ludots.screenshot` / `ui.tree` 看到进入选关，而不是停在主菜单

不再用手写 `{x:691,y:388}`，也不用视觉模型认按钮。

### 4.2 Inspector 里展开 canvas

人打开 Inspector 调 `ui.tree`。browser canvas 下面能看到「新游戏」节点和矩形，和 Markup 工具栏按钮同一套形状。不需要另开 Chrome DevTools。

### 4.3 进度条 / 飘字断言

验收要断言「进度条在、文案是 70%」。清单里带 `role`/`text`/`localRect` 的可见节点即可。像素对比只做截图证据，不当主断言。

### 4.4 对照：真实键鼠

仍用 `input.raw` 验证「从窗口灌进去的指针」和 UI 层 `ui.click` 观感一致（hover、capture）。两者都经 `UIRoot.HandleInput` 进 canvas sink；`input.raw` 多一截宿主轮询。

## 5. 边界

- 不把 CEF DevTools、CDP、Ultralight inspector 做成 AgentBridge 工具。
- 不在 Core 持有 DOM 树、CSSOM、shadow tree。
- 不把清单做成玩法 topic，不新建平行 UI 真相。玩法数据仍只从 DataPlane 宿主侧 topic 流向页面。
- 覆盖层节点不进布局、不绘制、不改 `HitTest` 几何。
- 未上报禁止静默扒 DOM。
- `ui.click` 不改走 `input.raw`，以免 UI 点击漏进世界点选。
- 不改工具注册表去加平行 browser 查询工具。
- 宿主未实现 `UIRoot` / 无挂载 scene：继续 `ui.scene_not_mounted` / `service.unavailable`，无 fallback。
- 本 RFC 不管 UE5 宿主的 `input.raw` / `screenshot` 接线（#1493 注明那是另一条跟进）。本切片只处理 UiScene ↔ WebUI 控件可见性。

## 6. UAT

```gherkin
Feature: 浏览器面板里的按钮能被点名、被点到

  Scenario: 主菜单上的「新游戏」能按名字找到
    Given 游戏已经挂上浏览器主菜单，画面上能看见「新游戏」
    And 该页面已经上报可点控件清单
    When 我调用 ludots.ui.query，选择器是 button
    Then 返回里有一条 elementId 为 main-menu-new-game
    And 它的文案是「新游戏」
    And 它的矩形落在这块浏览器画布里面

  Scenario: 按名字点「新游戏」会进选关
    Given 上一条已经能查到 main-menu-new-game
    When 我调用 ludots.ui.click，带上这个 elementId
    Then 返回 handled 为 true
    And 再截一张图，主菜单已经换成选关画面

  Scenario: 没上报清单时不会假装扫到网页按钮
    Given 一块浏览器画布已经挂上，但页面没有发清单
    When 我调用 ludots.ui.query，选择器是 button
    Then 匹配数是 0
    And ui.tree 里这块 canvas 标着 browserInventory unpublished

  Scenario: 用坐标点画布也会进浏览器
    Given 浏览器主菜单已经挂上
    And 我从截图上量到「新游戏」中心的窗口坐标
    When 我调用 ludots.ui.click，只带这对 x/y
    Then 浏览器真的收到这次点击（与 ludots.input.raw 点同一点的效果一致）
    And 不再出现「命中了 canvas 但 handled false、画面不动」

  Scenario: Inspector 不用另开网页调试器
    Given 清单已经上报
    When 我在 Inspector 里展开 ui.tree
    Then 浏览器画布下面能看到和 Markup 按钮同一形状的节点
    And 仓库里没有内嵌的 Chrome DevTools 面板当作自动化入口
```

实现时的自动化锚点（给人看的 UAT 之上）：

- 切片 A：给带 `IUiCanvasInputSink` 的 canvas 做 `UiClickTool` 测试，断言 `HandleInput` 收到 Left Down 再 Up，而不是只收到 `UiScene.Dispatch`。
- 切片 B/C：内存清单 → `QuerySelectorAll("button")` 命中覆盖层节点；未上报时 `unpublished`。
- 切片 D：至少一个现有 WebUI showcase（优先 `browser_rts_production` 或主菜单同类面板）接入共享上报助手。

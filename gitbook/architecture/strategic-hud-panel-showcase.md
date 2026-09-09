# Strategic HUD 八方面板 Showcase 设计

状态：已实现，markup 首屏运行验收通过；其余皮肤的切换合同已由引擎目录与构建验证覆盖

## 一句话与目标用户

让第一次进入 Ludots 的作者，在一张地图上看到八个固定锚点面板，并只改 JSON 就能换数据源与四种皮肤。

## 概述

Base44 web demo 的面板布局已拆成八个显示区：左上时间、上中视图、右上小地图、左右中位的选中与实体摘要、左下事件、下中指令、右下子系统。Ludots 现有 PanelHost 已经能把 Graph 输出投影成 retained UI，并由 `panelSkin` 选择 `default/markup/compose/reactive/web` 渲染路径。

本次把“八方布局 + 数据驱动骨架”落成一个无 C# Mod。它复刻布局关系和数据流，不把 web demo 的 React 组件、头像、列表或 Canvas 小地图伪装成已经存在的引擎能力。

## 结构

```text
StrategicHudPanelsMod (asset-only)
  assets/Entities/templates.json        一份面板状态实体与属性
  assets/GAS/graphs.json                 一张 Query 图 + 一张 MapLoaded 挂载图
  assets/Panels/panel_templates.json     八个模板，三行 pin，固定锚点
  assets/PanelThemes/themes.json         一个可替换的 CSS 主题包
  assets/Maps/strategic_hud_panels.json  真实启动地图
        |
        v
PanelHost -> PanelProjectionReader -> GraphOutputValueStore
        |
        v
PanelPresentationSystem -> UiSurfaceHost -> UIRoot -> adapter
```

复用清单：`PanelTemplateCatalogLoader`、`PanelHost`、`PanelProjectionReader`、`PanelActivationApi`、`PanelPresentationInstaller`、`UiSurfaceHost`、Graph Query/Trigger 管线。新增基建只有 `PanelAnchorCatalog` 的左右中位锚点和对应矩形计算；没有新增 UI runtime、Registry 或输入管线。

## 详情

### 动态轴与说服点

动态轴是“同一份面板数据，在八个屏幕锚点和四种皮肤下保持一致”。说服点是：作者只换 `game.json` 的 `panelSkin`，八块面板仍由相同模板和 Graph 输出驱动。

### 八个面板映射

| web demo 区域 | Ludots 模板 | 锚点 | 当前数据 |
|---|---|---|---|
| 左上时间/任务 | `panel.strategic.time` | `screen.topLeft` | day、speed、viewMode |
| 上中视图控制 | `panel.strategic.view` | `screen.topCenter` | cameraX、cameraY、zoom |
| 右上小地图 | `panel.strategic.minimap` | `screen.topRight` | cameraX、cameraY、zoom |
| 左中选中详情 | `panel.strategic.selection` | `screen.middleLeft` | selectedHealth、selectedAttack、selectedCount |
| 右中实体列表 | `panel.strategic.entities` | `screen.middleRight` | entityCount、allyCount、hostileCount |
| 左下事件日志 | `panel.strategic.events` | `screen.bottomLeft` | eventCount、alertLevel、lastEvent |
| 下中指令/生产区 | `panel.strategic.command` | `screen.bottomCenter` | commandReady、selectedUnits、actionPoints |
| 右下子系统入口 | `panel.strategic.subsystems` | `screen.bottomRight` | diplomacy、research、logistics |

当前每块面板最多三行，是现有内置自动排版的正式边界。每个 pin 都来自 `Graph.StrategicHud.Values`，缺失时显示模板默认值，结构错误在加载期失败。

### 运行时旋钮

本片不伪造 React 按钮。可运行的配置旋钮是：

| 旋钮 | 位置 | 回答的问题 |
|---|---|---|
| `panelSkin` | `assets/game.json` | 同一数据能否切换四种皮肤 |
| `panelTheme` | `assets/game.json` | 皮肤与视觉主题是否正交 |
| 八个 `panelAnchor` | `assets/GAS/graphs.json` | 面板能否独立停靠八方 |
| pin 的 `mode` | `assets/Panels/panel_templates.json` | 哪些值随 realtime 刷新 |

## 场景

### 主演示

启动 `strategic_hud_panels` 地图，首帧出现八个面板。将 `panelSkin` 从 `markup` 改为 `compose`、`reactive` 或 `default` 后重启，数据与锚点不变而 accent 改变。

### 子场景

1. 只保留 `panel.strategic.selection`，验证中位锚点不会被错误当成底部锚点。
2. 将一个 pin 改成 `snapshot`，验证它不会被 realtime sweep 重算。
3. 删除一个 Graph output，验证该行保留模板默认值而不是空白。

### 首屏引导

面板底部 hint 会显示当前皮肤名；作者无需读代码即可确认当前渲染路径。

## 边界

本片已经支持：八方锚点、模板/Graph/实体 JSON、四种内置 native 皮肤、主题 CSS、默认值与失败可见性。

本片没有宣称支持：实体列表虚拟滚动、头像/图片、Canvas 小地图、Tab 过滤、按钮点击到权威命令、事件日志逐条追加、面板实例级显隐。它们需要 PanelKit 的列表/资源/命令契约和 web surface 的实例定位，属于后续基础设施，不在零代码 scalar panel 合同内绕过实现。

## 反向 API 审计

| 缺口 | 影响 | 归属 |
|---|---|---|
| 面板 pin 只有标量 | 无法表达实体列表与事件条目 | PanelKit 列表型 pin，后续 |
| Web 皮肤按模板只建一个固定 320x220 surface | 无法原样复刻八块 DOM/CSS 面板 | `PanelWebSkin` 多实例与锚点接入，后续 |
| 内置 renderer 无图片/图标/Canvas 节点 | 小地图和头像只能做独立 presenter | UI PanelKit/adapter，后续 |
| UI 事件没有通用 intent sink | 不能把按钮安全接入权威命令 | Input/Order/PanelKit 交界，后续 |

## UAT（Cucumber）

```gherkin
Feature: Strategic HUD 八方数据驱动面板

  Scenario: 首帧显示八个锚点面板
    Given 我从 StrategicHudPanelsMod 的启动预设进入 strategic_hud_panels 地图
    When 地图加载完成
    Then 我能在左上、上中、右上、左中、右中、左下、下中和右下看到面板
    And 每个面板至少显示三行数值

  Scenario: 更换四种皮肤不改变数据
    Given 八个面板已经显示
    When 我把 game.json 的 panelSkin 依次设为 markup、compose、reactive、default 并重新启动
    Then 八个面板的锚点和数值键保持不变
    And 面板的 accent 与 hint 能反映当前皮肤

  Scenario: Graph 输出缺失时显示默认值
    Given 一个模板 pin 声明了 default 值
    When 对应 Graph output 不存在
    Then 该行显示声明的 default 值
    And 屏幕上不出现空白行或静默失败

  Scenario: 中位锚点通过编译校验
    Given 一个 CreatePanel 节点使用 screen.middleLeft 或 screen.middleRight
    When Graph 编译
    Then 编译通过并把面板放在屏幕垂直中线附近
```

## 门户资产与完成判据

真实入口是 `StrategicHudPanelsMod/assets/game.json` 的 `startupMapId`，注册入口为 `showcase.registry.json`。运行验收必须证明：启动命令能进入该地图、八个面板可见、`pumpCount` 连续增长、UI 树包含八个模板、截图显示中位锚点没有跑到底部。

本次运行验收证据：`artifacts/acceptance/strategic-hud-panels/trace.jsonl`、`artifacts/acceptance/strategic-hud-panels/battle-report.md`、`artifacts/acceptance/strategic-hud-panels/path.mmd` 与 `artifacts/agent-bridge/shots/strategic-hud-panels-first-viewport.png`。markup 首屏已证明真实入口、八个面板、21 个属性投影、中位锚点和持续 pump；四种 native skin 的具体截图切换仍是后续补充验收，不在本次证据中冒充完成。

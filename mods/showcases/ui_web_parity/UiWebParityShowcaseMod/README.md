# Web ↔ Ludots UI 布局一致 Showcase

## 1. 概述

玩家打开后看到同一张「暂停菜单」设计稿：颜色、边框、分区和浏览器参考一致；切换桌面 / 平板 / 手机预览时，菜单跟着变宽变窄，结构不乱。

自动化证据：`fixtures/ui-web-parity/chrome-layout.golden.json`（Chrome 盒模型）对照 `UiWebParityTests`。

## 2. 结构

- 共享夹具：`fixtures/ui-web-parity/parity_menu.html` + `parity_menu.css`
- Chrome 导出：`scripts/ui-web-parity/dump-chrome-layout.mjs`
- Showcase 壳：`web_parity_showcase.html/css`（分辨率切换 + 旁白）
- 验收测试：`src/Tests/UiShowcaseTests/UiWebParityTests.cs`

## 3. 详情

- 盒模型约定：`box-sizing: border-box`（与 Ludots Flex 一致）
- 能力范围：flex / grid / gap / % 尺寸 / 颜色 / 边框（Native CSS Profile）
- 不引入 JS、不依赖 `@media`

## 4. 场景

玩家从 Launcher 进入本 Showcase，先看桌面预览，再点平板、手机，确认菜单仍完整可读。

## 5. 边界

- 证明的是「同一份 HTML/CSS 在支持的样式子集上布局一致」，不是浏览器全量 CSSOM
- 文本测宽使用确定性测字器做几何对照；截图走 Skia 渲染

## 6. UAT

```gherkin
Feature: 同一张暂停菜单在游戏里和浏览器里对得上

  Scenario: 桌面分辨率下关键区块对齐
    Given 玩家打开「同一张暂停菜单」Showcase
    And 当前预览是桌面 1280×720
    When 系统对照 Chrome 参考盒模型检查菜单壳、四宫格和侧栏
    Then 每个关键区块的位置和大小误差不超过 2.5 像素

  Scenario: 换到手机预览后布局跟着变且不塌
    Given 玩家正在看桌面预览
    When 玩家点击「手机 390×844」
    Then 预览标签变为手机尺寸
    And 菜单区域比桌面更窄
    And 继续冒险、地图、设置、回标题四个入口仍然可见

  Scenario: 静态样式来自同一份稿
    Given 玩家打开本 Showcase
    Then 主按钮是亮蓝、危险按钮是暗红
    And 右侧任务卡标题为「当前任务」
```

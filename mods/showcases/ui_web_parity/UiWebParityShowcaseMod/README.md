# 星港休整舱 · Web ↔ Ludots 同稿 Showcase

## 1. 概述

玩家推开一扇霓虹休整舱的门：护盾条在跳、任务板亮着、四个入口在正中。浏览器里这张舱门长什么样，游戏里就长什么样；切桌面观景窗 / 平板舷窗 / 手机通讯屏，布局跟着变形但不塌。

自动化证据：`fixtures/ui-web-parity/chrome-layout.golden.json`（Chrome 盒模型）对照 `UiWebParityTests`。

## 2. 结构

- 共享夹具：`fixtures/ui-web-parity/parity_menu.html` + `parity_menu.css`（星港菜单本体）
- Chrome 导出：`scripts/ui-web-parity/dump-chrome-layout.mjs`
- Showcase 壳：`web_parity_showcase.html/css` + 运行时拼接夹具 CSS（菜单外观 SSOT）
- 验收测试：`src/Tests/UiShowcaseTests/UiWebParityTests.cs`

## 3. 详情

- 盒模型：`box-sizing: border-box`
- 花活在支持子集内：线性渐变舱壁、光斑、圆角、阴影、进度条、`::before` 按钮缀符、flex/grid/gap
- 动效：光斑呼吸、星徽边框脉搏、主按钮霓虹闪、航程条明暗、`SYNCED` 与激活芯片闪烁（`@keyframes` + `UiScene.AdvanceTime`）
- flex 裸文字按匿名子项居中；中文缺字必须落到本机可用字体
- 不引入 JS、不依赖 `@media`

## 4. 场景

玩家从 Launcher 进入，先看舱门全貌，再点「平板舷窗」「手机通讯屏」，确认按钮仍在正中、侧栏任务仍能读完。

## 5. 边界

- 证明的是「同一份 HTML/CSS 在支持的样式子集上布局与绘制一致」，不是浏览器全量 CSSOM
- 字体行高敏感的文案盒不进严格几何黄金对照；结构盒与交互区仍对照

## 6. UAT

```gherkin
Feature: 推开同一扇星港休整舱的门

  Scenario: 桌面观景窗下舱门区块对齐
    Given 玩家打开「星港休整舱」Showcase
    And 当前预览是桌面 1280×720
    When 系统对照 Chrome 参考盒模型检查舱门壳、四宫格和侧栏
    Then 每个结构区块的位置和大小误差不超过 2.5 像素

  Scenario: 四宫格入口上的字落在正中
    Given 玩家正在看桌面观景窗
    When 玩家看「继续冒险」「打开星图」「舰桥设置」「回到标题」
    Then 每个入口上的字都在彩色方块正中央

  Scenario: 护盾条和航程条看得见
    Given 玩家正在看舱门
    Then 「船体护盾」「反应堆」「星币」下方都有一条进度色带
    And 右侧任务板底部显示航程百分比

  Scenario: 舱内有渐变和呼吸动效
    Given 玩家打开休整舱
    Then 舱壁、按钮、进度条带有青绿到琥珀的渐变
    And 背景光斑、星徽、主按钮、航程条在轻轻明暗呼吸
    And 预览角标 SYNCED 与当前分辨率芯片在闪烁

  Scenario: 切到手机通讯屏后仍可读
    Given 玩家正在看桌面观景窗
    When 玩家点击「手机通讯屏」
    Then 预览标签变为手机尺寸
    And 四个入口仍然可见
    And 右侧「当前任务」说明换行后仍能读完，字不叠在一起
```

# UI 原生能力 Checklist

面向做皮肤 / 做面板的人：能做什么、半成品是什么、明确不做是什么。  
运行时是 Ludots 原生 UI（Compose / Reactive / Markup → `UiScene` / Skia），**不是**浏览器全兼容。

图例：`✅` 可正式用 · `◐` 可用但有边界 · `🚧` 并行 PR / 未合主线 · `❌` 现阶段不做

## 1. 概述

这份清单是 UI 作者合同的 SSOT 摘要。Showcase「墨痕怎么裁、怎么铺」覆盖切铺与 SVG·动效；Compose / Markup Phase 1–5 覆盖选择器、静态变换、遮罩、文本、SVG 静态绘制、颜色/透明度关键帧。

## 2. 结构

| 分区 | 内容 |
| --- | --- |
| 布局与盒模型 | 宽高、定位、Flex |
| 视觉与皮肤 | 色、边、阴影、背景、遮罩 |
| 图与切铺 | 位图、SVG、九/三宫格、连续铺 |
| 动效 | keyframes / transition |
| 文本与交互 | 多语、伪类、输入 |
| 明确不做 | 浏览器专属能力 |

## 3. 详情

### 布局与盒模型

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| Flex 布局 | ✅ | 主路径 |
| `width` / `height` px / % / auto | ✅ | |
| `position: relative` / `absolute` | ✅ | |
| 静态 `transform`（translate / rotate / scale） | ✅ | 绘制与命中同步；布局盒不因 transform 改变 |
| `calc()` / `vw` `vh` `vmin` `vmax` | 🚧 | 见并行 PR 栈 |
| CSS Grid / `::before` `::after` | 🚧 | MVP 在并行 PR；`float` / `minmax()` 仍不承诺 |
| `position: fixed` / `sticky` | ❌ | |
| `float` | ❌ | |

### 视觉与皮肤

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 背景色 / 边框色 / 圆角 | ✅ | |
| 多背景 / 多阴影 / dashed border | ✅ | Phase 2 |
| `mask-image` / `clip-path`（子集） | ✅ | Phase 2 |
| `filter: blur` / `backdrop-filter` | ✅ | 可动画 |
| 主题皮肤包（Classic / Paper / SciFi） | ✅ | Skin Showcase |

### 图与切铺

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| `<img>` 位图 | ✅ | |
| SVG（`<img>` / data URI / 内联导入） | ✅ | Svg.Skia 静态绘制 |
| `image-slice` 九宫格 / 三宫格 | ◐ | **仅位图**；SVG 不可作切格框 |
| `background-repeat` 二方 / 四方连续 | ✅ | `repeat` / `repeat-x` / `repeat-y` |
| 矢量九宫格框 | ❌ | 装饰用 SVG，切边仍走位图 |
| `border-image-*` 全套 | ❌ | 目前只有 slice 别名路径 |

### 动效

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| `@keyframes` + `UiScene.AdvanceTime` | ✅ | |
| 可插值：颜色 / opacity / blur | ✅ | |
| 可插值：`transform`（translate / rotate / scale） | ✅ | 关键帧停点必须同构；单位不一致或 `matrix`/`skew` → **不建轨**（不静默半插值） |
| `transition` 同上属性 | ✅ | |
| 动画 `transform` 驱动布局 | ❌ | 仍是绘制时变换 |
| SVG SMIL / path morph / 矢量路径动画 | ❌ | 只动画宿主 CSS（含 transform） |
| 动画 `width` / `height` | ❌ | |

### 文本与交互

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 多语 / RTL / ellipsis / text-decoration | ✅ | Phase 3 |
| `:hover` `:focus` `:nth-*` `:not` `:is` `:where` | ✅ | |
| 表单校验表面 | ✅ | Compose / Reactive / Markup |
| JS / 完整 CSSOM | ❌ | |
| `@media` | ❌ | |

## 4. 场景

- **做卷轴框 / 按钮框**：用位图九宫格；角饰可用 SVG。
- **做墙纸 / 饰带**：用 `background-repeat`。
- **做印章呼吸、轻转、位移**：SVG + `@keyframes` 的 opacity / transform。
- **做和浏览器像素对齐的复杂 HUD**：等视口单位 / Grid / 伪元素 PR 合入后再宣称。

## 5. 边界

- 无效或不受支持的 `transform` 函数（如 `matrix`）出现在关键帧里：**整条 transform 轨道不建立**，不会假装在动。
- 关键帧之间 transform 操作列表不兼容（例如 `scale` ↔ `translate`）：**不插值**。
- Showcase 文案与本清单冲突时，以本清单与验收测试为准。

## 6. UAT

```gherkin
Feature: 作者能诚实选用 UI 能力
  作为一个做面板皮肤的人
  我想知道哪些写法会真的动起来、哪些会被拒绝
  以免上线后才发现“写了等于没写”

  Scenario: 朱印关键帧带动位移旋转
    Given 水墨匣 Showcase 打开在九宫格或 SVG·动效页
    When 时间向前推进约半拍
    Then 我能看到朱印透明度变化
    And 朱印同时发生可察觉的旋转或位移

  Scenario: 不支持的矩阵变换不会假装成功
    Given 某节点写了带 matrix 的 keyframes transform
    When 时间向前推进
    Then 该节点不会因此改变 transform
    And 系统不会静默丢掉半截轨道却继续“动画中”

  Scenario: 切格框仍是位图合同
    Given 水墨匣九宫格卷轴
    When 我查看框体资源
    Then 框体走位图 image-slice
    And 角上朱印可以是 SVG 装饰
```

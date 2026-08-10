# 墨痕怎么裁、怎么铺

玩家向 Showcase：中国水墨风拟物框，对照切铺 + 矢量动效能力。

## 玩法

1. **九宫格**：宣纸卷轴与墨钮；角上矢量朱印会呼吸并轻转。
2. **三宫格**：短/长签同一张图，只拉中间。
3. **二方连续**：云纹横条 / 竖条一节节接。
4. **四方连续**：竹雾墙纸四向铺满。
5. **SVG·动效**：大图朱印 + 笔锋矢量，位移/旋转/缩放与边框颜色在变。

## 能力挂靠

| 能力 | 挂靠 |
| --- | --- |
| 九/三宫格 | `<img>` + `image-slice`（位图） |
| 二方/四方连续 | `background-repeat` |
| SVG | `data:image/svg+xml` → `UiImageSourceCache` / Svg.Skia |
| 动效 | CSS `@keyframes` + `UiScene.AdvanceTime`（opacity / border-color / transform） |

注意：九宫格切边只走位图路径；矢量图做装饰与动效，不替代切格框。  
完整 UI 能力清单见 `gitbook/reference/ui-native-capability-checklist.md`。

## 验收

`UiNineSlicePanelTests`

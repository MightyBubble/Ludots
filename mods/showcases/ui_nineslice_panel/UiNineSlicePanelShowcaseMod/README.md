# 框体怎么切、怎么铺

玩家向 Showcase：对比拟物框的四种铺法。

## 玩法

1. **九宫格**：卷宗与按钮四角钉死，中间拉开。
2. **三宫格**：短/长绶带同一张图，只拉中间。
3. **二方连续**：横条 `repeat-x`、竖条 `repeat-y` 一节节接。
4. **四方连续**：整面墙 `repeat` 铺满。

## 能力边界

| 铺法 | 支持 | 挂靠 |
| --- | --- | --- |
| 九宫格 | 有 | `<img>` + `image-slice` |
| 三宫格（横向） | 有 | `image-slice` 左右有值、上下为 0 |
| 二方连续 | 有 | `background-repeat: repeat-x` / `repeat-y` |
| 四方连续 | 有 | `background-repeat: repeat` |

注意：九/三宫格走 `<img>`；二方/四方走 `background-image`，不是同一条绘制路径。

## 验收

`UiNineSlicePanelTests`

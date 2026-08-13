# 热带岛字段对照验收

本文是 Epic #924 P0–P2 在可玩热带岛上的字段对照：玩家走进沙滩，同一类东西并排放着，只改一个配置项，发射载荷就要跟着变。玩家看见的贴花投影尺寸合同归 P3（#933），样条类型去路名归 P4（#931）。

## 1. 概述

热带岛不只是“有脚印”。沙滩东岸是对照区：

- 同一串脚印，一块小、一块大
- 同一块焦痕，投影盒子有薄有厚
- 同一串脚印，一白一红
- 同一道裂痕，一条顺着走、一条横着铺
- 岸边三条带子：细沙路、宽栈道、带边的水色弯带
- 两朵击中闪光：小绿、大黄，叶子网格 `cue_marker`，不再走旧拼装表

改 `presenters.json` / `decal_placements.json` 里对应字段，重新进岛，对照区的发射载荷必须换样子。本包验收是发射载荷 UAT，不是 P3 投影视觉尺寸，也不是 P4 去路名。

## 2. 结构

| 对照 | 配置落点 | 本包验收 |
|------|----------|----------|
| 印记大小 | `assetBinding.localScale` 的 X/Z | 发射进 `VisualProxy.Scale`；小/大可区分 |
| 投影厚度 | `localScale.Y` | 发射进 `VisualProxy.Scale.Y`；薄/厚可区分。适配器如何吃这个值见 P3 |
| 染色 | `style.color` | 白印 vs 红印进入载荷颜色 |
| 朝向 | 放置表 `yawDeg` | 裂痕顺着走 vs 横着铺写在放置表 |
| 带子宽度 | `worldSpline.width` | 细沙路 vs 宽栈道进入样条请求 |
| 带子颜色 | `worldSpline.fill` | 沙黄 vs 水色进入样条请求 |
| 描边 | `worldSpline.border` + `worldSpline.border.width` | 有边/无边进入样条请求 |
| 弯曲 | `worldSpline.p0`–`p3` | 弯带控制点进入样条请求 |
| 闪光大小/颜色 | cue presenter 的 `localScale` / `style.color` | 小绿块 vs 大黄块进入载荷 |
| 闪光寿命 | Core presenter `cue_marker` 的 `lifecycle.durationSeconds` | 应答链瞬态标记读这份数据；`lifetimeSeconds<=0` 失败响 |

拍摄机位 `08_decal_fields` / `09_spline_ribbons` / `10_cue_flashes` 仍可用来构图，但 **P0–P2 不把 GPU 截图当作本包合同**。故事脚印仍是 `07_beach_decals`。

## 3. 详情

- 编排仍是 Presenter `AssetBinding`。禁止再写 Prefab 零件。
- 印记尺寸必须进 `VisualProxy.Scale`。适配器侧投影盒子如何消费该缩放是 P3 合同。
- 带子走 `AssetKind.Spline` 绘制请求。类型名仍可能是 `RoadSplineRequest`，去路名是 P4。
- 击中闪光用叶子网格 `cue_marker`（`mesh_assets.json` 唯一注册），缩放与寿命来自 Core presenter `cue_marker`，与应答链同一条瞬态网格路。
- 规则命令是 `CreatePresenter`，不是已删除的 Performer 词。

## 4. 场景

玩家从故事脚印往东走几步，看见并排对照。大小、颜色、朝向、宽窄应对着配置。本包用发射载荷测试锁住这些字段；玩家看见的投影贴合仍跟 P3。

## 5. 边界

- 不把 GroundOverlay 圈线冒充贴花
- 不做延迟深度贴花
- 工人沿看不见的路线走，仍以铁匠铺为准；本岛只验收“画出来的带子”
- 不把座位/相机、GPU 蒙皮卷进本页
- 不把 P3 投影尺寸或 P4 去路名写成已经落地

## 6. UAT

```gherkin
Feature: 沙滩对照区，改一个字段就能看出来

  Scenario: 脚印有大有小
    Given 我站在热带岛东岸对照沙滩
    When 发射载荷读到并排的两串脚印
    Then 左边 Scale.X 明显更小，右边明显更大
    And 它们都带 Decal 种类，不是 Prefab 零件

  Scenario: 焦痕盒子有薄有厚
    Given 同一块焦痕纹理放了两次
    When 作者把其中一次的高度写厚
    Then 发射载荷里厚的 Scale.Y 更大
    And 薄的那次 Scale.Y 更小

  Scenario: 脚印能染红
    Given 两串同样大小的脚印
    When 作者把其中一串的颜色写成红
    Then 载荷能立刻分出白印和红印

  Scenario: 裂痕能转方向
    Given 两道同样的裂痕
    When 作者把其中一道的朝向转过大约直角
    Then 放置表里一道顺着岸，一道横过沙面

  Scenario: 岸边带子有细有宽、有黄有蓝
    Given 沙滩上有三条带子
    When 发射样条请求
    Then 一条又细又黄，一条又宽又像栈道
    And 第三条带着描边、走成弯的水色

  Scenario: 击中闪光不靠旧拼装表
    Given 对照区有两朵常亮的击中闪光
    When 我读它们的发射载荷
    Then 一朵又小又绿，一朵又大又黄
    And 工程上它们用的是叶子闪光网格，不是 Prefab 拼装
```

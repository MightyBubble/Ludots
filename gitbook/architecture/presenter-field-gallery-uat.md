# 热带岛字段对照验收

本文是 Epic #924 在可玩热带岛上的字段对照：玩家走进沙滩，同一类东西并排放着，只改一个配置项，画面就要跟着变。

## 1. 概述

热带岛不只是“有脚印”。沙滩东岸是对照区：

- 同一串脚印，一块小、一块大
- 同一块焦痕，投影盒子有薄有厚
- 同一串脚印，一白一红
- 同一道裂痕，一条顺着走、一条横着铺
- 岸边三条带子：细沙路、宽栈道、带边的水色弯带
- 两朵击中闪光：小绿、大黄，不再走旧拼装表

改 `presenters.json` / `decal_placements.json` / `cue_field_gallery.json` 里对应字段，重新进岛，对照区必须换样子。

## 2. 结构

| 对照 | 配置落点 | 玩家看见 |
|------|----------|----------|
| 印记大小 | `assetBinding.localScale` 的 X/Z | 左边一小块，右边一大块 |
| 投影厚度 | `localScale.Y` | 薄盒贴坡；厚盒在起伏上仍能盖住 |
| 染色 | `style.color` | 白印 vs 红印 |
| 朝向 | 放置表 `yawDeg` | 裂痕顺着走 vs 横着铺 |
| 带子宽度 | `worldSpline.width` | 细沙路 vs 宽栈道 |
| 带子颜色 | `worldSpline.fill` | 沙黄 vs 水色 |
| 描边 | `worldSpline.border` + `worldSpline.border.width` | 一条有深色边，两条没有 |
| 弯曲 | `worldSpline.p0`–`p3` | 弯带不是直线 |
| 闪光大小/颜色 | cue presenter 的 `localScale` / `style.color` | 小绿块 vs 大黄块 |
| 闪光寿命 | `cue_field_gallery.json` `lifetimeSeconds` | 短闪先灭、长闪后灭（测试验收） |

拍摄机位：`08_decal_fields` / `09_spline_ribbons` / `10_cue_flashes`。故事脚印仍是 `07_beach_decals`。

## 3. 详情

- 编排仍是 Presenter `AssetBinding`。禁止再写 Prefab 零件。
- 印记尺寸必须进 `VisualProxy.Scale`，适配器不得改回一单位正方形（P3 合同）。
- 带子走 `AssetKind.Spline` 绘制请求，不叫道路请求。
- 击中闪光用叶子网格 `cue_marker`，与应答链同一条瞬态网格路。
- 规则命令是 `CreatePresenter`，不是已删除的 Performer 词。

## 4. 场景

玩家从故事脚印往东走几步，看见并排对照。不用打开调试面板：大小、颜色、朝向、宽窄自己会说话。

## 5. 边界

- 不把 GroundOverlay 圈线冒充贴花
- 不做延迟深度贴花
- 工人沿看不见的路线走，仍以铁匠铺为准；本岛只验收“画出来的带子”
- 不把座位/相机、GPU 蒙皮卷进本页

## 6. UAT

```gherkin
Feature: 沙滩对照区，改一个字段就能看出来

  Scenario: 脚印有大有小
    Given 我站在热带岛东岸对照沙滩
    When 我看并排的两串脚印
    Then 左边明显更小，右边明显更大
    And 它们都贴在沙子起伏上，不像插进地里的薄板

  Scenario: 焦痕盒子有薄有厚
    Given 同一块焦痕纹理放了两次
    When 作者把其中一次的高度写厚
    Then 厚的那次在坡上仍能盖住起伏
    And 薄的那次更贴着沙面

  Scenario: 脚印能染红
    Given 两串同样大小的脚印
    When 作者把其中一串的颜色写成红
    Then 我能立刻分出白印和红印

  Scenario: 裂痕能转方向
    Given 两道同样的裂痕
    When 作者把其中一道的朝向转过大约直角
    Then 一道顺着岸，一道横过沙面

  Scenario: 岸边带子有细有宽、有黄有蓝
    Given 沙滩上有三条带子
    When 我从高处看
    Then 一条又细又黄，一条又宽又像栈道
    And 第三条带着深色描边、走成弯的水色
    And 作者配置里没有「道路请求」这种内部词

  Scenario: 击中闪光不靠旧拼装表
    Given 对照区有两朵常亮的击中闪光
    When 我走近看
    Then 一朵又小又绿，一朵又大又黄
    And 工程上它们用的是叶子闪光网格，不是 Prefab 拼装
```

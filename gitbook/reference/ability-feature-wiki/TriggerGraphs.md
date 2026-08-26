# 出手之后图跟着跑

出手之后，技能自己带着的图跟着跑起来，字幕报图跑了。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_ability_feature_TriggerGraphs/poster.png" src="artifacts/evidence/capability_standard_ability_feature_TriggerGraphs/play.mp4">
这场还没有验收录像。启动器进 `capability_standard_ability_feature_TriggerGraphs` 看现场；采到录像后再补 `artifacts/evidence/capability_standard_ability_feature_TriggerGraphs/play.mp4`。
</video>

## 作者写法

这一场只讲一个技能合同。写法摘自画廊真实技能表，手册分册是全量字段。

手册分册：[技能定义骨架 · ab-01](../mod-editor-prd/config/ab-01-definition.md)

真实用例（`mods/showcases/capability_standard/CapabilityStandardAbilityFeatureGalleryMod/assets/GAS/abilities/`）：

```json
{
  "id": "Ability.AbilityFeature.TriggerGraphs",
  "exec": {
    "clockId": "FixedFrame",
    "items": [
      {
        "kind": "End",
        "tick": 0
      }
    ]
  },
  "triggerGraphs": [
    "Graph.AbilityFeature.OnCast"
  ],
  "presentation": {
    "displayName": "出手之后图跟着跑",
    "iconGlyph": "图",
    "hintText": "技能自带触发图。"
  }
}
```

## 这场是怎么搭出来的

短剧自己出手，不用先学键位。字幕用这场的结果填空：

> 技能自己带着的图{graphState}。

## 边界

- 这一场不演其它技能合同。冷却闭环拆成「自己挂印」和「禁招印」两间房。
- 配置册上的 `cooldown` 块加载器不收，不在这场假装能用。

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_ability_feature_TriggerGraphs --adapter raylib
```

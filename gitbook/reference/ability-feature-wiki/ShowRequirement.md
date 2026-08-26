# 没解锁栏上就没有

牌子点亮之前栏上只有一招；点亮之后，隐藏的那招才出现。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_ability_feature_ShowRequirement/poster.png" src="artifacts/evidence/capability_standard_ability_feature_ShowRequirement/play.mp4">
这场还没有验收录像。启动器进 `capability_standard_ability_feature_ShowRequirement` 看现场；采到录像后再补 `artifacts/evidence/capability_standard_ability_feature_ShowRequirement/play.mp4`。
</video>

## 作者写法

这一场只讲一个技能合同。写法摘自画廊真实技能表，手册分册是全量字段。

手册分册：[激活门 · ab-05](../mod-editor-prd/config/ab-05-activation-gates.md)

真实用例（`mods/showcases/capability_standard/CapabilityStandardAbilityFeatureGalleryMod/assets/GAS/abilities/`）：

```json
{
  "id": "Ability.AbilityFeature.ShowRequirement",
  "exec": {
    "clockId": "FixedFrame",
    "items": [
      {
        "kind": "End",
        "tick": 0
      }
    ]
  },
  "showRequirement": "Req.AbilityFeature.Unlock",
  "presentation": {
    "displayName": "隐藏招",
    "iconGlyph": "藏",
    "hintText": "没解锁就看不见。"
  }
}
```

## 这场是怎么搭出来的

短剧自己出手，不用先学键位。字幕用这场的结果填空：

> 看得见的招：{visibleAbilities}。

## 边界

- 这一场不演其它技能合同。冷却闭环拆成「自己挂印」和「禁招印」两间房。
- 配置册上的 `cooldown` 块加载器不收，不在这场假装能用。

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_ability_feature_ShowRequirement --adapter raylib
```

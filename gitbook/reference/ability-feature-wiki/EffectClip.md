# 火还在烧

出手之后火还挂在木桩身上，血条一格格往下掉。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_ability_feature_EffectClip/poster.png" src="artifacts/evidence/capability_standard_ability_feature_EffectClip/play.mp4">
这场还没有验收录像。启动器进 `capability_standard_ability_feature_EffectClip` 看现场；采到录像后再补 `artifacts/evidence/capability_standard_ability_feature_EffectClip/play.mp4`。
</video>

## 作者写法

这一场只讲一个技能合同。写法摘自画廊真实技能表，手册分册是全量字段。

手册分册：[执行时间轴 · ab-02](../mod-editor-prd/config/ab-02-exec-timeline.md)

真实用例（`mods/showcases/capability_standard/CapabilityStandardAbilityFeatureGalleryMod/assets/GAS/abilities/`）：

```json
{
  "id": "Ability.AbilityFeature.EffectClip",
  "exec": {
    "clockId": "FixedFrame",
    "items": [
      {
        "kind": "EffectClip",
        "tick": 0,
        "duration": 36,
        "template": "Effect.AbilityFeature.Burn"
      },
      {
        "kind": "End",
        "tick": 36
      }
    ]
  },
  "presentation": {
    "displayName": "火还在烧",
    "iconGlyph": "烧",
    "hintText": "持续效果。"
  }
}
```

## 这场是怎么搭出来的

短剧自己出手，不用先学键位。字幕用这场的结果填空：

> 火还在烧；木桩血条从 {targetBefore} 掉到 {targetAfter}。

## 边界

- 这一场不演其它技能合同。冷却闭环拆成「自己挂印」和「禁招印」两间房。
- 配置册上的 `cooldown` 块加载器不收，不在这场假装能用。

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_ability_feature_EffectClip --adapter raylib
```

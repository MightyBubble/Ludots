# 身上有禁招印就放不出

第一下挂上禁招印；印还在时再出手，字幕说放不出。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_ability_feature_BlockTagsBlocked/poster.png" src="artifacts/evidence/capability_standard_ability_feature_BlockTagsBlocked/play.mp4">
这场还没有验收录像。启动器进 `capability_standard_ability_feature_BlockTagsBlocked` 看现场；采到录像后再补 `artifacts/evidence/capability_standard_ability_feature_BlockTagsBlocked/play.mp4`。
</video>

## 作者写法

这一场只讲一个技能合同。写法摘自画廊真实技能表，手册分册是全量字段。

手册分册：[激活门 · ab-05](../mod-editor-prd/config/ab-05-activation-gates.md)

真实用例（`mods/showcases/capability_standard/CapabilityStandardAbilityFeatureGalleryMod/assets/GAS/abilities/`）：

```json
{
  "id": "Ability.AbilityFeature.BlockTagsBlocked",
  "exec": {
    "clockId": "FixedFrame",
    "items": [
      {
        "kind": "TagClip",
        "tick": 0,
        "duration": 48,
        "tag": "Cooldown.AbilityFeature.Lock"
      },
      {
        "kind": "EffectSignal",
        "tick": 0,
        "template": "Effect.AbilityFeature.Strike"
      },
      {
        "kind": "End",
        "tick": 0
      }
    ]
  },
  "blockTags": {
    "blockedAny": [
      "Cooldown.AbilityFeature.Lock"
    ]
  },
  "presentation": {
    "displayName": "身上有禁招印就放不出",
    "iconGlyph": "禁",
    "hintText": "禁招印在场就拒。"
  }
}
```

## 这场是怎么搭出来的

短剧自己出手，不用先学键位。字幕用这场的结果填空：

> 第二下{secondCast}。

## 边界

- 这一场不演其它技能合同。冷却闭环拆成「自己挂印」和「禁招印」两间房。
- 配置册上的 `cooldown` 块加载器不收，不在这场假装能用。

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_ability_feature_BlockTagsBlocked --adapter raylib
```

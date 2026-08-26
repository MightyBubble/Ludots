# 没有姿态印就放不出

没亮姿态印时出手放不出；印一挂上，木桩才掉血。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_ability_feature_BlockTagsRequired/poster.png" src="artifacts/evidence/capability_standard_ability_feature_BlockTagsRequired/play.mp4">
这场还没有验收录像。启动器进 `capability_standard_ability_feature_BlockTagsRequired` 看现场；采到录像后再补 `artifacts/evidence/capability_standard_ability_feature_BlockTagsRequired/play.mp4`。
</video>

## 作者写法

这一场只讲一个技能合同。写法摘自画廊真实技能表，手册分册是全量字段。

手册分册：[激活门 · ab-05](../mod-editor-prd/config/ab-05-activation-gates.md)

真实用例（`mods/showcases/capability_standard/CapabilityStandardAbilityFeatureGalleryMod/assets/GAS/abilities/`）：

```json
{
  "id": "Ability.AbilityFeature.BlockTagsRequired",
  "exec": {
    "clockId": "FixedFrame",
    "items": [
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
    "requiredAll": [
      "State.AbilityFeature.Stance"
    ]
  },
  "presentation": {
    "displayName": "没有姿态印就放不出",
    "iconGlyph": "姿",
    "hintText": "缺姿态印就拒。"
  }
}
```

## 这场是怎么搭出来的

短剧自己出手，不用先学键位。字幕用这场的结果填空：

> 挂上姿态印之后木桩血条从 {targetBefore} 掉到 {targetAfter}。

## 边界

- 这一场不演其它技能合同。冷却闭环拆成「自己挂印」和「禁招印」两间房。
- 配置册上的 `cooldown` 块加载器不收，不在这场假装能用。

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_ability_feature_BlockTagsRequired --adapter raylib
```

# 被晕就停手

出手之后还没打中，施法者被晕，这一招停在半路，木桩不掉血。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_ability_feature_InterruptAny/poster.png" src="artifacts/evidence/capability_standard_ability_feature_InterruptAny/play.mp4">
这场还没有验收录像。启动器进 `capability_standard_ability_feature_InterruptAny` 看现场；采到录像后再补 `artifacts/evidence/capability_standard_ability_feature_InterruptAny/play.mp4`。
</video>

## 作者写法

这一场只讲一个技能合同。写法摘自画廊真实技能表，手册分册是全量字段。

手册分册：[执行时间轴 · ab-02](../mod-editor-prd/config/ab-02-exec-timeline.md)

真实用例（`mods/showcases/capability_standard/CapabilityStandardAbilityFeatureGalleryMod/assets/GAS/abilities/`）：

```json
{
  "id": "Ability.AbilityFeature.InterruptAny",
  "exec": {
    "clockId": "FixedFrame",
    "items": [
      {
        "kind": "EffectSignal",
        "tick": 24,
        "template": "Effect.AbilityFeature.Strike"
      },
      {
        "kind": "End",
        "tick": 24
      }
    ],
    "interruptAny": [
      "Status.AbilityFeature.Stunned"
    ]
  },
  "presentation": {
    "displayName": "被晕就停手",
    "iconGlyph": "晕",
    "hintText": "身上有打断印就停。"
  }
}
```

## 这场是怎么搭出来的

短剧自己出手，不用先学键位。字幕用这场的结果填空：

> 停手时木桩血条仍是 {targetAfter}。

## 边界

- 这一场不演其它技能合同。冷却闭环拆成「自己挂印」和「禁招印」两间房。
- 配置册上的 `cooldown` 块加载器不收，不在这场假装能用。

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_ability_feature_InterruptAny --adapter raylib
```

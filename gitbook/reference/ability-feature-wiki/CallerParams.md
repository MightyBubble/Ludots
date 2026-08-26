# 同一招两波不同力道

同一张效果票先轻轻一下，再重重一下，木桩掉两截不同的血。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_ability_feature_CallerParams/poster.png" src="artifacts/evidence/capability_standard_ability_feature_CallerParams/play.mp4">
这场还没有验收录像。启动器进 `capability_standard_ability_feature_CallerParams` 看现场；采到录像后再补 `artifacts/evidence/capability_standard_ability_feature_CallerParams/play.mp4`。
</video>

## 作者写法

这一场只讲一个技能合同。写法摘自画廊真实技能表，手册分册是全量字段。

手册分册：[CallerParams 参数池 · ab-03](../mod-editor-prd/config/ab-03-caller-params.md)

真实用例（`mods/showcases/capability_standard/CapabilityStandardAbilityFeatureGalleryMod/assets/GAS/abilities/`）：

```json
{
  "id": "Ability.AbilityFeature.CallerParams",
  "exec": {
    "clockId": "FixedFrame",
    "items": [
      {
        "kind": "EffectSignal",
        "tick": 0,
        "template": "Effect.AbilityFeature.Wave",
        "callerParamsIdx": 0
      },
      {
        "kind": "EffectSignal",
        "tick": 12,
        "template": "Effect.AbilityFeature.Wave",
        "callerParamsIdx": 1
      },
      {
        "kind": "End",
        "tick": 12
      }
    ],
    "callerParams": [
      {
        "entries": [
          {
            "key": "abilityfeature.wave.damage",
            "value": 10
          }
        ]
      },
      {
        "entries": [
          {
            "key": "abilityfeature.wave.damage",
            "value": 30
          }
        ]
      }
    ]
  },
  "presentation": {
    "displayName": "同一招两波不同力道",
    "iconGlyph": "波",
    "hintText": "同一模板换两组数。"
  }
}
```

## 这场是怎么搭出来的

短剧自己出手，不用先学键位。字幕用这场的结果填空：

> 两波打完木桩血条从 {targetBefore} 掉到 {targetAfter}。

## 边界

- 这一场不演其它技能合同。冷却闭环拆成「自己挂印」和「禁招印」两间房。
- 配置册上的 `cooldown` 块加载器不收，不在这场假装能用。

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_ability_feature_CallerParams --adapter raylib
```

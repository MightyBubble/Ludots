# 关掉时补一刀收尾

打开时什么也不打；再按一次关掉，收尾那一刀打在木桩上。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_ability_feature_ToggleDeactivateExec/poster.png" src="artifacts/evidence/capability_standard_ability_feature_ToggleDeactivateExec/play.mp4">
这场还没有验收录像。启动器进 `capability_standard_ability_feature_ToggleDeactivateExec` 看现场；采到录像后再补 artifacts/evidence/capability_standard_ability_feature_ToggleDeactivateExec/play.mp4。
</video>

## 作者写法

这一场只讲一个技能合同。写法摘自画廊真实技能表，手册分册是全量字段。

手册分册：[开关 · ab-08](../mod-editor-prd/config/ab-08-toggle.md)

真实用例（`mods/showcases/capability_standard/CapabilityStandardAbilityFeatureGalleryMod/assets/GAS/abilities/`）：

```json
{
  "id": "Ability.AbilityFeature.ToggleDeactivateExec",
  "exec": {
    "clockId": "FixedFrame",
    "items": [
      {
        "kind": "End",
        "tick": 0
      }
    ]
  },
  "toggleSpec": {
    "toggleTag": "State.AbilityFeature.ToggleArmed",
    "activeEffects": [],
    "deactivateExec": {
      "clockId": "FixedFrame",
      "items": [
        {
          "kind": "EffectSignal",
          "tick": 0,
          "template": "Effect.AbilityFeature.ToggleClose"
        },
        {
          "kind": "End",
          "tick": 0
        }
      ]
    }
  },
  "presentation": {
    "displayName": "关掉时补一刀收尾",
    "iconGlyph": "收",
    "hintText": "关闭时跑收尾时间轴。"
  }
}
```

## 这场是怎么搭出来的

短剧自己出手，不用先学键位。字幕用这场的结果填空：

> 关掉之后木桩血条从 {targetBefore} 掉到 {targetAfter}。

## 边界

- 这一场不演其它技能合同。冷却拆成「自己挂印」和「禁招印」两间房。
- 配置册上的 `cooldown` 块加载器不收，不在这场假装能用。

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_ability_feature_ToggleDeactivateExec --adapter raylib
```

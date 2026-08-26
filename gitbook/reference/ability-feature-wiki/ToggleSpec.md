# 再按一次关掉

第一下打开姿态印；再按一次，印灭掉。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_ability_feature_ToggleSpec/poster.png" src="artifacts/evidence/capability_standard_ability_feature_ToggleSpec/play.mp4">
这场还没有验收录像。启动器进 `capability_standard_ability_feature_ToggleSpec` 看现场；采到录像后再补 `artifacts/evidence/capability_standard_ability_feature_ToggleSpec/play.mp4`。
</video>

## 作者写法

这一场只讲一个技能合同。写法摘自画廊真实技能表，手册分册是全量字段。

手册分册：[开关 · ab-08](../mod-editor-prd/config/ab-08-toggle.md)

真实用例（`mods/showcases/capability_standard/CapabilityStandardAbilityFeatureGalleryMod/assets/GAS/abilities/`）：

```json
{
  "id": "Ability.AbilityFeature.ToggleSpec",
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
    "toggleTag": "State.AbilityFeature.ToggleOn",
    "activeEffects": []
  },
  "presentation": {
    "displayName": "再按一次关掉",
    "iconGlyph": "开",
    "hintText": "开关技能。"
  }
}
```

## 这场是怎么搭出来的

短剧自己出手，不用先学键位。字幕用这场的结果填空：

> 关掉之后施法者{casterTagState}。

## 边界

- 这一场不演其它技能合同。冷却闭环拆成「自己挂印」和「禁招印」两间房。
- 配置册上的 `cooldown` 块加载器不收，不在这场假装能用。

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_ability_feature_ToggleSpec --adapter raylib
```

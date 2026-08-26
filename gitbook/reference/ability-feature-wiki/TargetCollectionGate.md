# 名单齐了才打出去

出手之后先停住等名单；名单一到，近处木桩才掉血。远处那根只是名单上的名字，这一场不挨打。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_ability_feature_TargetCollectionGate/poster.png" src="artifacts/evidence/capability_standard_ability_feature_TargetCollectionGate/play.mp4">
这场还没有验收录像。启动器进 `capability_standard_ability_feature_TargetCollectionGate` 看现场；采到录像后再补 artifacts/evidence/capability_standard_ability_feature_TargetCollectionGate/play.mp4。
</video>

## 作者写法

这一场只讲一个技能合同。写法摘自画廊真实技能表，手册分册是全量字段。

手册分册：[执行时间轴 · ab-02](../mod-editor-prd/config/ab-02-exec-timeline.md)

真实用例（`mods/showcases/capability_standard/CapabilityStandardAbilityFeatureGalleryMod/assets/GAS/abilities/`）：

```json
{
  "id": "Ability.AbilityFeature.TargetCollectionGate",
  "exec": {
    "clockId": "FixedFrame",
    "items": [
      {
        "kind": "TargetCollectionGate",
        "tick": 0,
        "tag": "Input.AbilityFeature.Collect",
        "payloadA": 0
      },
      {
        "kind": "EffectSignal",
        "tick": 1,
        "template": "Effect.AbilityFeature.Strike"
      },
      {
        "kind": "End",
        "tick": 1
      }
    ]
  },
  "presentation": {
    "displayName": "名单齐了才打出去",
    "iconGlyph": "单",
    "hintText": "等外部名单。"
  }
}
```

## 这场是怎么搭出来的

短剧自己出手，不用先学键位。字幕用这场的结果填空：

> 名单齐了；近木桩 {targetAfter}，远木桩仍是 {target2After}。

## 边界

- 这一场不演其它技能合同。冷却拆成「自己挂印」和「禁招印」两间房。
- 配置册上的 `cooldown` 块加载器不收，不在这场假装能用。

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_ability_feature_TargetCollectionGate --adapter raylib
```

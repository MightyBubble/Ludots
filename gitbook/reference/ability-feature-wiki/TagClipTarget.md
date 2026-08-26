# 给对面挂一阵印

出手之后木桩头顶亮一枚印，过一会儿印自己掉。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_ability_feature_TagClipTarget/poster.png" src="artifacts/evidence/capability_standard_ability_feature_TagClipTarget/play.mp4">
这场还没有验收录像。启动器进 `capability_standard_ability_feature_TagClipTarget` 看现场；采到录像后再补 `artifacts/evidence/capability_standard_ability_feature_TagClipTarget/play.mp4`。
</video>

## 作者写法

这一场只讲一个技能合同。写法摘自画廊真实技能表，手册分册是全量字段。

手册分册：[执行时间轴 · ab-02](../mod-editor-prd/config/ab-02-exec-timeline.md)

真实用例（`mods/showcases/capability_standard/CapabilityStandardAbilityFeatureGalleryMod/assets/GAS/abilities/`）：

```json
{
  "id": "Ability.AbilityFeature.TagClipTarget",
  "exec": {
    "clockId": "FixedFrame",
    "items": [
      {
        "kind": "TagClipTarget",
        "tick": 0,
        "duration": 24,
        "tag": "Mark.AbilityFeature.TargetTimed"
      },
      {
        "kind": "End",
        "tick": 24
      }
    ]
  },
  "presentation": {
    "displayName": "给对面挂一阵印",
    "iconGlyph": "印",
    "hintText": "给当前目标挂一阵标记。"
  }
}
```

## 这场是怎么搭出来的

短剧自己出手，不用先学键位。字幕用这场的结果填空：

> 木桩现在{targetTagState}。

## 边界

- 这一场不演其它技能合同。冷却闭环拆成「自己挂印」和「禁招印」两间房。
- 配置册上的 `cooldown` 块加载器不收，不在这场假装能用。

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_ability_feature_TagClipTarget --adapter raylib
```

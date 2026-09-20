# pres-03 reference · 动画配置

> 现状参考。第一性需求见 [pres-03 PRD](../prd/pres-03-animation.md)；配置说明见 [pres-03 配置说明](../config/pres-03-animation.md)。

## 1. 现状快照

- animator_controllers：字段 id、states（非空：packedStateIndex/durationSeconds/playbackSpeed/loop）、transitions（conditionKind/parameterIndex/threshold/混合/退出时间/打断）、defaultStateIndex 必填；消费 AnimatorRuntimeSystem。
- animation_clips：字段 id、assetKind（默认 Clip；另有 BlendTree）、locators 非空（backendId/assetRef）、blendInputs。
- animation_profiles：字段 id、animatorControllerId、stateClips（packedStateIndex→clipAssetId）；拒绝 snake_case `builtin_clips` 键；消费含 Mass 集群动画。
- 核心样例：LudotsCoreMod locomotion 三件套（101 idle / 102 walk / 103 run，速度参数驱动转移）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 控制器加载校验 | src/Core/Presentation/Config/AnimatorControllerConfigLoader.cs:29-107 |
| 剪辑加载校验 | src/Core/Presentation/Config/AnimationClipConfigLoader.cs:28-65 |
| 档案加载（拒 builtin_clips） | src/Core/Presentation/Config/AnimationProfileConfigLoader.cs:34-63 |
| 三表引擎挂接 | src/Core/Engine/GameEngine.cs:1118-1120 |
| 样例 | mods/LudotsCoreMod/assets/Presentation/animator_controllers.json、animation_clips.json、animation_profiles.json |

**相关文档**：[pres-03 PRD](../prd/pres-03-animation.md) · [pres-01 reference](pres-01-performers.md)

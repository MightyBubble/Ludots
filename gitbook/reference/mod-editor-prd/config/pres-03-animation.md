# pres-03 配置说明 · 动画配置

> 配置写法与行为。第一性需求见 [pres-03 PRD](../prd/pres-03-animation.md)；编辑器需求见 [UXD](../uxd/pres-03-animation.md)；现状见 [reference](../reference/pres-03-animation.md)。

## 1. 示例配置

核心 mod 真实三件（`mods/LudotsCoreMod/assets/Presentation/`，节选）：

```json
[
  {
    "id": "core.hero.locomotion",
    "defaultStateIndex": 0,
    "states": [
      { "packedStateIndex": 101, "durationSeconds": 1.0, "playbackSpeed": 1.0, "loop": true },
      { "packedStateIndex": 102, "durationSeconds": 0.72, "playbackSpeed": 1.0, "loop": true }
    ],
    "transitions": [
      {
        "fromStateIndex": 0, "toStateIndex": 1,
        "conditionKind": "FloatGreaterOrEqual",
        "parameterIndex": "core.hero.locomotion.speed",
        "threshold": 0.20,
        "durationSeconds": 0.08, "durationMode": "Seconds",
        "consumeTrigger": false, "hasExitTime": false, "exitTime": 0.0,
        "interruptSource": "None", "orderedInterruption": false
      }
    ]
  }
]
```

```json
[
  {
    "id": "core.hero.state.idle",
    "assetKind": "Clip",
    "locators": [ { "backendId": "raylib", "assetRef": "animations/core/hero_idle.glb#anim:hero_idle" } ]
  }
]
```

```json
[
  {
    "id": "core.hero.profile.locomotion",
    "animatorControllerId": "core.hero.locomotion",
    "stateClips": [
      { "packedStateIndex": 101, "clipAssetId": "core.hero.state.idle" },
      { "packedStateIndex": 102, "clipAssetId": "core.hero.state.walk" }
    ]
  }
]
```

## 2. 字段与行为

| 表 | 字段 | 这样配会产生什么效果 |
|---|---|---|
| animator_controllers | `states[]` | 非空；packedStateIndex/durationSeconds/playbackSpeed/loop |
| animator_controllers | `defaultStateIndex` | 必填；进入控制器后的初始状态 |
| animator_controllers | `transitions[]` | from/to + conditionKind/parameterIndex/threshold + 混合时长/退出时间/打断源 |
| animation_clips | `assetKind` | 默认 Clip；BlendTree 声明混合树 |
| animation_clips | `locators[]` | 非空；backendId + assetRef（含子资源锚点 `#anim:`） |
| animation_clips | `blendInputs` | 混合树输入 |
| animation_profiles | `animatorControllerId` | 绑定的控制器 |
| animation_profiles | `stateClips[]` | packedStateIndex → clipAssetId 映射 |
| animation_profiles | `builtin_clips` | **拒绝**：snake_case 旧键，出现即抛错 |

## 3. 文件结构

`assets/Presentation/` 下三表：`animator_controllers.json`、`animation_clips.json`、`animation_profiles.json`（均 ArrayById，目录计数见事实页）。

## 4. 运行时加载效果

控制器/剪辑/档案依序加载（引擎挂接顺序见 reference）；档案加载时校验控制器与剪辑引用。运行期由 AnimatorRuntimeSystem 求值状态机，Mass 集群动画按档案批量采样。**生效级别：重启**。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| states 为空 / defaultStateIndex 缺失 | 启动失败，指明控制器 |
| locators 为空 | 启动失败，指明剪辑 |
| 档案引用未注册控制器/剪辑 | 启动失败 |
| 出现 builtin_clips 键 | 启动失败，指路 stateClips |

## 6. 实例

- `mods/LudotsCoreMod/assets/Presentation/animator_controllers.json`（locomotion 状态机）
- `mods/LudotsCoreMod/assets/Presentation/animation_clips.json`（idle/walk/run + BlendTree）
- `mods/LudotsCoreMod/assets/Presentation/animation_profiles.json`（状态-剪辑映射）

**相关文档**：[pres-03 PRD](../prd/pres-03-animation.md) · [pres-02 配置说明](pres-02-asset-registry.md)

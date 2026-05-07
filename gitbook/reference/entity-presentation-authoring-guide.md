# 实体表现配置入门指南

本文面向第一次接触 Ludots 的作者：你想配置一个 entity，让它能在地图上出现、能被选择、能有血条/文字/GAS 数据，并知道哪些表现入口是正式的。

## 0. 最短配置路径

第一次配置一个能在地图上看到的单位，按这个顺序做：

1. 在 `Presentation/mesh_assets.json` 注册 mesh 或 primitive 资产。
2. 在 `Presentation/visual_templates.json` 用 `meshAssetId` 定义 visual template。
3. 在 `Entities/templates.json` 写 entity 组件，至少包含 `WorldPositionCm`、`FacingDirection` 和 `Presentation.visualTemplateId`。
4. 如果要能选中，给 entity 加 `SelectionSelectableTag`；如果要显示血条和技能，给 entity 加 `AttributeBuffer`、`AbilityStateBuffer`、`AbilityFormSetRef` 等 GAS 组件。
5. 如果要额外的圈、飘字、特效、出生反馈，把这些写进 `Presentation/performers.json`，再通过 `startupPerformerIds`、GAS event 或 presentation rule 创建。

不要在 UI、adapter 或 showcase 私有代码里重新扫 entity 拼表现数据。正式链路是：配置进入 registry，entity 拥有逻辑组件，Core 生成 typed presentation/HUD buffer，平台层只消费这些 buffer。

## 1. 先理解三条真相线

一个实体能被看见，至少涉及三条配置线：

| 线 | 你配置什么 | 运行时真相 |
|----|------------|------------|
| 逻辑实体 | `Entities/templates.json` | entity 的组件集合 |
| 视觉资产 | `Presentation/mesh_assets.json` + `Presentation/visual_templates.json` | adapter 要画什么资产 |
| 表现反馈 | `Presentation/performers.json`、text token、GAS event | HUD、文字、特效、选择圈等反馈 |

位置和朝向只来自逻辑组件：

- `WorldPositionCm` 是平面位置唯一真相，单位是厘米。
- `PreviousWorldPositionCm` 由系统维护，用于插值。
- `FacingDirection.AngleRad` 是地面 2D 朝向唯一真相。
- `VisualTransform` 是表现层派生结果，不是 gameplay 作者应该直接维护的第二套位置。

## 2. 注册可画的资产

最小资产配置放在 `Presentation/mesh_assets.json`：

```json
[
  { "id": "my.unit.mesh", "type": "Primitive", "primitiveKind": "Sphere" },
  { "id": "my.building.mesh", "type": "Primitive", "primitiveKind": "Cube" }
]
```

真实模型也走同一个文件：

```json
[
  {
    "id": "my.knight.model",
    "type": "Model",
    "sourceUris": ["MyMod:assets/Presentation/Models/knight.glb"]
  }
]
```

然后在 `Presentation/visual_templates.json` 定义实体默认外观：

```json
[
  {
    "id": "my.unit.visual",
    "renderPath": "StaticMesh",
    "meshAssetId": "my.unit.mesh",
    "materialId": 0,
    "baseScale": 1.2,
    "mobility": "Movable",
    "visibleByDefault": true
  }
]
```

如果是骨骼动画资产，`renderPath` 必须使用 skinned lane，并绑定有效的 `animationProfileId`。缺失 profile 会直接报错，不会降级成静态 mesh。

## 3. 配置实体模板

实体模板放在 `Entities/templates.json`。下面是一个能出现在地图上、可选择、带属性和技能的最小单位：

```json
[
  {
    "id": "my_footman",
    "components": {
      "Name": { "Value": "Footman" },
      "Team": { "Id": 1 },
      "SelectionSelectableTag": {},
      "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
      "FacingDirection": { "AngleRad": 0.0 },
      "AttributeBuffer": {
        "base": {
          "Health": 120,
          "MoveSpeed": 320,
          "Armor": 6
        }
      },
      "AbilityStateBuffer": {
        "abilityIds": [
          "Ability.MyMod.Footman.Attack",
          "Ability.MyMod.Footman.Guard"
        ]
      },
      "GameplayTagContainer": {},
      "OrderBuffer": {},
      "BlackboardSpatialBuffer": {},
      "BlackboardEntityBuffer": {},
      "BlackboardIntBuffer": {},
      "Presentation": {
        "visualTemplateId": "my.unit.visual"
      }
    }
  }
]
```

写了 `WorldPositionCm` 后，组件注册器会补齐插值和表现同步需要的伴随组件。`Presentation.visualTemplateId` 会解析到 `VisualTemplateRegistry`，并分配 `PresentationStableId`。

## 4. 给实体加 HUD bar、HUD text 和 GAS 飘字

血条和世界文字走 performer/HUD 管线，不要在 UI 里重新扫实体拼字符串。

内置血条定义是 `entity_health_bar`，由 Core 注册，读取带 `AttributeBuffer` 的实体。开关由 `RenderDebugState.DrawWorldHudBars` 控制。

一次性飘字通常来自 GAS 事件。真实运行链路是：

```text
GAS effect application
  -> GasPresentationEventBuffer(EffectApplied)
  -> PresentationBridgeSystem
  -> PresentationEventKind.EffectApplied
  -> PerformerRuleSystem
  -> floating_combat_text
  -> WorldHudBatchBuffer
  -> Skia/Web HUD renderer
```

内置 `floating_combat_text` 监听 `EffectApplied`，因此作者要做的是两件事：

1. 让技能或效果通过 GAS 正式应用属性变化。
2. 在 text catalog 中提供飘字 token。

文字内容来自 `Presentation/text_tokens.json` 和 `Presentation/text_locales.json`：

```json
[
  { "id": "hud.damage", "argCount": 1 }
]
```

```json
{
  "defaultLocale": "en-US",
  "locales": {
    "en-US": {
      "hud.damage": "-{0}"
    }
  }
}
```

WorldText performer 的 token 要写在 `defaultTextId`：

```json
[
  {
    "id": "my.damage_text",
    "visualKind": "WorldText",
    "defaultTextId": "hud.damage",
    "defaultFontSize": 18,
    "defaultLifetime": 1.0,
    "alphaFadeOverLifetime": true,
    "positionOffset": [0, 1.2, 0],
    "bindings": [
      { "paramKey": 0, "source": "attribute", "attribute": "Health" }
    ]
  }
]
```

禁止写 `source: "textToken"`。text token 是 `defaultTextId` 的职责；数值参数才走 `bindings`。

如果要自定义某个 GAS 事件对应的飘字或特效，在 performer rule 中监听正式 presentation event：

```json
[
  {
    "id": "my.damage_text",
    "visualKind": "WorldText",
    "defaultTextId": "hud.damage",
    "defaultFontSize": 18,
    "defaultLifetime": 1.0,
    "alphaFadeOverLifetime": true,
    "positionOffset": [0, 1.2, 0],
    "rules": [
      {
        "event": { "kind": "EffectApplied" },
        "condition": { "inline": "SourceHasVisualTransform" },
        "command": {
          "kind": "CreatePerformer",
          "definitionId": "my.damage_text",
          "scopeSource": "SourceStableId"
        }
      }
    ]
  }
]
```

`EffectApplied` 的事件数据由 Core 桥接：`Source` 是 actor，`Target` 是 effect target，`Magnitude` 是属性 delta，`PayloadA` 是 attribute id。当前 JSON binding 仍只读取正式支持的 `ValueRef` 数据源；需要把 event payload 直接作为 text 参数时，应补正式 binding/source contract，不要临时发明 `source` 字段。

## 5. 配置选择圈、施法预览和其他 performer

`Presentation/performers.json` 里可以定义选择圈、落点预览、命中特效等表现反馈：

```json
[
  {
    "id": "my.selection_ring",
    "visualKind": "GroundOverlay",
    "meshOrShapeId": "Ring",
    "defaultColor": [0.27, 0.74, 0.96, 0.18],
    "defaultScale": 1.3,
    "defaultLifetime": -1,
    "positionOffset": [0, 0.04, 0],
    "bindings": [
      { "paramKey": 1, "source": "constant", "constantValue": 0.95 },
      { "paramKey": 12, "source": "constant", "constantValue": 0.03 }
    ]
  }
]
```

如果希望实体出生后自带某个持久 performer，在模板的 `Presentation` 块里声明：

```json
{
  "Presentation": {
    "visualTemplateId": "my.unit.visual",
    "startupPerformerIds": ["my.selection_ring"]
  }
}
```

如果希望某类实体出生/销毁时自动创建或销毁 performer，使用 `rules`：

```json
[
  {
    "id": "my.spawn_marker",
    "visualKind": "Marker3D",
    "meshOrShapeId": "Sphere",
    "defaultColor": [0.3, 0.8, 1.0, 0.9],
    "defaultScale": 0.4,
    "defaultLifetime": 0.25,
    "rules": [
      {
        "event": { "kind": "EntitySpawned", "key": "my_footman" },
        "condition": { "inline": "SourceHasVisualTransform" },
        "command": {
          "kind": "CreatePerformer",
          "definitionId": "my.spawn_marker",
          "scopeSource": "EventPayloadA"
        }
      }
    ]
  }
]
```

`SourceHasVisualTransform` 的意思是：事件源必须已经接入正式表现同步链路。缺这个条件时，如果事件源没有 `VisualTransform`，后续锚点无法成立，会在运行时暴露为错误或无输出。

## 6. Performer 参数不是临时魔法数

当前 JSON authoring 仍使用 `paramKey` 数字，但这些数字不是临时约定。它们由 `WellKnownPerformerParamKeys` 定义，文档里只在正式参数表中出现。

| VisualKind | 名称 | paramKey | 含义 |
|------------|------|----------|------|
| `Marker3D` | `MarkerScale` | `0` | 统一缩放 |
| `Marker3D` | `MarkerScaleX/Y/Z` | `1/2/3` | 分轴缩放 |
| `Marker3D` | `MarkerColorR/G/B/A` | `4/5/6/7` | 颜色 |
| `Marker3D` | `MarkerUseOwnerRotation` | `8` | 非 0 时继承 owner 的 `VisualTransform.Rotation` |
| `WorldBar` | `BarFillRatio` | `0` | 填充比例 |
| `WorldBar` | `BarWidth/Height` | `1/2` | 屏幕像素尺寸 |
| `WorldBar` | `BarForegroundR/G/B/A` | `4/5/6/7` | 前景颜色 |
| `WorldBar` | `BarBackgroundR/G/B/A` | `8/9/10/11` | 背景颜色 |
| `WorldText` | `TextValue0/1` | `0/1` | 格式化数值 |
| `WorldText` | `TextFontSize` | `3` | 字号 |
| `WorldText` | `TextColorR/G/B/A` | `4/5/6/7` | 颜色 |
| `WorldText` | `TextTokenId` | `15` | 覆盖 `defaultTextId` 的 token id |
| `WorldText` | `TextValueMode` | `16` | legacy adapter value mode |
| `GroundOverlay` | `OverlayRadius` | `0` | 半径 |
| `GroundOverlay` | `OverlayInnerRadius` | `1` | 内半径 |
| `GroundOverlay` | `OverlayAngle` | `2` | 扇形角度 |
| `GroundOverlay` | `OverlayRotation` | `3` | 旋转 |
| `GroundOverlay` | `OverlayFillR/G/B/A` | `4/5/6/7` | 填充颜色 |
| `GroundOverlay` | `OverlayBorderR/G/B/A` | `8/9/10/11` | 边线颜色 |
| `GroundOverlay` | `OverlayBorderWidth` | `12` | 边线宽 |
| `GroundOverlay` | `OverlayLength/Width` | `13/14` | 矩形覆盖尺寸 |

如果你在 C# 里生成配置或测试，请引用 `WellKnownPerformerParamKeys`，不要手写数字。

## 7. 什么时候显示、裁剪和关闭

常用开关如下：

| 需求 | 正式入口 |
|------|----------|
| 实体默认是否画出来 | `Presentation.visualTemplateId` + visual template 的 `visibleByDefault` |
| 本模板实例是否覆盖可见性 | `Presentation.visible` |
| performer 是否创建 | `rules`、`startupPerformerIds`、GAS/输入系统发出的正式 presentation command |
| performer 是否可见 | `visibility` + inline condition，例如 `OwnerCullVisible` |
| HUD bar/text 总开关 | `RenderDebugState.DrawWorldHudBars` / `DrawWorldHudText` |
| 地形贴合 | `WorldToVisualSyncSystem` 后的 `TerrainHeightSyncSystem`，读取 Core 的 `IVisualHeightmap` |
| 距离裁剪 | 正式 culling/AOI 系统；不要在 performer JSON 写 `maxVisibilityDistanceCm` |

已移除字段会直接报错：

- `entityScope`
- `requiredTemplate`
- `maxVisibilityDistanceCm`
- command 里的 `commandKind`
- command 里的 `scopeId`
- command 里的 `behaviorSlot`
- behavior 里的 `slotIndex`
- binding 里的 `source: "textToken"`
- binding 里的 `source: "graph"`

## 8. 小地图状态

当前这棵工作树还没有 Core-owned `MinimapMarker` authoring/runtime contract。不要把旧 `mods/capabilities/minimap` 的名字扫描、`MapEntity`、`Team` 推断或标签聚合写进新配置指南。

未来正式小地图入口应满足：

- 是否上小地图由表现/performer authoring 明确声明。
- 位置来自同一个 owner transform 真相，不再重复投影。
- 输出是 typed buffer，Skia 和 Web 可以消费同一份数据。
- 距离裁剪、显示层级、开关状态由明确配置表达，不走 fallback 推断。

在 Core contract 落地前，新用户不要用小地图字段判断 entity 是否配置正确。

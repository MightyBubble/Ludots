# Core Minimap Authoring

本文定义 Ludots Core 小地图的正式接入口：一个对象是否显示在小地图上，只由 presenter authoring 中显式声明的 `MinimapMarker` behavior 决定。小地图不扫描 gameplay entity 名字，不读取 `MapEntity` 作为信号入口，也不从 `Team`、chunk、LOD、heightmap 或可见性 fallback 推断 marker。

## 1. 数据流

正式链路如下：

```text
Entity WorldPositionCm / FacingDirection
  -> WorldToVisualSyncSystem
  -> PresenterEntityTransformSyncSystem
  -> PresenterWorldPlanePosition / PresenterWorldFacing
  -> PresenterMinimapMarkerSystem
  -> MinimapMarkerBuffer
  -> MinimapPresentationSystem
  -> MinimapScreenMarkerBuffer
  -> PresentationOverlayScene MinimapMarker lane
  -> SkiaOverlayRenderer
```

职责边界：

- `WorldPositionCm` 是逻辑位置 SSOT；`FacingDirection.AngleRad` 是地面 2D 朝向 SSOT。
- `PresenterWorldPlanePosition` 和 `PresenterWorldFacing` 是 presenter 输出给小地图的正式表现层快照。
- `MinimapMarkerBuffer` 保存 world marker SoA 数据：`stableId/worldXcm/worldYcm/color/sizePx/flags/orientationRad/orientationLengthPx`。
- `MinimapScreenMarkerBuffer` 保存投影后的 screen marker SoA 数据。
- Skia 只画已经投影好的 marker lane，不回查 ECS，不做聚合，不塞进 Rect/Text fallback。

## 2. 如何让实体显示在小地图

给拥有该实体表现的 presenter definition 增加 `MinimapMarker` behavior：

```json
{
  "slot": 3,
  "kind": "MinimapMarker",
  "activeByDefault": true,
  "minimapMarker": {
    "shape": "Circle",
    "color": [1.0, 0.24, 0.06, 1.0],
    "sizePx": 11.0,
    "colorParamKey": -1,
    "sizeParamKey": -1,
    "visibilityParamKey": -1,
    "orientationMode": "PresenterForward",
    "orientationParamKey": -1,
    "orientationOffsetRad": 0.0,
    "orientationLengthPx": 15.0
  }
}
```

字段语义：

- `shape`: v1 只支持 `Circle`。非法 shape 必须报错。
- `color`: 默认 RGBA 颜色。
- `sizePx`: 默认屏幕像素尺寸。
- `colorParamKey`: `Vector` param 覆盖颜色；`-1` 表示不用参数覆盖。
- `sizeParamKey`: `Float` param 覆盖尺寸；`-1` 表示不用参数覆盖。
- `visibilityParamKey`: `Int` 或 `Float` param 控制显示；`-1` 表示该 behavior 激活时总显示。
- `orientationMode`: `None`、`PresenterForward`、`ParamRadians`、`ParamDegrees`。
- `orientationParamKey`: 参数朝向来源，仅在 `ParamRadians` 或 `ParamDegrees` 下使用。
- `orientationOffsetRad`: 在最终朝向上追加的弧度偏移。
- `orientationLengthPx`: 小地图上朝向线长度。

## 3. 如何让实体不显示

推荐顺序：

1. 不需要小地图显示的 presenter 不声明 `MinimapMarker` behavior。
2. 需要运行时开关时，用 presenter command 激活/停用该 behavior。
3. 需要由数据控制显示时，配置 `visibilityParamKey`，由正式 presenter binding、rule 或其他业务系统写对应 param。

`visibilityParamKey` 规则：

- `Int` lane: `0` 隐藏，非 `0` 显示。
- `Float` lane: `> 0.5` 显示，否则隐藏。
- 参数不存在时隐藏；这是显式配置缺失，不 fallback 到其他来源。

## 4. 显示多少内容

Core minimap v1 只表达 authored marker：

- 圆点位置
- 颜色
- 尺寸
- 可见性
- 可选 2D 地面朝向

不表达：

- 名字
- 阵营关系推断
- 地形背景
- 战略热力图
- 聚合格子
- entity component 查询 fallback

如果需要阵营色、选中态、隐身态、建筑类型大小等内容，应由 presenter 参数写入 `colorParamKey`、`sizeParamKey` 或 `visibilityParamKey`，小地图只消费这些参数。

## 5. 距离裁剪和显示时机

小地图 runtime 不做 marker 距离裁剪。原因是距离裁剪属于 gameplay/presentation authoring 策略，不属于小地图 view。

正式做法：

- 按距离隐藏：业务系统或 presenter binding 计算距离，把结果写入 `visibilityParamKey`。
- 按玩家视野隐藏：视野系统写入 presenter param，`MinimapMarker` 读取该 param。
- 按选择、任务、阵营、高度层、调试状态开关：同样写 param 或激活/停用 behavior。

禁止做法：

- 让 minimap 扫 `Name + WorldPositionCm + MapEntity`。
- 用 `CullState.IsVisible`、visual LOD、chunk active state、heightmap 是否加载来 gate marker 存在性。
- 因为没有 marker 就改扫其他组件。

## 6. Viewport 和交互接口

Core runtime 类型：`MinimapRuntime`。

常用接口：

- `UseRtsFullMapPreset()`: RTS 全图模式，viewport 来自 `WorldSizeSpec.Bounds`。
- `UseFollowCameraPreset(float halfExtentCm, bool rotateWithCamera)`: 跟随相机目标点。
- `UseFollowEntityPreset(Entity entity, float halfExtentCm)`: 跟随指定 presenter/entity。
- `SetRotateWithCamera(bool enabled)`: 开关小地图是否跟随相机旋转。
- `SetZoomNormalized(float normalized)`: 设置缩放 SSOT，范围 `[0, 1]`。
- `ApplyWheelZoom(float wheelDelta, Vector2 screenAnchor)`: 鼠标滚轮缩放，内部仍写同一个 normalized zoom。
- `SetZoomFromSliderPointer(Vector2 screenPosition)`: 缩放条输入，内部仍写同一个 normalized zoom。
- `JumpCameraTo(GameEngine engine, Vector2 worldCm)`: RTS 点击/拖拽跳转相机。
- `TryScreenToWorld` / `TryScreenToWorldClamped`: 小地图屏幕点到世界坐标。

统一输入 action 常量在 `MinimapInputActions`：

- `Minimap.Toggle`
- `Minimap.TogglePreset`
- `Minimap.ToggleRotateWithCamera`
- `Minimap.Zoom`
- `Minimap.ZoomIn`
- `Minimap.ZoomOut`
- `Minimap.Pan`
- `Minimap.CenterOnSelection`

输入必须走统一 input 基建。滚轮、点击、拖拽、缩放条和 toggle 都通过 `MinimapInputConsumer` 消费，并在命中小地图交互区域时设置 pointer capture，防止穿透到相机或世界交互。

## 7. 缩放配置

配置入口在 `presentation.minimap`：

```json
{
  "presentation": {
    "minimap": {
      "initialZoomNormalized": 1.0,
      "wheelZoomNormalizedStep": 0.08,
      "buttonZoomNormalizedStep": 0.18,
      "zoomSliderEnabled": true,
      "modeToggleEnabled": true,
      "rotateToggleEnabled": true,
      "debugMarkerSampleCapacity": 64,
      "minZoomExtentMode": "OneChunk",
      "maxZoomExtentMode": "FullMap",
      "minZoomExplicitHalfExtentCm": 750.0,
      "maxZoomExplicitHalfExtentCm": 0.0
    }
  }
}
```

缩放 SSOT 是 `MinimapRuntime.ZoomNormalized`。滚轮和缩放条都只改变这个 normalized 值，再由 runtime 根据当前 map bounds 和配置换算成 `HalfExtentCm`。

`minZoomExtentMode` / `maxZoomExtentMode`：

- `OneChunk`: 从默认 board 的 `ChunkSizeCells * GridCellSizeCm * 0.5` 推导半径。
- `FullMap`: 从 `WorldSizeSpec.Bounds` 推导全图半径。
- `ExplicitCm`: 使用显式半径。

若 board/bounds 缺失，runtime 报错；不得切换到其他推断路径。

## 8. Showcase 验收基线

`presenter_blacksmith_minimap_marker_large_world_showcase` 是当前大世界小地图验收场景。

验收点：

- 256x256 chunk 大世界。
- presenter authoring 显式声明 `MinimapMarker`。
- 世界中可看到大球与朝向 primitive。
- 小地图中可看到对应 marker 和朝向。
- RTS 模式可点击/拖拽跳相机。
- Follow Camera 模式以相机为中心。
- 缩放条和滚轮共享同一个 normalized zoom。
- visual heightmap 只影响世界场景地形和 grounding，不决定 marker 是否存在。

30k marker 是性能压力基线；近景 300 个可辨识大球是视觉验收基线。

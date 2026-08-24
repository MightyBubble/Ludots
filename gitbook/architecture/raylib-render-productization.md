# Raylib Render Productization

本页记录 Raylib 客户端产品化的正式口径：窗口、输入、生命周期和诊断由 host 驱动；画面组织按 Core 表现请求的缓冲执行。这条路径是 Unity、UE、Godot 适配器要抄的标准答案。

## 标准答案

1. Core 发出 `PresentationRequest`（Mesh / Decal / VFX / Surface / GroundOverlay / SplineRibbon / HUD）
2. 类型化通道收齐后，`PresentationRequestFlushSystem` 写入 `PrimitiveDrawBuffer`、地面提示缓冲、样条带缓冲、HUD 缓冲
3. 引擎适配器只画这些缓冲，不另开 Prefab 定稿、不在 adapter 里猜语义

Raylib 当前执行面：

- 有水面反射时，host 先走天空与水面通道，再画 Core 缓冲（水面帧缓冲不能和后处理叠在一起）
- 没有水面时，后处理包住世界画面
- `AssetKind.VFX` 的缓冲项交给 `RaylibVfxRenderer`，粒子定义来自 `Presentation/particle_vfx.json`

`RaylibFrameRenderer.BuildPassPlan` 记录世界 pass 再 UI composite 的顺序，给其他引擎对照。

## VFX 资产

VFX 是 Presentation 资产，不是 Raylib 私有配置。当前 SSOT：

- 粒子定义：`Presentation/particle_vfx.json`（Quarks schema，见 [Quarks Particle Schema](quarks-particle-schema.md)）
- Mesh 句柄：`mesh_assets.json` 的 `vfx.particleVfxId`
- Presenter 的 `AssetBinding`（`assetKind: VFX`）引用上述句柄；flush 后成为 `PrimitiveDrawItem`

最小示例：

```json
{
  "id": "effect.camera.projection_cue",
  "type": "Primitive",
  "primitiveKind": "Sphere",
  "vfx": {
    "particleVfxId": "camera.projection_cue.particles"
  }
}
```

Raylib 只消费 flush 后的 VFX 项与粒子 runtime snapshot。它不拥有效果 key 解析，不创建平行 registry，也不替 Presenter 决定语义。贴图、trail 长度、spawnMode 等观感字段全部由粒子资产驱动；缺失或无法解析时直接报错。

## 地形环境

正式 Showcase 地形路径包含天空、光照/雾、水面、后处理与大地图远景。地图可通过 `VisualHeightmapRenderProfile` 声明海平面、水体开关、高度夸张和颜色对比度。超大 chunk 必须降采样，避免索引上限；截图证据必须做完整 PNG 校验，不允许“有文件就算过”。

大地图远景实拍（引擎画廊 `terrain_heightmap` 场景：绝对海拔色带 + 水下陆架 + 超密降采样）：

<img src="artifacts/acceptance/engine_gallery_all/terrain_heightmap.png" alt="视觉高度图验收截图" width="880">

## 演进方向

继续扩展 Presentation-owned Quarks 合同（curve、burst、shape module、material binding、GPU buffer plan）。Raylib adapter 只增加执行能力；Core 仍然负责配置合同、校验和平台无关的 runtime 项。禁止回到 `vfx.emitter` 内嵌合同或在 mesh 资产上双写 spawnMode。

## 引擎能力画廊

各渲染能力的标准化独立展示（零 Core 依赖、一能力一场景）见 [Raylib 引擎能力标准化 Showcase](engine-capability-showcases.md)；本文保持产品化合同与演进方向的真源。

## 光照栈

单物体 GGX 通道、方向光 shadow map、材质标量 PBR、解析式天空 IBL 的合同与用法见 [渲染光照栈与下游使用指南](render-lighting-guide.md)；本文保持产品化合同与演进方向真源。

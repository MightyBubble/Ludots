# Raylib Render Productization

本页记录 Raylib 客户端产品化的正式口径：Raylib host 负责窗口、输入、生命周期和诊断；画面组织由 renderer 模块接管；VFX 必须从 Presentation 资产数据进入渲染，不允许在 adapter 里临时硬编码效果。

## 渲染入口

`RaylibHostLoop` 只负责驱动每帧。具体画面顺序由 `RaylibFrameRenderer` 统一组织：

1. 清屏与 3D 相机状态
2. 地形、全局场、benchmark 场景
3. primitive / prefab / performer 输出
4. 地面提示、道路样条、debug draw
5. browser layer 与 UI composite

这个顺序是 Raylib 后续接入 PBR、后处理、阴影、粒子和平台资源缓存的正式入口。新增 pass 应该进入 frame renderer，而不是回到 host loop 里继续堆条件分支。

## VFX 资产

VFX 是 Presentation 资产，不是 Raylib 私有配置。当前 SSOT：

- 粒子定义：`Presentation/particle_vfx.json`（Quarks schema，见 [Quarks Particle Schema](quarks-particle-schema.md)）
- Mesh 句柄：`mesh_assets.json` 的 `vfx.particleVfxId`
- Prefab / performer / instanced batch 通过 effect asset key 引用上述句柄

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

Raylib 只消费 finalization 后的 VFX leaf 与粒子 runtime snapshot。它不拥有效果 key 解析，不创建平行 registry，也不替 prefab 或 performer 决定语义。贴图、trail 长度、spawnMode 等观感字段全部由粒子资产驱动；缺失或无法解析时 fail-loud。

## 地形环境

正式 Showcase 地形路径包含天空、光照/雾、水面、后处理与大地图远景 LOD。地图可通过 `VisualHeightmapRenderProfile` 声明海平面、水体开关、高度夸张和颜色对比度。超大 chunk 必须降采样，避免索引上限；截图证据必须做完整 PNG 校验，不允许“有文件就算过”。

## 演进方向

继续扩展 Presentation-owned Quarks 合同（curve、burst、shape module、material binding、GPU buffer plan）。Raylib adapter 只增加执行能力；Core 仍然负责配置合同、校验和平台无关的 runtime leaf。禁止回到 `vfx.emitter` 内嵌合同或在 mesh 资产上双写 spawnMode。

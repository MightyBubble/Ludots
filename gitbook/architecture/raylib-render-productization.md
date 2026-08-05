# Raylib Render Productization

本页记录 Raylib 客户端产品化的第一条正式口径：Raylib host 负责窗口、输入、生命周期和诊断；画面组织由 renderer 模块接管；VFX 必须从 Presentation 资产数据进入渲染，不允许在 adapter 里临时硬编码效果。

## 渲染入口

`RaylibHostLoop` 只负责驱动每帧。具体画面顺序由 `RaylibFrameRenderer` 统一组织：

1. 清屏与 3D 相机状态
2. 地形、全局场、benchmark 场景
3. primitive / prefab / performer 输出
4. 地面提示、道路样条、debug draw
5. browser layer 与 UI composite

这个顺序是 Raylib 后续接入 PBR、后处理、阴影、粒子和平台资源缓存的正式入口。新增 pass 应该进入 frame renderer，而不是回到 host loop 里继续堆条件分支。

## VFX 资产

VFX effect 是 Presentation 资产，不是 Raylib 私有配置。当前 SSOT 是 `MeshAssetRegistry`：

- `prefabs.json` 的 VFX part 使用 `effectAssetId` 引用资产 key；
- `instanced_batches.json` 的 effect 操作也使用同一类资产 key；
- 被引用的 effect asset 必须在 `mesh_assets.json` 中声明 `vfx.emitter`；
- 裸数字、未知 key、缺少 emitter 数据都会直接报错。

最小示例：

```json
{
  "id": "effect.camera.projection_cue",
  "type": "Primitive",
  "primitiveKind": "Sphere",
  "vfx": {
    "emitter": {
      "shape": "PrimitiveSphere",
      "particleCount": 24,
      "ringSegments": 20,
      "radiusScale": 1.15,
      "coreRadiusScale": 0.28,
      "particleRadiusScale": 0.085,
      "lifetimeSeconds": 0.75,
      "pulseSpeedRadPerSecond": 5.2,
      "orbitSpeedRadPerSecond": 1.7
    }
  }
}
```

Raylib 只消费 finalization 后的 VFX leaf 与 effect asset 数据，生成本帧 emitter plan 并绘制。它不拥有 effect key 解析，不创建平行 registry，也不替 prefab 或 performer 决定语义。

## 演进方向

下一阶段如果要做接近 Quarks 的粒子框架，应继续扩展这份 Presentation-owned effect asset contract，例如多 emitter、curve、burst、shape module、material binding 和 GPU buffer plan。Raylib adapter 只增加执行能力；Core 仍然负责配置合同、校验和平台无关的 runtime leaf。

# 火花、烟雾、火星拖尾

三组粒子同时在场：加色火花、逐帧贴图烟雾、拉伸火星拖尾——`ParticleVfxAssetData` 手工构造，走图元渲染器的 VFX 通道驱动 `ParticleSystemRuntime`。

<img src="artifacts/acceptance/engine_gallery_all/particles.png" alt="particles 验收截图" width="880">

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `particles` |
| preset | `engine_raylib_particles` |
| 场景源码 | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Scenes/ParticlesScene.cs` |
| 承接渲染器 | `RaylibPrimitiveRenderer` VFX 通道 + `ParticleSystemRuntime` |
| 注册表条目 | `engine_raylib_particles`（`showcase.registry.json`，tier T1） |

效果是资产数据不是代码：`ParticleVfxAssetData` 给出生成模式（`ParticleVfxSpawnMode.Loop`）、尺寸区间（如火花 `startSize 0.05–0.11`）等字段，三组效果三个资产 id（301 火花 / 302 烟雾 / 303 火星）。Quarks 粒子 schema 的全量字段与语义见 [Quarks Particle Schema](../../architecture/quarks-particle-schema.md)。

## 这场演的是什么

- 加色混合的火花往上迸、贴图烟雾逐帧翻页扩散、拉伸火星拖出尾迹——三种粒子形态对应三种渲染分支。
- 资产贴图（烟雾帧表）程序化生成（`SmokeSheetAssetId` 310），线上资产走正式粒子 JSON。
- 粒子是 VFX 语义：不投深度阴影，混合走加色/覆盖分支。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/particles.png` / `.json`（CI 批跑，30 帧）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 30 | 1.79 | 0.78 | 43.01 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_particles --adapter raylib
```

## 边界与深读

- 粒子不做碰撞与光照反应（无光照粒子是当前合同）；需要受光的烟雾请走地表/模型车道。
- 深读：[Quarks Particle Schema](../../architecture/quarks-particle-schema.md)。

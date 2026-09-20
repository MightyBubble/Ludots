# 草和树是贴片，影子会漏光

整片草丛和树都是镂空公告板（billboard），alpha-cutout 材质把贴图透明处打穿；阴影深度 pass 同样按 alpha 打孔——树冠的影子是斑驳的，不是一整块实心矩形。

<video controls playsinline preload="metadata" poster="artifacts/evidence/engine_raylib_vegetation_cutout/poster.png" src="artifacts/evidence/engine_raylib_vegetation_cutout/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/engine_raylib_vegetation_cutout/play.mp4`。
</video>

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `vegetation_cutout` |
| preset | `engine_raylib_vegetation_cutout` |
| 场景源码 | `src/Content/Ludots.Content.EngineGallery/Scenes/VegetationCutoutScene.cs` |
| 承接渲染器 | billboard 资产（`MeshAssetDescriptor.Billboard`）+ `vegetation_cutout` 着色器 |
| 注册表条目 | `engine_raylib_vegetation_cutout`（`showcase.registry.json`，tier T1） |

作者面两条配置：mesh 资产行声明 Billboard 类型挂贴图，材质行走 Cutout 混合模式。贴图由场景程序化生成（草 96×128 九叶束、树 128×192），线上换成正式植被贴图即可；alpha 阈值取 `DefaultVegetationAlphaCutoff`，不逐材质散设。

## 这场演的是什么

- 草丛阵列 + 六棵树全是面向相机的贴片；走近看边缘是硬裁切（cutout），不是半透明渐隐。
- 阴影深度 pass 用 `shadow_depth_cutout` 采 albedo alpha 打孔——树影里漏出光斑，这是本车道最值得看的一眼。
- HUD 报告当帧 billboard 数（草束 + 树），合批走图元车道的实例化路径。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/vegetation_cutout.png` / `.json`（120 帧验收批）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 120 | 3.83 | 2.39 | 259.74 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_vegetation_cutout --adapter raylib
```

## 边界与深读

- 半透明（AlphaBlend）植被不在本车道：cutout 与 alpha-blend 是两种混合语义，后者不投深度影（见材质合同）。
- 投影资格矩阵：[渲染光照栈与下游使用指南](../../architecture/render-lighting-guide.md)的「方向光 Shadow Map」节。

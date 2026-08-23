# 八块立方，八种材质合同

两排立方把材质系统的三个正交轴全部摆上台面：同一网格换贴图/改参数（实例链）、改着色行为（shaderKey 自定义车道）、混合模式差异（不透明/裁切/半透明）。

<video controls playsinline preload="metadata" poster="artifacts/evidence/engine_raylib_material_binding/poster.png" src="artifacts/evidence/engine_raylib_material_binding/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/engine_raylib_material_binding/play.mp4`。
</video>

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `material_binding` |
| preset | `engine_raylib_material_binding` |
| 场景源码 | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Scenes/MaterialBindingScene.cs` |
| 承接渲染器 | `RaylibMaterialLibrary`（`src/Client/Ludots.Raylib.Render` 的材质装订库） |
| 注册表条目 | `engine_raylib_material_binding`（`showcase.registry.json`，tier T1） |

作者面就是 `Presentation/materials.json` 的 `MaterialAssetDescriptor`：`ParentKey` 指父材质，子材质只写差异字段（`MaterialAssetResolver` 沿链合并）；`ShaderKey` 分派自定义着色车道（`RaylibShaderCatalog` 注册表，非实例化车道遇自定义 key fail-loud）；`FloatParams`/`ColorParams` 按键名直推 uniform。场景里 `iron → rusty`（改贴图 + roughness 0.95）与 `emissive → hot`（`uEmissiveStrength` 3.0 + 橙红 `uEmissiveColor`）两条链就是作者写法的活样例。

## 这场演的是什么

- 第一排三槽：不透明棋盘 / 裁切条纹（Cutout）/ 半透明光斑（AlphaBlend）——混合模式只影响材质本身，不改变装订路径。
- 第二排四立方走实例链：铁基底、锈蚀覆盖、青色自发光、热覆盖，同网格不同材质合同。
- 两排立方自旋方向相反，全部投深度阴影；自发光立方夜里也不「照亮」别人（单灯合同，发光是着色不是光源）。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/material_binding.png` / `.json`（120 帧验收批）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 120 | 3.01 | 1.41 | 239.16 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_material_binding --adapter raylib
```

## 边界与深读

- 三轴正交：换贴图/改参数、改着色、混合模式——不存在第四个轴；材质不携带几何信息。
- 深读：[渲染光照栈与下游使用指南](../../architecture/render-lighting-guide.md)的「材质实例与 shaderKey」节。

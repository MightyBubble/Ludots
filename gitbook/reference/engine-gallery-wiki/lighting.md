# 一排球把整条光照栈亮给你看

二十一颗球按「粗糙度 × 金属度」排成梯度阵，太阳绕着走，金属带映出天空——GGX 直射光、split-sum 天空 IBL、深度阴影一次看全。

<video controls playsinline preload="metadata" poster="artifacts/evidence/engine_raylib_lighting/poster.png" src="artifacts/evidence/engine_raylib_lighting/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/engine_raylib_lighting/play.mp4`。
</video>

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `lighting` |
| preset | `engine_raylib_lighting` |
| 场景源码 | `src/Content/Ludots.Content.EngineGallery/Scenes/LightingScene.cs` |
| 承接渲染器 | `RaylibLitModel` + `RaylibSkyIbl` + `RaylibDirectionalShadowMap`（`src/Client/Ludots.Raylib.Render`） |
| 注册表条目 | `engine_raylib_lighting`（`showcase.registry.json`，tier T1） |

这条车道就是宿主里道具与少量模型用的单物体通道：构造 `RaylibLitModel`，每帧 `BeginFrame(lighting, camera.position, shadowMap)`，然后 `DrawMesh`/`AttachToModel`。mod 作者不写代码——材质面全走 `Presentation/material_assets.json`（标量 roughness/metalness 或贴图），光照/阴影/IBL 由宿主按帧喂。合同细节见[渲染光照栈指南](../../architecture/render-lighting-guide.md)。

## 这场演的是什么

- 球阵 7 步粗糙度 × 3 档金属度（`LightingScene` 常量 `RoughnessSteps`/`MetallicLanes`）：从左到右越磨越糙，从前往后金属感越强。
- 天空的太阳圆盘、GGX 主光、阴影投射共用同一个 `SunDirectionToward`——看到的光斑即灯光即阴影源，不存在三处各调各的。
- 相位弧线限定白昼区间 0.58–0.68 缓慢推进，首帧就能看清落地阴影；金属带的高光随天空 IBL 变色，环境立方图按相位步进重烘。
- 基座与球阵全部进深度 pass，接收端 3×3 PCF 软化。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_raylib_lighting/screen.png` / `stats.json`（重场景独立批，120 帧）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 120 | 1.20 | 0.80 | 56.20 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_lighting --adapter raylib
```

## 边界与深读

- 单灯合同：方向光只有一个，多灯不在本车道词汇里；IBL 由天空解析函数烘出，不加载外部 HDR。
- 深读：[渲染光照栈与下游使用指南](../../architecture/render-lighting-guide.md)（车道接线与 IBL 实现）、[光照与 IBL 合同](../../architecture/raylib-engine-capabilities.md)。

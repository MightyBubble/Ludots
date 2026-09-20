# 关卡容器的组合实拍

多节点组合场景：岛屿地形基座 + 36 实例静态网格环带（材质实例链）+ 双 guard 美术动画，全部由一个关卡容器（`scenes/composition.scene.json`）声明——这是"引擎工程 + 关卡容器"格式的第一份多节点实证，加场景不再需要写一个新场景类。

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `composition` |
| preset | `engine_raylib_composition` |
| 关卡容器 | `projects/engine_gallery/scenes/composition.scene.json` |
| 组件实现 | `src/Content/Ludots.Content.EngineGallery/Components/`（island_terrain / static_mesh / animator） |
| 材质资产 | `projects/engine_gallery/materials/rock.json` + `rock_mossy.json`（parent 实例链） |
| 注册表条目 | `engine_raylib_composition`（`showcase.registry.json`，tier T1） |

四个节点挂三种组件：`island`（island_terrain 基座，程序化高度场 + 全帧天空/阴影，不触碰相机）、`rocks`（static_mesh：同一 cube mesh ×36 实例进 ISM 合批车道，逐实例 TRS/颜色/材质引用）、`guard_a`/`guard_b`（animator：GLB 内嵌 Walking clip 播放，phaseOffset 错相）。材质链是 Unreal Material Instance 语义：`rock` 父材质持有贴图与 PBR 参数，`rock_mossy` 子材质只覆盖 roughness/metalness 并继承父贴图——蓝灰色的一批石头就是子材质实例在跑。

## 这场演的是什么

- 关卡容器一次装载多节点多组件：基座画环境，覆盖组件（static_mesh/animator）不清屏、叠加绘制。
- 36 个实例一次合批提交；其中 6 个逐实例切换到子材质，证明"实例级不复制材质"的链路。
- 双 guard 同 clip 错相行军，证明美术动画独立于世界侧也能在组合场景里动。
- 相机初始位姿来自关卡文档 `camera` 声明——基座组件无权改相机。

## 组件作者合同（覆盖组件）

覆盖模式组件（不清屏、不画自己的基座帧）里走 lit 绘制路径时，必须自己画一遍天空（`RaylibSkyboxRenderer.Draw`，不清屏直接叠加）：lit 材质的 IBL 环境由帧内天空通道喂给，跳过这一步模型会渲染成近黑。基座组件无此要求。

## 验收证据

视觉验收证据（`screen.png` + `stats.json` 到 `artifacts/acceptance/engine_raylib_composition/`，录像到 `artifacts/evidence/engine_raylib_composition/`）由标准验收 CLI 与 `scripts/record-engine-galleries.py` 实跑生成；真实运行采样是证据的硬性要求，见 [引擎画廊开发指南](../../architecture/raylib-engine-gallery-dev-guide.md)。

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_composition --adapter raylib
```

## 边界与深读

- 格式与分层规范见 [Raylib 引擎工程分层与关卡容器格式](../../architecture/raylib-engine-project-scene-format.md)；本场景是规范的活样本。
- 构图调优只改关卡 JSON（实例坐标/相机/守卫位置），不需要碰任何代码——数据驱动即交付。

# Raylib VFX Forge Showcase

## 1. 概述

这个 showcase 用一个小型锻造台场景展示 raylib 后端的 Quarks 粒子能力。玩家进入后会直接看到九组不同效果：火花柱、能量环、尾迹弧线、余烬雨、护盾半球、引力井、火焰序列帧、烟雾序列帧和拉伸火星。

目标不是展示配置字段，而是证明一件事：粒子效果由跨平台 `Presentation/particle_effects.json` 定义，raylib 只负责把正式资产渲染出来。

## 2. 结构

- `assets/Presentation/particle_effects.json`：九组 Quarks 粒子资产，全部显式声明混合模式。
- `assets/Presentation/mesh_assets.json`：九个 VFX 资产和三个 Billboard 纹理资产。
- `assets/Presentation/host_assets.json`：raylib 后端的三张序列帧图集路径。
- `assets/Presentation/textures/`：火焰、烟雾、火星三张透明 PNG 图集。
- `assets/Presentation/performers.json`：三排锻造台布局和 VFX 绑定。
- `assets/Entities/templates.json`：地图锚点实体。
- `assets/Maps/vfx_forge_raylib_showcase.json`：玩家进入的展示地图。
- `assets/game.json`：将启动地图指向本 showcase。

## 3. 详情

后排左侧火花柱使用 Cone 形状和 Primitive 球粒子，强调上升、爆发和衰减。

后排中间能量环使用 Sphere 形状和 Primitive 方块粒子，强调环绕、漂浮和颜色渐变。

后排右侧尾迹弧线使用 Circle 形状和 Trail 渲染，强调射线式粒子轨迹。

前排左侧余烬雨使用 Point 形状和 Primitive 球粒子，强调持续下落与热量衰减。

前排中间护盾半球使用 Hemisphere 形状和 Primitive 球粒子，强调包裹感和稳定边缘。

前排右侧引力井使用 Sphere 形状和 Trail 渲染，强调向心轨迹和深色收束。

新增第三排左侧火焰序列帧使用 Billboard 渲染和 Additive 混合，强调逐帧火焰变化。

新增第三排中间烟雾序列帧使用 Billboard 渲染和 PremultipliedAlpha 混合，强调柔和透明烟团。

新增第三排右侧拉伸火星使用 StretchedBillboard 渲染和 Additive 混合，强调高速粒子的光迹。

所有 VFX 都是 performer 的 `AssetBinding`，使用正式 `AssetKind.VFX` 和 `StaticMesh` 表现通道，不引入 raylib 私有配置。

## 4. 场景

玩家打开 showcase 后，相机会停在锻造台正前方。九座底座分成三排展示九种粒子行为，便于对比粒子形状、颜色、尺寸变化、序列帧播放和 raylib 后端渲染模式。

## 5. 边界

- 只允许 `mesh_assets.json` 通过 `particleEffectId` 引用粒子资产。
- 粒子资产只来自 `Presentation/particle_effects.json`。
- raylib 后端支持 Primitive、Trail、Billboard 和 StretchedBillboard 粒子。
- Billboard 和 StretchedBillboard 必须声明 `textureSheet`，贴图路径必须通过 `host_assets.json` 提供。
- 每个粒子资产必须显式声明 `blendMode`，不允许后端自行猜默认混合方式。
- 这个 showcase 不承担粒子编辑器职责，只验证 raylib runtime 展示链路。

## 6. UAT

```gherkin
Feature: Raylib Quarks VFX Forge
  新玩家可以进入一个正式场景，直接看到 raylib 后端渲染跨平台粒子资产。

  Scenario: 玩家进入 VFX Forge 后看到九组粒子效果
    Given 玩家从 showcase 列表启动 "Raylib VFX Forge"
    When 地图 "vfx_forge_raylib_showcase" 加载完成
    Then 画面中央有九座锻造底座
    And 后排左侧底座持续喷出火花粒子
    And 后排中间底座持续显示环绕能量粒子
    And 后排右侧底座持续显示尾迹弧线
    And 前排左侧底座持续落下余烬粒子
    And 前排中间底座持续包裹护盾半球粒子
    And 前排右侧底座持续收束引力井粒子
    And 新增一排左侧底座持续播放火焰序列帧
    And 新增一排中间底座持续播放烟雾序列帧
    And 新增一排右侧底座持续显示拉伸火星

  Scenario: 粒子资产走统一 Quarks 数据源
    Given showcase 已加载
    When 引擎读取 VFX 资产
    Then 每个 VFX 资产都通过 particleEffectId 引用粒子定义
    And mesh 资产中没有内嵌 particleSystem
    And mesh 资产中没有 legacy emitter 和 Quarks 粒子混写

  Scenario: 玩家看到序列帧贴图和混合模式生效
    Given showcase 已加载
    When 玩家观察第三排三个底座
    Then 火焰粒子使用透明序列帧图集逐帧跳动
    And 烟雾粒子柔和扩散并保持透明边缘
    And 火星粒子随运动速度拉长成明亮光迹

  Scenario: raylib 后端只承担渲染
    Given showcase 已加载
    When raylib 渲染 VFX performer
    Then raylib 从 Core 的 VfxEffectAssetData 获取粒子运行时数据
    And raylib 不读取私有粒子 JSON
```

# Raylib VFX Forge Showcase

## 1. 概述

这个 showcase 用一个小型锻造台场景展示 raylib 后端的 Quarks 粒子能力。玩家进入后会直接看到三组不同效果：左侧火花柱、中间能量环、右侧尾迹弧线。

目标不是展示配置字段，而是证明一件事：粒子效果由跨平台 `Presentation/particle_effects.json` 定义，raylib 只负责把正式资产渲染出来。

## 2. 结构

- `assets/Presentation/particle_effects.json`：三组 Quarks 粒子资产。
- `assets/Presentation/mesh_assets.json`：三个 VFX 资产，只通过 `particleEffectId` 引用粒子资产。
- `assets/Presentation/performers.json`：锻造台布局和 VFX 绑定。
- `assets/Entities/templates.json`：地图锚点实体。
- `assets/Maps/vfx_forge_raylib_showcase.json`：玩家进入的展示地图。
- `assets/game.json`：将启动地图指向本 showcase。

## 3. 详情

左侧火花柱使用 Cone 形状和 Mesh 球粒子，强调上升、爆发和衰减。

中间能量环使用 Sphere 形状和 Mesh 方块粒子，强调环绕、漂浮和颜色渐变。

右侧尾迹弧线使用 Circle 形状和 Trail 渲染，强调射线式粒子轨迹。

所有 VFX 都是 performer 的 `AssetBinding`，使用正式 `AssetKind.VFX` 和 `StaticMesh` 表现通道，不引入 raylib 私有配置。

## 4. 场景

玩家打开 showcase 后，相机会停在锻造台正前方。三座底座从左到右展示三种粒子行为，便于对比粒子形状、颜色、尺寸变化和 raylib 后端渲染模式。

## 5. 边界

- 只允许 `mesh_assets.json` 通过 `particleEffectId` 引用粒子资产。
- 粒子资产只来自 `Presentation/particle_effects.json`。
- 当前 raylib 后端支持 Mesh 和 Trail 粒子。
- Billboard 和 StretchedBillboard 需要贴图 billboard 渲染器，当前会明确报错。
- 这个 showcase 不承担粒子编辑器职责，只验证 raylib runtime 展示链路。

## 6. UAT

```gherkin
Feature: Raylib Quarks VFX Forge
  新玩家可以进入一个正式场景，直接看到 raylib 后端渲染跨平台粒子资产。

  Scenario: 玩家进入 VFX Forge 后看到三组粒子效果
    Given 玩家从 showcase 列表启动 "Raylib VFX Forge"
    When 地图 "vfx_forge_raylib_showcase" 加载完成
    Then 画面中央有三座锻造底座
    And 左侧底座持续喷出火花粒子
    And 中间底座持续显示环绕能量粒子
    And 右侧底座持续显示尾迹弧线

  Scenario: 粒子资产走统一 Quarks 数据源
    Given showcase 已加载
    When 引擎读取 VFX 资产
    Then 每个 VFX 资产都通过 particleEffectId 引用粒子定义
    And mesh 资产中没有内嵌 particleSystem
    And mesh 资产中没有 legacy emitter 和 Quarks 粒子混写

  Scenario: raylib 后端只承担渲染
    Given showcase 已加载
    When raylib 渲染 VFX performer
    Then raylib 从 Core 的 VfxEffectAssetData 获取粒子运行时数据
    And raylib 不读取私有粒子 JSON
```

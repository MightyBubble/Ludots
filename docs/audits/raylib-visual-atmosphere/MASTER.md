# Raylib 客户端观感对齐 — 更新总单（热带岛参考水平）

> SSOT：本目录。状态写 `STATUS.md`。  
> 参考学习：`REFERENCE_RaylibErosionStandalone.md`（只学技法，不抄无许可资源）  
> 分支：`cursor/raylib-visual-atmosphere-ef14`  
> 范围：**Raylib 客户端观感**；Mod/Host 表分离；不重做 Core Animator/GAS；不做侵蚀模拟移植。

## 1. 概述

目标：玩家打开 Showcase 时，第一眼接近 [RaylibErosionStandalone](https://github.com/Delvix000/RaylibErosionStandalone) 的「有天空、有日照、有透贴植被、有雾、有像样水面」观感，而不是黑底清屏 + 不透明木块。

本轨在已完成的 client-parity（实例/蒙皮/albedo/特效 tint）之上叠加**环境与材质观感**。

## 2. 结构（工作包）

| ID | 工作包 | 并行 | 主要独占路径 |
|----|--------|------|----------------|
| V1 | 天空盒 + 昼夜驱动清屏/天空 | 可与 V2/V3 并行 | `RaylibSkyEnvironment*`；Host 天空 URI；HostLoop 天空通道 |
| V2 | 方向光 + 环境光接到地形/ISM/蒙皮 | 可与 V1/V3 并行 | `RaylibFrameLighting`；`terrain/instancing/skinning` shader uniforms |
| V3 | 透贴 cutout + 半透明/additive 混合 | 可与 V1/V2 并行 | `vegetation_cutout.*` / blend 车道；billboard + VFX |
| V4 | 大气距离雾 | 依赖 V2 shader 入口 | 雾 uniforms 进 lit/unlit 通道 |
| V5 | 反射/折射水面 FBO 基线 | 串行偏后 | `RaylibWaterPass`；反射/折射 RT；升级 `water.*` |
| V6 | Showcase + 截图验收 | 依赖 V1–V4 至少进分支 | `mods/showcases/raylib_visual_atmosphere/` |

## 3. 详情

### V1 — 天空（P0）

- [x] 主帧在 `BeginMode3D` 后、不透明物体前绘制天空（立方体/穹顶 + 渐变或 cubemap）
- [x] Host 可挂天空资源 URI（渐变条或 HDR→cubemap）；缺资源 **fail-loud**
- [x] 消费 `GlobalDayNight` phase（已有事件）驱动白天/夜晚着色；禁止只靠硬编码时刻
- [x] 禁止再把「全黑 ClearBackground」当成最终天空

### V2 — 日照（P0）

- [x] 每帧 `RaylibFrameLighting`：方向光方向 + 环境色/强度（可由昼夜相位查表，表 data-driven）
- [x] 接到 `terrain` / `instancing` / `skinning_instanced`（至少 N·L + ambient）
- [x] 网格不再永久「贴图×染色无光」冒充成品光照

### V3 — 透贴 / 半透明 / Additive（P0）

- [x] Billboard/植被：alpha **cutout**（`discard` 阈值，默认 data-driven）
- [x] VFX/材质：至少支持 `Opaque` / `Cutout` / `AlphaBlend` / `Additive` 四种混合语义之一组合同
- [x] 无静默占位；未知混合模式 fail-loud

### V4 — 距离雾（P1，本轨要有最小可见）

- [x] 相机距离雾接入 lit 地形与网格通道
- [x] 与战争迷雾（FoW）分离：FoW 仍是视野场，本项是大气雾

### V5 — 水面反射折射（P1）

- [x] 反射/折射 RenderTexture 双通道（可降分辨率）
- [x] 水面 shader 采样两张 RT + 扰动贴图（Host URI）
- [x] 无 RT 时 fail-loud，禁止静默退回纯色半透明却宣称「反射水面」

### V6 — 验收（P0）

- [x] Showcase `raylib_visual_atmosphere`
- [x] 截图 01–06（见第 6 节）
- [x] `ACCEPTANCE.md` 合同

## 4. 场景（作者/玩家视角）

1. 打开 Showcase → 先看到天空和日照下的地形，不是黑盒子  
2. 远处山体被雾柔化  
3. 岸边/坡上有树叶镂空的广告牌树，不是矩形白边  
4. 水面能映出天空与岸线轮廓  
5. 切换昼夜参数 → 天空与环境光跟着变  
6. 特效能配 additive 光晕，植被能配透贴

## 5. 边界

- **不做**上游侵蚀模拟移植与交互热键  
- **不抄**上游无许可着色器/贴图原文；技法自研 + CC0/自有资产  
- **不做** IBL / BRDF LUT / cubemap / 级联阴影 / 多光源完整 PBR（仍为 P2）  
- **做** 最小方向光 MR：host `sourceUris[0..3]` = albedo / roughness / metallic / normal（可选槽用标量默认，无假贴图）；`instancing.fs` Cook-Torrance GGX + 既有 ambient/fog；法线贴图在无切线时跳过（见 `materials-notes.md`）  
- **做** ContinuousHeightmap 高度分层地形贴图（`Presentation/terrain_albedo_environments.json`，非 splatmap）  
- **做** 同光源月光重映射 + 夜环境光可读剪影（非第二套灯光系统）  

- **不删** Prefab 全库；贴画占位另轨可并行但本 DoD 不强制  
- Core：只消费已有 `GlobalDayNight`；缺 Host 天空表字段时先补 Host/客户端配置，不发明平行 Core 资产体系

## 6. UAT（截图验收）

```gherkin
Feature: Raylib 客户端接近热带岛参考观感
  作为玩家
  我想一进场景就看到天空、日照、透贴植被和像样水面
  以便确认客户端不只是黑底摆模型

  Scenario: 天空与日照可见
    Given Showcase 已配置天空 Host URI 与昼夜相位
    When 我打开 raylib_visual_atmosphere
    Then 截图 01_sky_day 中背景是天空而非纯黑
    And 地形或建筑受方向光明暗影响

  Scenario: 昼夜切换
    When 我将昼夜相位切到夜晚
    Then 截图 02_sky_night 天空与环境光明显变暗

  Scenario: 透贴植被
    Given 场景中有 cutout 广告牌树
    When 我贴近观察树冠
    Then 截图 03_cutout_vegetation 中树叶镂空，无整块矩形底板

  Scenario: 半透明与 Additive
    When 我查看特效样例
    Then 截图 04_blend_modes 中能区分半透明与 additive 光晕

  Scenario: 大气雾
    When 我远眺场景
    Then 截图 05_distance_fog 中远处被雾柔化，且战争迷雾未冒充大气雾

  Scenario: 反射水面
    Given 水面反射通道已启用
    When 我看向水面
    Then 截图 06_water_reflect 中能看到天空或岸线倒影轮廓
```

## 7. 完成定义（DoD）

- [x] V1–V4 进分支并可演示  
- [x] V5 至少有反射轮廓可见（可低分辨率 RT）  
- [x] V6 六张截图落入 acceptance  
- [x] STATUS 全勾；PR 链到本 MASTER  
- [x] 明确声明未抄上游无许可资源

未抄 RaylibErosionStandalone 无许可着色器/贴图；Showcase 使用程序生成 CC0 贴图与仓库内 Ludots 管线（sky/water/fog/cutout/blend）。

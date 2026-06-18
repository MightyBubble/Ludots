# 能力标准 Showcase

本页定义 core 仓库当前唯一的生产级能力验收 showcase SSOT。验收、回归启动和平台 adapter 对齐都应优先使用这里列出的 Root Mod，不再依赖历史命名的业务 showcase mod。

## 唯一验收 Root Mod

| 场景 | Binding | Root Mod | 验收重点 |
|------|---------|----------|----------|
| 静态 Performer 集群 | `capability_standard_static_performer_30k` | `mods/showcases/capability_standard/CapabilityStandardStaticPerformer30kMod` | 30K 静态 performer、HUD bar、HUD text、GAS effect 状态变化 |
| 大世界 Mass Navigation | `capability_standard_mass_nav_large_world_10k` | `mods/showcases/capability_standard/CapabilityStandardMassNavigationLargeWorld10kMod` | 10K nav agent、大世界 residency、performer、HUD bar/text、effect/minimap 变化 |
| Total War Like | `capability_standard_total_war_like` | `mods/showcases/capability_standard/CapabilityStandardTotalWarLikeMod` | formation command、mass movement、selection、path preview、large battle presentation |
| Physics2D 宏观调参 | `capability_standard_physics2d_showcase` | `mods/showcases/capability_standard/CapabilityStandardPhysics2DShowcaseMod` | 15Hz Physics2D、30K 动态实体、100K 静态实体、broadphase 策略、静态障碍物模板与 polygon authoring |

标准启动命令：

```powershell
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_static_performer_30k' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_mass_nav_large_world_10k' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_total_war_like' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_physics2d_showcase' --adapter raylib
```

也可以通过 preset 启动：

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_static_performer_30k_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_mass_nav_large_world_10k_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_total_war_like_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_physics2d_showcase_raylib'
```

## 依赖口径

- Root Mod 负责场景入口、产品化配置和少量场景 glue。
- 可复用能力继续放在 capability mod，例如 `MassNavigationMod`。
- 标准 Root Mod 的 dependency closure 禁止包含历史 showcase entry，例如 `PerformerBlacksmithShowcaseMod`、`PerformerBlacksmithScatterHudTextBenchmarkEntryMod`、`MassNavigationTotalWarEntryMod`、`Physics2DPlaygroundMod`。
- 历史 showcase 可以保留为局部调试材料，但不得作为平台 adapter 或 core 主线验收 SSOT。

## Adapter 作者职责

Raylib、Unity、UE5 等平台 adapter 应以这些 Root Mod 的 launcher plan 为对齐对象。平台层需要补齐的是 adapter 侧配置、asset binding、host asset resolver 和 platform rendering path，不能把某个商业项目的私有 adapter glue 写回 core。

平台作者至少需要确认：

- launcher binding/preset 能解析到同一批 ordered mod IDs；
- `game.json`、`config_catalog.json`、map、presentation/GAS/input/camera 配置都通过 ConfigPipeline 进入运行时；
- HUD bar、HUD text、minimap、selection/path preview 使用平台正式渲染路径；
- asset reference 的解析规则支持 `ModId:assets/...`；
- adapter 不硬编码这些 showcase 的私有路径或业务名字。

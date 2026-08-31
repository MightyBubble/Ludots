# NavBakeIslandShowcaseMod

这是给开发者照抄的 Mass Navigation + Animator Presenter 范本。启动后会进入一张真实连续高度图小岛，自动生成两队共 64 名单位。单位使用 MassNavigation Core 的 typed MovePlan、离线 Recast NavMesh 和正式 Presenter；Raylib 载入 MassNavigationMod 提供的 KayKit soldier，并按配置播放 `Idle` / `Walking_A`。

## 运行

```powershell
.\scripts\run-mod-launcher.cmd cli launch --preset nav_bake_island_showcase_raylib --adapter raylib --build never
```

首屏操作：

- 左键点选或拖框选中单位。
- 右键点击岛面下达 `massNavigationMove`。
- 鼠标滚轮调整视野；`PageUp` / `PageDown` 调整小地图范围。
- `M` 切换小地图，`F6` 切换小地图预设，`F7` 切换小地图随镜头旋转。

## 配置结构

| 配置 | 作用 |
|---|---|
| `assets/game.json` | 启动地图、容量、输入和小地图预算 |
| `assets/MassNavigationConfig.json` | solver、队伍、spawn layout、agent profile 和运行时容量 |
| `assets/Maps/nav_bake_island.json` | 连续高度图、5×5 board、NavTileGrid 和默认镜头 |
| `assets/Navigation/navmesh.json` | `light` / `heavy` 两个 NavMesh profile，均明确 `PreferMesh` |
| `assets/Navigation/pathing.json` | 两个 agent type 的路径域与 area cost |
| `assets/terrain/tropical_island.height` | 真实连续高度图，单位为厘米 |

单位模板、Presenter、Animator profile 与 KayKit 动画 clip 都复用 `MassNavigationMod`。这个 Mod 只提供地图、数量、导航 profile 和容量，不复制实体生产或渲染配置。

## 验收

玩家视角的 Cucumber 场景在 `gitbook/acceptance/nav-bake-island.feature`。实机证据放在 `artifacts/acceptance/nav-bake-island/`，其中的 trace 必须能对应到目标地图、真实单位、移动前后位置和 Animator 状态。

## 边界

这个范本验证陆地 Mass Navigation 的生产链。动态障碍增量重烤编辑器、水陆双层和运行时直线移动消融不在本 Mod 内另造旁路；需要这些能力时，应扩展 Core 正式接口后再接入。

# NavMesh Bake Island showcase 设计

## 一句话与目标用户

在一张真实高度图小岛上，选中 64 名 KayKit 士兵下达移动命令，直接看到 Mass Navigation、离线 NavMesh、Presenter 和 Animator 如何一起工作；目标用户是要给新单位接入寻路和美术表现的 Ludots 开发者。

## 主循环

- **谁改变世界**：玩家用左键框选或点选队伍，用右键在岛上给出目标点。
- **用户看到什么变**：命令标记出现，单位通过离线烘焙的 NavMesh 规划路径，随后沿路径行进；移动时 KayKit 模型从 `Idle` 切到 `Walking_A`，停下后回到 `Idle`。
- **惊喜时刻**：把两队目标点放在岛的另一侧。单位不是直线穿越高度变化，而是沿真实可通行区域绕行；同一帧能看到路线移动、角色动画和小地图位置同步改变。

## 消融对照

本 issue 的交付重点是“生产链范本”，不伪造一个旁路的无导航模式。可见的对照是同一场景中的两种真实 agent profile：`light` 与 `heavy` 共享 NavMesh，但使用不同速度、体型和 Presenter 尺寸。若后续要做“关闭导航后直线移动”的消融，应扩展正式的 MassNavigation execution mode，不在 showcase 内私自写第二套移动器。

## 解释层

- **世界反馈**：蓝队与红队使用正式 Presenter 的颜色覆盖；单位头顶的生命值来自 `AttributeBuffer`，不是静态标签。
- **动画反馈**：`AnimatorPackedState` 的 packed state `41` 对应 `Idle`，`42` 对应 `Walking_A`；这些绑定直接复用 `MassNavigationMod` 的 Animator profile、动画 clip 与 KayKit GLB，不在 showcase 里另存一份。
- **导航反馈**：右键命令会经过 `massNavigationMove`、MovePlan projection、MassNavigation route execution 和 Flow solver；命令标记与小地图标记来自同一实体状态。
- **图例**：蓝色 = Azure Shore Guard，红色 = Crimson Ridge Scouts，单位脚下的实际高度来自 `tropical_island.height`，不是平面占位物。

## 旋钮清单

这些旋钮都在运行时已有正式输入，不需要改配置文件重启：

| 旋钮 | 操作 | 回答什么 |
|---|---|---|
| 选中范围 | 左键点选或拖框 | 一条命令能否同时驱动多个 MassNav actor |
| 目标点 | 右键点击岛面 | 单点命令是否经过正式 order/MovePlan 链 |
| 视野范围 | 鼠标滚轮 | 拉远后能否同时读出两队路径和地形关系 |
| 小地图缩放 | `PageUp` / `PageDown` | 全图与局部队形之间如何切换 |
| 小地图模式 | `M` / `F6` / `F7` | 队伍位置、预设和跟随镜头是否保持同源 |

## 场景结构

- **主演示**：`nav_bake_island` 高度图小岛；Azure 与 Crimson 各 32 名单位，light/heavy 两种配置；两队都通过 `PreferMesh` 使用烘焙 NavMesh。
- **子场景**：开发者可以只改 `MassNavigationConfig.json` 中的队伍数量、profile 和速度，再复用相同地图、Presenter 和动画配置，作为新单位的最小抄作业。
- **首屏引导**：进入后先左键框选一队，再右键点岛的另一侧；观察单位沿地形移动，注意行走动画和小地图同步。`M` 打开/关闭小地图，滚轮改变视野。

## 门户资产

- 设计说明：本页。
- 开发者入口：`mods/showcases/navmesh_bake_island/NavBakeIslandShowcaseMod/README.md`。
- 真实运行证据：`artifacts/acceptance/nav-bake-island/` 下的截图、日志、结构化 trace 和路径图。
- 同源原则：地图、单位数量、profile、Presenter、动画 clip、host asset 和容量都来自 Mod 资产；README 只解释这些配置，不复制第二份运行数据。

## 反向 API 审计

| 需要 | 当前接口 | 归属 |
|---|---|---|
| profile 状态到后端 clip 的解析 | `IRenderAnimationClipResolver` + `AnimationProfileRegistry` + `AnimationClipRegistry` | 本次交付 |
| 命名动画在 Raylib 中稳定解析 | `ClipAssetLocatorSelector` + `RaylibSkinnedPlayback` 名称索引 | 本次交付 |
| locator 与实际 mesh 一致 | `ValidateSelectorSource`，错绑直接抛错 | 本次交付 |
| 真实批量蒙皮 | `RaylibGpuSkinnedBatchRenderer` 姿势纹理批次 | 复用 |
| 64 个单位的 typed MovePlan 执行 | Core MassNavigation route/Flow/command group | 复用 |
| 动态障碍增量重烤编辑器 | 当前不作为本范本的运行时功能 | 后续专项；不得在 Mod 内另造移动/烘焙管线 |
| 水陆双层 transport navigation | 当前不作为本范本的运行时功能 | 后续专项；本场景只验证陆地 MassNav |

## 交付边界与完成判据

本次实现提供一个可由 launcher 启动的、配置驱动的陆地 Mass Navigation 范本：真实高度图、离线 NavMesh、两队单位、正式 Presenter、KayKit 骨骼和命名动画、Raylib host asset 绑定、容量配置与错误合同均在正式管线上。

启动入口：

```powershell
.\scripts\run-mod-launcher.cmd cli launch --preset nav_bake_island_showcase_raylib --adapter raylib --build never
```

可玩交付必须同时满足：能从上述 preset 进入 `nav_bake_island`；玩家能完成“选中 → 右键下令”；真实实体位置和 Animator 状态发生变化；Agent Bridge 的 `/health` 两次 `pumpCount` 增长；并保存 `battle-report.md`、`trace.jsonl`、`path.mmd` 与至少一张实机截图。动态重烤和水陆双层不属于本次完成判据。

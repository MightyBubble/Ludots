# GAS Composition Gate — 冠军沙盒工具条改走数据

- **Task**: 删掉 `ChampionSkillSandboxToolbarTail.cs`，镜头 / 主题 / 压力按钮和副标题改走现有数据工具条和图。
- **Date**: 2026-10-14
- **Agent / Author**: cursor agent

## GAS Composition Gate — Self Review

- **Task / Issue**: champion toolbar tail → data toolbar + trigger graph
- **Date**: 2026-10-14
- **Agent / Author**: cursor agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 按钮行为是图节点组合。唯一新 opcode `ApplyMapDefaultCamera` 只做一件事：把当前地图 `DefaultCamera` 的机位和姿态写成已有的镜头请求。主题、人数、血条、选中视图都是 `SetPref` / `IsPref` / `ReadMapVarInt` / `WriteMapVarInt`。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 切到某台虚拟相机 | 0 已有 | `ActivateVirtualCamera` |
| 回到地图默认机位和姿态 | 0 新增 | `ApplyMapDefaultCamera`（551） |
| 写偏好、读偏好、加减地图整数 | 0 已有 | `SetPref` / `IsPref` / `ReadMapVarInt` / `AddInt` / `WriteMapVarInt` |
| 每个按钮一条入口 | 2 | `graph.champion.toolbar` |
| 按钮、高亮、副标题 | 3 目录 | `UI/entity_command_panel_toolbars.json` |
| 机位姿态 | 3 目录 | `Camera/virtual_cameras.json` + 地图 `DefaultCamera` |

### 3. Reuse list

- Handlers: `ActivateVirtualCamera`、`SetPref`、`IsPref`、`ReadMapVarInt`、`WriteMapVarInt`、`AddInt`、`CompareLtInt`、`JumpIfFalse`
- Queues / Systems: `VirtualCameraRequest`、`CameraPoseRequest`、`CameraRuntimeSystem`；`InjectButtonPress`
- Resolvers / Registries: `VirtualCameraRegistry`、`InteractionPrefCatalog`、`MapVariableStore`
- Existing presets / graphs: 施法方式仍走 `graph.aim.set_cast_mode`；`ResetCamera` 与 F4 共用一个图入口

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| `ApplyMapDefaultCamera` | 用当前地图 `DefaultCamera` 发替换镜头栈的请求，并带上地图写明的姿态，混合时长 0 | `ActivateVirtualCamera` 的机位 id 是编译期符号，一张图没法说「这张地图自己点的那台」。地图姿态覆盖也不在虚拟相机定义上 |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。镜头请求和偏好写入沿用现有单步副作用，失败即抛。

### 6. Config SSOT

行为配置落在: graph `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/graphs/graph.champion.toolbar.json`；工具条 `UI/entity_command_panel_toolbars.json`；偏好 `Input/interaction_prefs.json`；机位 `Camera/virtual_cameras.json`；压力人数是压力地图 `Variables`。

是否新增 JSON schema: YES — 工具条 `highlight.equals`（非穷尽偏好相等）和 `subtitle` 模板（偏好标签 / 地图整数槽）。穷尽 `pref` 盖不住「默认 Classic 没有按钮」，字符串副标题盖不住实时人数。这两项在 `EntityCommandPanelToolbarCatalog` 加载期失败，不另开档案。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线

若选了 Core enum → FAIL

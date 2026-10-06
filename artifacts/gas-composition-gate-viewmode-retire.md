# GAS Composition Gate — ViewMode 退役（相机行为归 vcam 组合）

## GAS Composition Gate — Self Review

- **Task / Issue**: ViewMode 整套退役（#1713 设计文档 3.8 片，按"相机轴走虚拟相机激活、交互轴走交互状态、技能栏轴走既有配置写键"拆解）；分支 `viewmode-retire`，基于 #1713（`cursor/retire-stance-de53`）。
- **Date**: 2026-10-06
- **Agent / Author**: ZCode (stevie)

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（既有 op 组合 + 数据资产；删除一个 preset 开关体系）

结论: PASS

一句话理由: ViewMode 的三根轴全部由既有原子机制承接——相机轴 = `ActivateVirtualCamera` 图 op（op 530，#1713 已落）或 `VirtualCameraRequest` 服务直写；交互轴 = 交互状态（`ActivateContext`/`DeactivateContext`）与 `SetInteractionMode`；技能栏轴 = `LocalOrderSource` 配置写键路径（`LocalOrderSourceHelper.cs:335-340` 既有）。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| 按键切镜头 | 2 | 消费方 mod 自持：CameraShowcase 的 F1-F4 轮询系统（自有 CameraShowcase.Controls 上下文）、RoadNetwork 的 F1/F3 轮询系统（自有上下文）、CapStd 的 F5-F8（InteractionModeMap + request）。CameraProfilesMod 回归纯预设资产，不携带任何按键（AGENTS.md「资产方不声明调用方」+ 设计文档 3.11.4「没有公共默认按键」） |
| 切施法方式 | 2 | 交互状态 profile + `SetInteractionMode` / `ActivateContext` 图 |
| 初始状态 | 3 | game.json `startupInputContexts` + 模板 `initialInteractionContext` + 地图 `DefaultCamera`（引擎 `ApplyDefaultCamera` 既有语义） |
| ViewMode 删除 | — | `mods/CoreInputMod/ViewMode/*` + `ViewModeSwitchSystem` + `InstallCoreInputOnGameStartTrigger` 注册点 |

### 3. Reuse list

- Queues / Systems: `InputContextProjectionSystem`（InteractionMode/挂载上下文 → IMC 栈投影，唯一翻译器）、`CameraRuntimeSystem`（request 消费）
- Resolvers / Registries: `VirtualCameraRegistry`、`InteractionModeMap`、`InteractionContextProfileRegistry`
- Existing presets / graphs: `ActivateVirtualCamera`（op 530，节点画廊词条 + TimeFlow/CameraShowcase 面板先例）、`SetInteractionMode`（已有）、`ActivateContext`/`DeactivateContext`（moba_demo 片 5 先例：`graph.moba.switch_to_aim_cast`）、`CameraManager.ActivateVirtualCamera`（TimeFlow C# 先例）

### 4. New Layer 0 ops (if any)

N/A — 不新增任何 op。

### 5. Transaction boundary

必须原子 rollback 的步骤: 无（相机切换是单 request 替换栈，由 `CameraRuntimeSystem` 现有 fail-fast 语义保证；交互状态切换由 context kernel mount/unmount 原子性保证）。

### 6. Config SSOT

行为配置落在: 各 mod 自己的 `assets/Input/interaction_context_profiles.json`、`assets/GAS/graphs/*.json`、`assets/Camera/virtual_cameras.json`（既有）。

是否新增 JSON schema: NO — 全部复用既有 schema；`viewmodes.json` 是删除项。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（无 vcam 定义时仍走 `ApplyDefaultCamera` 既有 pose-only 路径）

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / 数据资产（该 mod 加一张切换图或一条 vcam 定义即可）。

（不触 Core enum。）

## 附：三轴拆解证据（决策依据）

- 相机轴是纯转发：`ViewModeManager.ApplyCamera`（ViewModeManager.cs:148-188）构造的 `VirtualCameraRequest`，其 FollowTargetKind/FollowCollectionKey/SnapToFollowTarget 全部回退 vcam 定义本身——vcam 栈本就是相机模式系统，ViewMode 是第二份状态源。
- 元组耦合无人使用：全仓 8 份 viewmodes.json 23 行中，仅 InteractionShowcase 的 Action 行同时变相机+交互两轴；`applyCamera:false` 全仓零调用。
- 键位归属修正：初版曾把 F1-F3 轮询系统放进 CameraProfilesMod（40 个 mod 依赖的共享资产 mod），违反「资产方不声明调用方」与「没有公共默认按键」——已纠正：系统删除，键位归 CameraShowcase/RoadNetwork 各自的上下文与轮询系统。
- 技能栏轴不阻塞：技能栏键另有写者（`LocalOrderSourceConfig` → mapping 配置），ViewMode 删除不孤儿化 SkillBarOverlaySystem。


## 附二：对抗性审计修正记录（2026-10-06，两路独立审计）

审计发现并已修复：
1. CameraShowcase F4 早退门吞掉 F1-F3（功能坏）——重写按键分派。
2. CameraAcceptance F3/F4 对 EntityCollectionPrimary 定义发无 owner request → CameraFollowTargetFactory 抛异常（崩溃级）——新增 CameraAcceptanceCameras 助手解析 sole-rep owner，fail-fast。
3. 五个轮询系统落 InputCollection，复制客户端只执行 LocalInput → 模式键在复制客户端全灭——全部迁 LocalInput（champion/ux 拆出独立 LocalInput 轮询系统）。
4. GetActive 对外来 CastModeType 静默回默认导致 Ensure 恒不纠正（跨 mod 残留不复位）——改 null 哨兵，Ensure 视为需复位。
5. interaction Action 模式相机从"跟随选中集合"漂移为"跟随 rep"——恢复 EntityCollectionPrimary@collection.command.source + owner。
6. CapStd 按键分支缺地图守卫（与 fallback 分支不对称）——补齐。
7. CoreInputMod 残留 ViewModeNext/Prev 死动作与 V/B 全局绑定——删除。
8. 三处注释引用不存在的 CameraProfileSwitchSystem（幽灵先例）、四处注释写修复史——重写为当前约束。
9. 魔法字符串换 Ids 常量；CameraProfileIds 三个死 Mode 常量删除。
10. 文档 SSOT 更新：camera_standards.md / camera_character_control.md / 3c_capability_matrix.md / showcase.registry.json camera_profiles 摘要。
11. champion 技能栏键位标签随 viewmodes 删除后无人写入——回填 local_order_source.json。
12. 七个 mod 的 csproj 死 CoreInputMod ProjectReference 断链（mod.json 依赖按设计文档片 8 统一处理）。

已知遗留（未修，记录在案）：InteractionModesConfig 的 DeepObject 数组整替语义下，CapStd 片段重述根 mode.normal/mode.targeting——根文件演进时需同步（引擎 schema 决定，改合并策略属引擎任务）；技能栏离图清理（旧 ClearActiveMode 的标签删除）无消费方接盘，暴露面小。

---
文档类型: RFC 提案
创建日期: 2026-03-13
维护人: X28技术团队
RFC编号: RFC-0002
状态: Draft
---

# RFC-0002 Presentation Hotpath 可玩 Mod 设计

本提案定义三个面向玩家感知的 presentation hotpath playable mod 设计，用来补齐 `#51` 当前技术 harness 与真实产品场景之间的缺口。现有 `CameraAcceptanceMod` 已经提供 lane-isolated headless harness、battle-report、trace 和 path artifact，但它仍以诊断与切 lane 为中心；本提案要求把相同的可测能力映射回玩家实际会感知的场景压力。

本提案只定义 follow-up mod 设计，不替代当前 `mods/fixtures/camera/CameraAcceptanceMod/` 的共享 acceptance harness，也不引入新的 Core / Presentation 平行运行时。

## 1 背景

当前仓库已经具备以下可复用基础：

* `mods/fixtures/camera/CameraAcceptanceMod/`
* `mods/fixtures/camera/CameraAcceptanceMod/Systems/CameraAcceptanceHotpathLaneSystem.cs`
* `mods/fixtures/camera/CameraAcceptanceMod/Systems/CameraAcceptanceDiagnosticsToggleSystem.cs`
* `src/Tests/ThreeCTests/CameraAcceptanceModTests.cs`
* `src/Core/Presentation/Hud/WorldHudToScreenSystem.cs`
* `src/Core/Presentation/Rendering/RenderDebugState.cs`

`CameraAcceptanceMod_HotpathHarness_TogglesPresentationLanes_AndWritesAcceptanceArtifacts` 已经能证明：

* crowd / culling 压力可确定性生成与回收
* panel / diagnostics HUD / selection labels / bars / HUD text / terrain / primitives 可以独立切换
* headless acceptance 会写出 `battle-report.md`、`trace.jsonl`、`path.mmd`

当前缺口是：这些证据主要验证 buffer count、gate 状态和 timing sample，还没有把“玩家可见变化”收束到一组可玩的产品场景。

## 2 目标

本提案的目标是：

1. 为 presentation hotpath 补齐三个可玩场景，而不是只停留在技术 harness。
2. 让同一组 lane toggle 在玩家语义下仍然可观察、可验证、可跨 adapter 复用。
3. 保持 reuse-first，继续复用 `CameraAcceptanceMod`、shared input、shared camera profile、shared acceptance artifact 生成模式。

## 3 非目标

本提案不做以下事情：

1. 不新建 Core 层专用 benchmark runtime。
2. 不把 adapter 特定逻辑写进 mod 设计本身。
3. 不把 timing sample 变成硬编码性能门槛。
4. 不替代 `#51` 已存在的 shared hotpath harness。

## 4 共享设计规则

三个 playable mod 都必须遵守以下规则：

1. 继续复用 `ConfigPipeline`、现有 presentation buffer、`RenderDebugState`、`WorldHudToScreenSystem`、`CoreInputMod`、camera capability mods。
2. 同一场景定义必须可以从同一份 map / mod 数据复用到 `Web`、`Raylib`、未来 `Unity`、未来 `Unreal` 适配器。
3. 每个 mod 都需要同时提供：
   * 玩家可操作的 live toggle 入口
   * headless acceptance
   * readable battle-report
   * path visualization
4. lane toggle 的通过条件必须写成玩家可见结果，不能只写内部 buffer count。
5. 如果未来实现过程中暴露出 Core / adapter 层缺陷，按技术债 fuse 流程升级，不在 mod 内旁路。

## 5 设计一：Crowd Battlefield Mod

### 5.1 玩家目标

玩家在一片持续交战的 battlefield 中移动相机、框选小队、切换视觉辅助信息，判断在高 crowd / high culling pressure 下哪些表现层对可读性与操作稳定性贡献最大。

### 5.2 最小场景

* 一个可控指挥单位
* 两个友方 squad anchor
* 一条前线与一个夺点目标
* 大量 crowd dummy，用来制造稳定 culling 压力
* 高低起伏明显的 terrain，与前线 smoke / marker / primitive 提示

建议落点：

* Mod：`mods/fixtures/presentation/PresentationCrowdBattlefieldMod/`
* 地图：`assets/Maps/presentation_crowd_battlefield.json`

### 5.3 Visual Lanes

默认开启：

* panel
* diagnostics HUD
* selection labels
* world HUD bars
* world HUD text
* terrain
* primitives
* crowd / culling pressure

允许关闭：

* HUD bars
* HUD text
* selection labels
* terrain
* primitives
* crowd pressure

### 5.4 用户可见预期变化

* 关闭 `selection labels` 后，框选和拖选仍然成立，但前线单位头顶标签立即消失，画面密度显著下降。
* 关闭 `HUD bars` 后，生命条立即消失，但命中与选择仍然成立。
* 关闭 `HUD text` 后，数字类读数消失，bars 与 selection 不应被误伤。
* 关闭 `terrain` 后，前线地表消失，验证玩家是否仍能依赖 markers / overlays 完成任务。
* 关闭 `primitives` 后，目标点、危险区、marker mesh 消失，玩家仍能移动相机但失去空间提示。
* 关闭 `crowd pressure` 后，远处 crowd 被移除，culling / HUD 投影成本下降，同时玩家主任务对象保持不变。

### 5.5 Adapter 复用要求

* 场景数据、地图、实体模板、camera profile、toggle action、acceptance trace 定义由 mod 统一提供。
* `Raylib` 与 `Web` 当前都应复用同一份 launch plan、同一份 acceptance script。
* 未来 `Unity` / `Unreal` 只替换 adapter 渲染与输入桥，不改玩家目标、场景脚本、battle-report 语义。

### 5.6 验收重点

* 玩家在移动相机和框选时能直接感知 lane 开关对 battlefield readability 的影响。
* crowd 关闭前后，任务目标和相机控制不变，只改变表现压力。
* acceptance artifact 需要同时说明“玩家看到了什么变化”和“内部 timing / count 如何变化”。

## 6 设计二：Town / Base Mod

### 6.1 玩家目标

玩家在一座静态 props 密集的 town / base 中检查建筑、路标、交互点、任务提示与面板叠加，验证 presentation hotpath 不只来自 crowd，也来自静态场景层、panel、overlay 与 marker 叠加。

### 6.2 最小场景

* 一个可游走的主角或巡视相机
* 建筑群、城门、仓库、路灯、树、地表 decal
* 多个交互点 marker
* 一个常驻面板与一个上下文提示层
* 少量 NPC，用来保留 selection 与 label 场景

建议落点：

* Mod：`mods/fixtures/presentation/PresentationTownBaseMod/`
* 地图：`assets/Maps/presentation_town_base.json`

### 6.3 Visual Lanes

默认开启：

* panel
* diagnostics HUD
* terrain
* primitives
* overlay / prompt
* 少量 selection labels

可选开启：

* world HUD bars
* HUD text
* crowd pressure

### 6.4 用户可见预期变化

* 关闭 `panel` 后，左侧或底部运营面板消失，玩家改为仅依赖世界提示巡检基地。
* 关闭 `terrain` 后，静态 props 仍可见，但地面语义消失，验证 props 与 overlay 是否足以支持空间判断。
* 关闭 `primitives` 后，交互 marker、范围框和 debug mesh 消失，玩家更难定位可交互物。
* 关闭 `overlay / prompt` 后，玩家仍看得到建筑与角色，但上下文提示、操作文案、导航提示消失。
* 打开 `crowd pressure` 时，只加入少量经过的市民或工人，用来验证静态基地主场景与轻量动态人群叠加时的成本变化。

### 6.5 Adapter 复用要求

* 建筑、提示点、UI panel、camera route 必须由 mod 数据统一描述。
* 当前 `Web` 与 `Raylib` 使用相同的巡视脚本、同样的 toggle 顺序和同一份 battle-report。
* 未来 `Unity` / `Unreal` 若有更复杂材质与后处理，仍要保留同一组玩家任务与 lane 对照，避免 adapter 把场景语义改写成另一套测试。

### 6.6 验收重点

* 证明静态 props + panel + overlay 本身就是独立的 presentation hotpath。
* 证明 town / base 场景能和 battlefield 场景形成互补，而不是重复验证 crowd。

## 7 设计三：Hero Focus / Replay Mod

### 7.1 玩家目标

玩家在一次小规模英雄交战的 replay / focus 场景中切换目标、查看 marker、阅读提示、控制镜头跟随与回放，验证相机控制、panel、marker、prompt、target switching 同时存在时的 presentation 成本与可读性。

### 7.2 最小场景

* 一个主角
* 一个友方目标
* 两个敌方关键目标
* 可切换的 focus target
* 可回放的短时间线片段
* replay 面板、事件 marker、目标提示、相机 shot 切换

建议落点：

* Mod：`mods/fixtures/presentation/PresentationHeroReplayMod/`
* 地图：`assets/Maps/presentation_hero_replay.json`

### 7.3 Visual Lanes

默认开启：

* panel
* replay timeline / prompt
* selection labels
* markers / primitives
* HUD bars
* HUD text
* terrain

可按步骤关闭：

* prompt / timeline
* markers / primitives
* HUD bars
* HUD text
* selection labels

### 7.4 用户可见预期变化

* 切换 focus target 时，相机、marker 和 panel 上下文应同时切换到新的主角或目标。
* 关闭 `prompt / timeline` 后，玩家失去回放上下文，但镜头与 target switching 仍然可操作。
* 关闭 `markers / primitives` 后，技能落点、威胁圈、事件锚点消失，玩家需要依赖地形和角色姿态理解战斗。
* 关闭 `selection labels` 与 `HUD bars` 后，玩家仍能看见角色动作，但精确状态读数消失。
* 同一段 replay 在 follow、blend、shot stack 切换下，仍应维持同一个可读剧情节点。

### 7.5 Adapter 复用要求

* replay timeline、shot 切换脚本、target switch 顺序、prompt 文本与 acceptance artifact 由 mod 统一定义。
* `Web` 与 `Raylib` 必须复用相同事件顺序，保证同一条 replay 不是两套不同演出。
* 未来 `Unity` / `Unreal` 可以用各自 adapter 呈现更丰富的 shot 过渡，但不能改写 replay 节点顺序与玩家目标。

### 7.6 验收重点

* 证明 hero-focused 场景的 presentation hotpath 不是 crowd 或静态 props 的变体，而是 camera control、prompt、marker 与 target switching 的组合压力。
* 证明 lane toggle 不会破坏 replay 的剧情顺序与镜头意图。

## 8 跨 Adapter 复用矩阵

| 项目 | Crowd Battlefield | Town / Base | Hero Focus / Replay |
|------|------|------|------|
| 玩家目标 | 前线指挥与信息筛选 | 基地巡检与上下文提示 | 目标切换与回放理解 |
| 主要压力源 | crowd + culling + HUD | terrain + props + panel + overlay | camera + prompt + marker + bars |
| Web / Raylib 现阶段 | 同一 mod 数据、同一 acceptance script、同一 battle-report 语义 | 同左 | 同左 |
| Unity / Unreal 未来适配 | 复用地图与 acceptance 脚本，只替换 adapter 桥接与渲染实现 | 同左 | 同左 |

## 9 建议落地顺序

1. `Crowd Battlefield Mod`
   因为它与当前 `camera_acceptance_hotpath` 的 crowd / culling harness 最接近，可以最快把技术 harness 映射成玩家语义。
2. `Town / Base Mod`
   用来证明 presentation hotpath 不等于 crowd benchmark。
3. `Hero Focus / Replay Mod`
   最后补齐 camera control、prompt、marker、target switching 的组合场景。

## 10 相关实现与证据

* 当前 shared technical harness：`mods/fixtures/camera/CameraAcceptanceMod/`
* hotpath lane isolation：`mods/fixtures/camera/CameraAcceptanceMod/Systems/CameraAcceptanceHotpathLaneSystem.cs`
* live toggle wiring：`mods/fixtures/camera/CameraAcceptanceMod/Systems/CameraAcceptanceDiagnosticsToggleSystem.cs`
* headless acceptance：`src/Tests/ThreeCTests/CameraAcceptanceModTests.cs`
* 当前 hotpath 地图：`mods/fixtures/camera/CameraAcceptanceMod/assets/Maps/camera_acceptance_hotpath.json`


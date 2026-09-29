# GAS Composition Gate — 切镜头节点、下令排队、下令摊方阵

本票自审（CoreInputMod 退役 · 片 4 的 C1 和片 5 的前置）。不覆盖 `artifacts/gas-composition-gate.md` 正本。

## 任务摘要

老下令映射（`InputOrderMappingSystem`）和视角模式（`ViewModeManager`）要删，它们做的三件事在图里还没有写法：

- 视角模式切镜头：新节点 `ActivateVirtualCamera`，`camera` 写 `Camera/virtual_cameras.json` 里的 id。
- 按住 Shift 下令排到队尾：`SubmitCommandIntent` / `SubmitCast` / `SubmitEngageBatch` 加参数 `queue`（不写 = 替换；`onQueueModifier` = 看按键那一刻有没有按排队修饰键；`always` = 一律排队）。
- 一组兵右键地面摊成方阵：`SubmitCommandIntent` 加参数 `layout` + `layoutSpacingCm`，复用已有的 `MoveTargetLayoutPlanner`。

## GAS Composition Gate — Self Review

- **Task / Issue**: CoreInputMod 退役设计 §3.2、§3.8、§3.9 C1
- **Date**: 2026-09-29
- **Agent / Author**: Cursor Cloud Agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 切镜头是一个新的 Layer 0 节点；排队和摊方阵是已有下令节点的参数，落到已有的订单提交方式和已有的方阵规划器上。

起初把摊方阵写成命令意图档案上的 `groupLayout` 字段，自审时按标准 B（新 profile 字段 + 枚举）判为不通过，改成节点参数后重审通过。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 切镜头 | 0 | 新节点 `ActivateVirtualCamera`，写已有的 `VirtualCameraRequest` |
| 排队 | 0（参数） | 下令节点 `queue` → 已有 `OrderSubmitMode`，排队语义由订单内核执行 |
| 读排队修饰键 | 0（已有） | 按键入口载荷 `MapTrigger.Modifiers` 的 `Queue` 位 |
| 摊方阵 | 0（参数） | `SubmitCommandIntent.layout` → 下令缓冲消化时调已有 `MoveTargetLayoutPlanner` |
| 视角模式、下令键位 | 2 | 各 showcase 的交互状态 + 图 |

### 3. Reuse list

- Handlers: 无新增
- Queues / Systems: `CommandIntentSubmissionBuffer`、`CommandIntentBufferDrainSystem`、`OrderQueue`、`CameraRuntimeSystem`
- Resolvers / Registries: `VirtualCameraRegistry`、`CameraFollowTargetFactory`、`MoveTargetLayoutPlanner`、`ConfigKeyRegistry`
- Existing presets / graphs: TW / Case E 的下令图写法

### 4. New Layer 0 ops (if any)

| Op | 单一职责说明 | 为何不能用现有 op 组合 |
|----|-------------|------------------------|
| `ActivateVirtualCamera` | 按 id 激活一台已声明的虚拟镜头，替换当前镜头栈；镜头要跟随集合时跟跑图的玩家代表实体 | 现有图节点没有任何一个能写镜头请求；叙事、过场各自在 C# 里写 |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。摊方阵时某个兵没有世界坐标，整批下令在入队前拒绝，不会只下一半。

### 6. Config SSOT

行为配置落在: 镜头参数在 `Camera/virtual_cameras.json`；键位在各 mod 的 `Input/default_input.json`；排队、摊方阵写在下令图节点上。

是否新增 JSON schema: NO。节点新参数由图编译器校验（GASG0029、GASG0030），镜头 id 在运行时对注册表校验。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（`onQueueModifier` 在非按键入口直接报错，不猜）

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / 节点参数**（换个镜头 = 改 `camera`；换队形间距 = 改 `layoutSpacingCm`）

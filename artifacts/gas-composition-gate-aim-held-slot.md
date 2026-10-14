## GAS Composition Gate — Self Review

- **Task / Issue**: 瞄准确认点击不得改选中；握着的槽位缺黑板失败，没有旧演员不重开形状
- **Date**: 2026-10-14
- **Agent / Author**: cursor agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（已有 op 的辅助输出 + 图连线；输入相位末尾恢复已有挂载门）

结论: PASS

一句话理由: 前台关掉后，同一输入相位里被停掉的祖先动作留到相位结束再恢复。`ReadBlackboardInt` 沿用 `validOutput`：键在则为 1 并写出整数，键不在则为 0，整型结果不使用；不写这个端口时缺键仍抛。没有旧演员的返回值是 -1，刷新图见到 -1 就不派 `Aim.Shape.Bind`。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 前台仍开着时停掉祖先的动作挂载，相位结束再恢复 | 2 | `InteractionContextTriggerMountSystem` 的 park / unpark；`TriggerGraphActionBindingSystem` 在每个输入相位前后调用 |
| 读整数黑板并给出有没有这个键 | 0 | `ReadBlackboardInt` 的 `validOutput`，Flags 0 仍缺键即抛 |
| 没有旧演员返回 -1，缺整数黑板 `Fail` | 数据 | `graph.aim.read_held_slot` |
| 返回值记在新演员的 `aim.held.code` | 数据 | 四个重绑图的 `WriteBlackboardInt` |
| -1 不派 `Aim.Shape.Bind`，其余先写 `aim.slot` 再派 | 数据 | `graph.aim.refresh_gesture` |
| 0 号槽没有技能的候选不写入 `aim.actors` | 数据 | `graph.champion.rebind_aim_actor` 对 `LoadSlotAbilityId` 使用 `validOutput` |

### 3. Reuse list

- Handlers: `HandleReadBlackboardInt`；`validOutput` 与 `PointToDirection` / `StickToDirection` 同一条辅助输出
- Queues / Systems: `InteractionContextTriggerMountSystem`、`TriggerGraphActionBindingSystem`
- Resolvers / Registries: `OrderBlackboardKeyRegistry`（`aim.held.code` 随写入注册）
- Existing presets / graphs: `graph.aim.read_held_slot`、`graph.aim.refresh_gesture`、`graph.aim.prepare_actor`、四个重绑图、`JumpIfFalse`、`Fail`

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。相位末恢复是挂载门的状态差，不新开窗口，不跑 Activated / Deactivated。

### 6. Config SSOT

行为配置落在: graph（`graph.aim.read_held_slot`、`graph.aim.refresh_gesture`、四个重绑图）+ 已有交互状态的 `foreground`

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线（重绑图把读数写到哪把黑板键，刷新图在什么返回值下派 `Aim.Shape.Bind`）

若选了 Core enum → FAIL

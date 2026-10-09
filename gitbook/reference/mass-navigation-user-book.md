# MassNavigation RTS 上手书

这份文档面向想做 RTS 集群移动的 Mod 作者。参考实现是：

`mods/showcases/formation_capability/FormationCapabilityShowcaseMod/`

## 玩家看到什么

玩家只需要理解：

- 选择一个方阵 anchor；
- 右键地面下达移动命令；
- 方阵成员整体移动，并在避障后重新形成相对布局；
- marker、血条、轮廓、小地图和相机继续正常工作。

当前 feature 不包含 Q/E 原地旋转。旧旋转只改变 facing/表现，没有形成真实移动玩法，已经删除。

## 启动

```powershell
.\scripts\run-mod-launcher.cmd cli launch FormationCapabilityShowcaseMod --adapter raylib --build auto
```

## Mod 作者真正拥有的内容

| 路径 | 职责 |
| --- | --- |
| `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/assets/FormationCapabilityShowcaseConfig.json` | 方阵、成员模板、slot 布局、初始位置、轮廓与障碍展示 |
| `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/assets/Entities/templates.json` | anchor、soldier、overlay 的 entity template |
| `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/assets/Input/interaction_context_profiles.json` | 框选与右键下令的按键、触发的图 |
| `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/assets/GAS/graphs/graph.formation.command_commit.json` | 右键下令图：读成员集合，按 `actorOrder` 布局提交 `massNavigationMove` |
| `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/assets/Input/command_intent_profiles.json` | CommandIntent route |
| `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/assets/MassNavigationConfig.json` | 成员 agent profile、solver、route 和容量 |
| `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/Runtime/FormationCapabilityShowcaseRuntime.cs` | 场景生成与 showcase 生命周期 |
| `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/Systems/FormationCommandMembersSystem.cs` | 把命令源里的 anchor 换成 members，维护 `formation.command.members` 集合 |

不要复制 `OrderQueue`、`OrderBuffer`、Command Router、spawn queue、MassNavigation runtime 或 presenter runtime。

## Anchor 与 Member

Anchor 是玩家操作对象：

- 有 `CommandSourceSelectableTag`；
- 可有 health、marker、outline；
- 有 showcase-owned `FormationAnchorState`；
- 没有 `OrderBuffer`；
- 没有 `MassNavigationAgent`；
- 不直接移动。

Member 是执行对象：

- 有 `FormationMemberState`；
- 有 `OrderBuffer`；
- 有 `MassNavigationAgent`；
- 绑定后有 typed MovePlan intent/result；
- 不进入玩家 command-source collection。

这让玩家操作“一个方阵”，同时保持每个士兵都是正式 order actor 和 navigation actor。

## 命令如何转发

```text
玩家右键
  -> collection.command.source 中的 anchor
  -> FormationCommandMembersSystem 维护的 formation.command.members
  -> graph.formation.command_commit（SubmitCommandIntent，actorOrder 布局）
  -> CommandIntentProfile
  -> CastDispatch
  -> 原子 shared OrderQueue batch
  -> 每个 member 的 OrderBuffer
  -> GAS projection
  -> typed MovePlan command group
  -> MassNavigation
  -> typed result
  -> GAS complete/cancel
```

Formation 不自己查 `OrderBuffer`，也不创建 Formation 专用 order consumer。它只给 Command Router 提供“这个 anchor 展开成哪些 actor”。

## 配方阵

`FormationCapabilityShowcaseConfig.json` 中每个 formation 提供：

```json
{
  "id": "shu_left_vanguard",
  "teamId": 1,
  "soldierAgent": {
    "templateId": "formation_capability_showcase_soldier_azure_light",
    "profileId": "light"
  },
  "centerXCm": -2600,
  "centerYCm": -2200,
  "facingDeg": 78,
  "slots": {
    "layout": "Grid",
    "grid": {
      "columns": 20,
      "rows": 12,
      "spacingXCm": 46,
      "spacingYCm": 50
    }
  }
}
```

Slot 只用于初始成员布局和稳定展开顺序。移动后的 command-group 保留成员当前相对偏移，不需要新增 formation mode、rotate order 或 preset 开关。

## 配 Agent Profile

只有成员需要 MassNavigation profile。当前示例使用：

- `heavy`：重装成员；
- `light`：轻装成员。

已删除无人使用的 `formation` profile，因为 anchor 不是 navigation agent。

所有 capacity 必须显式配置：

- `movePlanExecutionGroupCapacity`；
- `movePlanExecutionMemberCapacity`；
- `navigationGroupCapacity`；
- route state 与 waypoint capacity；
- showcase 的每 formation 最大成员数与总展开数。

容量不足必须明确失败，不得扩容或丢成员。

## 多方阵目标

当玩家同时选择多个 anchor，`graph.formation.command_commit` 的 `SubmitCommandIntent` 节点按 `actorOrder` 布局分配目标，避免多个方阵重叠到同一点。

```json
{
  "id": "submit",
  "op": "SubmitCommandIntent",
  "queue": "onQueueModifier",
  "layout": "actorOrder",
  "layoutSpacingCm": 1800
}
```

成员 fan-out 发生在每个中心目标确定之后；同一 anchor 的成员共享一个 command-group token，不同 anchor 使用不同 token。
`ActorOrder` 是这里的显式合同：槽位由稳定的 anchor 顺序决定，不读取成员当前位置，也不会隐式切换为方向保持分配。

## 失败语义

- actor 重复、缺 `OrderBuffer`、被规则阻塞：整个 admission batch 不激活。
- 空间 payload 非法：GAS projection 产生 typed failure。
- route profile 或 agent binding 不可用：MassNavigation 产生 typed failure，不先写 solver。
- `Arrived`：GAS 完成当前 order。
- `Failed`：GAS 取消当前 order，删除其 continuation，不伪装成完成。

## 做自己的 RTS Mod

1. 复制 showcase 的 Mod 目录结构和配置入口。
2. 定义自己的 anchor/member 业务组件。
3. 实现一个 `ICommandActorExpander`，按稳定业务顺序输出成员。
4. 在 input mapping 中复用 `massNavigationMove`。
5. 显式配置成员和总展开容量。
6. 用行为测试验证原子 admission、slot 顺序、typed result 和 unload/reload。

只有当第二个真实 Mod 也需要完全相同的 Formation 业务规则时，才讨论抽取独立 Formation capability Mod；不能提前塞回 Core。

## 验收清单

- 玩家只能选择 anchor，不能直接选择成员。
- anchor 无 `OrderBuffer`、无 `MassNavigationAgent`。
- member 有 `OrderBuffer`、`MassNavigationAgent` 和 MovePlan contract。
- 一个 anchor 的成员按 slot 稳定展开。
- 任一成员拒绝时没有部分激活。
- MassNavigation 源码不引用 Order lifecycle 类型。
- 到达/失败只通过 typed result 回到 GAS。
- Road 的 `Individual` MovePlan 与 Formation 的 `CommandGroup` MovePlan 不会被重复消费。

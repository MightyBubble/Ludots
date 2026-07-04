# RFC-0062 Interaction Context Stack — Input 基建与 Context-Bound Collection 路由

Status: Proposed  
Epic: [#536](https://github.com/MightyBubble/Ludots/issues/536)

## 1. 问题

「Selection」被实现成单一全局语义，无法表达：

- 默认 RTS 框选指挥 vs 超级武器施法中「指定单位」框选
- 技能结束回到默认 context 后，框选语义自动恢复
- MOBA 单英雄 vs RTS 多选共用同一 input cast 基建、不同 collection key

`InteractionModeType` 把 commit、presentation、targeting 捆在一个 enum，导致 SmartCast 陷阱与 per-ability 域无法正交扩展。

**Context Stack 是 Input 基建**，不属于 ParticipantView Mode，不属于 Selection hub。

## 2. 结论

引入 **InteractionContextStack**（Input 层 SSOT）：

- 每个 local client session 维护 **context 栈**（push / pop）
- 栈顶决定 **activeCollectionKey** 与 **activeEntityViewKey**
- 同一 InputCast（box / polygon / screen / world）始终走同一 cast 管线，仅 **写入 key** 随 context 变化

```text
默认 context:
  activeKey = collection.command.source
  activeView  = view.command.default

超级武器 ConfirmTargets context:
  activeKey = collection.ability.superweapon.targets
  activeView  = view.ability.superweapon.targets
  contextEntity = abilityExecInstanceEntity（descriptor.ContextEntity）

pop → 恢复 default，command.source 未被 superweapon 污染
```

## 3. Context Stack 数据模型

### 3.1 Core 契约（拟新增）

```csharp
public readonly record struct InteractionContextFrame(
    string ContextId,              // e.g. "default", "ability:nuke:exec42"
    string ActiveCollectionKey,
    string ActiveEntityViewKey,
    Entity ContextEntity,          // ability exec / order lease / default null
    Entity FilterProfileEntity);   // optional, data-driven filter binding

public sealed class InteractionContextStack
{
    void Push(in InteractionContextFrame frame);
    bool TryPop(out InteractionContextFrame frame);
    bool TryPeek(out InteractionContextFrame frame);
}
```

### 3.2 挂载点

| 选项 | 结论 |
|------|------|
| Client session service | **首选**：`CoreServiceKeys.InteractionContextStack`，local-only，不参与 sim 同步 |
| Player rep 组件 | 仅当需要 replay / 观战同步 context 时才考虑；默认不做 |

Input 系统在 `InputCollection` phase 读 stack top，决定：

1. InputCast 结果写入哪个 `(playerRepEntity, activeCollectionKey)`
2. Order mapping 读哪个 EntityView / collection role

### 3.3 Context 生命周期来源

| 来源 | 动作 |
|------|------|
| Map load | push `default` frame |
| AbilityExec 进入 PendingConfirm / Channel | push ability frame（data：`AbilityDefinition.interactionContextProfile`） |
| AbilityExec 结束 / cancel | pop |
| Super weapon / multi-phase skill | 多个 frame 可嵌套 |

禁止 `_isAiming` / `AbilityAimPresentationRuntime` 作为 context SSOT；presentation 读 entity tag + collection revision。

## 4. Input Cast 管线（geometry 无关）

```text
InputCastSpec (from binding / CastProfile)
  → spatial / screen query
  → (clientSession, collection.ui.cast.raw)     // 原始命中，不过滤
  → FilterProfile.evaluate(localPlayerRep, raw)
  → (playerRepEntity, stack.activeCollectionKey) // 语义写入
  → PresentationEvent / EntityCollection revision
```

### 4.1 FilterProfile（data）

```json
{
  "id": "filter.controllable.default",
  "associationQuery": {
    "anchor": "localPlayerRep",
    "edgeTypes": ["owns", "controls"],
    "exclude": ["dead", "hidden"]
  }
}
```

Filter 只读 association graph（RFC-0063），不改 relationship。

### 4.2 与「Selection」一词的关系

**Selection 只是 default context 下 `collection.command.source` 的俗名**，不是 Core 类型名。文档与 Mod 配置可使用 `selection` 作为 viewKey alias，Core 不得保留 `SelectionRuntime` hub。

## 5. 技能域 Collection 示例

| 场景 | ContextId | CollectionKey |
|------|-----------|---------------|
| RTS 默认指挥 | default | collection.command.source |
| 超级武器指定单位 | ability:nuke | collection.ability.nuke.targets |
| MOBA 地面指示器 | ability:skillw | collection.ability.skillw.ground |
| Tab 循环候选 | input:tabcycle | collection.input.tab.candidates |

同一 `InputCastSpec.BoxScreen` 在各行写入不同 key。

## 6. 分层边界

| 模块 | 做 | 不做 |
|------|-----|------|
| InteractionContextStack | push/pop/peek；暴露 active key | 存 entity 列表 |
| InputCastSystem | 几何查询 → raw collection | 判断 controllable |
| FilterProfileRuntime | association 过滤 | 写 order |
| CollectionWriteSystem | filtered → playerRep collection | merge 跨 player namespace |
| AbilityExec | 触发 push/pop | 直接写 SelectionRuntime |
| Performer | 读 collection revision + provenance | 决定 active key |

## 7. Sub-issues（CTX-*）

Parent: [#536](https://github.com/MightyBubble/Ludots/issues/536)

| ID | Issue |
|----|-------|
| CTX-1 | [#539](https://github.com/MightyBubble/Ludots/issues/539) InteractionContextStack Core |
| CTX-2 | [#540](https://github.com/MightyBubble/Ludots/issues/540) Default frame 初始化 |
| CTX-3 | [#541](https://github.com/MightyBubble/Ludots/issues/541) InputCast → raw collection |
| CTX-4 | [#542](https://github.com/MightyBubble/Ludots/issues/542) FilterProfile registry |
| CTX-5 | [#543](https://github.com/MightyBubble/Ludots/issues/543) Context-bound collection write |
| CTX-6 | [#544](https://github.com/MightyBubble/Ludots/issues/544) AbilityExec push/pop |
| CTX-7 | [#546](https://github.com/MightyBubble/Ludots/issues/546) CastProfile 正交拆分 |
| CTX-8 | [#548](https://github.com/MightyBubble/Ludots/issues/548) ClientCastPreference per-slot |
| CTX-9 | [#550](https://github.com/MightyBubble/Ludots/issues/550) Showcase superweapon |
| CTX-10 | [#552](https://github.com/MightyBubble/Ludots/issues/552) ArchitectureTests + 文档 |

## 8. 依赖

- RFC-0061（Order intake 读 active collection）
- RFC-0063（FilterProfile association query）
- 可选并行：#522 ORD-5

## 9. 非目标

- 不实现 ParticipantView Mode enum
- 不在 Core 硬编码 RTS/MOBA 分支
- 不做 SelectionRuntime 兼容层

## 10. 验收

- [ ] box / polygon / screen / world 共用 InputCastSystem
- [ ] superweapon 施法框选写入独立 key，pop 后 default 恢复
- [ ] 零 `SelectionRuntime` 作为 context 路由 SSOT
- [ ] FilterProfile 数据驱动，Core 无 `if (rts)` 分支
- [ ] Playable showcase + headless acceptance

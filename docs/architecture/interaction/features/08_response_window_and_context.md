# Feature: Response Window (O1–O8) & Insertable Context (P1–P7)

> 清单覆盖: O1-O8 响应窗口(陷阱/反制/连锁/重定向/暂停选目标/Hook/Modify/Chain/超时), P1-P7 插入式上下文填充

## 交互层

这些不是传统的"技能施放交互"——它们是**效果结算过程中的玩家决策点**。
在 Ludots 中由 **Response Chain + Gate 系统** 承载。

---

## O: 响应窗口 — Ludots Response Chain 映射

### 现有架构完美匹配

Ludots 的 `EffectProposalProcessingSystem` 已实现完整的响应窗口:

```
WindowPhase:
  None → Collect → WaitInput → Resolve
```

| 清单 | ResponseType | Ludots 实现 |
|------|-------------|------------|
| O1 陷阱/反制 | PromptInput → Chain | OrderRequest 弹出 → 玩家选择激活哪个 → Chain 新 effect |
| O2 连锁响应 | Chain × N | 多个 ResponseChainListener 按 Priority 排序, 依次处理 |
| O3 重定向 | Modify (target) | Modify ResponseType 修改 EffectContext.Target |
| O4 暂停选目标 | PromptInput | OrderRequest → ResponseChainUiState.Visible=true → 等待玩家输入 |
| O5 Hook/取消 | Hook | Hook ResponseType → 取消效果, 不执行 |
| O6 Modify/修改 | Modify | Modify ResponseType → ModifyValue + ModifyOp 修改伤害值 |
| O7 Chain/追加 | Chain | Chain ResponseType → EffectTemplateId → 创建新 effect |
| O8 超时自动通过 | GateDeadline | AbilityExecInstance.GateDeadline → 超时自动 Pass |

### O1: 陷阱/反制激活窗口 (游戏王)

```
当敌方 Effect 进入 Proposal 阶段:

1. EffectProposalProcessingSystem 收集 effect proposals
2. 查询所有 ResponseChainListener 匹配该 EventTag
3. 找到玩家的 "trap_card" listener (ResponseType=PromptInput)
4. 创建 OrderRequest:
   - PromptTagId = trap_activation_tag
   - AllowedOrderTypeIds = [ChainPass, ChainNegate, ChainActivateEffect]
5. ResponseChainUiState.Visible = true → UI 显示选项
6. 等待 ResponseChainHumanOrderSourceSystem 的输入:
   - Space = Pass (不激活)
   - N = Negate (无效化对方效果)
   - 1 = Activate (激活陷阱, 创建 Chain effect)
7. 结算
```

**已完全实现**, 无新代码。

### O2: 连锁响应 (游戏王连锁)

```
实现: 多个 ResponseChainListener 的嵌套处理

Effect A proposed →
  Player1 有 Listener (Priority=10) → PromptInput → 选择 Chain → 创建 Effect B
  Effect B proposed →
    Player2 有 Listener (Priority=20) → PromptInput → 选择 Chain → 创建 Effect C
    Effect C proposed → 无更多 listener → Resolve
  Effect C resolved (先结算)
  Effect B resolved (后结算)
Effect A resolved (最后结算)
```

- **逆序结算**: 已有, Proposal queue LIFO 结算
- **多层 Chain**: 已有, Chain 创建的新 EffectRequest 会被再次走 Proposal 流程

### O3: 重定向 (游戏王重定向陷阱)

```
ResponseChainListener:
  eventTagId: damage_applied
  responseType: Modify
  responseGraphId: redirect_graph

redirect_graph:
  1. 弹出 SelectionGate → 让玩家选新目标
  2. I[0] = new_target_entity_id
  3. ModifyValue → write new target to EffectContext
```

- **需要**: ResponseType.Modify 支持修改 target (目前 Modify 只改 ModifyValue 数值)
- 或: 用 ResponseType.Hook (取消原效果) + Chain (创建新效果指向新目标)

### O4: 暂停选目标 (三国志)

```
实现路径 1 (Gate-based):
  AbilityExecSpec:
    Item[0]: EffectSignal → show_target_selection_ui
    Item[1]: SelectionGate → 等待玩家选择目标
    Item[2]: EffectSignal → apply_to_selected (读取 SelectionResponse)

实现路径 2 (ResponseChain-based):
  PromptInput 类型的 ResponseChainListener
  → OrderRequest 包含可选目标列表
  → UI 显示选项 → 玩家选择 → Order 消费
```

- **已有**: SelectionGate + GasSelectionResponseSystem

### O8: 超时自动通过

```
AbilityExecInstance.GateDeadline:
  设置为 current_tick + timeout_ticks
  AbilityExecSystem 检查: if current_tick > GateDeadline → auto-pass
```

- **已有**: GateDeadline 字段在 AbilityExecInstance

---

## P: 插入式上下文填充 — Gate 系统映射

### P1: 选择额外目标

```
AbilityExecSpec:
  Item[0]: EffectSignal → primary_damage (hit main target)
  Item[1]: SelectionGate @ tick 30 → 等待玩家选择分摊目标
    → requestTagId: redirect_selection
  Item[2]: EffectSignal → redirect_effect (用 SelectionResponse 的 entity)
```

- 已有: `SelectionGate` + `GasSelectionResponseSystem`

### P2: 选择效果变体

```
AbilityExecSpec:
  Item[0]: InputGate @ tick 0 → 等待玩家选择 (Q/W/E哪个增强)
    → UI 显示可选效果列表
  Item[1]: EffectSignal → 根据 InputResponse.PayloadA 选择 CallerParams
    → CallerParamsIdx 指向不同配置 (4 个 CallerParams slot)
```

- 已有: `InputGate` + `AbilityExecCallerParamsPool` (最多 4 组配置)

### P3: 拖拽确定二阶段方向

```
AbilityExecSpec:
  Item[0]: EffectSignal → execute_phase_1 (如: 抓住友方)
  Item[1]: SelectionGate → 等待玩家选择方向/落点
  Item[2]: EffectSignal → execute_phase_2 (如: 投出友方到选定方向)
```

### P4: 填写数值参数

```
AbilityExecSpec:
  Item[0]: InputGate → 等待 UI 输入
    → UI 显示滑块/数字选择
    → InputResponse.PayloadA = user_value
  Item[1]: EffectSignal → apply_with_value
    → ConfigParams: value = InputResponse.PayloadA
```

### P5: 确认/取消高代价技能

```
AbilityExecSpec:
  Item[0]: InputGate → 等待确认
    → GasInputResponseSystem: ConfirmActionId press → confirm
    → CancelActionId press → cancel (ability interrupted)
  Item[1]: if confirmed → EffectSignal → execute_nuke
```

### P6: 多次选取

```
AbilityExecSpec:
  Item[0]: SelectionGate → 选第一组目标 (MaxCount=32)
    → GasSelectionResponseSystem 框选
  Item[1]: EffectSignal → process_group_1
  Item[2]: SelectionGate → 选第二组目标
  Item[3]: EffectSignal → process_group_2
```

### P7: 条件分支选择

```
AbilityExecSpec:
  Item[0]: InputGate → UI 显示选项列表
    → ResponseChainUiState.AllowedOrderTypeIds 列出可选项
    → 玩家选择 → InputResponse.PayloadA = selected_option_id
  Item[1]: EffectSignal → Phase Graph:
    1. ReadBlackboardInt(selected_option)
    2. CompareEqInt(option, 1) → branch_A
    3. CompareEqInt(option, 2) → branch_B
    4. ApplyEffectTemplate(branch_result)
```

---

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| ResponseChainListener | ✅ 已有 | O1-O8 的核心 |
| ResponseType (Hook/Modify/Chain/PromptInput) | ✅ 已有 | 4 种响应类型 |
| WindowPhase (Collect/WaitInput/Resolve) | ✅ 已有 | 响应窗口生命周期 |
| OrderRequest/OrderRequestQueue | ✅ 已有 | 提示玩家输入 |
| ResponseChainUiState | ✅ 已有 | UI 状态 |
| ResponseChainHumanOrderSourceSystem | ✅ 已有 | Space/N/1 键处理 |
| ResponseChainAiOrderSourceSystem | ✅ 已有 | AI 自动 Pass |
| InputGate | ✅ 已有 | P1-P7 的核心 |
| SelectionGate | ✅ 已有 | P1, P3, P6 |
| EventGate | ✅ 已有 | 等待特定事件 |
| GateDeadline | ✅ 已有 | O8 超时 |
| CallerParamsPool (4 slots) | ✅ 已有 | P2 效果变体 |

## 新增需求

| 需求 | 优先级 | 清单项 |
|------|--------|--------|
| Modify ResponseType 支持修改 target (不仅是数值) | P1 | O3 重定向 |
| 嵌套 Chain 的递归 Proposal 处理验证 | P2 | O2 多层连锁 |
| UI 层: 选项列表/滑块/确认对话框组件 | P2 | P2, P4, P7 |
| ResponseChainUiSyncSystem 支持丰富 UI (不仅 debug overlay) | P2 | O1-P7 通用 |

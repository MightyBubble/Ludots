## GAS Composition Gate - Self Review

- **Task / Issue**: Graph editor milestone audit: node association, Select/Break authoring, composition entry points, and string-template/Concat boundary
- **Date**: 2026-08-24
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS（控制流/黑板/trace 现状；文本能力未开放）**

一句话理由: 节点联想、Break 和组合入口复用运行时 descriptor/compiler；字符串模板与 Concat 只有在正式 graph value/runtime 合同存在后才能成为可保存节点，本次不以编辑器假能力绕过该边界。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2) | 实现载体 |
|-----------|-----------------|----------|
| 节点联想与端口展示 | 2 | Editor Bridge runtime descriptor projection + React authoring surface |
| Break 控制流糖 | 2 | `GraphAuthoringSugar` + `GraphControlFlowCompiler` lowers to existing Jump |
| Select | 2 | Existing `SelectEntity` descriptor/compiler/runtime contract |
| Composition entry | 2 | Existing `InvokeScript`/FuncLib descriptor and compiler contract |
| String template / Concat boundary | 2 (deferred) | Requires a formal text value/runtime API; editor must reject until registered |

### 3. Reuse list

- Handlers: existing `Jump`, `SelectEntity`, `InvokeScript` handlers
- Queues / Systems: existing graph compiler and program registry; no new execution pipeline
- Resolvers / Registries: `GraphOpDescriptorTable`, `GraphAuthoringSugar`, Bridge descriptor endpoint
- Existing presets / graphs: existing Script/TriggerGraph composition and FuncLib catalog

### 4. New Layer 0 ops (if any)

N/A. Break is authoring sugar and lowers to existing `Jump`; no new runtime handler.

### 5. Transaction boundary

None. This editor milestone does not mutate entity lifecycle state.

### 6. Config SSOT

Behavior configuration remains in graph JSON (`assets/GAS/graphs.json`) and the runtime descriptor/compiler tables. The editor layout remains in `assets/GAS/graph_editor.json`.

是否新增 JSON schema: **NO**.

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加默认 fallback；缺 descriptor/unsupported op remains an explicit error

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / effect 步骤**

字符串花括号自动引脚和 Concat 的运行时实现仍是后续独立切片，必须先增加 text value contract、固定容量/零分配策略、symbol patching 和 presentation sink，再开放为可保存节点。

### 9. Audit hardening applied

- Blackboard capability now installs and batch-materializes `BlackboardFloatBuffer` together with the existing order blackboard buffers; `RequireInstalled` checks the complete set.
- Trigger graph debug trace carries the last instruction PC through the execution cursor. Live pin/watch records therefore resolve to the instruction that actually ran, and missing source-map entries fail closed in AgentBridge.
- Editor graph descriptors are mandatory, layout entries are schema-checked, and node deletion removes connected edges before validation/save.
- The descriptor projection now includes the runtime-authoritative control output ports for ordinary nodes (`Jump.target`, `Call.call/next`, and the normal `next` continuation); the React canvas consumes this projection instead of inventing an op list.
- Authoring sugars exposed by the Bridge are `BranchBool`, `SwitchInt`, `Wait`, `While`, `Until`, and `Break`, with their compiler-required control/value ports. `SwitchInt` case ports remain explicit `case:<int>` edges and are added to the node only when authored.
- Live trace source-map lookup is fail-closed for root and nested graph ids; nested `InvokeScript` execution shares the fixed trace ring and carries its child graph id. Blackboard entity events use `keyId` consistently with int/float events.
- Trace currently records executed instruction/node attribution, pin and blackboard changes, Yield/budget suspension, and Halt. It does not manufacture `NodeExit`; the earlier wording that claimed a complete enter/exit lifecycle is intentionally removed.
- Blackboard buffers are runtime capabilities and are still installed through the existing order-blackboard capability path. Authoring-time entity capability declarations are a remaining contract slice; missing buffers continue to fail explicitly at execution.

# GAS Composition Gate - Executable Graph Op Registration

- Date: 2026-07-24
- Agent: Codex
- Result: PASS

## Task summary

把 Graph op 从“只注册名字/别名”推进到“Mod 加载期注册名字、编译形状、执行 handler，运行前统一冻结”。目标是给后续关卡蓝图、技能蓝图、状态机、行为树共用底层 graph API 打基础。

本次不新增玩法变体、不新增 effect preset、不新增 profile enum、不新增平行脚本运行时。

## GAS Composition Gate — Self Review

- **Task / Issue**: Graph op registry + executable custom handler foundation
- **Date**: 2026-07-24
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 新增的是 graph 节点注册与执行扩展能力，行为仍由 graph op 组合表达，不通过 profile enum 或 preset 开关表达。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| graph op 名称、opcode、编译形状注册 | Graph 编译基础设施 | `GraphOpDescriptor`, `GraphOpRegistry` |
| GAS 自定义 op 声明辅助 | GAS 节点库 | `GasGraphOpDescriptor` |
| GAS op handler 加载期注册、运行前冻结 | GAS Graph VM | `GasGraphOpHandlerTable` |
| Mod 加载期注册自定义 op 和 handler | Mod 扩展入口 | `IModContext.GraphOpRegistry`, `IModContext.GasGraphOpHandlers` |
| Engine 使用同一份冻结 handler 表执行 graph | Startup/Runtime 装配 | `GameEngine`, `GraphReturnWriter`, `EffectPhaseExecutor` |
| graph 配置编译使用注册表 | Layer 2 组合加载 | `GraphProgramConfigLoader`, `GraphCompiler`, `GraphValidator` |

### 3. Reuse list

- Handlers: 复用并扩展现有 `GasGraphOpHandlerTable`，不新增第二个 VM。
- Queues / Systems: 复用现有 graph 配置加载、`GraphProgramRegistry`、`EffectPhaseExecutor`、`GraphReturnWriter`。
- Resolvers / Registries: 复用 `GraphProgramConfigLoader`、`GraphIdRegistry`、`CoreServiceKeys`、`ModLoader`、`ModContext`。
- Existing presets / graphs: 现有 `GAS/graphs.json` schema 不变；旧内置 op 名继续可用。

### 4. New Layer 0 ops (if any)

N/A。没有新增 entity lifecycle atomic op，也没有新增 gameplay graph op。新增的是 op 注册和执行基础设施。

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A。此切片只发生在 Mod 加载、graph 编译、VM handler 注册阶段，不执行实体结构替换事务。

### 6. Config SSOT

行为配置落在: graph asset，例如 `GAS/graphs.json`。Mod 自定义 op 的 C# handler 与编译形状在 Mod `OnLoad` 注册。

是否新增 JSON schema: NO。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤。

若需要新行为，先注册明确的 graph op 编译形状和 handler，或组合现有 op；不得新增 Core enum/profile 开关。

## Boundary note

当前自定义 op 编译形状支持：一个输出类型、可选固定输出寄存器、最多 3 个寄存器输入，以及 `intValue` / `floatValue` / `boolValue` 标量立即数。超出范围会在编译或执行时 fail-fast；不会静默降级为 no-op。

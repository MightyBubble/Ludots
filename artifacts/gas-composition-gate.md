# GAS Composition Gate — LiveGasEditPipeline (#615 / #618–#622)

## Scope
Real Stage → Classify → Commit hot-apply for GAS Graph body + Tag rule body + Attr constraints + effect numeric + actor attribute commands (#622 complete).
Not ReloadConfigs. Not Clear+Register-all. #874 shortcut superseded.

## Reuse
- `LiveEditSession` / `LiveDebugPatch` (#637)
- `GraphProgramAuthoringFrontDoor` + `GraphControlFlowCompiler`
- `GraphProgramRegistry.ReplaceProgram`
- `TagOps.ReplaceTagRuleSet` + `TagRuleSetLoader.CompileRuleSetForHotApply`
- `AttributeRegistry.ReplaceConstraints`
- `EffectTemplateRegistry.TryReplaceHotNumericField`
- `AttributeMutationOps` for Immediate attribute commands
- `CoreServiceKeys.LiveGasEditPipeline`

## Op vs enum
No new EffectPresetType / BuiltinHandler. Apply-mode enum is LSW classification only.

## Fail-closed
- Stage never clears live registries
- Graph/Tag/Attr identity change → EngineRestartRequired
- Unknown effect/constraint field → MapReloadRequired
- Hot tag compile never Register()s new tag names
- NextCast commit only inside SafeFrame

## GAS Composition Gate — Self Review

- **Task / Issue**: Effect-phase authoring expressiveness for FuncLib InvokeScript and BranchBool
- **Date**: 2026-08-12
- **Agent / Author**: Cursor Cloud Agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本任务扩展既有 graph 作者前门与 kind policy，让 Effect/Score/Validation/Derived 复用已有 FuncLib 调用，并让 Effect 使用现有 BranchBool 糖，不新增 profile enum、preset 开关或平行管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Effect 允许 InvokeScript FuncLib 调用 | 2 | `GraphControlFlowCompiler` 线性 kind 白名单与 ParseOps |
| Effect 允许 BranchBool 作者糖 | 2 | `GraphControlFlowCompiler` lowering 到现有 jump ops |
| Wait/Yield 失败关闭 | 2 | 既有 linear kind validation / `GraphKindOperationPolicy` |
| 覆盖测试 | 2 | GAS graph front door tests |

### 3. Reuse list

- Handlers: existing graph op handlers, `InvokeScript` VM path
- Queues / Systems: existing GAS graph compilation and execution front door
- Resolvers / Registries: existing FuncLib registration/patching and graph registry
- Existing presets / graphs: existing graph asset model and control-flow lowering

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A；本任务只调整单次 Effect 阶段图的作者表达力，事务边界仍由现有 Effect 阶段执行负责。

### 6. Config SSOT

行为配置落在: graph / catalog（`assets/Configs/GAS/graphs.json`, `assets/Configs/GAS/func_lib.json` 及测试夹具）

是否新增 JSON schema: NO — 使用既有 `InvokeScript.functionName` 与 `BranchBool` 作者节点。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤

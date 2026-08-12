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

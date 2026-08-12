# GAS Composition Gate — LiveGasEditPipeline (#615 / #618–#622)

## Scope
Real Stage → Classify → Commit hot-apply for GAS Graph body + effect numeric + actor attribute commands.
Not ReloadConfigs. Not Clear+Register-all.

## Reuse
- `LiveEditSession` / `LiveDebugPatch` (#637)
- `GraphProgramAuthoringFrontDoor` + `GraphControlFlowCompiler`
- `GraphProgramRegistry` (add ReplaceProgram, same id/kind)
- `EffectTemplateRegistry` (add hot numeric field replace after finalization)
- `AttributeMutationOps` for Immediate attribute commands
- `CoreServiceKeys` typed service publish

## Op vs enum
No new EffectPresetType / BuiltinHandler. New apply-mode enum is the LSW contract classification axis (Immediate / NextCast / MapReload / EngineRestart), not a gameplay preset switch.

## Fail-closed
- Stage never clears live registries
- Graph id / kind change → EngineRestartRequired
- Unknown effect field path → MapReloadRequired (explicit, no silent skip)
- Commit of NextCast only via SafeFrame queue

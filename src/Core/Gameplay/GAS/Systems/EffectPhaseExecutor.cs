using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    /// <summary>
    /// Unified executor for Effect lifecycle phase graphs + builtin handlers.
    /// Executes the Pre/Main/Post three-stage pattern for any EffectPhaseId,
    /// then dispatches Phase Listeners (Step 4).
    ///
    /// Execution order per phase:
    ///   1. Pre  graph (user-defined, from EffectPhaseGraphBindings)
    ///   2. Main handler (preset-defined, from PresetTypeDefinition.DefaultPhaseHandlers):
    ///      - Builtin → BuiltinHandlerRegistry.Invoke(...)
    ///      - Graph   → execute graph program
    ///      - None    → skip
    ///      Skipped if SkipMain is set in the user's EffectPhaseGraphBindings.
    ///   3. Post graph (user-defined, from EffectPhaseGraphBindings)
    ///   4. Dispatch Phase Listeners (target buffer scope=Target, caster buffer scope=Source, global)
    ///
    /// All graphs share the same scratch registers (single-threaded).
    /// </summary>
    public sealed class EffectPhaseExecutor
    {
        private readonly GraphProgramRegistry _programs;
        private readonly PresetTypeRegistry _presetTypes;
        private readonly BuiltinHandlerRegistry _builtinHandlers;
        private readonly GasGraphOpHandlerTable _handlers;
        private readonly EffectTemplateRegistry _templates;
        private readonly GlobalPhaseListenerRegistry? _globalListeners;
        private readonly GameplayEventBus? _eventBus;
        private readonly GasBudget? _budget;

        // Scratch register arrays — reused across calls to avoid per-call allocations.
        private readonly float[] _floatRegs = new float[GraphVmLimits.MaxFloatRegisters];
        private readonly int[] _intRegs = new int[GraphVmLimits.MaxIntRegisters];
        private readonly byte[] _boolRegs = new byte[GraphVmLimits.MaxBoolRegisters];
        private readonly Entity[] _entityRegs = new Entity[GraphVmLimits.MaxEntityRegisters];
        private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];

        // Scratch buffer for collected listener actions
        private readonly PhaseListenerCollectedAction[] _collectedActions = new PhaseListenerCollectedAction[32];
        private int[] _scratchRegisterCountsByGraphId = new int[64];

        public EffectPhaseExecutor(
            GraphProgramRegistry programs,
            PresetTypeRegistry presetTypes,
            BuiltinHandlerRegistry builtinHandlers,
            GasGraphOpHandlerTable handlers,
            EffectTemplateRegistry templates,
            GlobalPhaseListenerRegistry? globalListeners = null,
            GameplayEventBus? eventBus = null,
            GasBudget? budget = null)
        {
            _programs = programs;
            _presetTypes = presetTypes;
            _builtinHandlers = builtinHandlers;
            _handlers = handlers;
            _templates = templates;
            _globalListeners = globalListeners;
            _eventBus = eventBus;
            _budget = budget;
        }

        /// <summary>
        /// Execute a single phase for an effect (overload without listener dispatch).
        /// </summary>
        public void ExecutePhase(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            Entity targetContext,
            IntVector2 targetPos,
            EffectPhaseId phase,
            in EffectPhaseGraphBindings behavior,
            EffectPresetType presetType,
            BuiltinHandlerExecutionContext? builtinRuntime = null)
        {
            EffectConfigParams mergedParams = default;
            ExecutePhase(world, api, caster, target, targetContext, targetPos, phase, behavior, presetType, effectTagId: 0, effectTemplateId: 0, in mergedParams, builtinRuntime);
        }

        /// <summary>
        /// Execute a single phase for an effect, including Phase Listener dispatch.
        /// </summary>
        public void ExecutePhase(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            Entity targetContext,
            IntVector2 targetPos,
            EffectPhaseId phase,
            in EffectPhaseGraphBindings behavior,
            EffectPresetType presetType,
            int effectTagId,
            int effectTemplateId,
            BuiltinHandlerExecutionContext? builtinRuntime = null)
        {
            EffectConfigParams mergedParams = default;
            ExecutePhase(world, api, caster, target, targetContext, targetPos, phase, behavior, presetType, effectTagId, effectTemplateId, in mergedParams, builtinRuntime);
        }

        public void ExecutePhase(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            Entity targetContext,
            IntVector2 targetPos,
            EffectPhaseId phase,
            in EffectPhaseGraphBindings behavior,
            EffectPresetType presetType,
            int effectTagId,
            int effectTemplateId,
            in EffectConfigParams mergedParams,
            BuiltinHandlerExecutionContext? builtinRuntime = null,
            uint randomSeed = 0,
            int rootId = 0)
        {
            byte validationResult = 0;
            ExecutePhase(
                world,
                api,
                caster,
                target,
                targetContext,
                targetPos,
                phase,
                in behavior,
                presetType,
                effectTagId,
                effectTemplateId,
                in mergedParams,
                builtinRuntime,
                randomSeed,
                rootId,
                trackValidationResult: false,
                ref validationResult);
        }

        private void ExecutePhase(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            Entity targetContext,
            IntVector2 targetPos,
            EffectPhaseId phase,
            in EffectPhaseGraphBindings behavior,
            EffectPresetType presetType,
            int effectTagId,
            int effectTemplateId,
            in EffectConfigParams mergedParams,
            BuiltinHandlerExecutionContext? builtinRuntime,
            uint randomSeed,
            int rootId,
            bool trackValidationResult,
            ref byte validationResult)
        {
            // ① Pre graph (user-defined)
            int preGraphId = behavior.GetGraphId(phase, PhaseSlot.Pre);
            if (preGraphId > 0)
            {
                ExecuteGraph(world, api, caster, target, targetContext, targetPos, preGraphId, effectTemplateId, phase, in mergedParams, builtinRuntime, randomSeed, rootId, trackValidationResult, ref validationResult);
            }

            // ② Main handler (unless SkipMain)
            if (!behavior.IsSkipMain(phase))
            {
                ExecuteMainHandler(world, api, caster, target, targetContext, targetPos, phase, presetType, effectTemplateId, in mergedParams, builtinRuntime, randomSeed, rootId, trackValidationResult, ref validationResult);
            }

            // ③ Post graph (user-defined)
            int postGraphId = behavior.GetGraphId(phase, PhaseSlot.Post);
            if (postGraphId > 0)
            {
                ExecuteGraph(world, api, caster, target, targetContext, targetPos, postGraphId, effectTemplateId, phase, in mergedParams, builtinRuntime, randomSeed, rootId, trackValidationResult, ref validationResult);
            }

            // ④ Dispatch Phase Listeners
            if (effectTagId != 0 || effectTemplateId != 0)
            {
                DispatchListeners(world, api, caster, target, targetContext, targetPos, phase, effectTagId, effectTemplateId, randomSeed, rootId, trackValidationResult, ref validationResult);
            }
        }

        /// <summary>
        /// Execute a phase graph and return whether validation convention B[0] remains set (1 = pass).
        /// </summary>
        public bool ExecutePhaseWithValidationResult(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            Entity targetContext,
            IntVector2 targetPos,
            EffectPhaseId phase,
            in EffectPhaseGraphBindings behavior,
            EffectPresetType presetType,
            int effectTagId,
            int effectTemplateId,
            in EffectConfigParams mergedParams,
            BuiltinHandlerExecutionContext? builtinRuntime = null,
            uint randomSeed = 0,
            int rootId = 0)
        {
            byte validationResult = 1;
            ExecutePhase(
                world,
                api,
                caster,
                target,
                targetContext,
                targetPos,
                phase,
                behavior,
                presetType,
                effectTagId,
                effectTemplateId,
                in mergedParams,
                builtinRuntime,
                randomSeed,
                rootId,
                trackValidationResult: true,
                ref validationResult);
            return validationResult != 0;
        }

        /// <summary>
        /// Execute the Main handler for a phase based on PresetTypeDefinition.
        /// </summary>
        private void ExecuteMainHandler(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            Entity targetContext,
            IntVector2 targetPos,
            EffectPhaseId phase,
            EffectPresetType presetType,
            int effectTemplateId,
            in EffectConfigParams mergedParams,
            BuiltinHandlerExecutionContext? builtinRuntime,
            uint randomSeed,
            int rootId,
            bool trackValidationResult,
            ref byte validationResult)
        {
            if (!_presetTypes.IsRegistered(presetType)) return;

            ref readonly var def = ref _presetTypes.Get(presetType);
            var handler = def.DefaultPhaseHandlers[phase];

            if (!handler.IsValid) return;

            switch (handler.Kind)
            {
                case PhaseHandlerKind.Builtin:
                {
                    if (!_templates.TryGetRef(effectTemplateId, out int tplIdx))
                    {
                        throw new InvalidOperationException(
                            $"EffectPhaseExecutor: Builtin handler for phase {phase} requires template {effectTemplateId}, but it is not registered.");
                    }
                    ref readonly var tplData = ref _templates.GetRef(tplIdx);
                    var context = new EffectContext { RootId = rootId, Source = caster, Target = target, TargetContext = targetContext };
                    var builtinParams = mergedParams.Count > 0 ? mergedParams : tplData.ConfigParams;
                    _builtinHandlers.Invoke(
                        (BuiltinHandlerId)handler.HandlerId,
                        world, default, ref context, in builtinParams, in tplData, builtinRuntime);
                    break;
                }
                case PhaseHandlerKind.Graph:
                {
                    ExecuteGraph(world, api, caster, target, targetContext, targetPos, handler.HandlerId, effectTemplateId, phase, in mergedParams, builtinRuntime, randomSeed, rootId, trackValidationResult, ref validationResult);
                    break;
                }
            }
        }

        /// <summary>
        /// Step 4: Dispatch phase listeners from target buffer, caster buffer, and global registry.
        /// </summary>
        private void DispatchListeners(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            Entity targetContext,
            IntVector2 targetPos,
            EffectPhaseId phase,
            int effectTagId,
            int effectTemplateId,
            uint randomSeed,
            int rootId,
            bool trackValidationResult,
            ref byte validationResult)
        {
            Span<PhaseListenerCollectedAction> scratch = _collectedActions;
            int totalCollected = 0;
            int totalDropped = 0;

            // a. Target entity's buffer (scope = Target)
            if (world.IsAlive(target) && world.Has<EffectPhaseListenerBuffer>(target))
            {
                ref var buf = ref world.Get<EffectPhaseListenerBuffer>(target);
                int n = buf.Collect(effectTagId, effectTemplateId, phase, PhaseListenerScope.Target, scratch.Slice(totalCollected), out int dropped);
                totalCollected += n;
                totalDropped += dropped;
            }

            // b. Caster entity's buffer (scope = Source)
            if (world.IsAlive(caster) && world.Has<EffectPhaseListenerBuffer>(caster))
            {
                ref var buf = ref world.Get<EffectPhaseListenerBuffer>(caster);
                int n = buf.Collect(effectTagId, effectTemplateId, phase, PhaseListenerScope.Source, scratch.Slice(totalCollected), out int dropped);
                totalCollected += n;
                totalDropped += dropped;
            }

            // c. Global listeners
            if (_globalListeners != null)
            {
                int n = _globalListeners.Collect(phase, effectTagId, effectTemplateId, scratch.Slice(totalCollected), out int dropped);
                totalCollected += n;
                totalDropped += dropped;
            }

            if (totalDropped > 0 && _budget != null)
            {
                _budget.PhaseListenerDispatchDropped += totalDropped;
            }

            if (totalCollected == 0) return;

            // If buffer is full, some listeners may have been truncated (budget mode).
            // No Console.WriteLine to avoid GC allocation in hot path.

            // Sort by priority descending (higher = earlier)
            var actions = scratch.Slice(0, totalCollected);
            SortByPriorityDescending(actions);

            // Execute
            for (int i = 0; i < actions.Length; i++)
            {
                ref var action = ref actions[i];

                if ((action.Flags & PhaseListenerActionFlags.ExecuteGraph) != 0 && action.GraphProgramId > 0)
                {
                    ExecuteGraph(world, api, caster, target, targetContext, targetPos, action.GraphProgramId, effectTemplateId, phase, default, null, randomSeed, rootId, trackValidationResult, ref validationResult);
                }

                if ((action.Flags & PhaseListenerActionFlags.PublishEvent) != 0 && action.EventTagId != 0 && _eventBus != null)
                {
                    _eventBus.Publish(new GameplayEvent
                    {
                        TagId = action.EventTagId,
                        Source = caster,
                        Target = target,
                        Magnitude = 0f,
                    });
                }
            }
        }

        private static void SortByPriorityDescending(Span<PhaseListenerCollectedAction> actions)
        {
            for (int i = 1; i < actions.Length; i++)
            {
                var key = actions[i];
                int j = i - 1;
                while (j >= 0 && actions[j].Priority < key.Priority)
                {
                    actions[j + 1] = actions[j];
                    j--;
                }
                actions[j + 1] = key;
            }
        }

        /// <summary>
        /// Dispatch Phase Listeners only (skip Pre/Main/Post graph execution).
        /// Used by the pure-instant fast path in EffectProposalProcessingSystem:
        /// modifiers are applied inline, but Listeners still need to fire for
        /// observability (e.g. "whenever damage is dealt" triggers).
        /// </summary>
        public void DispatchPhaseListeners(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            Entity targetContext,
            IntVector2 targetPos,
            EffectPhaseId phase,
            int effectTagId,
            int effectTemplateId)
        {
            byte validationResult = 0;
            DispatchListeners(world, api, caster, target, targetContext, targetPos, phase, effectTagId, effectTemplateId, 0, rootId: 0, trackValidationResult: false, ref validationResult);
        }

        /// <summary>
        /// Execute a single graph program by ID.
        /// </summary>
        public void ExecuteGraph(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            Entity targetContext,
            IntVector2 targetPos,
            int graphProgramId)
        {
            byte validationResult = 0;
            ExecuteGraph(world, api, caster, target, targetContext, targetPos, graphProgramId, 0, EffectPhaseId.OnApply, default, null, 0, rootId: 0, trackValidationResult: false, ref validationResult);
        }

        private void ExecuteGraph(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            Entity targetContext,
            IntVector2 targetPos,
            int graphProgramId,
            int effectTemplateId,
            EffectPhaseId phase,
            in EffectConfigParams mergedParams,
            BuiltinHandlerExecutionContext? builtinRuntime,
            uint randomSeed,
            int rootId,
            bool trackValidationResult,
            ref byte validationResult)
        {
            if (graphProgramId <= 0) return;
            if (!_programs.TryGetProgram(graphProgramId, out var program))
            {
                throw new InvalidOperationException(
                    $"EffectPhaseExecutor references missing graphId={graphProgramId} for phase {phase} and effectTemplateId={effectTemplateId}.");
            }
            if (program.Length == 0) return;

            var scratchUsage = GetScratchUsage(graphProgramId, program);
            if (scratchUsage.RegisterCount > 0)
            {
                Array.Clear(_floatRegs, 0, scratchUsage.RegisterCount);
                Array.Clear(_intRegs, 0, scratchUsage.RegisterCount);
                Array.Clear(_boolRegs, 0, scratchUsage.RegisterCount);
                Array.Clear(_entityRegs, 0, scratchUsage.RegisterCount);
            }

            if (trackValidationResult)
            {
                _boolRegs[0] = validationResult;
            }

            // Set up fixed entity registers: E[0]=Caster, E[1]=Target, E[2]=TargetContext
            _entityRegs[0] = caster;
            _entityRegs[1] = target;
            _entityRegs[2] = targetContext;

            var targetList = new GraphTargetList(_targets);

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = target,
                TargetContext = targetContext,
                TargetPosCm = targetPos,
                RandomSeed = BuildRandomSeed(caster, target, targetContext, graphProgramId, effectTemplateId, phase, randomSeed),
                Api = api,
                F = _floatRegs,
                I = _intRegs,
                B = _boolRegs,
                E = _entityRegs,
                Targets = _targets,
                TargetList = targetList,
            };

            GasGraphRuntimeApi? graphHost = api as GasGraphRuntimeApi;
            bool ownsBuiltinInvocation = false;
            if (graphHost != null && effectTemplateId > 0)
            {
                EffectConfigParams builtinParams = mergedParams;
                if (builtinParams.Count == 0 && _templates.TryGetRef(effectTemplateId, out int tplIdx))
                {
                    builtinParams = _templates.GetRef(tplIdx).ConfigParams;
                }

                graphHost.BeginBuiltinInvocation(
                    _builtinHandlers,
                    _templates,
                    builtinRuntime,
                    effectTemplateId,
                    new EffectContext { RootId = rootId, Source = caster, Target = target, TargetContext = targetContext },
                    in builtinParams);
                ownsBuiltinInvocation = true;
            }

            try
            {
                GasGraphOpHandlerTable.Execute(ref state, program, _handlers);
                if (trackValidationResult && _boolRegs[0] == 0)
                {
                    validationResult = 0;
                }
            }
            finally
            {
                if (ownsBuiltinInvocation)
                {
                    graphHost!.EndBuiltinInvocation();
                }
            }
        }

        private static uint BuildRandomSeed(
            Entity caster,
            Entity target,
            Entity targetContext,
            int graphProgramId,
            int effectTemplateId,
            EffectPhaseId phase,
            uint executionSeed)
        {
            uint hash = executionSeed == 0u ? 2166136261u : executionSeed;
            hash = Mix(hash, caster.Id);
            hash = Mix(hash, caster.Version);
            hash = Mix(hash, target.Id);
            hash = Mix(hash, target.Version);
            hash = Mix(hash, targetContext.Id);
            hash = Mix(hash, targetContext.Version);
            hash = Mix(hash, graphProgramId);
            hash = Mix(hash, effectTemplateId);
            hash = Mix(hash, (int)phase);
            return hash == 0u ? 1u : hash;
        }

        private static uint Mix(uint hash, int value)
        {
            return (hash ^ unchecked((uint)value)) * 16777619u;
        }

        private ScratchUsage GetScratchUsage(int graphProgramId, ReadOnlySpan<GraphInstruction> program)
        {
            if ((uint)graphProgramId < (uint)_scratchRegisterCountsByGraphId.Length)
            {
                int cachedCountPlusOne = _scratchRegisterCountsByGraphId[graphProgramId];
                if (cachedCountPlusOne != 0)
                {
                    return new ScratchUsage(cachedCountPlusOne - 1);
                }
            }

            var usage = AnalyzeScratchUsage(program);
            EnsureScratchUsageCapacity(graphProgramId);
            _scratchRegisterCountsByGraphId[graphProgramId] = usage.RegisterCount + 1;
            return usage;
        }

        private void EnsureScratchUsageCapacity(int graphProgramId)
        {
            if ((uint)graphProgramId < (uint)_scratchRegisterCountsByGraphId.Length)
            {
                return;
            }

            int newSize = _scratchRegisterCountsByGraphId.Length;
            while (newSize <= graphProgramId)
            {
                newSize <<= 1;
            }

            Array.Resize(ref _scratchRegisterCountsByGraphId, newSize);
        }

        private static ScratchUsage AnalyzeScratchUsage(ReadOnlySpan<GraphInstruction> program)
        {
            int maxRegisterIndex = -1;
            for (int i = 0; i < program.Length; i++)
            {
                ref readonly var instruction = ref program[i];
                maxRegisterIndex = Math.Max(maxRegisterIndex, instruction.Dst);
                maxRegisterIndex = Math.Max(maxRegisterIndex, instruction.A);
                maxRegisterIndex = Math.Max(maxRegisterIndex, instruction.B);
                maxRegisterIndex = Math.Max(maxRegisterIndex, instruction.C);
            }

            if (maxRegisterIndex < 0)
            {
                return default;
            }

            int registerCount = maxRegisterIndex + 1;
            if (registerCount > GraphVmLimits.MaxFloatRegisters)
            {
                registerCount = GraphVmLimits.MaxFloatRegisters;
            }

            return new ScratchUsage(registerCount);
        }

        private readonly record struct ScratchUsage(int RegisterCount);
    }
}

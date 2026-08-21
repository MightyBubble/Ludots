using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

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
        public const string PhaseListenerDispatchCapacityExceededError = "GAS.PHASE_LISTENER.ERR.DispatchCapacityExceeded";
        public const string ExternalAtomicListenerConflictError = "GAS.EFFECT_PLAN.ERR.ExternalAtomicListenerConflict";
        public const string GraphProgramScratchCapacityExceededError = "GAS.EFFECT_PHASE.ERR.GraphProgramScratchCapacityExceeded";
        public const string ValidationEntryPointRequiredError = "GAS.EFFECT_PHASE.ERR.ValidationEntryPointRequired";
        public const string UnexpectedValidationEntryPointError = "GAS.EFFECT_PHASE.ERR.UnexpectedValidationEntryPoint";
        public const string MissingListenerEventBusError = "GAS.PHASE_LISTENER.ERR.MissingEventBus";
        public const string ListenerTransactionRequiredError = "GAS.PHASE_LISTENER.ERR.TransactionRequired";
        public const int DefaultGraphProgramScratchCapacity = 16384;
        private const int PhaseListenerDispatchScratchCapacity =
            EffectPhaseListenerBuffer.CAPACITY * 2 + GlobalPhaseListenerRegistry.MAX_LISTENERS;

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
        private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];

        // Scratch buffer for collected listener actions
        private readonly PhaseListenerCollectedAction[] _collectedActions = new PhaseListenerCollectedAction[PhaseListenerDispatchScratchCapacity];
        private readonly int _graphProgramScratchCapacity;

        public EffectPhaseExecutor(
            GraphProgramRegistry programs,
            PresetTypeRegistry presetTypes,
            BuiltinHandlerRegistry builtinHandlers,
            GasGraphOpHandlerTable handlers,
            EffectTemplateRegistry templates,
            GlobalPhaseListenerRegistry? globalListeners = null,
            GameplayEventBus? eventBus = null,
            GasBudget? budget = null,
            int graphProgramScratchCapacity = DefaultGraphProgramScratchCapacity)
        {
            if (graphProgramScratchCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(graphProgramScratchCapacity),
                    graphProgramScratchCapacity,
                    "Graph program scratch capacity must be positive.");
            }

            _programs = programs;
            _presetTypes = presetTypes;
            _builtinHandlers = builtinHandlers;
            _handlers = handlers;
            _templates = templates;
            _globalListeners = globalListeners;
            _eventBus = eventBus;
            _budget = budget;
            _graphProgramScratchCapacity = graphProgramScratchCapacity;
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
            ExecutePhase(world, api, caster, target, targetContext, targetPos, phase, behavior, (byte)presetType, effectTagId: 0, effectTemplateId: 0, in mergedParams, builtinRuntime);
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
            ExecutePhase(world, api, caster, target, targetContext, targetPos, phase, behavior, (byte)presetType, effectTagId, effectTemplateId, in mergedParams, builtinRuntime);
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
            ExecutePhase(
                world,
                api,
                caster,
                target,
                targetContext,
                targetPos,
                phase,
                in behavior,
                (byte)presetType,
                effectTagId,
                effectTemplateId,
                in mergedParams,
                builtinRuntime,
                randomSeed,
                rootId);
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
            int presetTypeId,
            int effectTagId,
            int effectTemplateId,
            in EffectConfigParams mergedParams,
            BuiltinHandlerExecutionContext? builtinRuntime = null,
            uint randomSeed = 0,
            int rootId = 0)
        {
            byte validationResult = 0;
            ExecutePhaseInConfigScope(
                world,
                api,
                caster,
                target,
                targetContext,
                targetPos,
                phase,
                in behavior,
                presetTypeId,
                effectTagId,
                effectTemplateId,
                in mergedParams,
                builtinRuntime,
                randomSeed,
                rootId,
                trackValidationResult: false,
                ref validationResult);
        }

        private void ExecutePhaseInConfigScope(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            Entity targetContext,
            IntVector2 targetPos,
            EffectPhaseId phase,
            in EffectPhaseGraphBindings behavior,
            int presetTypeId,
            int effectTagId,
            int effectTemplateId,
            in EffectConfigParams mergedParams,
            BuiltinHandlerExecutionContext? builtinRuntime,
            uint randomSeed,
            int rootId,
            bool trackValidationResult,
            ref byte validationResult)
        {
            RequireExecutionMode(phase, trackValidationResult);
            GasGraphRuntimeApi? graphHost = api as GasGraphRuntimeApi;
            graphHost?.SetConfigContext(in mergedParams);
            try
            {
                ExecutePhaseCore(
                    world,
                    api,
                    caster,
                    target,
                    targetContext,
                    targetPos,
                    phase,
                    in behavior,
                    presetTypeId,
                    effectTagId,
                    effectTemplateId,
                    in mergedParams,
                    builtinRuntime,
                    randomSeed,
                    rootId,
                    trackValidationResult,
                    ref validationResult);
            }
            finally
            {
                graphHost?.ClearConfigContext();
            }
        }

        private void ExecutePhaseCore(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            Entity targetContext,
            IntVector2 targetPos,
            EffectPhaseId phase,
            in EffectPhaseGraphBindings behavior,
            int presetTypeId,
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
            int listenerActionCount = effectTagId != 0 || effectTemplateId != 0
                ? CollectAndPreflightListeners(world, api, caster, target, phase, effectTagId, effectTemplateId)
                : 0;

            int preGraphId = behavior.GetGraphId(phase, PhaseSlot.Pre);
            if (preGraphId > 0)
            {
                ExecuteGraph(world, api, caster, target, targetContext, targetPos, preGraphId, effectTemplateId, phase, in mergedParams, builtinRuntime, randomSeed, rootId, trackValidationResult, ref validationResult);
            }

            // A template-owned Main graph is authoritative. PresetType only supplies
            // authoring sugar when the concrete template omits Main.
            int mainGraphId = behavior.GetGraphId(phase, PhaseSlot.Main);
            if (mainGraphId > 0)
            {
                ExecuteGraph(world, api, caster, target, targetContext, targetPos, mainGraphId, effectTemplateId, phase, in mergedParams, builtinRuntime, randomSeed, rootId, trackValidationResult, ref validationResult);
            }
            else if (!behavior.IsSkipMain(phase))
            {
                ExecuteMainHandler(world, api, caster, target, targetContext, targetPos, phase, presetTypeId, effectTemplateId, in mergedParams, builtinRuntime, randomSeed, rootId, trackValidationResult, ref validationResult);
            }

            // ③ Post graph (user-defined)
            int postGraphId = behavior.GetGraphId(phase, PhaseSlot.Post);
            if (postGraphId > 0)
            {
                ExecuteGraph(world, api, caster, target, targetContext, targetPos, postGraphId, effectTemplateId, phase, in mergedParams, builtinRuntime, randomSeed, rootId, trackValidationResult, ref validationResult);
            }

            // ④ Dispatch Phase Listeners
            if (listenerActionCount > 0)
            {
                ExecuteCollectedListeners(
                    world,
                    api,
                    caster,
                    target,
                    targetContext,
                    targetPos,
                    phase,
                    effectTemplateId,
                    randomSeed,
                    rootId,
                    trackValidationResult,
                    listenerActionCount,
                    ref validationResult);
            }
        }

        /// <summary>
        /// Execute a phase and return whether validation convention B[0] remains pass.
        /// Vacant phases (no validating graph work) pass. Each executed validating graph
        /// is seeded fail-closed (B[0]=0) and must explicitly write B[0]=1 to affirm.
        /// Rejection is sticky across Pre/Main/Post/listener graphs in the phase.
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
            return ExecutePhaseWithValidationResult(
                world,
                api,
                caster,
                target,
                targetContext,
                targetPos,
                phase,
                in behavior,
                (byte)presetType,
                effectTagId,
                effectTemplateId,
                in mergedParams,
                builtinRuntime,
                randomSeed,
                rootId);
        }

        public bool ExecutePhaseWithValidationResult(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            Entity targetContext,
            IntVector2 targetPos,
            EffectPhaseId phase,
            in EffectPhaseGraphBindings behavior,
            int presetTypeId,
            int effectTagId,
            int effectTemplateId,
            in EffectConfigParams mergedParams,
            BuiltinHandlerExecutionContext? builtinRuntime = null,
            uint randomSeed = 0,
            int rootId = 0)
        {
            byte validationResult = 1;
            ExecutePhaseInConfigScope(
                world,
                api,
                caster,
                target,
                targetContext,
                targetPos,
                phase,
                in behavior,
                presetTypeId,
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
            int presetTypeId,
            int effectTemplateId,
            in EffectConfigParams mergedParams,
            BuiltinHandlerExecutionContext? builtinRuntime,
            uint randomSeed,
            int rootId,
            bool trackValidationResult,
            ref byte validationResult)
        {
            if (!_presetTypes.IsRegistered(presetTypeId))
            {
                if (presetTypeId == 0)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"EffectPhaseExecutor: effectTemplateId={effectTemplateId} phase={phase} references unregistered presetTypeId={presetTypeId}.");
            }

            ref readonly var def = ref _presetTypes.Get(presetTypeId);
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
                        handler.HandlerId,
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
        /// Collect, sort, and certify the full listener batch before any phase work executes.
        /// </summary>
        private int CollectAndPreflightListeners(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            EffectPhaseId phase,
            int effectTagId,
            int effectTemplateId)
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

            if (totalDropped > 0)
            {
                throw new InvalidOperationException(
                    $"{PhaseListenerDispatchCapacityExceededError}: capacity={scratch.Length}, dropped={totalDropped}, phase={(int)phase}, effectTagId={effectTagId}, effectTemplateId={effectTemplateId}.");
            }

            if (totalCollected == 0) return 0;

            // If buffer is full, some listeners may have been truncated (budget mode).
            // No Console.WriteLine to avoid GC allocation in hot path.

            // Sort by priority descending (higher = earlier)
            var actions = scratch.Slice(0, totalCollected);
            SortByPriorityDescending(actions);

            for (int i = 0; i < actions.Length; i++)
            {
                ref readonly var action = ref actions[i];
                EffectPhaseListenerContract.RequireValidAction(
                    phase,
                    action.Flags,
                    action.GraphProgramId,
                    action.EventTagId);
                if ((action.Flags & PhaseListenerActionFlags.PublishEvent) != 0)
                {
                    if (api is GasGraphRuntimeApi graphHost && graphHost.HasActiveEffectSideEffectTransaction)
                    {
                        if (!graphHost.HasGameplayEventBus)
                        {
                            throw new InvalidOperationException(
                                $"{MissingListenerEventBusError}: listenerActionIndex={i} requires the transaction-bound graph runtime to provide a GameplayEventBus.");
                        }
                    }
                    else
                    {
                        _ = RequireListenerEventBus();
                    }
                }
                if ((action.Flags & PhaseListenerActionFlags.ExecuteGraph) != 0)
                {
                    PreflightListenerGraph(action.GraphProgramId, phase, effectTemplateId, i, api);
                }
            }

            if (!EffectPhaseListenerContract.IsPurePhase(phase) &&
                (api is not GasGraphRuntimeApi transactionalHost ||
                 !transactionalHost.HasActiveEffectSideEffectTransaction))
            {
                throw new InvalidOperationException(
                    $"{ListenerTransactionRequiredError}: phase={phase}, effectTemplateId={effectTemplateId}, listenerActionCount={totalCollected}.");
            }

            return totalCollected;
        }

        private void ExecuteCollectedListeners(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            Entity targetContext,
            IntVector2 targetPos,
            EffectPhaseId phase,
            int effectTemplateId,
            uint randomSeed,
            int rootId,
            bool trackValidationResult,
            int actionCount,
            ref byte validationResult)
        {
            Span<PhaseListenerCollectedAction> actions = _collectedActions.AsSpan(0, actionCount);
            for (int i = 0; i < actions.Length; i++)
            {
                ref var action = ref actions[i];

                if ((action.Flags & PhaseListenerActionFlags.ExecuteGraph) != 0)
                {
                    ExecuteGraph(
                        world,
                        api,
                        caster,
                        target,
                        targetContext,
                        targetPos,
                        action.GraphProgramId,
                        effectTemplateId,
                        phase,
                        default,
                        null,
                        randomSeed,
                        rootId,
                        trackValidationResult,
                        ref validationResult,
                        requireListenerCompatibility: true);
                }

                if ((action.Flags & PhaseListenerActionFlags.PublishEvent) != 0)
                {
                    ((GasGraphRuntimeApi)api).SendEvent(caster, target, action.EventTagId, 0f);
                }
            }
        }

        private void PreflightListenerGraph(
            int graphProgramId,
            EffectPhaseId phase,
            int effectTemplateId,
            int listenerActionIndex,
            IGraphRuntimeApi api)
        {
            if (!_programs.TryGetProgram(graphProgramId, out ReadOnlySpan<GraphInstruction> program))
            {
                throw new InvalidOperationException(
                    $"EffectPhaseExecutor listener preflight references missing graphId={graphProgramId} for phase {phase}, effectTemplateId={effectTemplateId}, listenerActionIndex={listenerActionIndex}.");
            }

            GraphKind expectedKind = EffectPhaseListenerContract.GetRequiredGraphKind(phase);
            try
            {
                _programs.RequireKind(graphProgramId, expectedKind);
                GraphKindOperationPolicy.RequireListenerCompatible(
                    expectedKind,
                    program,
                    _handlers,
                    EffectPhaseListenerContract.IsPurePhase(phase),
                    graphProgramId,
                    nameof(EffectPhaseExecutor));
                RequireListenerGraphServices(graphProgramId, program, api);
                _ = GetScratchUsage(graphProgramId, program);
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException(
                    $"{error.Message} Listener preflight context: phase={phase}, effectTemplateId={effectTemplateId}, listenerActionIndex={listenerActionIndex}.",
                    error);
            }
        }

        private GameplayEventBus RequireListenerEventBus()
            => _eventBus ?? throw new InvalidOperationException(MissingListenerEventBusError);

        private static void RequireListenerGraphServices(
            int graphProgramId,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api)
        {
            for (int instructionIndex = 0; instructionIndex < program.Length; instructionIndex++)
            {
                if ((GraphNodeOp)program[instructionIndex].Op != GraphNodeOp.SendEvent)
                {
                    continue;
                }

                if (api is GasGraphRuntimeApi { HasGameplayEventBus: true })
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"{MissingListenerEventBusError}: graphId={graphProgramId}, instructionIndex={instructionIndex} uses SendEvent but the graph runtime has no GameplayEventBus.");
            }
        }

        public bool HasMatchingListener(
            World world,
            Entity caster,
            Entity target,
            EffectPhaseId phase,
            int effectTagId,
            int effectTemplateId)
        {
            if (world.IsAlive(target) &&
                world.Has<EffectPhaseListenerBuffer>(target) &&
                world.Get<EffectPhaseListenerBuffer>(target).HasMatch(
                    effectTagId, effectTemplateId, phase, PhaseListenerScope.Target))
            {
                return true;
            }

            if (world.IsAlive(caster) &&
                world.Has<EffectPhaseListenerBuffer>(caster) &&
                world.Get<EffectPhaseListenerBuffer>(caster).HasMatch(
                    effectTagId, effectTemplateId, phase, PhaseListenerScope.Source))
            {
                return true;
            }

            return _globalListeners?.HasMatch(phase, effectTagId, effectTemplateId) == true;
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
            RequireExecutionMode(phase, trackValidationResult: false);
            byte validationResult = 0;
            int actionCount = CollectAndPreflightListeners(
                world,
                api,
                caster,
                target,
                phase,
                effectTagId,
                effectTemplateId);
            ExecuteCollectedListeners(
                world,
                api,
                caster,
                target,
                targetContext,
                targetPos,
                phase,
                effectTemplateId,
                0,
                0,
                false,
                actionCount,
                ref validationResult);
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
            ref byte validationResult,
            bool requireListenerCompatibility = false)
        {
            if (graphProgramId <= 0) return;
            if (!_programs.TryGetProgram(graphProgramId, out var program))
            {
                throw new InvalidOperationException(
                    $"EffectPhaseExecutor references missing graphId={graphProgramId} for phase {phase} and effectTemplateId={effectTemplateId}.");
            }

            RequireExecutionMode(phase, trackValidationResult);
            GraphKind expectedKind = EffectPhaseListenerContract.GetRequiredGraphKind(phase);
            _programs.RequireKind(graphProgramId, expectedKind);
            if (requireListenerCompatibility)
            {
                GraphKindOperationPolicy.RequireListenerCompatible(
                    expectedKind,
                    program,
                    _handlers,
                    EffectPhaseListenerContract.IsPurePhase(phase),
                    graphProgramId,
                    nameof(EffectPhaseExecutor));
            }
            else
            {
                GraphKindOperationPolicy.RequireAllowed(
                    expectedKind,
                    program,
                    _handlers,
                    graphProgramId,
                    nameof(EffectPhaseExecutor));
            }

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
                // Fail-closed per graph: do not inherit a prior pass into B[0].
                _boolRegs[0] = 0;
            }

            // Set up fixed entity registers: E[0]=Caster, E[1]=Target, E[2]=TargetContext
            _entityRegs[0] = caster;
            _entityRegs[1] = target;
            _entityRegs[2] = targetContext;

            Array.Clear(_callStack, 0, _callStack.Length);
            GraphFrame frame = GraphFrame.Bind(
                expectedKind,
                GraphEntityPreset.TargetContext(targetContext),
                world,
                caster,
                target,
                targetPos,
                api,
                _programs,
                _floatRegs,
                _intRegs,
                _boolRegs,
                _entityRegs,
                _targets,
                _callStack,
                randomSeed: BuildRandomSeed(caster, target, targetContext, graphProgramId, effectTemplateId, phase, randomSeed));

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
                GraphExecutor.Execute(ref frame, program, programAlreadyValidated: true);
                // Sticky reject: a later graph may affirm B[0]=1, but cannot clear an earlier reject.
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

        private static void RequireExecutionMode(EffectPhaseId phase, bool trackValidationResult)
        {
            if (phase == EffectPhaseId.OnPropose && !trackValidationResult)
            {
                throw new InvalidOperationException(
                    $"{ValidationEntryPointRequiredError}: OnPropose must execute through ExecutePhaseWithValidationResult.");
            }
            if (phase != EffectPhaseId.OnPropose && trackValidationResult)
            {
                throw new InvalidOperationException(
                    $"{UnexpectedValidationEntryPointError}: validation result tracking is only valid for OnPropose; phase={phase}.");
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
            var hash = Ludots.Core.Engine.Randomization.RngSeed.Begin(executionSeed);
            hash = Ludots.Core.Engine.Randomization.RngSeed.Mix(hash, caster.Id);
            hash = Ludots.Core.Engine.Randomization.RngSeed.Mix(hash, caster.Version);
            hash = Ludots.Core.Engine.Randomization.RngSeed.Mix(hash, target.Id);
            hash = Ludots.Core.Engine.Randomization.RngSeed.Mix(hash, target.Version);
            hash = Ludots.Core.Engine.Randomization.RngSeed.Mix(hash, targetContext.Id);
            hash = Ludots.Core.Engine.Randomization.RngSeed.Mix(hash, targetContext.Version);
            hash = Ludots.Core.Engine.Randomization.RngSeed.Mix(hash, graphProgramId);
            hash = Ludots.Core.Engine.Randomization.RngSeed.Mix(hash, effectTemplateId);
            hash = Ludots.Core.Engine.Randomization.RngSeed.Mix(hash, (int)phase);
            return Ludots.Core.Engine.Randomization.RngSeed.Finalize(hash);
        }

        private ScratchUsage GetScratchUsage(int graphProgramId, ReadOnlySpan<GraphInstruction> program)
        {
            EnsureScratchUsageCapacity(graphProgramId);
            return AnalyzeScratchUsage(graphProgramId, program);
        }

        private void EnsureScratchUsageCapacity(int graphProgramId)
        {
            if ((uint)graphProgramId < (uint)_graphProgramScratchCapacity)
            {
                return;
            }

            throw new InvalidOperationException(
                $"{GraphProgramScratchCapacityExceededError}: graphProgramId={graphProgramId}, capacity={_graphProgramScratchCapacity}.");
        }

        private static ScratchUsage AnalyzeScratchUsage(
            int graphProgramId,
            ReadOnlySpan<GraphInstruction> program)
        {
            int maxRegisterIndex = -1;
            for (int i = 0; i < program.Length; i++)
            {
                ref readonly var instruction = ref program[i];
                RequireRegisterIndex(graphProgramId, i, nameof(GraphInstruction.Dst), instruction.Dst);
                RequireRegisterIndex(graphProgramId, i, nameof(GraphInstruction.A), instruction.A);
                RequireRegisterIndex(graphProgramId, i, nameof(GraphInstruction.B), instruction.B);
                RequireRegisterIndex(graphProgramId, i, nameof(GraphInstruction.C), instruction.C);
                maxRegisterIndex = Math.Max(maxRegisterIndex, instruction.Dst);
                maxRegisterIndex = Math.Max(maxRegisterIndex, instruction.A);
                maxRegisterIndex = Math.Max(maxRegisterIndex, instruction.B);
                maxRegisterIndex = Math.Max(maxRegisterIndex, instruction.C);
            }

            if (maxRegisterIndex < 0)
            {
                return default;
            }

            return new ScratchUsage(maxRegisterIndex + 1);
        }

        private static void RequireRegisterIndex(
            int graphProgramId,
            int instructionIndex,
            string operand,
            byte registerIndex)
        {
            if (registerIndex < GraphVmLimits.MaxFloatRegisters)
            {
                return;
            }

            throw new InvalidOperationException(
                $"{GraphProgramScratchCapacityExceededError}: graphProgramId={graphProgramId}, instructionIndex={instructionIndex}, operand={operand}, registerIndex={registerIndex}, capacity={GraphVmLimits.MaxFloatRegisters}.");
        }

        private readonly record struct ScratchUsage(int RegisterCount);
    }
}

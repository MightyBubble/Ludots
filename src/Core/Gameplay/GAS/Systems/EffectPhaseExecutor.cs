using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
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

        // Scratch register arrays — reused across calls to avoid per-call allocations.
        private readonly float[] _floatRegs = new float[GraphVmLimits.MaxFloatRegisters];
        private readonly int[] _intRegs = new int[GraphVmLimits.MaxIntRegisters];
        private readonly byte[] _boolRegs = new byte[GraphVmLimits.MaxBoolRegisters];
        private readonly Entity[] _entityRegs = new Entity[GraphVmLimits.MaxEntityRegisters];
        private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];

        // Scratch buffer for collected listener actions
        private readonly PhaseListenerCollectedAction[] _collectedActions = new PhaseListenerCollectedAction[32];

        public EffectPhaseExecutor(
            GraphProgramRegistry programs,
            PresetTypeRegistry presetTypes,
            BuiltinHandlerRegistry builtinHandlers,
            GasGraphOpHandlerTable handlers,
            EffectTemplateRegistry templates,
            GlobalPhaseListenerRegistry? globalListeners = null,
            GameplayEventBus? eventBus = null)
        {
            _programs = programs;
            _presetTypes = presetTypes;
            _builtinHandlers = builtinHandlers;
            _handlers = handlers;
            _templates = templates;
            _globalListeners = globalListeners;
            _eventBus = eventBus;
        }

        /// <summary>
        /// Execute a single phase for an effect (legacy overload without listener dispatch).
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
            EffectPresetType presetType)
        {
            ExecutePhase(world, api, caster, target, targetContext, targetPos, phase, behavior, presetType, effectTagId: 0, effectTemplateId: 0);
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
            int effectTemplateId)
        {
            // ① Pre graph (user-defined)
            int preGraphId = behavior.GetGraphId(phase, PhaseSlot.Pre);
            if (preGraphId > 0)
            {
                ExecuteGraph(world, api, caster, target, targetContext, targetPos, preGraphId);
            }

            // ② Main handler (unless SkipMain)
            if (!behavior.IsSkipMain(phase))
            {
                ExecuteMainHandler(world, api, caster, target, targetContext, targetPos, phase, presetType, effectTemplateId);
            }

            // ③ Post graph (user-defined)
            int postGraphId = behavior.GetGraphId(phase, PhaseSlot.Post);
            if (postGraphId > 0)
            {
                ExecuteGraph(world, api, caster, target, targetContext, targetPos, postGraphId);
            }

            // ④ Dispatch Phase Listeners
            if (effectTagId != 0 || effectTemplateId != 0)
            {
                DispatchListeners(world, api, caster, target, targetContext, targetPos, phase, effectTagId, effectTemplateId);
            }
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
            int effectTemplateId)
        {
            if (!_presetTypes.IsRegistered(presetType)) return;

            ref readonly var def = ref _presetTypes.Get(presetType);
            var handler = def.DefaultPhaseHandlers[phase];

            if (!handler.IsValid) return;

            switch (handler.Kind)
            {
                case PhaseHandlerKind.Builtin:
                {
                    if (!_templates.TryGet(effectTemplateId, out var tplData))
                    {
                        throw new InvalidOperationException(
                            $"EffectPhaseExecutor: Builtin handler for phase {phase} requires template {effectTemplateId}, but it is not registered.");
                    }
                    var context = new EffectContext { Source = caster, Target = target, TargetContext = targetContext };
                    var mergedParams = tplData.ConfigParams;
                    _builtinHandlers.Invoke(
                        (BuiltinHandlerId)handler.HandlerId,
                        world, default, ref context, in mergedParams, in tplData);
                    break;
                }
                case PhaseHandlerKind.Graph:
                {
                    ExecuteGraph(world, api, caster, target, targetContext, targetPos, handler.HandlerId);
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
            int effectTemplateId)
        {
            Span<PhaseListenerCollectedAction> scratch = _collectedActions;
            int totalCollected = 0;

            // a. Target entity's buffer (scope = Target)
            if (world.IsAlive(target) && world.Has<EffectPhaseListenerBuffer>(target))
            {
                ref var buf = ref world.Get<EffectPhaseListenerBuffer>(target);
                int n = buf.Collect(effectTagId, effectTemplateId, phase, PhaseListenerScope.Target, scratch.Slice(totalCollected));
                totalCollected += n;
            }

            // b. Caster entity's buffer (scope = Source)
            if (world.IsAlive(caster) && world.Has<EffectPhaseListenerBuffer>(caster))
            {
                ref var buf = ref world.Get<EffectPhaseListenerBuffer>(caster);
                int n = buf.Collect(effectTagId, effectTemplateId, phase, PhaseListenerScope.Source, scratch.Slice(totalCollected));
                totalCollected += n;
            }

            // c. Global listeners
            if (_globalListeners != null)
            {
                int n = _globalListeners.Collect(phase, effectTagId, effectTemplateId, scratch.Slice(totalCollected));
                totalCollected += n;
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
                    ExecuteGraph(world, api, caster, target, targetContext, targetPos, action.GraphProgramId);
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
            DispatchListeners(world, api, caster, target, targetContext, targetPos, phase, effectTagId, effectTemplateId);
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
            if (graphProgramId <= 0) return;
            if (!_programs.TryGetProgram(graphProgramId, out var program)) return;
            if (program.Length == 0) return;

            // Clear scratch registers
            Array.Clear(_floatRegs, 0, _floatRegs.Length);
            Array.Clear(_intRegs, 0, _intRegs.Length);
            Array.Clear(_boolRegs, 0, _boolRegs.Length);
            Array.Clear(_entityRegs, 0, _entityRegs.Length);

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
                TargetPos = targetPos,
                Api = api,
                F = _floatRegs,
                I = _intRegs,
                B = _boolRegs,
                E = _entityRegs,
                Targets = _targets,
                TargetList = targetList,
            };

            GasGraphOpHandlerTable.Execute(ref state, program, _handlers);
        }
    }
}

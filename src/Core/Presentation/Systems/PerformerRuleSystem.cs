using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Matches <see cref="PresentationEvent"/>s against <see cref="PerformerRule"/>s
    /// from registered <see cref="PerformerDefinition"/>s. When an event matches and
    /// the condition evaluates to true, a <see cref="PresentationCommand"/> is produced.
    ///
    /// Replaces PresentationControlSystem's event-to-command mapping with a fully
    /// declarative, configuration-driven rule engine.
    ///
    /// Graph dependency is one-way: this system calls GraphExecutor, but Graph
    /// has no knowledge of the Performer domain.
    /// </summary>
    public sealed class PerformerRuleSystem : BaseSystem<World, float>
    {
        private readonly PresentationEventStream _events;
        private readonly PresentationCommandBuffer _commands;
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly GraphProgramRegistry _programs;
        private readonly IGraphRuntimeApi _graphApi;
        private readonly Dictionary<string, object> _globals;

        // ── Pre-allocated Graph VM registers (same pattern as EffectPhaseExecutor) ──
        private readonly float[] _floatRegs = new float[GraphVmLimits.MaxFloatRegisters];
        private readonly int[] _intRegs = new int[GraphVmLimits.MaxIntRegisters];
        private readonly byte[] _boolRegs = new byte[GraphVmLimits.MaxBoolRegisters];
        private readonly Entity[] _entityRegs = new Entity[GraphVmLimits.MaxEntityRegisters];
        private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
        private readonly GasGraphOpHandlerTable _handlers = GasGraphOpHandlerTable.Instance;

        // ── Inverted rule index: replaces O(E×D×R) triple loop with O(E × matched) ──
        //
        // Exact match: packed (eventKind, keyId) → IndexedRule[]
        // Wildcard:    eventKind → IndexedRule[]  (rules where KeyId == -1)
        //
        // Built lazily on first Update; rebuilt when registry version changes.
        private Dictionary<long, IndexedRule[]> _exactIndex;
        private Dictionary<PresentationEventKind, IndexedRule[]> _wildcardIndex;
        private int _indexVersion = -1;

        private struct IndexedRule
        {
            public ConditionRef Condition;
            public PerformerCommand Command;
        }

        public PerformerRuleSystem(
            World world,
            PresentationEventStream events,
            PresentationCommandBuffer commands,
            PerformerDefinitionRegistry definitions,
            GraphProgramRegistry programs,
            IGraphRuntimeApi graphApi,
            Dictionary<string, object> globals)
            : base(world)
        {
            _events = events;
            _commands = commands;
            _definitions = definitions;
            _programs = programs;
            _graphApi = graphApi;
            _globals = globals;
        }

        public override void Update(in float dt)
        {
            var span = _events.GetSpan();
            if (span.Length == 0) return;

            // Rebuild index if registry changed since last build
            if (_indexVersion != _definitions.Version)
                RebuildRuleIndex();

            for (int ei = 0; ei < span.Length; ei++)
            {
                ref readonly var evt = ref span[ei];

                // 1. Check exact-match rules: (eventKind, keyId)
                long exactKey = PackKey(evt.Kind, evt.KeyId);
                if (_exactIndex.TryGetValue(exactKey, out var exactRules))
                {
                    for (int ri = 0; ri < exactRules.Length; ri++)
                    {
                        if (!EvaluateCondition(in exactRules[ri].Condition, in evt)) continue;
                        EmitCommand(in exactRules[ri].Command, in evt);
                    }
                }

                // 2. Check wildcard rules: (eventKind, any keyId)
                if (_wildcardIndex.TryGetValue(evt.Kind, out var wildcardRules))
                {
                    for (int ri = 0; ri < wildcardRules.Length; ri++)
                    {
                        if (!EvaluateCondition(in wildcardRules[ri].Condition, in evt)) continue;
                        EmitCommand(in wildcardRules[ri].Command, in evt);
                    }
                }
            }

            _events.Clear();
        }

        // ── Inverted Index Construction ──

        private static long PackKey(PresentationEventKind kind, int keyId)
            => ((long)kind << 32) | (uint)keyId;

        private void RebuildRuleIndex()
        {
            var exactBuild = new Dictionary<long, List<IndexedRule>>();
            var wildcardBuild = new Dictionary<PresentationEventKind, List<IndexedRule>>();

            var registeredIds = _definitions.RegisteredIds;
            for (int di = 0; di < registeredIds.Count; di++)
            {
                if (!_definitions.TryGet(registeredIds[di], out var def)) continue;
                if (def.Rules == null || def.Rules.Length == 0) continue;

                for (int ri = 0; ri < def.Rules.Length; ri++)
                {
                    ref var rule = ref def.Rules[ri];
                    var entry = new IndexedRule
                    {
                        Condition = rule.Condition,
                        Command = rule.Command,
                    };

                    if (rule.Event.KeyId < 0)
                    {
                        // Wildcard — matches any KeyId for this event kind
                        if (!wildcardBuild.TryGetValue(rule.Event.Kind, out var wlist))
                        {
                            wlist = new List<IndexedRule>();
                            wildcardBuild[rule.Event.Kind] = wlist;
                        }
                        wlist.Add(entry);
                    }
                    else
                    {
                        // Exact match
                        long key = PackKey(rule.Event.Kind, rule.Event.KeyId);
                        if (!exactBuild.TryGetValue(key, out var elist))
                        {
                            elist = new List<IndexedRule>();
                            exactBuild[key] = elist;
                        }
                        elist.Add(entry);
                    }
                }
            }

            // Freeze to arrays for cache-friendly iteration
            _exactIndex = new Dictionary<long, IndexedRule[]>(exactBuild.Count);
            foreach (var kv in exactBuild)
                _exactIndex[kv.Key] = kv.Value.ToArray();

            _wildcardIndex = new Dictionary<PresentationEventKind, IndexedRule[]>(wildcardBuild.Count);
            foreach (var kv in wildcardBuild)
                _wildcardIndex[kv.Key] = kv.Value.ToArray();

            _indexVersion = _definitions.Version;
        }

        // ── Condition Evaluation ──

        private bool EvaluateCondition(in ConditionRef cond, in PresentationEvent evt)
        {
            // Inline fast path
            if (cond.Inline != InlineConditionKind.None)
                return EvaluateInline(cond.Inline, in evt);

            // Graph path
            if (cond.GraphProgramId > 0)
                return EvaluateGraph(cond.GraphProgramId, evt.Source, evt.Target);

            // Default: always true
            return true;
        }

        private bool EvaluateInline(InlineConditionKind kind, in PresentationEvent evt)
        {
            switch (kind)
            {
                case InlineConditionKind.None:
                    return true;

                case InlineConditionKind.SourceIsLocalPlayer:
                    return IsLocalPlayer(evt.Source);

                case InlineConditionKind.TargetIsLocalPlayer:
                    return IsLocalPlayer(evt.Target);

                case InlineConditionKind.SourceIsAlive:
                    return World.IsAlive(evt.Source);

                case InlineConditionKind.TargetIsAlive:
                    return World.IsAlive(evt.Target);

                case InlineConditionKind.TagGained:
                    return evt.Kind == PresentationEventKind.TagEffectiveChanged && evt.Magnitude > 0f;

                case InlineConditionKind.TagLost:
                    return evt.Kind == PresentationEventKind.TagEffectiveChanged && evt.Magnitude == 0f;

                case InlineConditionKind.OwnerCullVisible:
                    return IsOwnerCullVisible(evt.Source);

                default:
                    return true;
            }
        }

        private bool IsLocalPlayer(Entity entity)
        {
            if (!_globals.TryGetValue(ContextKeys.LocalPlayerEntity, out var obj)) return false;
            return obj is Entity lp && lp == entity;
        }

        private bool IsOwnerCullVisible(Entity owner)
        {
            if (!World.IsAlive(owner)) return false;
            if (!World.Has<CullState>(owner)) return true; // no cull component = always visible
            return World.Get<CullState>(owner).IsVisible;
        }

        /// <summary>
        /// Execute a Graph program and read B[0] as the boolean result.
        /// Same register setup pattern as EffectPhaseExecutor.ExecuteGraph().
        /// </summary>
        private bool EvaluateGraph(int graphProgramId, Entity source, Entity target)
        {
            if (!_programs.TryGetProgram(graphProgramId, out var program)) return false;
            if (program.Length == 0) return false;

            Array.Clear(_floatRegs, 0, _floatRegs.Length);
            Array.Clear(_intRegs, 0, _intRegs.Length);
            Array.Clear(_boolRegs, 0, _boolRegs.Length);
            Array.Clear(_entityRegs, 0, _entityRegs.Length);

            _entityRegs[0] = source;
            _entityRegs[1] = target;

            var targetList = new GraphTargetList(_targets);

            var state = new GraphExecutionState
            {
                World = World,
                Caster = source,
                ExplicitTarget = target,
                TargetPos = IntVector2.Zero,
                Api = _graphApi,
                F = _floatRegs,
                I = _intRegs,
                B = _boolRegs,
                E = _entityRegs,
                Targets = _targets,
                TargetList = targetList,
            };

            GasGraphOpHandlerTable.Execute(ref state, program, _handlers);

            // Convention: B[0] holds the boolean condition result
            return _boolRegs[0] != 0;
        }

        // ── Command Emission ──

        private void EmitCommand(in PerformerCommand cmd, in PresentationEvent evt)
        {
            _commands.TryAdd(new PresentationCommand
            {
                LogicTickStamp = evt.LogicTickStamp,
                Kind = cmd.CommandKind,
                IdA = cmd.CommandKind == PresentationCommandKind.CreatePerformer
                    ? cmd.PerformerDefinitionId
                    : 0,
                IdB = cmd.ScopeId,
                Source = evt.Source,
                Target = evt.Target,
                Param1 = cmd.ParamGraphProgramId > 0
                    ? EvaluateGraphFloat(cmd.ParamGraphProgramId, evt.Source, evt.Target)
                    : cmd.ParamValue,
                Param2 = cmd.ParamKey,
            });
        }

        /// <summary>
        /// Execute a Graph program and read F[0] as a float result (for dynamic param values).
        /// </summary>
        private float EvaluateGraphFloat(int graphProgramId, Entity source, Entity target)
        {
            if (!_programs.TryGetProgram(graphProgramId, out var program)) return 0f;
            if (program.Length == 0) return 0f;

            Array.Clear(_floatRegs, 0, _floatRegs.Length);
            Array.Clear(_intRegs, 0, _intRegs.Length);
            Array.Clear(_boolRegs, 0, _boolRegs.Length);
            Array.Clear(_entityRegs, 0, _entityRegs.Length);

            _entityRegs[0] = source;
            _entityRegs[1] = target;

            var targetList = new GraphTargetList(_targets);

            var state = new GraphExecutionState
            {
                World = World,
                Caster = source,
                ExplicitTarget = target,
                TargetPos = IntVector2.Zero,
                Api = _graphApi,
                F = _floatRegs,
                I = _intRegs,
                B = _boolRegs,
                E = _entityRegs,
                Targets = _targets,
                TargetList = targetList,
            };

            GasGraphOpHandlerTable.Execute(ref state, program, _handlers);

            // Convention: F[0] holds the float result
            return _floatRegs[0];
        }
    }
}

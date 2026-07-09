using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Matches <see cref="PresentationEvent"/>s against <see cref="PerformerRule"/>s
    /// from registered <see cref="PerformerDefinition"/>s. When an event matches and
    /// the condition evaluates to true, a <see cref="PerformerCommand"/> is produced.
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
        private readonly PerformerCommandBuffer _commands;
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly PerformerEntityRuntime? _runtime;
        private readonly GraphProgramRegistry _programs;
        private readonly IGraphRuntimeApi _graphApi;
        private readonly Dictionary<string, object> _globals;

        // ── Pre-allocated Graph VM registers (same pattern as EffectPhaseExecutor) ──
        private readonly float[] _floatRegs = new float[GraphVmLimits.MaxFloatRegisters];
        private readonly int[] _intRegs = new int[GraphVmLimits.MaxIntRegisters];
        private readonly byte[] _boolRegs = new byte[GraphVmLimits.MaxBoolRegisters];
        private readonly Entity[] _entityRegs = new Entity[GraphVmLimits.MaxEntityRegisters];
        private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
        private readonly GasGraphOpHandlerTable? _handlers;

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
            public int OwnerDefinitionId;
            public ConditionRef Condition;
            public PerformerCommand Command;
        }

        public PerformerRuleSystem(
            World world,
            PresentationEventStream events,
            PerformerCommandBuffer commands,
            PerformerDefinitionRegistry definitions,
            PerformerEntityRuntime? runtime,
            GraphProgramRegistry programs,
            IGraphRuntimeApi graphApi,
            Dictionary<string, object> globals,
            GasGraphOpHandlerTable handlers = null)
            : base(world)
        {
            _events = events;
            _commands = commands;
            _definitions = definitions;
            _runtime = runtime;
            _programs = programs;
            _graphApi = graphApi;
            _globals = globals;
            _handlers = handlers;
            _runtime?.BindDefinitions(_definitions);
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
                        if (!EvaluateCondition(in exactRules[ri], in evt)) continue;
                        EmitRule(exactRules[ri], in evt);
                    }
                }

                // 2. Check wildcard rules: (eventKind, any keyId)
                if (_wildcardIndex.TryGetValue(evt.Kind, out var wildcardRules))
                {
                    for (int ri = 0; ri < wildcardRules.Length; ri++)
                    {
                        if (!EvaluateCondition(in wildcardRules[ri], in evt)) continue;
                        EmitRule(wildcardRules[ri], in evt);
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
                    if (_definitions.BootstrapRegistry.IsRootBootstrapRule(in rule))
                    {
                        continue;
                    }

                    var entry = new IndexedRule
                    {
                        OwnerDefinitionId = rule.OwnerDefinitionId,
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

        private bool EvaluateCondition(in IndexedRule rule, in PresentationEvent evt)
        {
            ref readonly ConditionRef cond = ref rule.Condition;
            // Inline fast path
            if (cond.Inline != InlineConditionKind.None)
                return EvaluateInline(cond.Inline, in evt, in rule);

            // Graph path
            if (cond.GraphProgramId > 0)
                return EvaluateGraph(cond.GraphProgramId, evt.Source, evt.Target);

            // Default: always true
            return true;
        }

        private bool EvaluateInline(InlineConditionKind kind, in PresentationEvent evt, in IndexedRule rule)
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

                case InlineConditionKind.SourceHasAttributes:
                    return SourceSatisfiesAttributeRequirements(evt.Source, in rule);

                case InlineConditionKind.SourceHasVisualTransform:
                    return World.IsAlive(evt.Source) && World.Has<VisualTransform>(evt.Source);

                case InlineConditionKind.EventMagnitudePositive:
                    return evt.Magnitude > 0f;

                case InlineConditionKind.EventMagnitudeNonPositive:
                    return evt.Magnitude <= 0f;

                default:
                    throw new InvalidOperationException($"Unsupported performer rule inline condition '{kind}'.");
            }
        }

        private bool IsLocalPlayer(Entity entity)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var obj)) return false;
            return obj is Entity lp && lp == entity;
        }

        private bool IsOwnerCullVisible(Entity owner)
        {
            if (!World.IsAlive(owner)) return false;
            if (!World.Has<CullState>(owner)) return true; // no cull component = always visible
            return World.Get<CullState>(owner).IsVisible;
        }

        private bool SourceSatisfiesAttributeRequirements(Entity source, in IndexedRule rule)
        {
            if (!World.IsAlive(source) || !World.Has<AttributeBuffer>(source))
            {
                return false;
            }

            ref AttributeBuffer attributes = ref World.Get<AttributeBuffer>(source);
            if (TryGetConditionTargetDefinition(in rule, out PerformerDefinition definition))
            {
                return DefinitionAttributesSatisfied(ref attributes, definition);
            }

            return true;
        }

        private bool TryGetConditionTargetDefinition(in IndexedRule rule, out PerformerDefinition definition)
        {
            definition = null!;
            int definitionId = ResolveRouteStrategy(in rule.Command) == PerformerCommandRouteStrategy.CreatePerformer
                ? rule.Command.PerformerDefinitionId
                : rule.OwnerDefinitionId;
            if (definitionId > 0)
            {
                if (_definitions.TryGet(definitionId, out definition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DefinitionAttributesSatisfied(ref AttributeBuffer attributes, PerformerDefinition definition)
        {
            int[] required = definition.RequiredAttributeIds;
            if (required == null || required.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < required.Length; i++)
            {
                if (!attributes.HasAttribute(required[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Execute a Graph program and read B[0] as the boolean result.
        /// Same register setup pattern as EffectPhaseExecutor.ExecuteGraph().
        /// </summary>
        private bool EvaluateGraph(int graphProgramId, Entity source, Entity target)
        {
            if (!_programs.TryGetProgram(graphProgramId, out var program))
            {
                throw new InvalidOperationException($"Performer rule condition references unknown graphProgramId={graphProgramId}.");
            }

            if (program.Length == 0)
            {
                throw new InvalidOperationException($"Performer rule condition graphProgramId={graphProgramId} has no instructions.");
            }

            if (_handlers == null)
            {
                throw new InvalidOperationException($"Performer rule condition graphProgramId={graphProgramId} requires a graph handler table.");
            }

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

        private void EmitRule(in IndexedRule rule, in PresentationEvent evt)
        {
            if (rule.OwnerDefinitionId > 0 && _runtime != null)
            {
                if (CommandTargetsExistingPerformerInstances(in rule.Command))
                {
                    EmitForMatchingInstances(rule.OwnerDefinitionId, in rule.Command, in evt);
                    return;
                }

                if (CommandTargetsScopedPerformer(ResolveRouteStrategy(in rule.Command)))
                {
                    if (ScopedCommandRequiresOwnerDefinitionInstance(rule.OwnerDefinitionId))
                    {
                        EmitForMatchingInstances(rule.OwnerDefinitionId, in rule.Command, in evt);
                        return;
                    }

                    EmitCommand(in rule.Command, in evt, performerEntity: Entity.Null, ownerDefinitionId: rule.OwnerDefinitionId);
                    return;
                }
            }

            EmitCommand(in rule.Command, in evt, performerEntity: Entity.Null, ownerDefinitionId: rule.OwnerDefinitionId);
        }

        private bool ScopedCommandRequiresOwnerDefinitionInstance(int ownerDefinitionId)
        {
            if (_runtime != null &&
                _runtime.GetActiveByDefinition(ownerDefinitionId).Count != 0)
            {
                return true;
            }

            if (!_definitions.TryGet(ownerDefinitionId, out PerformerDefinition definition))
            {
                return false;
            }

            return DefinitionHasRuntimeInstanceAuthoring(definition);
        }

        private static bool DefinitionHasRuntimeInstanceAuthoring(PerformerDefinition definition)
        {
            return HasAny(definition.Behaviors) ||
                   HasAny(definition.Children) ||
                   HasAny(definition.Bindings) ||
                   HasAny(definition.ParamDefaults) ||
                   HasAny(definition.InstancedBatches) ||
                   definition.Surface != null;
        }

        private static bool HasAny<T>(T[]? items)
        {
            return items != null && items.Length != 0;
        }

        private void EmitForMatchingInstances(int ownerDefinitionId, in PerformerCommand command, in PresentationEvent evt)
        {
            bool globalEvent = IsGlobalEvent(evt.Kind);
            PerformerCommand localCmd = command;
            PresentationEvent localEvt = evt;
            IReadOnlyList<Entity> candidates = globalEvent
                ? _runtime!.GetActiveByDefinition(ownerDefinitionId)
                : _runtime!.GetActiveByOwnerDefinition(ownerDefinitionId, evt.Source);

            for (int i = 0; i < candidates.Count; i++)
            {
                Entity entity = candidates[i];
                if (!World.IsAlive(entity) || !World.Has<PerformerState>(entity))
                {
                    continue;
                }

                ref PerformerState state = ref World.Get<PerformerState>(entity);
                if (state.DefId != ownerDefinitionId || (!globalEvent && state.OwnerEntity != localEvt.Source))
                {
                    continue;
                }

                EmitCommand(in localCmd, in localEvt, entity, ownerDefinitionId);
            }
        }

        private void EmitCommand(in PerformerCommand cmd, in PresentationEvent evt, Entity performerEntity, int ownerDefinitionId)
        {
            int scopeId = cmd.ScopeSource switch
            {
                PerformerCommandScopeSource.EventPayloadA => evt.PayloadA,
                PerformerCommandScopeSource.EventPayloadB => evt.PayloadB,
                PerformerCommandScopeSource.EventKeyId => evt.KeyId,
                PerformerCommandScopeSource.SourceStableId => ResolveStableId(evt.Source, nameof(PerformerCommandScopeSource.SourceStableId)),
                PerformerCommandScopeSource.EventTargetStableId => ResolveStableId(evt.Target, nameof(PerformerCommandScopeSource.EventTargetStableId)),
                _ => cmd.ScopeTag,
            };

            var emitted = cmd;
            emitted.RouteStrategy = ResolveRouteStrategy(in cmd);
            emitted.ScopeTag = scopeId;
            emitted.ScopeSource = PerformerCommandScopeSource.Fixed;
            emitted.AnchorKind = cmd.UseEventPosition
                ? Commands.PresentationAnchorKind.WorldPosition
                : Commands.PresentationAnchorKind.Entity;
            emitted.Source = ResolveCommandOwner(in cmd, in evt);
            emitted.Target = evt.Target;
            emitted.Viewer = evt.Viewer;
            emitted.Position = cmd.UseEventPosition ? evt.Position : default;
            emitted.PerformerEntity = performerEntity;
            Entity normalizedParent = NormalizeOptionalEntity(cmd.ParentEntity);
            emitted.ParentEntity = normalizedParent != Entity.Null
                ? normalizedParent
                : ResolveImplicitParent(in evt);
            emitted.ParamValue = cmd.ParamGraphProgramId > 0
                ? EvaluateGraphFloat(cmd.ParamGraphProgramId, evt.Source, evt.Target)
                : ResolveParamFloatValue(in cmd, in evt);
            emitted.IntValue = ResolveParamIntValue(in cmd, in evt);
            emitted.VectorValue = ResolveParamVectorValue(in cmd, in evt);
            emitted.ParamGraphProgramId = 0;
            emitted.ValueSource = PerformerCommandValueSource.Fixed;
            emitted.VectorXSource = PerformerCommandValueSource.Fixed;
            emitted.VectorYSource = PerformerCommandValueSource.Fixed;
            emitted.VectorZSource = PerformerCommandValueSource.Fixed;
            emitted.VectorWSource = PerformerCommandValueSource.Fixed;
            emitted.UseEventPosition = cmd.UseEventPosition;

            if (emitted.CommandKind == PerformerCommandKind.CreatePerformer &&
                emitted.ParentEntity == Entity.Null &&
                performerEntity != Entity.Null &&
                ownerDefinitionId > 0)
            {
                emitted.ParentEntity = performerEntity;
            }

            if (!_commands.TryAdd(in emitted))
            {
                throw new InvalidOperationException(
                    $"PerformerCommandBuffer overflowed while emitting {emitted.CommandKind} from {evt.Kind}; capacity={_commands.Capacity}.");
            }
        }

        private static Entity ResolveCommandOwner(in PerformerCommand cmd, in PresentationEvent evt)
        {
            return cmd.OwnerSource switch
            {
                PerformerCommandEntitySource.EventTarget => evt.Target,
                _ => evt.Source,
            };
        }

        private static Entity NormalizeOptionalEntity(Entity entity)
        {
            return entity == default || entity.Id < 0 ? Entity.Null : entity;
        }

        private Entity ResolveImplicitParent(in PresentationEvent evt)
        {
            if (evt.Kind == PresentationEventKind.PerformerCreated)
            {
                return NormalizeOptionalEntity(evt.PerformerEntity);
            }

            if (evt.Kind is PresentationEventKind.SelectionMemberAdded or PresentationEventKind.SelectionMemberRemoved &&
                World.IsAlive(evt.Source) &&
                World.Has<PresentationOwnerHasPerformerPayload>(evt.Source))
            {
                Entity parent = World.Get<PresentationOwnerHasPerformerPayload>(evt.Source).SingleRootPerformer;
                return NormalizeOptionalEntity(parent);
            }

            return Entity.Null;
        }

        private static bool EventTargetsExistingPerformerInstances(PresentationEventKind kind)
        {
            return kind is PresentationEventKind.TagEffectiveChanged
                or PresentationEventKind.SelectionMemberAdded
                or PresentationEventKind.SelectionMemberRemoved
                or PresentationEventKind.GlobalDayNight
                or PresentationEventKind.GlobalRegionChanged
                or PresentationEventKind.GlobalWeather
                or PresentationEventKind.AttributeValueChanged;
        }

        private static bool CommandTargetsExistingPerformerInstances(in PerformerCommand command)
        {
            return ResolveRouteStrategy(in command) == PerformerCommandRouteStrategy.ExistingInstances;
        }

        private static bool CommandTargetsScopedPerformer(PerformerCommandRouteStrategy route)
        {
            return route is PerformerCommandRouteStrategy.CreatePerformer
                or PerformerCommandRouteStrategy.DestroyScope
                or PerformerCommandRouteStrategy.ScopedInstance;
        }

        private static PerformerCommandRouteStrategy ResolveRouteStrategy(in PerformerCommand command)
        {
            if (command.RouteStrategy != PerformerCommandRouteStrategy.None)
            {
                return command.RouteStrategy;
            }

            return command.CommandKind switch
            {
                PerformerCommandKind.CreatePerformer => PerformerCommandRouteStrategy.CreatePerformer,
                PerformerCommandKind.DestroyPerformerScope => PerformerCommandRouteStrategy.DestroyScope,
                PerformerCommandKind.DestroyScopedPerformer => PerformerCommandRouteStrategy.ScopedInstance,
                PerformerCommandKind.SetParam when command.PerformerDefinitionId > 0 => PerformerCommandRouteStrategy.ScopedInstance,
                PerformerCommandKind.SetParam => PerformerCommandRouteStrategy.ExistingInstances,
                PerformerCommandKind.ActivateBehavior => PerformerCommandRouteStrategy.ExistingInstances,
                PerformerCommandKind.DeactivateBehavior => PerformerCommandRouteStrategy.ExistingInstances,
                PerformerCommandKind.InitializeTransform => PerformerCommandRouteStrategy.ExistingInstances,
                PerformerCommandKind.DestroyPerformer => PerformerCommandRouteStrategy.ExistingInstances,
                PerformerCommandKind.SinkParamToAsset => PerformerCommandRouteStrategy.SingleRuntime,
                PerformerCommandKind.Extension => throw new InvalidOperationException(
                    $"Extension performer command id {command.CommandKindId} must declare routeStrategy before rule routing."),
                _ => throw new InvalidOperationException($"Unsupported performer command kind '{command.CommandKind}'."),
            };
        }

        private int ResolveStableId(Entity source, string scopeSourceName)
        {
            if (!World.IsAlive(source) || !World.Has<PresentationStableId>(source))
            {
                throw new InvalidOperationException($"Performer command scopeSource={scopeSourceName} requires an alive entity with PresentationStableId.");
            }

            int stableId = World.Get<PresentationStableId>(source).Value;
            if (stableId <= 0)
            {
                throw new InvalidOperationException($"Performer command scopeSource={scopeSourceName} requires a positive PresentationStableId, got {stableId}.");
            }

            return stableId;
        }

        private static bool IsGlobalEvent(PresentationEventKind kind)
        {
            return kind is PresentationEventKind.GlobalDayNight
                or PresentationEventKind.GlobalRegionChanged
                or PresentationEventKind.GlobalWeather;
        }

        private static float ResolveParamFloatValue(in PerformerCommand cmd, in PresentationEvent evt)
        {
            return cmd.ValueSource switch
            {
                PerformerCommandValueSource.EventKeyId => evt.KeyId,
                PerformerCommandValueSource.EventPayloadA => evt.PayloadA,
                PerformerCommandValueSource.EventPayloadB => evt.PayloadB,
                PerformerCommandValueSource.EventMagnitude => evt.Magnitude,
                PerformerCommandValueSource.EventFloatA => evt.FloatA,
                PerformerCommandValueSource.EventFloatB => evt.FloatB,
                PerformerCommandValueSource.EventFloatC => evt.FloatC,
                PerformerCommandValueSource.EventFloatD => evt.FloatD,
                PerformerCommandValueSource.EventPositionX => evt.Position.X,
                PerformerCommandValueSource.EventPositionY => evt.Position.Y,
                PerformerCommandValueSource.EventPositionZ => evt.Position.Z,
                _ => cmd.ParamValue,
            };
        }

        private static int ResolveParamIntValue(in PerformerCommand cmd, in PresentationEvent evt)
        {
            return cmd.ValueSource switch
            {
                PerformerCommandValueSource.EventKeyId => evt.KeyId,
                PerformerCommandValueSource.EventPayloadA => evt.PayloadA,
                PerformerCommandValueSource.EventPayloadB => evt.PayloadB,
                PerformerCommandValueSource.EventMagnitude => (int)evt.Magnitude,
                PerformerCommandValueSource.EventFloatA => (int)evt.FloatA,
                PerformerCommandValueSource.EventFloatB => (int)evt.FloatB,
                PerformerCommandValueSource.EventFloatC => (int)evt.FloatC,
                PerformerCommandValueSource.EventFloatD => (int)evt.FloatD,
                PerformerCommandValueSource.EventPositionX => (int)evt.Position.X,
                PerformerCommandValueSource.EventPositionY => (int)evt.Position.Y,
                PerformerCommandValueSource.EventPositionZ => (int)evt.Position.Z,
                _ => cmd.IntValue,
            };
        }

        private static Vector4 ResolveParamVectorValue(in PerformerCommand cmd, in PresentationEvent evt)
        {
            if (cmd.ParamLane != ParamLane.Vector ||
                (cmd.VectorXSource == PerformerCommandValueSource.Fixed &&
                 cmd.VectorYSource == PerformerCommandValueSource.Fixed &&
                 cmd.VectorZSource == PerformerCommandValueSource.Fixed &&
                 cmd.VectorWSource == PerformerCommandValueSource.Fixed))
            {
                return cmd.VectorValue;
            }

            return new Vector4(
                ResolveValueSource(cmd.VectorXSource, in evt, cmd.VectorValue.X),
                ResolveValueSource(cmd.VectorYSource, in evt, cmd.VectorValue.Y),
                ResolveValueSource(cmd.VectorZSource, in evt, cmd.VectorValue.Z),
                ResolveValueSource(cmd.VectorWSource, in evt, cmd.VectorValue.W));
        }

        private static float ResolveValueSource(PerformerCommandValueSource source, in PresentationEvent evt, float fixedValue)
        {
            return source switch
            {
                PerformerCommandValueSource.EventKeyId => evt.KeyId,
                PerformerCommandValueSource.EventPayloadA => evt.PayloadA,
                PerformerCommandValueSource.EventPayloadB => evt.PayloadB,
                PerformerCommandValueSource.EventMagnitude => evt.Magnitude,
                PerformerCommandValueSource.EventFloatA => evt.FloatA,
                PerformerCommandValueSource.EventFloatB => evt.FloatB,
                PerformerCommandValueSource.EventFloatC => evt.FloatC,
                PerformerCommandValueSource.EventFloatD => evt.FloatD,
                PerformerCommandValueSource.EventPositionX => evt.Position.X,
                PerformerCommandValueSource.EventPositionY => evt.Position.Y,
                PerformerCommandValueSource.EventPositionZ => evt.Position.Z,
                _ => fixedValue,
            };
        }

        /// <summary>
        /// Execute a Graph program and read F[0] as a float result (for dynamic param values).
        /// </summary>
        private float EvaluateGraphFloat(int graphProgramId, Entity source, Entity target)
        {
            if (!_programs.TryGetProgram(graphProgramId, out var program))
            {
                throw new InvalidOperationException($"Performer command paramGraphProgramId={graphProgramId} references an unknown graph program.");
            }

            if (program.Length == 0)
            {
                throw new InvalidOperationException($"Performer command paramGraphProgramId={graphProgramId} has no instructions.");
            }

            if (_handlers == null)
            {
                throw new InvalidOperationException($"Performer command paramGraphProgramId={graphProgramId} requires a graph handler table.");
            }

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

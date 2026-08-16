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
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Matches <see cref="PresentationEvent"/>s against <see cref="PresenterRule"/>s
    /// from registered <see cref="PresenterDefinition"/>s. When an event matches and
    /// the condition evaluates to true, a <see cref="PresenterCommand"/> is produced.
    ///
    /// Replaces PresentationControlSystem's event-to-command mapping with a fully
    /// declarative, configuration-driven rule engine.
    ///
    /// Graph dependency is one-way: this system calls GraphExecutor, but Graph
    /// has no knowledge of the Presenter domain.
    /// </summary>
    public sealed class PresenterRuleSystem : BaseSystem<World, float>
    {
        private readonly PresentationEventStream _events;
        private readonly PresenterCommandBuffer _commands;
        private readonly PresenterDefinitionRegistry _definitions;
        private readonly PresenterEntityRuntime? _runtime;
        private readonly GraphProgramRegistry _programs;
        private readonly IGraphRuntimeApi _graphApi;
        private readonly Dictionary<string, object> _globals;

        // ── Pre-allocated Graph VM registers (same pattern as EffectPhaseExecutor) ──
        private readonly float[] _floatRegs = new float[GraphVmLimits.MaxFloatRegisters];
        private readonly int[] _intRegs = new int[GraphVmLimits.MaxIntRegisters];
        private readonly byte[] _boolRegs = new byte[GraphVmLimits.MaxBoolRegisters];
        private readonly Entity[] _entityRegs = new Entity[GraphVmLimits.MaxEntityRegisters];
        private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
        private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
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
            public int OwnerDefinitionId;
            public ConditionRef Condition;
            public PresenterCommand Command;
        }

        public PresenterRuleSystem(
            World world,
            PresentationEventStream events,
            PresenterCommandBuffer commands,
            PresenterDefinitionRegistry definitions,
            PresenterEntityRuntime? runtime,
            GraphProgramRegistry programs,
            IGraphRuntimeApi graphApi,
            Dictionary<string, object> globals,
            GasGraphOpHandlerTable? handlers = null)
            : base(world)
        {
            _events = events;
            _commands = commands;
            _definitions = definitions;
            _runtime = runtime;
            _programs = programs;
            _graphApi = graphApi;
            _globals = globals;
            _handlers = handlers ?? GasGraphOpHandlerTable.Instance;
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
                return EvaluateGraph(cond.GraphProgramId, in evt);

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
                    throw new InvalidOperationException($"Unsupported presenter rule inline condition '{kind}'.");
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
            if (TryGetConditionTargetDefinition(in rule, out PresenterDefinition definition))
            {
                return DefinitionAttributesSatisfied(ref attributes, definition);
            }

            return true;
        }

        private bool TryGetConditionTargetDefinition(in IndexedRule rule, out PresenterDefinition definition)
        {
            definition = null!;
            int definitionId = ResolveRouteStrategy(in rule.Command) == PerformerCommandRouteStrategy.CreatePerformer
                ? rule.Command.PresenterDefinitionId
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

        private static bool DefinitionAttributesSatisfied(ref AttributeBuffer attributes, PresenterDefinition definition)
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
        private bool EvaluateGraph(int graphProgramId, in PresentationEvent evt)
        {
            if (!_programs.TryGetProgram(graphProgramId, out var program))
            {
                throw new InvalidOperationException($"Presenter rule condition references unknown graphProgramId={graphProgramId}.");
            }

            if (program.Length == 0)
            {
                throw new InvalidOperationException($"Presenter rule condition graphProgramId={graphProgramId} has no instructions.");
            }

            _programs.RequireKind(graphProgramId, GraphKind.Validation);
            ExecuteEventGraph(graphProgramId, GraphKind.Validation, program, in evt);

            // Convention: B[0] holds the boolean condition result
            return _boolRegs[0] != 0;
        }

        /// <summary>
        /// Executes a graph program with the event evaluation context (RFC-0065 PROV-4b):
        /// E[0]=Source, E[1]=Target, E[2]=Viewer, plus the event payload slots
        /// readable through LoadEventPayloadInt/Float ops.
        /// </summary>
        private void ExecuteEventGraph(
            int graphProgramId,
            GraphKind kind,
            ReadOnlySpan<GraphInstruction> program,
            in PresentationEvent evt)
        {
            GraphKindOperationPolicy.RequireAllowed(
                kind,
                program,
                _handlers,
                graphProgramId,
                nameof(PresenterRuleSystem));
            Array.Clear(_floatRegs, 0, _floatRegs.Length);
            Array.Clear(_intRegs, 0, _intRegs.Length);
            Array.Clear(_boolRegs, 0, _boolRegs.Length);
            Array.Clear(_entityRegs, 0, _entityRegs.Length);
            Array.Clear(_callStack, 0, _callStack.Length);

            GraphFrame frame = GraphFrame.Bind(
                kind,
                GraphEntityPreset.Viewer(evt.Viewer),
                World,
                evt.Source,
                evt.Target,
                IntVector2.Zero,
                _graphApi,
                _programs,
                _floatRegs,
                _intRegs,
                _boolRegs,
                _entityRegs,
                _targets,
                _callStack,
                eventPayload: new GraphEventPayload
                {
                    PayloadA = evt.PayloadA,
                    PayloadB = evt.PayloadB,
                    FloatA = evt.FloatA,
                    FloatB = evt.FloatB,
                    FloatC = evt.FloatC,
                    FloatD = evt.FloatD,
                });
            GraphExecutor.Execute(ref frame, program, programAlreadyValidated: true);
        }

        // ── Command Emission ──

        private void EmitRule(in IndexedRule rule, in PresentationEvent evt)
        {
            if (rule.OwnerDefinitionId > 0 && _runtime != null)
            {
                if (CommandTargetsExistingPresenterInstances(in rule.Command))
                {
                    EmitForMatchingInstances(rule.OwnerDefinitionId, in rule.Command, in evt);
                    return;
                }

                if (CommandTargetsScopedPresenter(ResolveRouteStrategy(in rule.Command)))
                {
                    if (ScopedCommandRequiresOwnerDefinitionInstance(
                        rule.OwnerDefinitionId,
                        in rule.Command,
                        evt.Kind))
                    {
                        EmitForMatchingInstances(rule.OwnerDefinitionId, in rule.Command, in evt);
                        return;
                    }

                    EmitCommand(in rule.Command, in evt, presenterEntity: Entity.Null, ownerDefinitionId: rule.OwnerDefinitionId);
                    return;
                }
            }

            EmitCommand(in rule.Command, in evt, presenterEntity: Entity.Null, ownerDefinitionId: rule.OwnerDefinitionId);
        }

        private bool ScopedCommandRequiresOwnerDefinitionInstance(
            int ownerDefinitionId,
            in PresenterCommand command,
            PresentationEventKind eventKind)
        {
            if (command.CommandKind == PresenterCommandKind.CreatePresenter &&
                command.PresenterDefinitionId == ownerDefinitionId &&
                !EventTargetsExistingPresenterInstances(eventKind))
            {
                return false;
            }

            if (_runtime != null &&
                _runtime.GetActiveByDefinition(ownerDefinitionId).Count != 0)
            {
                return true;
            }

            if (!_definitions.TryGet(ownerDefinitionId, out PresenterDefinition definition))
            {
                return false;
            }

            return DefinitionHasRuntimeInstanceAuthoring(definition);
        }

        private static bool DefinitionHasRuntimeInstanceAuthoring(PresenterDefinition definition)
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

        private void EmitForMatchingInstances(int ownerDefinitionId, in PresenterCommand command, in PresentationEvent evt)
        {
            bool globalEvent = IsGlobalEvent(evt.Kind);
            PresenterCommand localCmd = command;
            PresentationEvent localEvt = evt;
            IReadOnlyList<Entity> candidates = globalEvent
                ? _runtime!.GetActiveByDefinition(ownerDefinitionId)
                : _runtime!.GetActiveByOwnerDefinition(ownerDefinitionId, evt.Source);

            for (int i = 0; i < candidates.Count; i++)
            {
                Entity entity = candidates[i];
                if (!World.IsAlive(entity) || !World.Has<PresenterState>(entity))
                {
                    continue;
                }

                ref PresenterState state = ref World.Get<PresenterState>(entity);
                if (state.DefId != ownerDefinitionId || (!globalEvent && state.OwnerEntity != localEvt.Source))
                {
                    continue;
                }

                EmitCommand(in localCmd, in localEvt, entity, ownerDefinitionId);
            }
        }

        private void EmitCommand(in PresenterCommand cmd, in PresentationEvent evt, Entity presenterEntity, int ownerDefinitionId)
        {
            int scopeId = cmd.ScopeSource switch
            {
                PresenterCommandScopeSource.EventPayloadA => evt.PayloadA,
                PresenterCommandScopeSource.EventPayloadB => evt.PayloadB,
                PresenterCommandScopeSource.EventKeyId => evt.KeyId,
                PresenterCommandScopeSource.SourceStableId => ResolveStableId(evt.Source, nameof(PresenterCommandScopeSource.SourceStableId)),
                PresenterCommandScopeSource.EventTargetStableId => ResolveStableId(evt.Target, nameof(PresenterCommandScopeSource.EventTargetStableId)),
                _ => cmd.ScopeTag,
            };

            var emitted = cmd;
            emitted.RouteStrategy = ResolveRouteStrategy(in cmd);
            emitted.ScopeTag = scopeId;
            emitted.ScopeSource = PresenterCommandScopeSource.Fixed;
            emitted.AnchorKind = cmd.UseEventPosition
                ? Commands.PresentationAnchorKind.WorldPosition
                : Commands.PresentationAnchorKind.Entity;
            emitted.Source = ResolveCommandOwner(in cmd, in evt);
            emitted.Target = evt.Target;
            emitted.Viewer = evt.Viewer;
            emitted.Position = cmd.UseEventPosition ? evt.Position : default;
            emitted.PresenterEntity = presenterEntity;
            Entity normalizedParent = NormalizeOptionalEntity(cmd.ParentEntity);
            emitted.ParentEntity = normalizedParent != Entity.Null
                ? normalizedParent
                : ResolveImplicitParent(in evt, emitted.PresenterDefinitionId, emitted.Source, emitted.ScopeTag);
            emitted.ParamValue = cmd.ParamGraphProgramId > 0
                ? EvaluateGraphFloat(cmd.ParamGraphProgramId, in evt)
                : ResolveParamFloatValue(in cmd, in evt);
            emitted.IntValue = ResolveParamIntValue(in cmd, in evt);
            emitted.VectorValue = ResolveParamVectorValue(in cmd, in evt);
            emitted.ParamGraphProgramId = 0;
            emitted.ValueSource = PresenterCommandValueSource.Fixed;
            emitted.VectorXSource = PresenterCommandValueSource.Fixed;
            emitted.VectorYSource = PresenterCommandValueSource.Fixed;
            emitted.VectorZSource = PresenterCommandValueSource.Fixed;
            emitted.VectorWSource = PresenterCommandValueSource.Fixed;
            emitted.UseEventPosition = cmd.UseEventPosition;

            if (emitted.CommandKind == PresenterCommandKind.CreatePresenter &&
                emitted.ParentEntity == Entity.Null &&
                presenterEntity != Entity.Null &&
                ownerDefinitionId > 0)
            {
                emitted.ParentEntity = presenterEntity;
            }

            if (!_commands.TryAdd(in emitted))
            {
                throw new InvalidOperationException(
                    $"PresenterCommandBuffer overflowed while emitting {emitted.CommandKind} from {evt.Kind}; capacity={_commands.Capacity}.");
            }

        }

        private static Entity ResolveCommandOwner(in PresenterCommand cmd, in PresentationEvent evt)
        {
            return cmd.OwnerSource switch
            {
                PresenterCommandEntitySource.EventTarget => evt.Target,
                _ => evt.Source,
            };
        }

        private static Entity NormalizeOptionalEntity(Entity entity)
        {
            return entity == default || entity.Id < 0 ? Entity.Null : entity;
        }

        private Entity ResolveImplicitParent(
            in PresentationEvent evt,
            int presenterDefinitionId,
            Entity owner,
            int scopeId)
        {
            if (evt.Kind == PresentationEventKind.PresenterCreated)
            {
                return NormalizeOptionalEntity(evt.PresenterEntity);
            }

            if (evt.Kind is PresentationEventKind.EntityCollectionMemberAdded or PresentationEventKind.EntityCollectionMemberRemoved &&
                World.IsAlive(evt.Source) &&
                World.Has<PresentationOwnerHasPresenterPayload>(evt.Source))
            {
                Entity parent = World.Get<PresentationOwnerHasPresenterPayload>(evt.Source).SingleRootPresenter;
                parent = NormalizeOptionalEntity(parent);
                if (parent != Entity.Null &&
                    presenterDefinitionId > 0 &&
                    scopeId > 0 &&
                    World.IsAlive(parent) &&
                    World.Has<PresenterState>(parent))
                {
                    ref readonly PresenterState parentState = ref World.Get<PresenterState>(parent);
                    if (parentState.DefId == presenterDefinitionId &&
                        parentState.OwnerEntity == owner &&
                        parentState.ScopeId == scopeId)
                    {
                        return Entity.Null;
                    }
                }

                return parent;
            }

            return Entity.Null;
        }

        private static bool EventTargetsExistingPresenterInstances(PresentationEventKind kind)
        {
            return kind is PresentationEventKind.TagEffectiveChanged
                or PresentationEventKind.EntityCollectionMemberAdded
                or PresentationEventKind.EntityCollectionMemberRemoved
                or PresentationEventKind.GlobalDayNight
                or PresentationEventKind.GlobalRegionChanged
                or PresentationEventKind.GlobalWeather
                or PresentationEventKind.AttributeValueChanged;
        }

        private static bool CommandTargetsExistingPresenterInstances(in PresenterCommand command)
        {
            return ResolveRouteStrategy(in command) == PerformerCommandRouteStrategy.ExistingInstances;
        }

        private static bool CommandTargetsScopedPresenter(PerformerCommandRouteStrategy route)
        {
            return route is PerformerCommandRouteStrategy.CreatePerformer
                or PerformerCommandRouteStrategy.DestroyScope
                or PerformerCommandRouteStrategy.ScopedInstance;
        }

        private static PerformerCommandRouteStrategy ResolveRouteStrategy(in PresenterCommand command)
        {
            if (command.RouteStrategy != PerformerCommandRouteStrategy.None)
            {
                return command.RouteStrategy;
            }

            return command.CommandKind switch
            {
                PresenterCommandKind.CreatePresenter => PerformerCommandRouteStrategy.CreatePerformer,
                PresenterCommandKind.DestroyPresenterScope => PerformerCommandRouteStrategy.DestroyScope,
                PresenterCommandKind.DestroyScopedPresenter => PerformerCommandRouteStrategy.ScopedInstance,
                PresenterCommandKind.SetParam when command.PresenterDefinitionId > 0 => PerformerCommandRouteStrategy.ScopedInstance,
                PresenterCommandKind.SetParam => PerformerCommandRouteStrategy.ExistingInstances,
                PresenterCommandKind.ActivateBehavior => PerformerCommandRouteStrategy.ExistingInstances,
                PresenterCommandKind.DeactivateBehavior => PerformerCommandRouteStrategy.ExistingInstances,
                PresenterCommandKind.InitializeTransform => PerformerCommandRouteStrategy.ExistingInstances,
                PresenterCommandKind.DestroyPresenter => PerformerCommandRouteStrategy.ExistingInstances,
                PresenterCommandKind.SinkParamToAsset => PerformerCommandRouteStrategy.SingleRuntime,
                PresenterCommandKind.Extension => throw new InvalidOperationException(
                    $"Extension presenter command id {command.CommandKindId} must declare routeStrategy before rule routing."),
                _ => throw new InvalidOperationException($"Unsupported presenter command kind '{command.CommandKind}'."),
            };
        }

        private int ResolveStableId(Entity source, string scopeSourceName)
        {
            if (!World.IsAlive(source) || !World.Has<PresentationStableId>(source))
            {
                throw new InvalidOperationException($"Presenter command scopeSource={scopeSourceName} requires an alive entity with PresentationStableId.");
            }

            int stableId = World.Get<PresentationStableId>(source).Value;
            if (stableId <= 0)
            {
                throw new InvalidOperationException($"Presenter command scopeSource={scopeSourceName} requires a positive PresentationStableId, got {stableId}.");
            }

            return stableId;
        }

        private static bool IsGlobalEvent(PresentationEventKind kind)
        {
            return kind is PresentationEventKind.GlobalDayNight
                or PresentationEventKind.GlobalRegionChanged
                or PresentationEventKind.GlobalWeather;
        }

        private static float ResolveParamFloatValue(in PresenterCommand cmd, in PresentationEvent evt)
        {
            return cmd.ValueSource switch
            {
                PresenterCommandValueSource.EventKeyId => evt.KeyId,
                PresenterCommandValueSource.EventPayloadA => evt.PayloadA,
                PresenterCommandValueSource.EventPayloadB => evt.PayloadB,
                PresenterCommandValueSource.EventMagnitude => evt.Magnitude,
                PresenterCommandValueSource.EventFloatA => evt.FloatA,
                PresenterCommandValueSource.EventFloatB => evt.FloatB,
                PresenterCommandValueSource.EventFloatC => evt.FloatC,
                PresenterCommandValueSource.EventFloatD => evt.FloatD,
                PresenterCommandValueSource.EventPositionX => evt.Position.X,
                PresenterCommandValueSource.EventPositionY => evt.Position.Y,
                PresenterCommandValueSource.EventPositionZ => evt.Position.Z,
                _ => cmd.ParamValue,
            };
        }

        private static int ResolveParamIntValue(in PresenterCommand cmd, in PresentationEvent evt)
        {
            return cmd.ValueSource switch
            {
                PresenterCommandValueSource.EventKeyId => evt.KeyId,
                PresenterCommandValueSource.EventPayloadA => evt.PayloadA,
                PresenterCommandValueSource.EventPayloadB => evt.PayloadB,
                PresenterCommandValueSource.EventMagnitude => (int)evt.Magnitude,
                PresenterCommandValueSource.EventFloatA => (int)evt.FloatA,
                PresenterCommandValueSource.EventFloatB => (int)evt.FloatB,
                PresenterCommandValueSource.EventFloatC => (int)evt.FloatC,
                PresenterCommandValueSource.EventFloatD => (int)evt.FloatD,
                PresenterCommandValueSource.EventPositionX => (int)evt.Position.X,
                PresenterCommandValueSource.EventPositionY => (int)evt.Position.Y,
                PresenterCommandValueSource.EventPositionZ => (int)evt.Position.Z,
                _ => cmd.IntValue,
            };
        }

        private static Vector4 ResolveParamVectorValue(in PresenterCommand cmd, in PresentationEvent evt)
        {
            if (cmd.ParamLane != ParamLane.Vector ||
                (cmd.VectorXSource == PresenterCommandValueSource.Fixed &&
                 cmd.VectorYSource == PresenterCommandValueSource.Fixed &&
                 cmd.VectorZSource == PresenterCommandValueSource.Fixed &&
                 cmd.VectorWSource == PresenterCommandValueSource.Fixed))
            {
                return cmd.VectorValue;
            }

            return new Vector4(
                ResolveValueSource(cmd.VectorXSource, in evt, cmd.VectorValue.X),
                ResolveValueSource(cmd.VectorYSource, in evt, cmd.VectorValue.Y),
                ResolveValueSource(cmd.VectorZSource, in evt, cmd.VectorValue.Z),
                ResolveValueSource(cmd.VectorWSource, in evt, cmd.VectorValue.W));
        }

        private static float ResolveValueSource(PresenterCommandValueSource source, in PresentationEvent evt, float fixedValue)
        {
            return source switch
            {
                PresenterCommandValueSource.EventKeyId => evt.KeyId,
                PresenterCommandValueSource.EventPayloadA => evt.PayloadA,
                PresenterCommandValueSource.EventPayloadB => evt.PayloadB,
                PresenterCommandValueSource.EventMagnitude => evt.Magnitude,
                PresenterCommandValueSource.EventFloatA => evt.FloatA,
                PresenterCommandValueSource.EventFloatB => evt.FloatB,
                PresenterCommandValueSource.EventFloatC => evt.FloatC,
                PresenterCommandValueSource.EventFloatD => evt.FloatD,
                PresenterCommandValueSource.EventPositionX => evt.Position.X,
                PresenterCommandValueSource.EventPositionY => evt.Position.Y,
                PresenterCommandValueSource.EventPositionZ => evt.Position.Z,
                _ => fixedValue,
            };
        }

        /// <summary>
        /// Execute a Graph program and read F[0] as a float result (for dynamic param values).
        /// </summary>
        private float EvaluateGraphFloat(int graphProgramId, in PresentationEvent evt)
        {
            if (!_programs.TryGetProgram(graphProgramId, out var program))
            {
                throw new InvalidOperationException($"Presenter command paramGraphProgramId={graphProgramId} references an unknown graph program.");
            }

            if (program.Length == 0)
            {
                throw new InvalidOperationException($"Presenter command paramGraphProgramId={graphProgramId} has no instructions.");
            }

            _programs.RequireKind(graphProgramId, GraphKind.Score);
            ExecuteEventGraph(graphProgramId, GraphKind.Score, program, in evt);

            // Convention: F[0] holds the float result
            return _floatRegs[0];
        }
    }
}

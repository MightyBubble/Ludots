using Arch.Core;
using Arch.Core.Extensions;
using Arch.Buffer;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Components;
using Ludots.Core.Spatial;
using Ludots.Core.Mathematics;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class EffectLifetimeSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _activeEffectsQuery = new QueryDescription()
            .WithAll<GameplayEffect, EffectContext>();
        private static readonly QueryDescription _periodicEffectsQuery = new QueryDescription()
            .WithAll<GameplayEffect, EffectContext, EffectPeriodicTick>();
        private static readonly QueryDescription _expirationEffectsQuery = new QueryDescription()
            .WithAll<GameplayEffect, EffectContext, EffectExpirationCheck>();

        private readonly EffectRequestQueue _effectRequests;
        private readonly GasBudget _budget;
        private readonly Ludots.Core.Engine.IClock _clock;
        private readonly GasConditionRegistry _conditions;
        private readonly EffectTemplateRegistry _templates;
        private readonly ISpatialQueryService _spatialQueries;

        // ── Phase Graph execution (optional) ──
        private readonly EffectPhaseExecutor _phaseExecutor;
        private readonly Ludots.Core.NodeLibraries.GASGraph.IGraphRuntimeApi _graphApi;
        private readonly Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi _graphApiHost;
        private readonly TagOps _tagOps;

        private readonly CommandBuffer _commandBuffer = new CommandBuffer();

        private struct CallbackCommand
        {
            public int RootId;
            public Entity Source;
            public Entity Target;
            public Entity TargetContext;
            public int EffectTemplateId;
        }

        // ?? TargetResolver fan-out (period) ??
        private readonly List<FanOutCommand> _fanOutCommands = new(256);
        private readonly Entity[] _resolverBuffer = new Entity[256];
        private readonly BuiltinHandlerExecutionContext _builtinRuntime = new BuiltinHandlerExecutionContext();
        private int _fanOutDropped;


        private readonly List<CallbackCommand> _onPeriodCallbacks = new List<CallbackCommand>(64);
        private readonly List<CallbackCommand> _onExpireCallbacks = new List<CallbackCommand>(64);
        private readonly List<CallbackCommand> _onRemoveCallbacks = new List<CallbackCommand>(64);
        private bool _callbackBudgetFused;
        private readonly RootBudgetTable _callbackCreateBudget = new RootBudgetTable(16384);
        private int _callbackDropped;

        /// <summary>
        /// Records effects whose phase graphs need execution.
        /// Stores the effect's own template ID and context for phase graph execution.
        /// </summary>
        private struct PhaseGraphEntry
        {
            public int TemplateId;
            public int EffectEntityId;
            public int ClockTick;
            public Entity EffectEntity;
            public Components.EffectContext Context;
        }

        private readonly List<PhaseGraphEntry> _periodPhaseGraphs = new(64);
        private readonly List<PhaseGraphEntry> _expirePhaseGraphs = new(64);
        private readonly List<PhaseGraphEntry> _removePhaseGraphs = new(64);

        public EffectLifetimeSystem(World world, Ludots.Core.Engine.IClock clock, GasConditionRegistry conditions, EffectRequestQueue effectRequests = null, GasBudget budget = null, EffectTemplateRegistry templates = null, ISpatialQueryService spatialQueries = null, RuntimeEntitySpawnQueue spawnRequests = null, EffectPhaseExecutor phaseExecutor = null, Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi graphApi = null, TagOps tagOps = null, ProgressionRequirementEvaluator progressionEvaluator = null) : base(world)
        {
            _effectRequests = effectRequests;
            _budget = budget;
            _clock = clock;
            _conditions = conditions;
            _templates = templates;
            _spatialQueries = spatialQueries;
            _phaseExecutor = phaseExecutor;
            _graphApiHost = graphApi;
            _graphApi = graphApi;
            _tagOps = tagOps ?? new TagOps();
            _builtinRuntime.SpatialQueries = spatialQueries;
            _builtinRuntime.FanOutBudget = _callbackCreateBudget;
            _builtinRuntime.FanOutCommands = _fanOutCommands;
            _builtinRuntime.ResolverBuffer = _resolverBuffer;
            _builtinRuntime.SpawnRequests = spawnRequests;
            _builtinRuntime.ProgressionEvaluator = progressionEvaluator;

        }

        public override void Update(in float dt)
        {
            _callbackCreateBudget.NextFrame();
            _callbackDropped = 0;
            _fanOutDropped = 0;

            _onPeriodCallbacks.Clear();
            _onExpireCallbacks.Clear();
            _onRemoveCallbacks.Clear();
            _fanOutCommands.Clear();
            _periodPhaseGraphs.Clear();
            _expirePhaseGraphs.Clear();
            _removePhaseGraphs.Clear();

            var tickJob = new LifetimeTickJob
            {
                World = World,
                Clock = _clock,
                Conditions = _conditions,
                OnPeriodCallbacks = _onPeriodCallbacks,
                PeriodPhaseGraphs = _periodPhaseGraphs,
                Budget = _callbackCreateBudget
            };
            World.InlineEntityQuery<LifetimeTickJob, GameplayEffect, EffectContext>(in _periodicEffectsQuery, ref tickJob);


            var cleanupJob = new LifetimeCleanupJob
            {
                World = World,
                Clock = _clock,
                Conditions = _conditions,
                CommandBuffer = _commandBuffer,
                OnExpireCallbacks = _onExpireCallbacks,
                OnRemoveCallbacks = _onRemoveCallbacks,
                ExpirePhaseGraphs = _expirePhaseGraphs,
                RemovePhaseGraphs = _removePhaseGraphs,
                Budget = _callbackCreateBudget,
                TagOps = _tagOps,
                GasBudget = _budget
            };
            World.InlineEntityQuery<LifetimeCleanupJob, GameplayEffect, EffectContext>(in _expirationEffectsQuery, ref cleanupJob);

            // ── Execute Phase Graphs for period/expire/remove ──
            ExecutePhaseGraphsForEntries(_periodPhaseGraphs, EffectPhaseId.OnPeriod, _builtinRuntime);
            ExecutePhaseGraphsForEntries(_expirePhaseGraphs, EffectPhaseId.OnExpire, _builtinRuntime);
            ExecutePhaseGraphsForEntries(_removePhaseGraphs, EffectPhaseId.OnRemove, _builtinRuntime);

            PublishCallbacks(_onPeriodCallbacks);
            PublishCallbacks(_onExpireCallbacks);
            PublishCallbacks(_onRemoveCallbacks);
            TargetResolverFanOutHelper.PublishFanOutCommands(_fanOutCommands, _effectRequests);

            _callbackDropped = tickJob.Dropped + cleanupJob.Dropped;
            if (_callbackDropped > 0 && !_callbackBudgetFused)
            {
                _callbackBudgetFused = true;
                // Budget fused — telemetry exposed via _callbackBudgetFused + _callbackDropped
            }
            if (_callbackDropped > 0 && _budget != null)
            {
                _budget.DurationCallbackCreatesDropped += _callbackDropped;
            }
            if (_fanOutDropped > 0)
            {
                // Budget fused — telemetry exposed via _fanOutDropped
            }

            _commandBuffer.Playback(World, dispose: true);
        }

        private struct LifetimeTickJob : IForEachWithEntity<GameplayEffect, EffectContext>
        {
            public World World;
            public Ludots.Core.Engine.IClock Clock;
            public GasConditionRegistry Conditions;
            public List<CallbackCommand> OnPeriodCallbacks;
            public List<PhaseGraphEntry> PeriodPhaseGraphs;
            public RootBudgetTable Budget;
            public int Dropped;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref GameplayEffect effect, ref EffectContext context)
            {
                if (effect.State < EffectState.Committed) return;
                int now = Clock.Now(effect.ClockId.ToDomainId());

                if ((effect.LifetimeKind == EffectLifetimeKind.After || effect.LifetimeKind == EffectLifetimeKind.Infinite) && effect.PeriodTicks > 0)
                {
                    if (effect.NextTickAtTick == 0)
                    {
                        effect.NextTickAtTick = now + ResolveInitialPeriodOffset(entity, effect.PeriodTicks);
                    }

                    if (now >= effect.NextTickAtTick)
                    {
                        // OnPeriod callbacks are handled via Phase Graph bindings.


                        // Collect for Phase Graph execution (OnPeriod)
                        if (World.Has<EffectTemplateRef>(entity))
                        {
                            PeriodPhaseGraphs.Add(new PhaseGraphEntry
                            {
                                EffectEntity = entity,
                                TemplateId = World.Get<EffectTemplateRef>(entity).TemplateId,
                                EffectEntityId = entity.Id,
                                ClockTick = now,
                                Context = context
                            });
                        }

                        effect.NextTickAtTick = now + effect.PeriodTicks;
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int ResolveInitialPeriodOffset(Entity entity, int periodTicks)
            {
                if (periodTicks <= 1)
                {
                    return 1;
                }

                uint hash = (uint)entity.Id;
                hash ^= (uint)entity.Version * 0x9E3779B9u;
                hash ^= hash >> 16;
                return 1 + (int)(hash % (uint)periodTicks);
            }
        }

        private struct LifetimeCleanupJob : IForEachWithEntity<GameplayEffect, EffectContext>
        {
            public World World;
            public Ludots.Core.Engine.IClock Clock;
            public GasConditionRegistry Conditions;
            public CommandBuffer CommandBuffer;
            public List<CallbackCommand> OnExpireCallbacks;
            public List<CallbackCommand> OnRemoveCallbacks;
            public List<PhaseGraphEntry> ExpirePhaseGraphs;
            public List<PhaseGraphEntry> RemovePhaseGraphs;
            public RootBudgetTable Budget;
            public TagOps TagOps;
            public GasBudget GasBudget;
            public int Dropped;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref GameplayEffect effect, ref EffectContext context)
            {
                if (effect.State < EffectState.Committed) return;
                bool shouldExpire = false;
                bool cancelled = effect.CancelRequested || World.Has<EffectCancelled>(entity);
                int now = Clock.Now(effect.ClockId.ToDomainId());

                if (!cancelled && effect.LifetimeKind == EffectLifetimeKind.After)
                {
                    if (effect.ExpiresAtTick == 0)
                    {
                        effect.ExpiresAtTick = now + effect.TotalTicks;
                    }
                    if (now >= effect.ExpiresAtTick)
                    {
                        shouldExpire = true;
                    }
                }
                else if (effect.LifetimeKind == EffectLifetimeKind.Infinite)
                {
                    shouldExpire = false;
                }

                if (cancelled)
                {
                    shouldExpire = true;
                }

                if (!shouldExpire && effect.ExpireCondition.IsValid)
                {
                    ref readonly var cond = ref Conditions.Get(effect.ExpireCondition);
                    if (cond.Kind != GasConditionKind.None)
                    {
                        shouldExpire = GasConditionEvaluator.ShouldExpire(World, context.Target, in cond, TagOps);
                    }
                }

                if (!shouldExpire)
                {
                    return;
                }

                // OnExpire/OnRemove callbacks are handled via Phase Graph bindings.

                // Collect for Phase Graph execution (OnExpire + OnRemove)
                if (World.Has<EffectTemplateRef>(entity))
                {
                    int tplId = World.Get<EffectTemplateRef>(entity).TemplateId;
                    var entry = new PhaseGraphEntry { TemplateId = tplId, EffectEntityId = entity.Id, ClockTick = now, EffectEntity = entity, Context = context };
                    if (!cancelled)
                    {
                        ExpirePhaseGraphs.Add(entry);
                    }
                    RemovePhaseGraphs.Add(entry);
                }

                // Revoke granted tags from target before destroying
                if (World.Has<EffectGrantedTags>(entity) && World.IsAlive(context.Target))
                {
                    ref readonly var grantedTags = ref World.Get<EffectGrantedTags>(entity);
                    int stackCount = World.Has<EffectStack>(entity) ? World.Get<EffectStack>(entity).Count : 1;
                    EffectTagContributionHelper.RevokeFromEntity(World, context.Target, in grantedTags, stackCount, TagOps, GasBudget);
                }

                if (World.IsAlive(context.Target) && World.Has<ActiveEffectContainer>(context.Target))
                {
                    ref var container = ref World.Get<ActiveEffectContainer>(context.Target);
                    container.Remove(entity);
                    if (effect.AggregatesModifiers && !World.Has<AttributeAggregateDirty>(context.Target))
                    {
                        CommandBuffer.Add(context.Target, new AttributeAggregateDirty());
                    }
                }

                CommandBuffer.Destroy(entity);
            }
        }


        private void PublishCallbacks(List<CallbackCommand> callbacks)
        {
            if (_effectRequests == null || callbacks.Count == 0) return;

            for (int i = 0; i < callbacks.Count; i++)
            {
                var cmd = callbacks[i];
                _effectRequests.Publish(new EffectRequest
                {
                    RootId = cmd.RootId,
                    Source = cmd.Source,
                    Target = cmd.Target,
                    TargetContext = cmd.TargetContext,
                    TemplateId = cmd.EffectTemplateId
                });
            }
        }

        /// <summary>
        /// Execute phase graphs for all effects that triggered a lifecycle event.
        /// Uses the effect's own template for behavior/config lookup.
        /// Also handles listener unregistration on OnExpire/OnRemove.
        /// </summary>
        private void ExecutePhaseGraphsForEntries(List<PhaseGraphEntry> entries, EffectPhaseId phase, BuiltinHandlerExecutionContext? builtinRuntime = null)
        {
            if (_phaseExecutor == null || _graphApi == null || _templates == null) return;

            for (int i = 0; i < entries.Count; i++)
            {
                builtinRuntime?.ResetPerEffect();
                var entry = entries[i];
                if (entry.TemplateId <= 0) continue;
                if (!_templates.TryGetRef(entry.TemplateId, out int tplIdx)) continue;
                ref readonly var tpl = ref _templates.GetRef(tplIdx);

                // Build merged config: template params + caller overrides
                var mergedConfig = ConfigParamsMerger.BuildMergedConfig(World, entry.EffectEntity, in tpl.ConfigParams);

                if (_graphApiHost != null && mergedConfig.Count > 0)
                    _graphApiHost.SetConfigContext(in mergedConfig);

                _phaseExecutor.ExecutePhase(
                    World, _graphApi,
                    entry.Context.Source, entry.Context.Target, entry.Context.TargetContext,
                    default,
                    phase,
                    in tpl.PhaseGraphBindings,
                    tpl.PresetType,
                    tpl.TagId,
                    entry.TemplateId,
                    in mergedConfig,
                    builtinRuntime,
                    BuildExecutionSeed(entry.EffectEntity, phase, entry.TemplateId, entry.ClockTick, entry.Context));

                if (builtinRuntime != null)
                {
                    _fanOutDropped += builtinRuntime.DroppedCount;
                }


                _graphApiHost?.ClearConfigContext();

                // Unregister listeners on OnExpire / OnRemove
                if (phase == EffectPhaseId.OnExpire || phase == EffectPhaseId.OnRemove)
                {
                    UnregisterListeners(entry.Context, entry.EffectEntityId);
                }
            }
        }

        /// <summary>
        /// Remove all phase listeners owned by the given effect template from target and caster entities.
        /// </summary>
        private void UnregisterListeners(in Components.EffectContext context, int ownerEffectId)
        {
            if (World.IsAlive(context.Target) && World.Has<EffectPhaseListenerBuffer>(context.Target))
            {
                ref var buf = ref World.Get<EffectPhaseListenerBuffer>(context.Target);
                buf.RemoveByOwner(ownerEffectId);
            }
            if (World.IsAlive(context.Source) && World.Has<EffectPhaseListenerBuffer>(context.Source))
            {
                ref var buf = ref World.Get<EffectPhaseListenerBuffer>(context.Source);
                buf.RemoveByOwner(ownerEffectId);
            }
        }

        private static uint BuildExecutionSeed(Entity effectEntity, EffectPhaseId phase, int templateId, int clockTick, in Components.EffectContext context)
        {
            uint hash = 2166136261u;
            hash = Mix(hash, effectEntity.Id);
            hash = Mix(hash, effectEntity.Version);
            hash = Mix(hash, context.Source.Id);
            hash = Mix(hash, context.Target.Id);
            hash = Mix(hash, context.TargetContext.Id);
            hash = Mix(hash, templateId);
            hash = Mix(hash, (int)phase);
            hash = Mix(hash, clockTick);
            return hash == 0u ? 1u : hash;
        }

        private static uint Mix(uint hash, int value)
        {
            return (hash ^ unchecked((uint)value)) * 16777619u;
        }
    }
}

using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Components;
using Ludots.Core.Spatial;
using Ludots.Core.Mathematics;
using Ludots.Core.Vision;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class EffectLifetimeSystem : BaseSystem<World, float>, ITimeSlicedSystem
    {
        public const string ResetAfterExternalCommitError =
            "GAS.EFFECT_LIFETIME.ERR.ResetAfterExternalCommit";

        private static readonly QueryDescription _activeEffectsQuery = new QueryDescription()
            .WithAll<GameplayEffect, EffectContext>();
        private readonly EffectRequestQueue _effectRequests;
        private readonly GasBudget _budget;
        private readonly Ludots.Core.Engine.IClock _clock;
        private readonly GasConditionRegistry _conditions;
        private readonly EffectTemplateRegistry _templates;
        private readonly ISpatialQueryService _spatialQueries;
        private readonly GasPresentationEventBuffer _presentationEvents;

        // ── Phase Graph execution (optional) ──
        private readonly EffectPhaseExecutor _phaseExecutor;
        private readonly Ludots.Core.NodeLibraries.GASGraph.IGraphRuntimeApi _graphApi;
        private readonly Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi _graphApiHost;
        private readonly TagOps _tagOps;
        private readonly OrderTypeRegistry? _orderTypeRegistry;
        private readonly OrderRuleRegistry? _orderRuleRegistry;
        private readonly int _stepRateHz;

        // ?? TargetResolver fan-out (period) ??
        private readonly FanOutCommandBuffer _fanOutCommands;
        private readonly RootBudgetTable _fanOutBudget;
        // An injected budget is advanced by the effect-loop owner once per processing transaction.
        private readonly bool _ownsFanOutBudget;
        private readonly Entity[] _resolverBuffer = new Entity[256];
        private readonly BuiltinHandlerExecutionContext _builtinRuntime = new BuiltinHandlerExecutionContext();
        private readonly EffectPhaseSideEffectTransaction _phaseTransaction;
        private int _fanOutDropped;


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

        private readonly List<PhaseGraphEntry> _periodPhaseGraphs;
        private readonly List<PhaseGraphEntry> _expirePhaseGraphs;
        private readonly List<PhaseGraphEntry> _removePhaseGraphs;
        private readonly List<Entity> _dirtyTargets;
        private readonly List<Entity> _effectsToDestroy;
        private readonly Entity[] _effectSnapshot;
        private readonly Entity[] _rollbackEffectEntities;
        private readonly GameplayEffect[] _rollbackEffectValues;
        private readonly TargetRollbackEntry[] _rollbackTargets;

        private struct TargetRollbackEntry
        {
            public Entity Target;
            public byte HasActiveEffects;
            public byte HasTagState;
            public ActiveEffectContainer ActiveEffects;
            public GameplayTagContainer Tags;
            public TagCountContainer TagCounts;
            public DirtyFlags DirtyFlags;
        }

        private enum LifetimeStage : byte
        {
            Scan = 0,
            PeriodGraphs = 1,
            ExpireGraphs = 2,
            RemoveGraphs = 3,
            FanOut = 4,
            DirtyTargets = 5,
            DestroyEffects = 6,
            Done = 7,
        }

        private bool _sliceActive;
        private LifetimeStage _stage;
        private int _snapshotCount;
        private int _cursor;
        private int _rollbackEffectCount;
        private int _rollbackTargetCount;
        private int _presentationWriteCheckpoint;
        private DirtyEntityQueue.WriteCheckpoint _dirtyEntityWriteCheckpoint;
        private bool _hasDirtyEntityWriteCheckpoint;
        private bool _externalWorkCommitted;
        private bool _graphTransactionBound;

        public int MaxWorkUnitsPerSlice { get; set; } = int.MaxValue;
        public int SnapshotCapacity => _effectSnapshot.Length;
        public int LastSliceProcessed { get; private set; }
        public int DeferredEntityCount => _sliceActive && _stage == LifetimeStage.Scan ? _snapshotCount - _cursor : 0;

        public EffectLifetimeSystem(World world, Ludots.Core.Engine.IClock clock, GasConditionRegistry conditions, int snapshotCapacity, int fanOutCommandCapacity, EffectRequestQueue effectRequests = null, GasBudget budget = null, EffectTemplateRegistry templates = null, ISpatialQueryService spatialQueries = null, RuntimeEntitySpawnQueue spawnRequests = null, RuntimeEntityLifecycleQueue lifecycleRequests = null, EntityLifecycleRuntimeServices lifecycleServices = null, EffectPhaseExecutor phaseExecutor = null, Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi graphApi = null, TagOps tagOps = null, ExchangeRuntime exchangeRuntime = null, ProgressionRequirementEvaluator progressionEvaluator = null, OrderTypeRegistry orderTypeRegistry = null, OrderRuleRegistry orderRuleRegistry = null, int stepRateHz = 30, RelationshipRuntime relationshipRuntime = null, GasPresentationEventBuffer presentationEvents = null, KnowledgeAreaRevealRuntime knowledgeAreaRevealRuntime = null, OrderQueue orderIntake = null, RootBudgetTable fanOutBudget = null) : base(world)
        {
            if (snapshotCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(snapshotCapacity));
            }

            _effectSnapshot = new Entity[snapshotCapacity];
            _fanOutCommands = new FanOutCommandBuffer(fanOutCommandCapacity);
            _fanOutBudget = fanOutBudget ?? new RootBudgetTable(16384);
            _ownsFanOutBudget = fanOutBudget == null;
            _periodPhaseGraphs = new List<PhaseGraphEntry>(snapshotCapacity);
            _expirePhaseGraphs = new List<PhaseGraphEntry>(snapshotCapacity);
            _removePhaseGraphs = new List<PhaseGraphEntry>(snapshotCapacity);
            _dirtyTargets = new List<Entity>(snapshotCapacity);
            _effectsToDestroy = new List<Entity>(snapshotCapacity);
            _rollbackEffectEntities = new Entity[snapshotCapacity];
            _rollbackEffectValues = new GameplayEffect[snapshotCapacity];
            _rollbackTargets = new TargetRollbackEntry[snapshotCapacity];
            _effectRequests = effectRequests;
            _budget = budget;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _conditions = conditions;
            _templates = templates;
            _spatialQueries = spatialQueries;
            _presentationEvents = presentationEvents;
            _phaseExecutor = phaseExecutor;
            _graphApi = graphApi;
            _graphApiHost = graphApi;
            _tagOps = tagOps;
            _builtinRuntime.SpatialQueries = spatialQueries;
            _builtinRuntime.FanOutBudget = _fanOutBudget;
            _builtinRuntime.FanOutCommands = _fanOutCommands;
            _builtinRuntime.ResolverBuffer = _resolverBuffer;
            _builtinRuntime.SpawnRequests = spawnRequests;
            _builtinRuntime.LifecycleRequests = lifecycleRequests;
            _builtinRuntime.LifecycleServices = lifecycleServices;
            _builtinRuntime.Exchange = exchangeRuntime;
            _builtinRuntime.Relationships = relationshipRuntime;
            _builtinRuntime.ProgressionEvaluator = progressionEvaluator;
            _builtinRuntime.KnowledgeAreaReveal = knowledgeAreaRevealRuntime;
            _builtinRuntime.TagOps = _tagOps;
            _builtinRuntime.OrderIntake = orderIntake;
            _orderTypeRegistry = orderTypeRegistry;
            _orderRuleRegistry = orderRuleRegistry;
            _stepRateHz = GasStepRate.RequirePositive(stepRateHz, nameof(EffectLifetimeSystem));
            _phaseTransaction = new EffectPhaseSideEffectTransaction(
                world,
                tagOps,
                effectRequests,
                spawnRequests,
                presentationEvents,
                snapshotCapacity);
        }

        private void RefreshBuiltinOrderContext()
        {
            _builtinRuntime.OrderTypeRegistry = _orderTypeRegistry;
            _builtinRuntime.OrderRuleRegistry = _orderRuleRegistry;
            _builtinRuntime.StepRateHz = _stepRateHz;
            _builtinRuntime.CurrentStep = _clock.Now(Ludots.Core.Engine.ClockDomainId.Step);
        }

        public override void Update(in float dt)
        {
            int previous = MaxWorkUnitsPerSlice;
            MaxWorkUnitsPerSlice = int.MaxValue;
            while (!UpdateSlice(dt, int.MaxValue)) { }
            MaxWorkUnitsPerSlice = previous;
        }

        public bool UpdateSlice(float dt, int timeBudgetMs)
        {
            try
            {
                return UpdateSliceCore(dt, timeBudgetMs);
            }
            catch
            {
                if (_sliceActive && !_externalWorkCommitted)
                {
                    AbortUncommittedSlice();
                }
                throw;
            }
        }

        private bool UpdateSliceCore(float dt, int timeBudgetMs)
        {
            LastSliceProcessed = 0;
            if (!_sliceActive)
            {
                BeginSlice();
            }

            int workUnits = 0;
            while (workUnits < MaxWorkUnitsPerSlice)
            {
                if (_stage == LifetimeStage.Scan)
                {
                    if (_cursor >= _snapshotCount)
                    {
                        BeginPhaseTransaction();
                        _stage = LifetimeStage.PeriodGraphs;
                        _cursor = 0;
                        continue;
                    }

                    Entity entity = _effectSnapshot[_cursor++];
                    CountWork(ref workUnits);
                    if (!World.IsAlive(entity) || !World.Has<GameplayEffect>(entity) || !World.Has<EffectContext>(entity))
                    {
                        continue;
                    }

                    ref GameplayEffect effect = ref World.Get<GameplayEffect>(entity);
                    ref EffectContext context = ref World.Get<EffectContext>(entity);
                    CaptureEffectRollback(entity, in effect);
                    if (World.Has<EffectPeriodicTick>(entity))
                    {
                        ProcessPeriod(entity, ref effect, ref context);
                    }
                    if (World.Has<EffectExpirationCheck>(entity))
                    {
                        CaptureTargetRollback(context.Target);
                        ProcessExpiration(entity, ref effect, ref context);
                    }
                    continue;
                }

                if (ProcessPhaseStage(LifetimeStage.PeriodGraphs, LifetimeStage.ExpireGraphs, _periodPhaseGraphs, EffectPhaseId.OnPeriod, ref workUnits) ||
                    ProcessPhaseStage(LifetimeStage.ExpireGraphs, LifetimeStage.RemoveGraphs, _expirePhaseGraphs, EffectPhaseId.OnExpire, ref workUnits) ||
                    ProcessPhaseStage(LifetimeStage.RemoveGraphs, LifetimeStage.FanOut, _removePhaseGraphs, EffectPhaseId.OnRemove, ref workUnits))
                {
                    continue;
                }

                if (_stage == LifetimeStage.FanOut)
                {
                    if (_cursor < _fanOutCommands.Count)
                    {
                        FanOutCommand command = _fanOutCommands[_cursor++];
                        _phaseTransaction.StageFanOutCommand(in command);
                        CountWork(ref workUnits);
                        continue;
                    }
                    AdvanceTo(LifetimeStage.DirtyTargets);
                    continue;
                }

                if (_stage == LifetimeStage.DirtyTargets)
                {
                    if (_cursor < _dirtyTargets.Count)
                    {
                        Entity target = _dirtyTargets[_cursor++];
                        if (World.IsAlive(target))
                        {
                            _phaseTransaction.StageAggregateDirty(target);
                        }
                        CountWork(ref workUnits);
                        if (_cursor >= _dirtyTargets.Count)
                        {
                            CommitPhaseTransaction();
                            _externalWorkCommitted = true;
                            AdvanceTo(LifetimeStage.DestroyEffects);
                        }
                        continue;
                    }
                    CommitPhaseTransaction();
                    _externalWorkCommitted = true;
                    AdvanceTo(LifetimeStage.DestroyEffects);
                    continue;
                }

                if (_stage == LifetimeStage.DestroyEffects)
                {
                    if (_cursor < _effectsToDestroy.Count)
                    {
                        Entity effect = _effectsToDestroy[_cursor++];
                        if (World.IsAlive(effect))
                        {
                            _externalWorkCommitted = true;
                            World.Destroy(effect);
                        }
                        CountWork(ref workUnits);
                        continue;
                    }
                    _stage = LifetimeStage.Done;
                    continue;
                }

                CompleteSlice();
                return true;
            }

            return false;
        }

        public void ResetSlice()
        {
            if (_sliceActive)
            {
                if (_externalWorkCommitted)
                {
                    CompleteCommittedCleanup();
                }
                else
                {
                    RollbackPhaseTransaction();
                    RollbackScannedWork();
                }
            }

            _sliceActive = false;
            _stage = LifetimeStage.Scan;
            _snapshotCount = 0;
            _cursor = 0;
            ClearPendingWork();
            ClearRollbackState();
        }

        private void CompleteCommittedCleanup()
        {
            if (_stage == LifetimeStage.DestroyEffects)
            {
                while (_cursor < _effectsToDestroy.Count)
                {
                    Entity effect = _effectsToDestroy[_cursor++];
                    if (World.IsAlive(effect))
                    {
                        World.Destroy(effect);
                    }
                }
                _stage = LifetimeStage.Done;
            }
        }

        private void AbortUncommittedSlice()
        {
            RollbackPhaseTransaction();
            RollbackScannedWork();
            _sliceActive = false;
            _stage = LifetimeStage.Scan;
            _snapshotCount = 0;
            _cursor = 0;
            ClearPendingWork();
            ClearRollbackState();
        }

        private void BeginSlice()
        {
            RefreshBuiltinOrderContext();
            if (_ownsFanOutBudget)
            {
                _fanOutBudget.NextFrame();
            }
            _fanOutDropped = 0;
            ClearPendingWork();
            ClearRollbackState();
            _presentationWriteCheckpoint = _presentationEvents?.Count ?? 0;
            if (_tagOps != null)
            {
                _dirtyEntityWriteCheckpoint = _tagOps.DirtyEntities.CaptureWriteCheckpoint();
                _hasDirtyEntityWriteCheckpoint = true;
            }

            _snapshotCount = World.CountEntities(in _activeEffectsQuery);
            if (_snapshotCount > _effectSnapshot.Length)
            {
                throw new InvalidOperationException(
                    $"GAS.EFFECT_LIFETIME.ERR.SnapshotCapacityExceeded: required={_snapshotCount}, capacity={_effectSnapshot.Length}.");
            }

            World.GetEntities(in _activeEffectsQuery, _effectSnapshot);
            _cursor = 0;
            _stage = LifetimeStage.Scan;
            _sliceActive = true;
        }

        private void ClearPendingWork()
        {
            _fanOutCommands.Clear();
            _periodPhaseGraphs.Clear();
            _expirePhaseGraphs.Clear();
            _removePhaseGraphs.Clear();
            _dirtyTargets.Clear();
            _effectsToDestroy.Clear();
        }

        private void CaptureEffectRollback(Entity entity, in GameplayEffect effect)
        {
            _rollbackEffectEntities[_rollbackEffectCount] = entity;
            _rollbackEffectValues[_rollbackEffectCount] = effect;
            _rollbackEffectCount++;
        }

        private void CaptureTargetRollback(Entity target)
        {
            if (!World.IsAlive(target))
            {
                return;
            }

            ref TargetRollbackEntry entry = ref _rollbackTargets[_rollbackTargetCount++];
            entry = default;
            entry.Target = target;
            if (World.Has<ActiveEffectContainer>(target))
            {
                entry.HasActiveEffects = 1;
                entry.ActiveEffects = World.Get<ActiveEffectContainer>(target);
            }
            if (World.Has<GameplayTagContainer>(target) &&
                World.Has<TagCountContainer>(target) &&
                World.Has<DirtyFlags>(target))
            {
                entry.HasTagState = 1;
                entry.Tags = World.Get<GameplayTagContainer>(target);
                entry.TagCounts = World.Get<TagCountContainer>(target);
                entry.DirtyFlags = World.Get<DirtyFlags>(target);
            }
        }

        private void RollbackScannedWork()
        {
            _presentationEvents?.RollbackWrites(_presentationWriteCheckpoint);
            if (_hasDirtyEntityWriteCheckpoint)
            {
                _tagOps!.DirtyEntities.RollbackWrites(in _dirtyEntityWriteCheckpoint);
            }

            for (int i = _rollbackTargetCount - 1; i >= 0; i--)
            {
                ref readonly TargetRollbackEntry entry = ref _rollbackTargets[i];
                if (!World.IsAlive(entry.Target))
                {
                    throw new InvalidOperationException(
                        $"{ResetAfterExternalCommitError}: target={entry.Target.Id} destroyed during scan.");
                }
                if (entry.HasActiveEffects != 0)
                {
                    if (!World.Has<ActiveEffectContainer>(entry.Target))
                    {
                        throw new InvalidOperationException(
                            $"{ResetAfterExternalCommitError}: target={entry.Target.Id} lost ActiveEffectContainer.");
                    }
                    World.Get<ActiveEffectContainer>(entry.Target) = entry.ActiveEffects;
                }
                if (entry.HasTagState != 0)
                {
                    TagOps.RequireTagState(World, entry.Target);
                    World.Get<GameplayTagContainer>(entry.Target) = entry.Tags;
                    World.Get<TagCountContainer>(entry.Target) = entry.TagCounts;
                    World.Get<DirtyFlags>(entry.Target) = entry.DirtyFlags;
                }
            }

            for (int i = _rollbackEffectCount - 1; i >= 0; i--)
            {
                Entity effect = _rollbackEffectEntities[i];
                if (!World.IsAlive(effect) || !World.Has<GameplayEffect>(effect))
                {
                    throw new InvalidOperationException(
                        $"{ResetAfterExternalCommitError}: effect={effect.Id} destroyed during scan.");
                }
                World.Get<GameplayEffect>(effect) = _rollbackEffectValues[i];
            }
        }

        private void ClearRollbackState()
        {
            _rollbackEffectCount = 0;
            _rollbackTargetCount = 0;
            _presentationWriteCheckpoint = 0;
            _dirtyEntityWriteCheckpoint = default;
            _hasDirtyEntityWriteCheckpoint = false;
            _externalWorkCommitted = false;
        }

        private void BeginPhaseTransaction()
        {
            _phaseTransaction.Begin();
            _builtinRuntime.EffectSideEffects = _phaseTransaction;
            if (_graphApiHost != null)
            {
                _graphApiHost.BeginEffectSideEffectTransaction(_phaseTransaction);
                _graphTransactionBound = true;
            }
        }

        private void CommitPhaseTransaction()
        {
            try
            {
                _phaseTransaction.Commit();
            }
            finally
            {
                UnbindPhaseTransaction();
            }
        }

        private void RollbackPhaseTransaction()
        {
            try
            {
                _phaseTransaction.Rollback();
            }
            finally
            {
                UnbindPhaseTransaction();
            }
        }

        private void UnbindPhaseTransaction()
        {
            if (_graphTransactionBound)
            {
                _graphApiHost!.EndEffectSideEffectTransaction(_phaseTransaction);
                _graphTransactionBound = false;
            }
            _builtinRuntime.EffectSideEffects = null;
        }

        private void ProcessPeriod(Entity entity, ref GameplayEffect effect, ref EffectContext context)
        {
            var job = new LifetimeTickJob
            {
                World = World,
                Clock = _clock,
                Conditions = _conditions,
                PeriodPhaseGraphs = _periodPhaseGraphs,
            };
            job.Update(entity, ref effect, ref context);
        }

        private void ProcessExpiration(Entity entity, ref GameplayEffect effect, ref EffectContext context)
        {
            var job = new LifetimeCleanupJob
            {
                World = World,
                Clock = _clock,
                Conditions = _conditions,
                DirtyTargets = _dirtyTargets,
                EffectsToDestroy = _effectsToDestroy,
                ExpirePhaseGraphs = _expirePhaseGraphs,
                RemovePhaseGraphs = _removePhaseGraphs,
                PresentationEvents = _presentationEvents,
                TagOps = _tagOps,
                GasBudget = _budget,
            };
            job.Update(entity, ref effect, ref context);
        }

        private bool ProcessPhaseStage(
            LifetimeStage current,
            LifetimeStage next,
            List<PhaseGraphEntry> entries,
            EffectPhaseId phase,
            ref int workUnits)
        {
            if (_stage != current) return false;
            if (_cursor < entries.Count)
            {
                PhaseGraphEntry entry = entries[_cursor++];
                ExecutePhaseGraphEntry(in entry, phase, _builtinRuntime);
                CountWork(ref workUnits);
                return true;
            }
            AdvanceTo(next);
            return true;
        }

        private void CompleteSlice()
        {
            if (_fanOutDropped > 0 && _budget != null)
            {
                _budget.EffectLifetimeFanOutDropped += _fanOutDropped;
            }

            _sliceActive = false;
            _stage = LifetimeStage.Scan;
            _snapshotCount = 0;
            _cursor = 0;
        }

        private void AdvanceTo(LifetimeStage stage)
        {
            _stage = stage;
            _cursor = 0;
        }

        private void CountWork(ref int workUnits)
        {
            workUnits++;
            LastSliceProcessed++;
        }

        private struct LifetimeTickJob : IForEachWithEntity<GameplayEffect, EffectContext>
        {
            public World World;
            public Ludots.Core.Engine.IClock Clock;
            public GasConditionRegistry Conditions;
            public List<PhaseGraphEntry> PeriodPhaseGraphs;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref GameplayEffect effect, ref EffectContext context)
            {
                if (effect.State < EffectState.Committed) return;
                int now = GasClockRuntime.Now(World, Clock, effect.ClockId, context.Target, "Effect period");

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
            public List<Entity> DirtyTargets;
            public List<Entity> EffectsToDestroy;
            public List<PhaseGraphEntry> ExpirePhaseGraphs;
            public List<PhaseGraphEntry> RemovePhaseGraphs;
            public GasPresentationEventBuffer PresentationEvents;
            public TagOps TagOps;
            public GasBudget GasBudget;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref GameplayEffect effect, ref EffectContext context)
            {
                if (effect.State < EffectState.Committed) return;
                bool shouldExpire = false;
                bool cancelled = effect.CancelRequested || World.Has<EffectCancelled>(entity);
                int now = GasClockRuntime.Now(World, Clock, effect.ClockId, context.Target, "Effect expiration");

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
                    PresentationEvents?.Publish(new GasPresentationEvent
                    {
                        Kind = cancelled ? GasPresentationEventKind.EffectCancelled : GasPresentationEventKind.EffectExpired,
                        Actor = context.Source,
                        Target = context.Target,
                        EffectTemplateId = tplId
                    });
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
                        DirtyTargets.Add(context.Target);
                    }
                }

                EffectsToDestroy.Add(entity);
            }
        }


        private bool ExecutePhaseGraphEntry(
            in PhaseGraphEntry entry,
            EffectPhaseId phase,
            BuiltinHandlerExecutionContext? builtinRuntime)
        {
            if (_phaseExecutor == null || _graphApi == null || _templates == null) return false;

            builtinRuntime?.ResetPerEffect();
            if (entry.TemplateId <= 0) return false;
            if (!_templates.TryGetRef(entry.TemplateId, out int tplIdx)) return false;
            ref readonly var tpl = ref _templates.GetRef(tplIdx);

            var mergedConfig = ConfigParamsMerger.BuildMergedConfig(World, entry.EffectEntity, in tpl.ConfigParams);
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
                BuildExecutionSeed(entry.EffectEntity, phase, entry.TemplateId, entry.ClockTick, entry.Context),
                entry.Context.RootId);

            if (builtinRuntime != null)
            {
                _fanOutDropped += builtinRuntime.DroppedCount;
            }

            if (phase == EffectPhaseId.OnExpire || phase == EffectPhaseId.OnRemove)
            {
                _phaseTransaction.StageListenerRemoval(entry.Context, entry.EffectEntityId);
            }
            return true;
        }

        public override void Dispose()
        {
            ResetSlice();
            _phaseTransaction.Dispose();
            base.Dispose();
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

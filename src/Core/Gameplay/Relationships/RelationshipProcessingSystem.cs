using System;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Components;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Relationships
{
    public sealed class RelationshipProcessingSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly RelationshipChangeBuffer _changeBuffer;
        private readonly RelationshipCallbackProcessor _callbackProcessor;
        private readonly RelationshipSynergyProcessor _synergyProcessor;

        public RelationshipProcessingSystem(
            GameEngine engine,
            RelationshipChangeBuffer changeBuffer,
            TagOps tagOps,
            TeamEntityLookup teamLookup)
        {
            _engine = engine;
            _changeBuffer = changeBuffer;
            _callbackProcessor = new RelationshipCallbackProcessor(engine.World, tagOps, teamLookup);
            _synergyProcessor = new RelationshipSynergyProcessor(engine.World, tagOps, teamLookup);
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            RelationshipCatalogRuntime? catalogRuntime = _engine.GetService(CoreServiceKeys.RelationshipCatalogRuntime);
            if (catalogRuntime == null)
            {
                _changeBuffer.Clear();
                return;
            }

            if (_changeBuffer.Count > 0)
            {
                RelationshipRuntime relationshipRuntime = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                    ?? throw new InvalidOperationException("RelationshipProcessingSystem requires RelationshipRuntime.");
                RelationshipBandRegistry bandRegistry = _engine.GetService(CoreServiceKeys.RelationshipBandRegistry)
                    ?? throw new InvalidOperationException("RelationshipProcessingSystem requires RelationshipBandRegistry.");

                // 前缀分批：图在事件回调里重入建边/改值会产生新记录，追加分批继续处理，
                // 不得被一次 Clear 吞掉（重入变更是本能力的主用法）。上限防自激环。
                int processed = 0;
                int guard = _changeBuffer.Count * 8 + 1024;
                while (processed < _changeBuffer.Count)
                {
                    int batchLength = _changeBuffer.Count - processed;
                    ReadOnlySpan<RelationshipChangeRecord> batch = _changeBuffer.GetSpan().Slice(processed, batchLength);
                    ApplyMetricBands(relationshipRuntime, bandRegistry, batch);
                    if (catalogRuntime.Callbacks.Count > 0)
                    {
                        _callbackProcessor.Process(_engine, catalogRuntime, batch);
                    }

                    PublishChangeEvents(batch);
                    if (catalogRuntime.Synergies.Count > 0)
                    {
                        _synergyProcessor.Evaluate(_engine, catalogRuntime);
                    }

                    processed += batchLength;
                    if (processed > guard)
                    {
                        // 抛之前清空：宿主若按拍捕获异常继续跑，不清会导致已处理记录逐拍重放
                        // （回调重抹、事件重发）且 guard 随更大的 Count 重算而逐拍升级——
                        // 熔断器不得放大它要防的故障。
                        _changeBuffer.Clear();
                        throw new InvalidOperationException(
                            "Relationship change reentrancy exceeded guard (" + guard + "); a graph is likely self-triggering relation mutations without refire limits.");
                    }
                }

                _changeBuffer.Clear();
            }

        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void ApplyMetricBands(
            RelationshipRuntime relationshipRuntime,
            RelationshipBandRegistry bandRegistry,
            ReadOnlySpan<RelationshipChangeRecord> changes)
        {
            var bands = bandRegistry.Bands;
            if (bands.Count == 0)
            {
                return;
            }

            for (int changeIndex = 0; changeIndex < changes.Length; changeIndex++)
            {
                ref readonly RelationshipChangeRecord change = ref changes[changeIndex];
                if (change.Kind != RelationshipChangeKind.MetricChanged ||
                    change.MetricId < 0 ||
                    !_engine.World.IsAlive(change.Source) ||
                    !_engine.World.IsAlive(change.Target))
                {
                    continue;
                }

                for (int bandIndex = 0; bandIndex < bands.Count; bandIndex++)
                {
                    RelationshipBandDefinition band = bands[bandIndex];
                    if (band.TypeId != change.TypeId || band.MetricId != change.MetricId)
                    {
                        continue;
                    }

                    bool oldMatches = MatchesBand(change.OldValue, band);
                    bool newMatches = MatchesBand(change.NewValue, band);
                    if (oldMatches != newMatches)
                    {
                        relationshipRuntime.SetFlag(change.Source, change.Target, change.TypeId, band.FlagId, newMatches);
                    }
                }
            }
        }

        private static bool MatchesBand(short value, in RelationshipBandDefinition band)
        {
            return band.Comparison switch
            {
                RelationshipBandComparison.GreaterOrEqual => value >= band.Threshold,
                RelationshipBandComparison.LessOrEqual => value <= band.Threshold,
                _ => throw new InvalidOperationException($"Unknown relationship band comparison '{band.Comparison}'."),
            };
        }

        /// <summary>
        /// 关系变更的第二消费面：把缓冲记录提升为 trigger 事件（与实体生命周期观察者同节奏——
        /// 变更下一拍统一发）。事件按 source 实体的 MapEntity 归属地图；无地图归属的边不发
        /// （对齐 MapEntityLifecycleObserverSystem 的 MapEntity 范围合同）。装载期物化走的
        /// EnsureLink/SetMetric 同样进缓冲，因此初始边与运行时变更走同一条事件路径。
        /// </summary>
        private void PublishChangeEvents(ReadOnlySpan<RelationshipChangeRecord> changes)
        {
            var sessions = _engine.MapSessions;
            if (sessions == null)
            {
                return;
            }

            for (int i = 0; i < changes.Length; i++)
            {
                ref readonly RelationshipChangeRecord change = ref changes[i];
                if (!_engine.World.IsAlive(change.Source) || !_engine.World.IsAlive(change.Target))
                {
                    continue;
                }

                if (!_engine.World.TryGet(change.Source, out MapEntity mapEntity))
                {
                    continue;
                }

                MapSession? session = sessions.GetSession(mapEntity.MapId);
                if (session == null)
                {
                    continue;
                }

                EventKey eventKey = change.Kind switch
                {
                    RelationshipChangeKind.LinkAdded => GameEvents.RelationLinkAdded,
                    RelationshipChangeKind.LinkRemoved => GameEvents.RelationLinkRemoved,
                    RelationshipChangeKind.MetricChanged => GameEvents.RelationMetricChanged,
                    RelationshipChangeKind.FlagChanged => GameEvents.RelationFlagChanged,
                    _ => throw new InvalidOperationException($"Unknown relationship change kind '{change.Kind}'."),
                };

                // 零订阅早退：每条记录的 CreateContext + schema 校验是固定成本，
                // 大图装载/所有权 churn 在无人订阅 Relation* 事件时必须免付。
                if (!_engine.TriggerManager.HasMapEventSubscribers(session.MapId, eventKey))
                {
                    continue;
                }

                ScriptContext context = _engine.CreateContext();
                context.Set(CoreServiceKeys.MapId, session.MapId);
                context.Set(CoreServiceKeys.MapSession, session);
                context.Set(CoreServiceKeys.MapTags, session.MapConfig?.Tags ?? new System.Collections.Generic.List<string>());
                context.Set(MapTriggerEventPayloadKeys.SourceEntity, change.Source);
                context.Set(MapTriggerEventPayloadKeys.TargetEntity, change.Target);
                context.Set(MapTriggerEventPayloadKeys.RelationTypeId, change.TypeId);
                switch (change.Kind)
                {
                    case RelationshipChangeKind.MetricChanged:
                        context.Set(MapTriggerEventPayloadKeys.RelationMetricId, change.MetricId);
                        context.Set(MapTriggerEventPayloadKeys.OldValueInt, (int)change.OldValue);
                        context.Set(MapTriggerEventPayloadKeys.VarValueInt, (int)change.NewValue);
                        break;
                    case RelationshipChangeKind.FlagChanged:
                        context.Set(MapTriggerEventPayloadKeys.OldValueInt, (int)change.OldFlags);
                        context.Set(MapTriggerEventPayloadKeys.VarValueInt, (int)change.NewFlags);
                        break;
                }

                _engine.TriggerManager.FireMapEvent(session.MapId, eventKey, context);
            }
        }
    }
}

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
                _callbackProcessor.Process(_engine, catalogRuntime, _changeBuffer.GetSpan());
                PublishChangeEvents(_changeBuffer.GetSpan());
                _changeBuffer.Clear();
            }

            if (catalogRuntime.Synergies.Count > 0)
            {
                _synergyProcessor.Evaluate(_engine, catalogRuntime);
            }
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
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

                ScriptContext context = _engine.CreateContext();
                context.Set(CoreServiceKeys.MapId, session.MapId);
                context.Set(CoreServiceKeys.MapSession, session);
                context.Set(CoreServiceKeys.MapTags, session.MapConfig?.Tags ?? new System.Collections.Generic.List<string>());
                context.Set(MapTriggerEventPayloadKeys.SourceEntity, change.Source);
                context.Set(MapTriggerEventPayloadKeys.TargetEntity, change.Target);
                context.Set(MapTriggerEventPayloadKeys.RelationTypeId, change.TypeId);

                EventKey eventKey;
                switch (change.Kind)
                {
                    case RelationshipChangeKind.LinkAdded:
                        eventKey = GameEvents.RelationLinkAdded;
                        break;
                    case RelationshipChangeKind.LinkRemoved:
                        eventKey = GameEvents.RelationLinkRemoved;
                        break;
                    case RelationshipChangeKind.MetricChanged:
                        context.Set(MapTriggerEventPayloadKeys.RelationMetricId, change.MetricId);
                        context.Set(MapTriggerEventPayloadKeys.OldValueInt, (int)change.OldValue);
                        context.Set(MapTriggerEventPayloadKeys.VarValueInt, (int)change.NewValue);
                        eventKey = GameEvents.RelationMetricChanged;
                        break;
                    case RelationshipChangeKind.FlagChanged:
                        context.Set(MapTriggerEventPayloadKeys.OldValueInt, (int)change.OldFlags);
                        context.Set(MapTriggerEventPayloadKeys.VarValueInt, (int)change.NewFlags);
                        eventKey = GameEvents.RelationFlagChanged;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown relationship change kind '{change.Kind}'.");
                }

                _engine.TriggerManager.FireMapEvent(session.MapId, eventKey, context);
            }
        }
    }
}

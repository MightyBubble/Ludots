using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Bridges same-step GameplayEventBus events into the TriggerManager map-scoped event
    /// domain after the EventDispatch buffer swap. EventKey = "Gas.Event.&lt;TagName&gt;";
    /// map is resolved from Target's MapEntity with Source as fallback. Reactions published
    /// from a bridged trigger graph land in the NEXT step's buffer (one-step lag, same
    /// visibility window as the post-swap presentation projection).
    /// </summary>
    public sealed class GasEventTriggerBridgeSystem : ISystem<float>
    {
        public const string EventKeyPrefix = "Gas.Event.";

        private readonly GameplayEventBus _eventBus;
        private readonly TriggerManager _triggerManager;
        private readonly World _world;
        private readonly Func<ScriptContext> _contextFactory;
        private readonly Dictionary<int, BridgeTagState> _tagStates = new();

        public int DroppedUnknownTagEvents { get; private set; }
        public int DroppedNoMapEvents { get; private set; }
        public int DroppedNoConsumerEvents { get; private set; }

        public GasEventTriggerBridgeSystem(
            GameplayEventBus eventBus,
            TriggerManager triggerManager,
            World world,
            Func<ScriptContext> contextFactory)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            GameplayEventBus.EventList events = _eventBus.Events;
            for (int i = 0; i < events.Count; i++)
            {
                PublishOne(events[i]);
            }
        }

        private readonly struct BridgeTagState
        {
            public BridgeTagState(MapId mapId, string eventKeyValue)
            {
                MapId = mapId;
                EventKeyValue = eventKeyValue;
            }

            public MapId MapId { get; }
            public string EventKeyValue { get; }
        }

        private void PublishOne(in GameplayEvent evt)
        {
            string? tagName = TagRegistry.GetName(evt.TagId);
            if (string.IsNullOrWhiteSpace(tagName))
            {
                DroppedUnknownTagEvents++;
                return;
            }

            MapId mapId = ResolveMap(evt.Target);
            if (string.IsNullOrEmpty(mapId.Value))
            {
                mapId = ResolveMap(evt.Source);
            }

            if (string.IsNullOrEmpty(mapId.Value))
            {
                DroppedNoMapEvents++;
                return;
            }

            // 无消费者的事件完全跳过分发：不再每笔 new ScriptContext + 字符串拼接。
            if (!_tagStates.TryGetValue(evt.TagId, out BridgeTagState state))
            {
                state = new BridgeTagState(mapId, EventKeyPrefix + tagName);
                _tagStates[evt.TagId] = state;
            }

            if (!_triggerManager.HasDispatchTarget(state.MapId, state.EventKeyValue))
            {
                DroppedNoConsumerEvents++;
                return;
            }

            ScriptContext context = _contextFactory();
            context.Set(ContextKeys.MapId, mapId);
            context.Set(MapTriggerEventPayloadKeys.SourceEntity, evt.Source);
            context.Set(MapTriggerEventPayloadKeys.TargetEntity, evt.Target);
            context.Set(MapTriggerEventPayloadKeys.TagId, evt.TagId);
            context.Set(MapTriggerEventPayloadKeys.Magnitude, evt.Magnitude);
            _triggerManager.FireMapEvent(mapId, new EventKey(state.EventKeyValue), context);
        }

        private MapId ResolveMap(Entity entity)
        {
            if (entity == Entity.Null || !_world.IsAlive(entity) || !_world.Has<MapEntity>(entity))
            {
                return default;
            }

            return _world.Get<MapEntity>(entity).MapId;
        }
    }
}

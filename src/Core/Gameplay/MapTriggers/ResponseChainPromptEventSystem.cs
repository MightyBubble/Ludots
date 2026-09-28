using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Announces response-chain prompts to the prompted player's representative entity as
    /// map-scoped <see cref="GameEvents.ResponseChainPromptOpened"/> /
    /// <see cref="GameEvents.ResponseChainPromptClosed"/>, so the mod that owns the rep decides
    /// which interaction context answers. Edge-detects on the prompt window id: a close and a
    /// new open inside one step publish Closed for the old window before Opened for the new one.
    /// </summary>
    public sealed class ResponseChainPromptEventSystem : ISystem<float>
    {
        private readonly ResponseChainPromptState _prompt;
        private readonly PlayerEntityLookup _players;
        private readonly TriggerManager _triggerManager;
        private readonly World _world;
        private readonly Func<ScriptContext> _contextFactory;

        private int _announcedWindowId;
        private Entity _announcedActor;
        private Entity _announcedRep;
        private MapId _announcedMap;

        /// <summary>Prompts whose player has no representative entity; nobody can answer them through a context.</summary>
        public int PromptsWithoutRepresentative { get; private set; }
        public int DroppedNoMapEvents { get; private set; }

        public ResponseChainPromptEventSystem(
            ResponseChainPromptState prompt,
            PlayerEntityLookup players,
            TriggerManager triggerManager,
            World world,
            Func<ScriptContext> contextFactory)
        {
            _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
            _players = players ?? throw new ArgumentNullException(nameof(players));
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
            int currentWindowId = _prompt.IsOpen ? _prompt.WindowId : 0;
            if (currentWindowId == _announcedWindowId)
            {
                return;
            }

            if (_announcedWindowId != 0)
            {
                if (_announcedRep != Entity.Null)
                {
                    Fire(_announcedMap, GameEvents.ResponseChainPromptClosed, _announcedActor, _announcedRep);
                }

                _announcedWindowId = 0;
                _announcedActor = Entity.Null;
                _announcedRep = Entity.Null;
                _announcedMap = default;
            }

            if (currentWindowId == 0)
            {
                return;
            }

            _announcedWindowId = currentWindowId;
            if (!_players.TryGet(_prompt.PlayerId, out Entity rep) || rep == Entity.Null)
            {
                PromptsWithoutRepresentative++;
                return;
            }

            if (!_world.IsAlive(rep) || !_world.Has<MapEntity>(rep) || string.IsNullOrEmpty(_world.Get<MapEntity>(rep).MapId.Value))
            {
                DroppedNoMapEvents++;
                return;
            }

            _announcedActor = _prompt.Actor;
            _announcedRep = rep;
            _announcedMap = _world.Get<MapEntity>(rep).MapId;
            Fire(_announcedMap, GameEvents.ResponseChainPromptOpened, _announcedActor, _announcedRep);
        }

        private void Fire(MapId mapId, EventKey eventKey, Entity actor, Entity rep)
        {
            ScriptContext context = _contextFactory();
            context.Set(ContextKeys.MapId, mapId);
            context.Set(MapTriggerEventPayloadKeys.SourceEntity, actor);
            context.Set(MapTriggerEventPayloadKeys.TargetEntity, rep);
            _triggerManager.FireMapEvent(mapId, eventKey, context);
        }
    }
}

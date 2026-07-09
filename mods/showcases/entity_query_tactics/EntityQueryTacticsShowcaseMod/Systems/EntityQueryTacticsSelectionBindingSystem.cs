using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using EntityQueryTacticsShowcaseMod.Runtime;

namespace EntityQueryTacticsShowcaseMod.Systems
{
    internal sealed class EntityQueryTacticsSelectionBindingSystem : ISystem<float>
    {
        private static readonly QueryDescription NamedMapEntityQuery = new QueryDescription().WithAll<Name, MapEntity>();

        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly EntityQueryTacticsScenarioState _state;

        public EntityQueryTacticsSelectionBindingSystem(GameEngine engine, EntityQueryTacticsScenarioState state)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _world = engine.World;
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!string.Equals(_engine.CurrentMapSession?.MapId.Value, _state.Config.MapId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!TryFindOwner(out Entity owner))
            {
                return;
            }

            BindCommandSourceOwner(owner);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private bool TryFindOwner(out Entity owner)
        {
            Entity foundOwner = Entity.Null;
            _world.Query(in NamedMapEntityQuery, (Entity entity, ref Name name, ref MapEntity mapEntity) =>
            {
                if (foundOwner != Entity.Null ||
                    !string.Equals(mapEntity.MapId.Value, _state.Config.MapId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(name.Value, _state.Config.Scenario.PlayerCommanderName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                foundOwner = entity;
            });

            owner = foundOwner;
            return owner != Entity.Null;
        }

        private void BindCommandSourceOwner(Entity owner)
        {
            _engine.SetService(CoreServiceKeys.LocalPlayerEntity, owner);
            if (_world.TryGet(owner, out PlayerOwner playerOwner) && playerOwner.PlayerId > 0)
            {
                _engine.SetService(CoreServiceKeys.LocalPlayerId, playerOwner.PlayerId);
            }
        }
    }
}

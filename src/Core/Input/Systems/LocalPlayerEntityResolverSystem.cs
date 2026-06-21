using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Scripting;
 
namespace Ludots.Core.Input.Systems
{
    public sealed class LocalPlayerEntityResolverSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
 
        public LocalPlayerEntityResolverSystem(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
        }
 
        public void Initialize() { }
 
        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.LocalPlayerId.Name, out object playerIdObj) ||
                playerIdObj is not int playerId ||
                playerId <= 0)
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.PlayerEntityLookup.Name, out object lookupObj) ||
                lookupObj is not PlayerEntityLookup lookup ||
                !lookup.TryGet(playerId, out Entity playerEntity) ||
                !_world.IsAlive(playerEntity))
            {
                _globals.Remove(CoreServiceKeys.LocalPlayerEntity.Name);
                return;
            }

            if (_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var obj) &&
                obj is Entity existing &&
                existing == playerEntity &&
                _world.IsAlive(existing))
            {
                return;
            }

            _globals[CoreServiceKeys.LocalPlayerEntity.Name] = playerEntity;
        }
 
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}

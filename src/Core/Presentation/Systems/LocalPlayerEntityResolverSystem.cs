using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Scripting;
 
namespace Ludots.Core.Presentation.Systems
{
    public sealed class LocalPlayerEntityResolverSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly int _playerId;
 
        private readonly QueryDescription _query = new QueryDescription().WithAll<PlayerOwner>();
 
        public LocalPlayerEntityResolverSystem(World world, Dictionary<string, object> globals, int playerId = 1)
        {
            _world = world;
            _globals = globals;
            _playerId = playerId;
        }
 
        public void Initialize() { }
 
        public void Update(in float dt)
        {
            if (_globals.TryGetValue(ContextKeys.LocalPlayerEntity, out var obj) && obj is Entity existing && _world.IsAlive(existing))
            {
                return;
            }
 
            var job = new FindJob { PlayerId = _playerId, Found = default };
            _world.InlineEntityQuery<FindJob, PlayerOwner>(in _query, ref job);
            if (_world.IsAlive(job.Found))
            {
                _globals[ContextKeys.LocalPlayerEntity] = job.Found;
            }
        }
 
        private struct FindJob : IForEachWithEntity<PlayerOwner>
        {
            public int PlayerId;
            public Entity Found;
 
            public void Update(Entity entity, ref PlayerOwner owner)
            {
                if (Found.Id != 0) return;
                if (owner.PlayerId != PlayerId) return;
                Found = entity;
            }
        }
 
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}

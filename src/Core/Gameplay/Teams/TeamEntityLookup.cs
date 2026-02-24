using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Gameplay.Teams
{
    /// <summary>
    /// Maps Team IDs to their ECS meta-entities.
    /// Populated during game setup; stored in GlobalContext.
    ///
    /// Usage:
    ///   lookup.Register(1, blueTeamEntity);
    ///   if (lookup.TryGet(teamId, out var teamEntity)) { ... }
    /// </summary>
    public sealed class TeamEntityLookup
    {
        private readonly Dictionary<int, Entity> _map = new();

        public void Register(int teamId, Entity entity)
        {
            _map[teamId] = entity;
        }

        public bool TryGet(int teamId, out Entity entity)
            => _map.TryGetValue(teamId, out entity);

        public Entity Get(int teamId)
            => _map.TryGetValue(teamId, out var e) ? e : Entity.Null;

        public void Clear() => _map.Clear();

        public int Count => _map.Count;

        public IEnumerable<KeyValuePair<int, Entity>> Entries => _map;
    }
}

using System.Collections.Generic;
using System;
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
            if (_map.TryGetValue(teamId, out Entity existing) && existing != entity)
            {
                throw new InvalidOperationException($"TeamEntityLookup already has a representative entity for team {teamId}.");
            }

            _map[teamId] = entity;
        }

        public bool TryGet(int teamId, out Entity entity)
            => _map.TryGetValue(teamId, out entity);

        public Entity Get(int teamId)
            => _map.TryGetValue(teamId, out var e) ? e : Entity.Null;

        public void Clear() => _map.Clear();

        public void ReplaceWith(TeamEntityLookup source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (ReferenceEquals(this, source))
            {
                return;
            }

            _map.Clear();
            foreach (var entry in source._map)
            {
                _map.Add(entry.Key, entry.Value);
            }
        }

        public int Count => _map.Count;

        public IEnumerable<KeyValuePair<int, Entity>> Entries => _map;
    }
}

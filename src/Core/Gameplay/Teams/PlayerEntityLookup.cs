using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Gameplay.Teams
{
    /// <summary>
    /// Maps Player IDs to their ECS representative entities.
    /// Populated from map participant bindings; stored on the focused map session.
    /// </summary>
    public sealed class PlayerEntityLookup
    {
        private readonly Dictionary<int, Entity> _map = new();

        public void Register(int playerId, Entity entity)
        {
            if (_map.TryGetValue(playerId, out Entity existing) && existing != entity)
            {
                throw new InvalidOperationException($"PlayerEntityLookup already has a representative entity for player {playerId}.");
            }

            _map[playerId] = entity;
        }

        public bool TryGet(int playerId, out Entity entity)
            => _map.TryGetValue(playerId, out entity);

        public Entity Get(int playerId)
            => _map.TryGetValue(playerId, out Entity entity) ? entity : Entity.Null;

        public void Clear() => _map.Clear();

        public void ReplaceWith(PlayerEntityLookup source)
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

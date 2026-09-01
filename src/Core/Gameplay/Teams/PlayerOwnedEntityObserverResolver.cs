using System;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Knowledge;

namespace Ludots.Core.Gameplay.Teams
{
    public sealed class PlayerOwnedEntityObserverResolver : IEntityObserverResolver
    {
        private readonly PlayerEntityLookup _players;

        public PlayerOwnedEntityObserverResolver(PlayerEntityLookup players)
        {
            _players = players ?? throw new ArgumentNullException(nameof(players));
        }

        public Entity ResolveObserver(World world, Entity source)
        {
            if (!world.TryGet(source, out PlayerOwner owner))
            {
                return source;
            }

            if (owner.PlayerId <= 0 ||
                !_players.TryGet(owner.PlayerId, out Entity player) ||
                !world.IsAlive(player))
            {
                throw new InvalidOperationException(
                    $"Vision emitter declares PlayerOwner {owner.PlayerId} without a live formal player representative.");
            }

            return player;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;

namespace Ludots.Core.UI.CommandDeck
{
    /// <summary>
    /// Resolves which aggregation-group member receives activation for a CommandDeck cell.
    /// Uses <see cref="CastDispatchProfileRegistry"/> so routing is profile-driven — never a silent
    /// "first member" fallback. Missing route profile or empty member set fails fast.
    /// </summary>
    public sealed class CommandDeckRouteResolver
    {
        private readonly CastDispatchProfileRegistry _dispatch;
        private readonly StringIntRegistry _profileIds;
        private Entity[] _actorScratch = new Entity[16];
        private Entity[] _selectedScratch = new Entity[16];

        public CommandDeckRouteResolver(CastDispatchProfileRegistry dispatch)
        {
            _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            _profileIds = dispatch.ProfileIdRegistry;
        }

        /// <summary>
        /// Select the activation target for one aggregated cell. Members are ordered as supplied
        /// (typically kernel aggregation order). The route profile decides which member wins.
        /// </summary>
        public CommandDeckRouteTarget Resolve(
            string routeProfileId,
            ReadOnlySpan<CommandDeckRouteMember> members,
            World world,
            Vector3 targetWorldCm,
            long groupKey)
        {
            if (string.IsNullOrWhiteSpace(routeProfileId))
            {
                throw new InvalidOperationException(
                    "CommandDeck route profile id is required; silent first-member routing is forbidden.");
            }

            if (members.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"CommandDeck route profile '{routeProfileId}' cannot resolve an empty member set.");
            }

            if (!_profileIds.TryGetId(routeProfileId, out int profileId) || !_dispatch.IsInstalled(profileId))
            {
                throw new InvalidOperationException(
                    $"CommandDeck route profile '{routeProfileId}' is not installed.");
            }

            EnsureCapacity(members.Length);
            for (int i = 0; i < members.Length; i++)
            {
                _actorScratch[i] = members[i].Owner;
            }

            var ctx = new CastDispatchContext(world, targetWorldCm, groupKey);
            int selectedCount = _dispatch.SelectDispatchTargets(
                profileId,
                _actorScratch.AsSpan(0, members.Length),
                in ctx,
                _selectedScratch.AsSpan(0, members.Length),
                out _);

            if (selectedCount <= 0)
            {
                throw new InvalidOperationException(
                    $"CommandDeck route profile '{routeProfileId}' selected zero members.");
            }

            Entity chosen = _selectedScratch[0];
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i].Owner == chosen)
                {
                    return new CommandDeckRouteTarget(members[i].Owner, members[i].SlotIndex);
                }
            }

            throw new InvalidOperationException(
                $"CommandDeck route profile '{routeProfileId}' selected entity {chosen.Id} that is not in the member set.");
        }

        private void EnsureCapacity(int count)
        {
            if (_actorScratch.Length < count)
            {
                int next = Math.Max(count, _actorScratch.Length * 2);
                Array.Resize(ref _actorScratch, next);
                Array.Resize(ref _selectedScratch, next);
            }
        }
    }
}

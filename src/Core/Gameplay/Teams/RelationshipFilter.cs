using System;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.Teams
{
    /// <summary>
    /// Relationship-based filter for effect/spatial targeting.
    /// Queries <see cref="TeamManager.GetRelationship"/> at runtime,
    /// so results always reflect current config (including runtime changes).
    /// </summary>
    public enum RelationshipFilter : byte
    {
        /// <summary>No filter — accept all teams.</summary>
        All = 0,
        /// <summary>Keep only entities whose team is Hostile to source.</summary>
        Hostile = 1,
        /// <summary>Keep only entities whose team is Friendly to source.</summary>
        Friendly = 2,
        /// <summary>Keep only entities whose team is Neutral to source.</summary>
        Neutral = 3,
        /// <summary>Hostile or Neutral (exclude friendlies).</summary>
        NotFriendly = 4,
        /// <summary>Friendly or Neutral (exclude hostiles).</summary>
        NotHostile = 5,
    }

    public static class RelationshipFilterUtil
    {
        /// <summary>
        /// Check if the relationship between source and target teams passes the filter.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Passes(RelationshipFilter filter, int sourceTeamId, int targetTeamId)
        {
            if (filter == RelationshipFilter.All) return true;
            var rel = TeamManager.GetRelationship(sourceTeamId, targetTeamId);
            return filter switch
            {
                RelationshipFilter.Hostile     => rel == TeamRelationship.Hostile,
                RelationshipFilter.Friendly    => rel == TeamRelationship.Friendly,
                RelationshipFilter.Neutral     => rel == TeamRelationship.Neutral,
                RelationshipFilter.NotFriendly => rel != TeamRelationship.Friendly,
                RelationshipFilter.NotHostile  => rel != TeamRelationship.Hostile,
                _ => true
            };
        }

        /// <summary>
        /// Parse a canonical <see cref="RelationshipFilter"/> name.
        /// Only accepts enum-defined names: All, Hostile, Friendly, Neutral, NotFriendly, NotHostile.
        /// Alias mapping (e.g. "Enemy") is NOT handled here — use the config Loader's migration path.
        /// </summary>
        public static RelationshipFilter Parse(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return RelationshipFilter.All;
            if (Enum.TryParse<RelationshipFilter>(filter, ignoreCase: true, out var result))
            {
                return result;
            }
            return RelationshipFilter.All;
        }
    }
}

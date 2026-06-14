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
            return filter switch
            {
                RelationshipFilter.All => true,
                RelationshipFilter.Hostile => Matches(sourceTeamId, targetTeamId, TeamRelationship.Hostile),
                RelationshipFilter.Friendly => Matches(sourceTeamId, targetTeamId, TeamRelationship.Friendly),
                RelationshipFilter.Neutral => Matches(sourceTeamId, targetTeamId, TeamRelationship.Neutral),
                RelationshipFilter.NotFriendly => DoesNotMatch(sourceTeamId, targetTeamId, TeamRelationship.Friendly),
                RelationshipFilter.NotHostile => DoesNotMatch(sourceTeamId, targetTeamId, TeamRelationship.Hostile),
                _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unsupported relationship filter.")
            };
        }

        /// <summary>
        /// Parse a canonical <see cref="RelationshipFilter"/> name.
        /// Only accepts enum-defined names: All, Hostile, Friendly, Neutral, NotFriendly, NotHostile.
        /// </summary>
        public static RelationshipFilter Parse(string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                throw new ArgumentException("Relationship filter must be explicitly authored.", nameof(filter));
            }

            if (filter != filter.Trim())
            {
                throw new InvalidOperationException($"Relationship filter '{filter}' must not contain leading or trailing whitespace.");
            }

            if (Enum.TryParse<RelationshipFilter>(filter, ignoreCase: false, out var result) &&
                string.Equals(Enum.GetName(typeof(RelationshipFilter), result), filter, StringComparison.Ordinal))
            {
                return result;
            }

            throw new InvalidOperationException(
                $"Unsupported relationship filter '{filter}'. Supported: All, Hostile, Friendly, Neutral, NotFriendly, NotHostile.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Matches(int sourceTeamId, int targetTeamId, TeamRelationship expected)
        {
            return ValidateRelationship(TeamManager.GetRelationship(sourceTeamId, targetTeamId), sourceTeamId, targetTeamId) == expected;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool DoesNotMatch(int sourceTeamId, int targetTeamId, TeamRelationship excluded)
        {
            return ValidateRelationship(TeamManager.GetRelationship(sourceTeamId, targetTeamId), sourceTeamId, targetTeamId) != excluded;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TeamRelationship ValidateRelationship(TeamRelationship relationship, int sourceTeamId, int targetTeamId)
        {
            return relationship switch
            {
                TeamRelationship.Hostile or TeamRelationship.Friendly or TeamRelationship.Neutral => relationship,
                _ => throw new InvalidOperationException(
                    $"Unsupported team relationship '{relationship}' between team {sourceTeamId} and team {targetTeamId}.")
            };
        }
    }
}

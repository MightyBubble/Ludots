using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Teams
{
    public enum TeamRelationship
    {
        Neutral = 0,
        Friendly = 1,
        Hostile = 2
    }

    public class RelationshipEntry
    {
        public int TeamA { get; set; }
        public int TeamB { get; set; }
        public string Attitude { get; set; }
        /// <summary>
        /// If true (default), sets both A→B and B→A to the same attitude.
        /// If false, only sets A→B (asymmetric).
        /// </summary>
        public bool Symmetric { get; set; } = true;
    }

    public class TeamConfig
    {
        /// <summary>
        /// Relationship returned for team pairs that have no explicit entry.
        /// "Neutral" (default for strategy), "Hostile" (typical for MOBA/arena).
        /// </summary>
        public string DefaultRelationship { get; set; } = "Neutral";

        public List<RelationshipEntry> Relationships { get; set; } = new List<RelationshipEntry>();
    }

    /// <summary>
    /// Manages Team relationships (Friendly, Hostile, Neutral).
    ///
    /// Relationships are asymmetric by default at the API level:
    ///   SetRelationship(a, b, rel)  sets ONLY A's view of B.
    ///   SetRelationshipSymmetric(a, b, rel)  sets both A→B and B→A.
    ///
    /// Config entries default to symmetric=true for convenience.
    /// GetRelationship(a, a) always returns Friendly (self).
    /// Unknown pairs return <see cref="DefaultRelationship"/> (configurable).
    /// </summary>
    public static class TeamManager
    {
        // Key = (TeamA << 32) | TeamB  — direction matters (A's view of B)
        private static readonly Dictionary<long, TeamRelationship> _relationships = new Dictionary<long, TeamRelationship>();

        /// <summary>
        /// Relationship returned for team pairs without explicit config.
        /// Set via <see cref="TeamConfig.DefaultRelationship"/> or directly at runtime.
        /// </summary>
        public static TeamRelationship DefaultRelationship { get; set; } = TeamRelationship.Neutral;

        public static void Clear()
        {
            _relationships.Clear();
            DefaultRelationship = TeamRelationship.Neutral;
        }

        public static void LoadConfig(TeamConfig config)
        {
            Clear();
            if (config == null) return;

            // Parse configurable default
            if (Enum.TryParse<TeamRelationship>(config.DefaultRelationship, true, out var defaultRel))
            {
                DefaultRelationship = defaultRel;
            }

            if (config.Relationships == null) return;

            foreach (var entry in config.Relationships)
            {
                if (Enum.TryParse<TeamRelationship>(entry.Attitude, true, out var rel))
                {
                    if (entry.Symmetric)
                    {
                        SetRelationshipSymmetric(entry.TeamA, entry.TeamB, rel);
                    }
                    else
                    {
                        SetRelationship(entry.TeamA, entry.TeamB, rel);
                    }
                }
            }
        }

        /// <summary>
        /// Set A's view of B (one-way / asymmetric).
        /// </summary>
        public static void SetRelationship(int teamA, int teamB, TeamRelationship relation)
        {
            _relationships[Combine(teamA, teamB)] = relation;
        }

        /// <summary>
        /// Convenience: set both A→B and B→A to the same relationship.
        /// </summary>
        public static void SetRelationshipSymmetric(int teamA, int teamB, TeamRelationship relation)
        {
            _relationships[Combine(teamA, teamB)] = relation;
            _relationships[Combine(teamB, teamA)] = relation;
        }

        /// <summary>
        /// Get A's view of B.
        /// Same team → Friendly. Unknown → <see cref="DefaultRelationship"/>.
        /// </summary>
        public static TeamRelationship GetRelationship(int teamA, int teamB)
        {
            if (teamA == teamB) return TeamRelationship.Friendly;

            if (_relationships.TryGetValue(Combine(teamA, teamB), out var rel))
            {
                return rel;
            }

            return DefaultRelationship;
        }

        private static long Combine(int a, int b)
        {
            return ((long)a << 32) | (uint)b;
        }
    }
}

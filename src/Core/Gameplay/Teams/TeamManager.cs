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

    public sealed class TeamRelationshipSnapshot
    {
        internal TeamRelationshipSnapshot(TeamRelationship defaultRelationship, IReadOnlyDictionary<long, TeamRelationship> relationships)
        {
            DefaultRelationship = defaultRelationship;
            Relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
        }

        public TeamRelationship DefaultRelationship { get; }
        internal IReadOnlyDictionary<long, TeamRelationship> Relationships { get; }
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
        private static TeamRelationship _defaultRelationship = TeamRelationship.Neutral;
        private static int _revision;

        /// <summary>
        /// Relationship returned for team pairs without explicit config.
        /// Set via <see cref="TeamConfig.DefaultRelationship"/> or directly at runtime.
        /// </summary>
        public static TeamRelationship DefaultRelationship
        {
            get => _defaultRelationship;
            set
            {
                if (_defaultRelationship == value)
                {
                    return;
                }

                _defaultRelationship = value;
                IncrementRevision();
            }
        }

        public static int Revision => _revision;

        public static void Clear()
        {
            _relationships.Clear();
            _defaultRelationship = TeamRelationship.Neutral;
            IncrementRevision();
        }

        public static void LoadConfig(TeamConfig config)
        {
            Clear();
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (!TryParseRelationship(config.DefaultRelationship, out var defaultRel))
            {
                throw new InvalidOperationException(
                    $"TeamConfig.DefaultRelationship is invalid: '{config.DefaultRelationship}'.");
            }

            DefaultRelationship = defaultRel;

            if (config.Relationships == null)
            {
                throw new InvalidOperationException("TeamConfig.Relationships must be an explicit collection.");
            }

            foreach (var entry in config.Relationships)
            {
                if (!TryParseRelationship(entry.Attitude, out var rel))
                {
                    throw new InvalidOperationException(
                        $"TeamConfig relationship [{entry.TeamA},{entry.TeamB}] is invalid: '{entry.Attitude}'.");
                }

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

        public static TeamRelationshipSnapshot CaptureSnapshot()
        {
            return new TeamRelationshipSnapshot(
                _defaultRelationship,
                new Dictionary<long, TeamRelationship>(_relationships));
        }

        public static void RestoreSnapshot(TeamRelationshipSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            _relationships.Clear();
            foreach (var entry in snapshot.Relationships)
            {
                _relationships.Add(entry.Key, entry.Value);
            }

            _defaultRelationship = snapshot.DefaultRelationship;
            IncrementRevision();
        }

        public static bool TryParseRelationship(string value, out TeamRelationship relationship)
        {
            relationship = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Enum.TryParse(value, ignoreCase: false, out relationship) &&
                   string.Equals(relationship.ToString(), value, StringComparison.Ordinal);
        }

        /// <summary>
        /// Set A's view of B (one-way / asymmetric).
        /// </summary>
        public static void SetRelationship(int teamA, int teamB, TeamRelationship relation)
        {
            if (SetRelationshipCore(teamA, teamB, relation))
            {
                IncrementRevision();
            }
        }

        /// <summary>
        /// Convenience: set both A→B and B→A to the same relationship.
        /// </summary>
        public static void SetRelationshipSymmetric(int teamA, int teamB, TeamRelationship relation)
        {
            bool changed = SetRelationshipCore(teamA, teamB, relation);
            changed |= SetRelationshipCore(teamB, teamA, relation);
            if (changed)
            {
                IncrementRevision();
            }
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

        private static bool SetRelationshipCore(int teamA, int teamB, TeamRelationship relation)
        {
            long key = Combine(teamA, teamB);
            if (_relationships.TryGetValue(key, out TeamRelationship existing) &&
                existing == relation)
            {
                return false;
            }

            _relationships[key] = relation;
            return true;
        }

        private static void IncrementRevision()
        {
            unchecked
            {
                _revision++;
            }
        }
    }
}

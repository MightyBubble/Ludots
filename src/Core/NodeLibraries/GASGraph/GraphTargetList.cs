using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Mathematics;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public ref struct GraphTargetList
    {
        private Span<Entity> _buffer;
        public int Count;

        public GraphTargetList(Span<Entity> buffer)
        {
            _buffer = buffer;
            Count = 0;
        }

        public Span<Entity> Span => _buffer.Slice(0, Count);

        public void SetCount(int count)
        {
            if (count < 0) count = 0;
            if (count > _buffer.Length) count = _buffer.Length;
            Count = count;
        }

        public void FilterRequireTag(World world, IGraphRuntimeApi api, int tagId)
        {
            int write = 0;
            for (int r = 0; r < Count; r++)
            {
                var ent = _buffer[r];
                if (!world.IsAlive(ent)) continue;
                if (!api.HasTag(ent, tagId)) continue;
                _buffer[write++] = ent;
            }
            Count = write;
        }

        public void SortStableDedup()
        {
            Span.Sort(new EntityStableComparer());
            Count = DedupSorted(Span);
        }

        public void Limit(int limit)
        {
            if (limit < 0) limit = 0;
            if (Count > limit) Count = limit;
        }

        /// <summary>
        /// Filter by EntityLayer category bitmask.
        /// Keeps only entities whose Category overlaps with requiredMask.
        /// </summary>
        public void FilterLayer(World world, IGraphRuntimeApi api, uint requiredMask)
        {
            int write = 0;
            for (int r = 0; r < Count; r++)
            {
                var ent = _buffer[r];
                if (!world.IsAlive(ent)) continue;
                uint category = api.GetEntityLayerCategory(ent);
                if ((category & requiredMask) == 0) continue;
                _buffer[write++] = ent;
            }
            Count = write;
        }

        /// <summary>
        /// Filter by team relationship using config-driven relationship protocol.
        /// mode: 1=Hostile, 2=Friendly, 3=Neutral, 4=NotFriendly, 5=NotHostile.
        /// Relationship values follow <see cref="GraphRelationship"/> constants.
        /// </summary>
        public void FilterRelationship(World world, IGraphRuntimeApi api, Entity reference, int mode)
        {
            GraphRelationshipFilterMode filterMode = ParseRelationshipFilterMode(mode);
            int refTeam = api.GetTeamId(reference);
            int write = 0;
            for (int r = 0; r < Count; r++)
            {
                var ent = _buffer[r];
                if (!world.IsAlive(ent)) continue;
                int entTeam = api.GetTeamId(ent);
                int rel = api.GetRelationship(refTeam, entTeam);
                bool pass = filterMode switch
                {
                    GraphRelationshipFilterMode.Hostile => MatchesRelationship(rel, GraphRelationship.Hostile),
                    GraphRelationshipFilterMode.Friendly => MatchesRelationship(rel, GraphRelationship.Friendly),
                    GraphRelationshipFilterMode.Neutral => MatchesRelationship(rel, GraphRelationship.Neutral),
                    GraphRelationshipFilterMode.NotFriendly => DoesNotMatchRelationship(rel, GraphRelationship.Friendly),
                    GraphRelationshipFilterMode.NotHostile => DoesNotMatchRelationship(rel, GraphRelationship.Hostile),
                    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported graph relationship filter mode.")
                };
                if (!pass) continue;
                _buffer[write++] = ent;
            }
            Count = write;
        }

        /// <summary>Removes a specific entity from the target list.</summary>
        public void FilterNotEntity(Entity exclude)
        {
            int write = 0;
            for (int r = 0; r < Count; r++)
            {
                if (_buffer[r].Equals(exclude)) continue;
                _buffer[write++] = _buffer[r];
            }
            Count = write;
        }

        public Entity MinByDistance(World world, IGraphRuntimeApi api, IntVector2 center)
        {
            int best = -1;
            long bestDist = long.MaxValue;
            for (int r = 0; r < Count; r++)
            {
                var ent = _buffer[r];
                if (!world.IsAlive(ent)) continue;
                if (!api.TryGetGridPos(ent, out var gridPos)) continue;
                long dx = gridPos.X - center.X;
                long dy = gridPos.Y - center.Y;
                long d = dx * dx + dy * dy;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = r;
                }
            }
            return best >= 0 ? _buffer[best] : default;
        }

        private readonly struct EntityStableComparer : IComparer<Entity>
        {
            public int Compare(Entity x, Entity y)
            {
                int c = x.WorldId.CompareTo(y.WorldId);
                if (c != 0) return c;
                c = x.Id.CompareTo(y.Id);
                if (c != 0) return c;
                return x.Version.CompareTo(y.Version);
            }
        }

        private static int DedupSorted(Span<Entity> sorted)
        {
            if (sorted.Length <= 1) return sorted.Length;
            int write = 1;
            for (int read = 1; read < sorted.Length; read++)
            {
                if (!sorted[read].Equals(sorted[write - 1]))
                {
                    sorted[write++] = sorted[read];
                }
            }
            return write;
        }

        private static GraphRelationshipFilterMode ParseRelationshipFilterMode(int mode)
        {
            return mode switch
            {
                1 => GraphRelationshipFilterMode.Hostile,
                2 => GraphRelationshipFilterMode.Friendly,
                3 => GraphRelationshipFilterMode.Neutral,
                4 => GraphRelationshipFilterMode.NotFriendly,
                5 => GraphRelationshipFilterMode.NotHostile,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported graph relationship filter mode.")
            };
        }

        private static bool MatchesRelationship(int relationship, int expected)
        {
            return ValidateRelationship(relationship) == expected;
        }

        private static bool DoesNotMatchRelationship(int relationship, int excluded)
        {
            return ValidateRelationship(relationship) != excluded;
        }

        private static int ValidateRelationship(int relationship)
        {
            return relationship switch
            {
                GraphRelationship.Hostile or GraphRelationship.Friendly or GraphRelationship.Neutral => relationship,
                _ => throw new InvalidOperationException($"Unsupported graph relationship value '{relationship}'.")
            };
        }
    }

    public enum GraphRelationshipFilterMode : int
    {
        Hostile = 1,
        Friendly = 2,
        Neutral = 3,
        NotFriendly = 4,
        NotHostile = 5
    }
}

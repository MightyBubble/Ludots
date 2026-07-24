using System;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships.Config;

namespace Ludots.Core.Gameplay.Relationships
{
    /// <summary>
    /// Cached projection of the stance between two control domains (RFC-0065 DEC-3).
    /// Stance keys are relationship catalog data; Core never interprets any stance name.
    /// The relationship graph stays the SSOT: results are cached per (domainA, domainB)
    /// and lazily invalidated when <see cref="RelationshipReverseIndex.Revision"/> changes.
    /// </summary>
    public sealed class DomainStanceQuery
    {
        /// <summary>Sentinel for "no stance id configured/resolved"; never a valid relationship type id.</summary>
        public const int NoStanceId = -1;

        private readonly RelationshipRuntime _relationships;
        private readonly int _memberOfTypeId;
        private readonly int[] _stanceTypeIds;
        private readonly int _sameDomainStanceId;
        private readonly int _sameTeamStanceId;
        private readonly int _defaultStanceId;
        private readonly EntityKeyedSoaTable<StancePayload> _cache;
        private uint _lastRevision;
        private uint _generation;

        public DomainStanceQuery(
            RelationshipRuntime relationships,
            int memberOfTypeId,
            int[] stanceTypeIds,
            int sameDomainStanceId,
            int sameTeamStanceId,
            int defaultStanceId)
        {
            _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
            if (memberOfTypeId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(memberOfTypeId));
            }

            _memberOfTypeId = memberOfTypeId;
            _stanceTypeIds = stanceTypeIds ?? throw new ArgumentNullException(nameof(stanceTypeIds));
            _sameDomainStanceId = sameDomainStanceId;
            _sameTeamStanceId = sameTeamStanceId;
            _defaultStanceId = defaultStanceId;
            _cache = new EntityKeyedSoaTable<StancePayload>(initialCapacity: 64);
            _lastRevision = relationships.ReverseIndex.Revision;
        }

        /// <summary>Relationship topology change signal; cached stances are recomputed after any edge mutation.</summary>
        public uint Revision => _relationships.ReverseIndex.Revision;

        /// <summary>
        /// Builds a query from catalog data, resolving stance names to relationship type ids at load time.
        /// A null config yields the built-in minimal default: no stance types, every lookup returns <see cref="NoStanceId"/>.
        /// </summary>
        public static DomainStanceQuery Create(RelationshipRuntime relationships, int memberOfTypeId, DomainStanceConfig? config)
        {
            ArgumentNullException.ThrowIfNull(relationships);
            if (config == null)
            {
                return new DomainStanceQuery(
                    relationships,
                    memberOfTypeId,
                    Array.Empty<int>(),
                    sameDomainStanceId: NoStanceId,
                    sameTeamStanceId: NoStanceId,
                    defaultStanceId: NoStanceId);
            }

            RelationshipTypeRegistry registry = relationships.TypeRegistry;
            var stanceTypeIds = new int[config.StanceTypes.Count];
            for (int i = 0; i < stanceTypeIds.Length; i++)
            {
                stanceTypeIds[i] = registry.GetId(config.StanceTypes[i]);
            }

            return new DomainStanceQuery(
                relationships,
                memberOfTypeId,
                stanceTypeIds,
                registry.GetId(config.SameDomainStance),
                registry.GetId(config.SameTeamStance),
                registry.GetId(config.DefaultStance));
        }

        /// <summary>
        /// Resolves the stance id between two domain reps. Resolution order (data-declared, no code fallback):
        /// same domain → direct rep→rep stance edge → same team (via member_of) → team→team stance edge →
        /// the configured default stance.
        /// </summary>
        public int GetStance(Entity domainA, Entity domainB)
        {
            if (domainA == Entity.Null || domainB == Entity.Null)
            {
                return _defaultStanceId;
            }

            uint revision = _relationships.ReverseIndex.Revision;
            if (revision != _lastRevision)
            {
                _lastRevision = revision;
                _generation++;
            }

            EntityKeyedSoaKey key = EntityKeyedSoaKey.ForPair(domainA, domainB);
            if (_cache.TryGet(key, currentTick: 0, out StancePayload payload, out _, out _) &&
                payload.Generation == _generation)
            {
                return payload.StanceId;
            }

            int stanceId = Resolve(domainA, domainB);
            _cache.Upsert(key, new StancePayload(stanceId, _generation), expiryTick: 0, payloadChanged: true, out _);
            return stanceId;
        }

        /// <summary>Returns true when the resolved stance between the two domains equals the given stance id.</summary>
        public bool HasStance(Entity a, Entity b, int stanceId)
        {
            return GetStance(a, b) == stanceId;
        }

        /// <summary>
        /// Resolves an entity that is itself a player/team representative or is explicitly linked to a
        /// team representative through the catalog member-of relationship.
        /// </summary>
        public bool TryResolveStanceDomain(Entity target, out Entity domainRep)
        {
            domainRep = Entity.Null;
            if (target == Entity.Null || !_relationships.World.IsAlive(target))
            {
                return false;
            }

            if (_relationships.World.Has<PlayerIdentity>(target) ||
                _relationships.World.Has<TeamIdentity>(target))
            {
                domainRep = target;
                return true;
            }

            Span<Entity> domains = stackalloc Entity[1];
            if (_relationships.CollectOutgoing(target, _memberOfTypeId, domains) <= 0)
            {
                return false;
            }

            Entity resolved = domains[0];
            if (!_relationships.World.Has<TeamIdentity>(resolved))
            {
                throw new InvalidOperationException(
                    $"Relationship member-of domain for entity {target.Id} must target a TeamIdentity representative.");
            }

            domainRep = resolved;
            return true;
        }

        /// <summary>
        /// Load-time, read-only resolution of a catalog stance name to the stance type id
        /// <see cref="GetStance"/> can return. Returns false for names that are not registered
        /// relationship types or not part of this query's configured stance space.
        /// </summary>
        public bool TryResolveStanceId(string stanceName, out int stanceId)
        {
            stanceId = NoStanceId;
            if (!_relationships.TypeRegistry.TryGetId(stanceName, out int typeId))
            {
                return false;
            }

            if (typeId == _sameDomainStanceId || typeId == _sameTeamStanceId || typeId == _defaultStanceId)
            {
                stanceId = typeId;
                return true;
            }

            for (int i = 0; i < _stanceTypeIds.Length; i++)
            {
                if (_stanceTypeIds[i] == typeId)
                {
                    stanceId = typeId;
                    return true;
                }
            }

            return false;
        }

        private int Resolve(Entity domainA, Entity domainB)
        {
            if (domainA == domainB)
            {
                return _sameDomainStanceId;
            }

            int direct = FindStanceEdge(domainA, domainB);
            if (direct != NoStanceId)
            {
                return direct;
            }

            Entity teamA = ResolveTeam(domainA);
            Entity teamB = ResolveTeam(domainB);
            if (teamA != Entity.Null && teamB != Entity.Null)
            {
                if (teamA == teamB)
                {
                    return _sameTeamStanceId;
                }

                int viaTeam = FindStanceEdge(teamA, teamB);
                if (viaTeam != NoStanceId)
                {
                    return viaTeam;
                }
            }

            return _defaultStanceId;
        }

        private int FindStanceEdge(Entity source, Entity target)
        {
            for (int i = 0; i < _stanceTypeIds.Length; i++)
            {
                if (_relationships.HasLink(source, target, _stanceTypeIds[i]))
                {
                    return _stanceTypeIds[i];
                }
            }

            return NoStanceId;
        }

        private Entity ResolveTeam(Entity rep)
        {
            Span<Entity> buffer = stackalloc Entity[1];
            int count = _relationships.CollectOutgoing(rep, _memberOfTypeId, buffer);
            return count > 0 ? buffer[0] : Entity.Null;
        }

        private readonly struct StancePayload
        {
            public StancePayload(int stanceId, uint generation)
            {
                StanceId = stanceId;
                Generation = generation;
            }

            public readonly int StanceId;
            public readonly uint Generation;
        }
    }
}

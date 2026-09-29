using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.Gameplay.Teams
{
    /// <summary>
    /// The only friend/foe question Core answers: does the source team representative carry a
    /// relationship edge of a given type to the target team representative. Team ids &lt;= 0 mean
    /// "no team", which carries no relationship; a positive team id without a bound representative
    /// is a data error.
    /// </summary>
    public sealed class TeamRelationQuery
    {
        private readonly World _world;
        private readonly RelationshipRuntime _relationships;
        private readonly TeamEntityLookup _teams;

        public TeamRelationQuery(RelationshipRuntime relationships, TeamEntityLookup teams)
        {
            _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
            _teams = teams ?? throw new ArgumentNullException(nameof(teams));
            _world = relationships.World;
        }

        public RelationshipTypeRegistry Types => _relationships.TypeRegistry;

        public uint Revision => _relationships.ReverseIndex.Revision;

        public RelationFilter ParseFilter(string authored) => RelationFilter.Parse(authored, _relationships.TypeRegistry);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Passes(in RelationFilter filter, int sourceTeamId, int targetTeamId)
        {
            return filter.IsAll || Has(sourceTeamId, targetTeamId, filter.RelationTypeId);
        }

        public bool Passes(in RelationFilter filter, Entity source, Entity target)
        {
            return filter.IsAll || Has(source, target, filter.RelationTypeId);
        }

        public bool Has(int sourceTeamId, int targetTeamId, int relationTypeId)
        {
            if (sourceTeamId <= 0 || targetTeamId <= 0)
            {
                return false;
            }

            return _relationships.HasLink(
                RequireTeamRepresentative(sourceTeamId),
                RequireTeamRepresentative(targetTeamId),
                relationTypeId);
        }

        public bool Has(Entity source, Entity target, int relationTypeId)
        {
            return Has(ResolveTeamId(source), ResolveTeamId(target), relationTypeId);
        }

        public int ResolveTeamId(Entity entity)
        {
            return _world.IsAlive(entity) && _world.TryGet(entity, out Team team) ? team.Id : 0;
        }

        private Entity RequireTeamRepresentative(int teamId)
        {
            if (!_teams.TryGet(teamId, out Entity representative) || !_world.IsAlive(representative))
            {
                throw new InvalidOperationException(
                    $"Team {teamId} has no live team representative; team relationships are edges between bound team representatives.");
            }

            return representative;
        }
    }
}

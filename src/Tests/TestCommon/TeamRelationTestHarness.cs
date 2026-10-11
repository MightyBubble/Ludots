using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Presenters;

namespace Ludots.Tests
{
    /// <summary>
    /// Stand-alone team relation wiring for world-level tests that run without a loaded map:
    /// team representatives carry <see cref="Team"/>, relations are authored edges between them.
    /// </summary>
    internal sealed class TeamRelationTestHarness
    {
        public const string HostileTypeName = "Hostile";
        public const string FriendlyTypeName = "Friendly";

        private readonly World _world;

        private TeamRelationTestHarness(World world, RelationshipRuntime relationships)
        {
            _world = world;
            Relationships = relationships;
            Teams = new TeamEntityLookup();
            Query = new TeamRelationQuery(relationships, Teams);
            HostileTypeId = relationships.TypeRegistry.Register(HostileTypeName);
            FriendlyTypeId = relationships.TypeRegistry.Register(FriendlyTypeName);
        }

        public RelationshipRuntime Relationships { get; }

        public TeamEntityLookup Teams { get; }

        public TeamRelationQuery Query { get; }

        public int HostileTypeId { get; }

        public int FriendlyTypeId { get; }

        public RelationFilter Hostile => RelationFilter.Require(HostileTypeId);

        public RelationFilter Friendly => RelationFilter.Require(FriendlyTypeId);

        public PresentTeamRelationClassifier CreatePresentClassifier()
        {
            return new PresentTeamRelationClassifier(
                Query,
                new PresentationTeamRelationColorsConfig
                {
                    FriendlyRelation = FriendlyTypeName,
                    HostileRelation = HostileTypeName,
                });
        }

        public static TeamRelationTestHarness Create(World world)
        {
            var relationships = new RelationshipRuntime(
                world,
                new RelationshipTypeRegistry(),
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(),
                new RelationshipReverseIndex(world));
            return new TeamRelationTestHarness(world, relationships);
        }

        public static TeamRelationTestHarness Over(World world, RelationshipRuntime relationships)
        {
            return new TeamRelationTestHarness(world, relationships);
        }

        public TeamRelationTestHarness Link(int sourceTeamId, int targetTeamId, int relationTypeId)
        {
            Relationships.EnsureLink(EnsureTeam(sourceTeamId), EnsureTeam(targetTeamId), relationTypeId);
            return this;
        }

        public TeamRelationTestHarness LinkSymmetric(int teamA, int teamB, int relationTypeId)
        {
            Link(teamA, teamB, relationTypeId);
            return teamA == teamB ? this : Link(teamB, teamA, relationTypeId);
        }

        public TeamRelationTestHarness Unlink(int sourceTeamId, int targetTeamId, int relationTypeId)
        {
            Relationships.RemoveLink(EnsureTeam(sourceTeamId), EnsureTeam(targetTeamId), relationTypeId);
            return this;
        }

        public Entity EnsureTeam(int teamId)
        {
            if (Teams.TryGet(teamId, out Entity representative))
            {
                return representative;
            }

            representative = _world.Create(new Team { Id = teamId });
            Teams.Register(teamId, representative);
            return representative;
        }
    }
}

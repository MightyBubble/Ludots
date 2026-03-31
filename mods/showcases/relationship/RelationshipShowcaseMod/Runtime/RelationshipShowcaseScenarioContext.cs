using System.Collections.Generic;
using Arch.Core;

namespace RelationshipShowcaseMod.Runtime
{
    public sealed class RelationshipShowcaseScenarioContext
    {
        public RelationshipShowcaseScenarioContext(
            RelationshipShowcaseConfig config,
            Entity[] heroEntities,
            Entity[] enemyEntities,
            Dictionary<string, Entity> entitiesByName,
            Dictionary<int, Entity> teamsById)
        {
            Config = config;
            HeroEntities = heroEntities;
            EnemyEntities = enemyEntities;
            EntitiesByName = entitiesByName;
            TeamsById = teamsById;
        }

        public RelationshipShowcaseConfig Config { get; }
        public IReadOnlyList<Entity> HeroEntities { get; }
        public IReadOnlyList<Entity> EnemyEntities { get; }
        public IReadOnlyDictionary<string, Entity> EntitiesByName { get; }
        public IReadOnlyDictionary<int, Entity> TeamsById { get; }
        public Entity SynergyTeam => TeamsById.TryGetValue(Config.SynergyTeamId, out var team) ? team : Entity.Null;
        public Entity GetEntityByName(string name) => EntitiesByName.TryGetValue(name, out var entity) ? entity : Entity.Null;
        public Entity GetTeamById(int teamId) => TeamsById.TryGetValue(teamId, out var entity) ? entity : Entity.Null;
    }
}

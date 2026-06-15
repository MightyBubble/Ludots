using System;
using System.Collections.Generic;
using Arch.Core;

namespace EntityQueryTacticsShowcaseMod.Runtime
{
    public sealed class EntityQueryTacticsScenarioContext
    {
        private readonly Dictionary<string, Entity> _entityByName;

        public EntityQueryTacticsScenarioContext(
            Entity owner,
            Entity enemyCommander,
            Entity[] allies,
            Entity[] enemies,
            Entity[] objectives,
            Dictionary<string, Entity> entityByName)
        {
            Owner = owner;
            EnemyCommander = enemyCommander;
            Allies = allies ?? Array.Empty<Entity>();
            Enemies = enemies ?? Array.Empty<Entity>();
            Objectives = objectives ?? Array.Empty<Entity>();
            _entityByName = entityByName ?? new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
        }

        public Entity Owner { get; }
        public Entity EnemyCommander { get; }
        public Entity[] Allies { get; }
        public Entity[] Enemies { get; }
        public Entity[] Objectives { get; }

        public Entity GetEntityByName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && _entityByName.TryGetValue(name, out Entity entity)
                ? entity
                : Entity.Null;
        }
    }
}

using System;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Relationships
{
    public sealed class RelationshipSynergyProcessor
    {
        private static readonly QueryDescription MemberQuery = new QueryDescription()
            .WithAll<Team, GameplayTagContainer, MapEntity>()
            .WithNone<TeamIdentity>();

        private readonly World _world;
        private readonly TagOps _tagOps;
        private readonly TeamEntityLookup _teamLookup;

        public RelationshipSynergyProcessor(World world, TagOps tagOps, TeamEntityLookup teamLookup)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _tagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
            _teamLookup = teamLookup ?? throw new ArgumentNullException(nameof(teamLookup));
        }

        public void Evaluate(GameEngine engine, RelationshipCatalogRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(runtime);

            foreach ((int teamId, Entity teamEntity) in _teamLookup.Entries)
            {
                if (teamEntity == Entity.Null || !_world.IsAlive(teamEntity))
                {
                    continue;
                }

                EnsureTagState(teamEntity);
                ref GameplayTagContainer teamTags = ref _world.Get<GameplayTagContainer>(teamEntity);
                ref TagCountContainer teamCounts = ref _world.Get<TagCountContainer>(teamEntity);

                for (int ruleIndex = 0; ruleIndex < runtime.Synergies.Count; ruleIndex++)
                {
                    RelationshipSynergyRule rule = runtime.Synergies[ruleIndex];
                    int matchingCount = CountMatchingMembers(teamId, rule.RequiredTags);
                    bool isActive = matchingCount >= rule.MinimumCount;
                    bool alreadyActive = rule.StateTagId > 0 && teamTags.HasTag(rule.StateTagId);

                    if (isActive && !alreadyActive)
                    {
                        ApplyTags(ref teamTags, ref teamCounts, rule.ApplyTagsToTeam);
                        if (!string.IsNullOrWhiteSpace(rule.EventKey.Value))
                        {
                            ScriptContext context = CreateContext(engine);
                            context.Set(CoreServiceKeys.RelationshipEventTeam, teamEntity);
                            context.Set(CoreServiceKeys.RelationshipEventCount, matchingCount);
                            engine.TriggerManager.FireEvent(rule.EventKey, context);
                        }
                    }
                    else if (!isActive && alreadyActive)
                    {
                        RemoveTags(ref teamTags, ref teamCounts, rule.ApplyTagsToTeam);
                    }
                }
            }
        }

        private int CountMatchingMembers(int teamId, int[] requiredTags)
        {
            int count = 0;
            _world.Query(in MemberQuery, (Entity _, ref Team team, ref GameplayTagContainer tags, ref MapEntity __) =>
            {
                if (team.Id != teamId)
                {
                    return;
                }

                for (int i = 0; i < requiredTags.Length; i++)
                {
                    if (requiredTags[i] <= 0 || !tags.HasTag(requiredTags[i]))
                    {
                        return;
                    }
                }

                count++;
            });

            return count;
        }

        private void ApplyTags(ref GameplayTagContainer tags, ref TagCountContainer counts, int[] tagIds)
        {
            for (int i = 0; i < tagIds.Length; i++)
            {
                if (tagIds[i] > 0)
                {
                    _tagOps.AddTag(ref tags, ref counts, tagIds[i]);
                }
            }
        }

        private void RemoveTags(ref GameplayTagContainer tags, ref TagCountContainer counts, int[] tagIds)
        {
            for (int i = 0; i < tagIds.Length; i++)
            {
                if (tagIds[i] > 0)
                {
                    _tagOps.RemoveTag(ref tags, ref counts, tagIds[i]);
                }
            }
        }

        private void EnsureTagState(Entity entity)
        {
            if (!_world.Has<GameplayTagContainer>(entity))
            {
                entity.Add(new GameplayTagContainer());
            }

            if (!_world.Has<TagCountContainer>(entity))
            {
                entity.Add(new TagCountContainer());
            }
        }

        private static ScriptContext CreateContext(GameEngine engine)
        {
            var context = new ScriptContext();
            context.Set(CoreServiceKeys.World, engine.World);
            context.Set(CoreServiceKeys.WorldMap, engine.WorldMap);
            context.Set(CoreServiceKeys.GameSession, engine.GameSession);
            context.Set(CoreServiceKeys.Engine, engine);
            context.Set(CoreServiceKeys.MapSession, engine.CurrentMapSession);
            context.Set(CoreServiceKeys.MapId, engine.CurrentMapSession?.MapId ?? default);
            foreach ((string key, object value) in engine.GlobalContext)
            {
                context.Set(key, value);
            }

            return context;
        }
    }
}

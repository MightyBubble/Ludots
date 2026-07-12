using System;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Relationships
{
    public sealed class RelationshipCallbackProcessor
    {
        private readonly World _world;
        private readonly TagOps _tagOps;
        private readonly TeamEntityLookup _teamLookup;

        public RelationshipCallbackProcessor(World world, TagOps tagOps, TeamEntityLookup teamLookup)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _tagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
            _teamLookup = teamLookup ?? throw new ArgumentNullException(nameof(teamLookup));
        }

        public void Process(GameEngine engine, RelationshipCatalogRuntime runtime, ReadOnlySpan<RelationshipChangeRecord> changes)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(runtime);

            for (int changeIndex = 0; changeIndex < changes.Length; changeIndex++)
            {
                ref readonly RelationshipChangeRecord change = ref changes[changeIndex];
                if (change.MetricId < 0)
                {
                    continue;
                }

                for (int callbackIndex = 0; callbackIndex < runtime.Callbacks.Count; callbackIndex++)
                {
                    RelationshipCallbackRule rule = runtime.Callbacks[callbackIndex];
                    if (rule.TypeId != change.TypeId || rule.MetricId != change.MetricId)
                    {
                        continue;
                    }

                    bool oldMatches = rule.Matches(change.OldValue);
                    bool newMatches = rule.Matches(change.NewValue);
                    if (!oldMatches && newMatches)
                    {
                        ApplyTags(change.Source, rule.AddTagsToSource);
                        ApplyTags(change.Target, rule.AddTagsToTarget);
                        if (TryResolveTeamEntity(change.Source, out Entity sourceTeam))
                        {
                            ApplyTags(sourceTeam, rule.AddTagsToSourceTeam);
                        }

                        if (TryResolveTeamEntity(change.Target, out Entity targetTeam))
                        {
                            ApplyTags(targetTeam, rule.AddTagsToTargetTeam);
                        }

                        FireEvent(engine, rule.EnterEventKey, change);
                    }
                    else if (oldMatches && !newMatches)
                    {
                        RemoveTags(change.Source, rule.RemoveTagsFromSource);
                        RemoveTags(change.Target, rule.RemoveTagsFromTarget);
                        if (TryResolveTeamEntity(change.Source, out Entity sourceTeam))
                        {
                            RemoveTags(sourceTeam, rule.RemoveTagsFromSourceTeam);
                        }

                        if (TryResolveTeamEntity(change.Target, out Entity targetTeam))
                        {
                            RemoveTags(targetTeam, rule.RemoveTagsFromTargetTeam);
                        }

                        FireEvent(engine, rule.ExitEventKey, change);
                    }
                }
            }
        }

        private void ApplyTags(Entity entity, ReadOnlySpan<int> tagIds)
        {
            if (!_world.IsAlive(entity) || tagIds.Length == 0)
            {
                return;
            }

            EnsureTagState(entity);
            ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(entity);
            ref TagCountContainer counts = ref _world.Get<TagCountContainer>(entity);
            ref DirtyFlags dirty = ref _world.Get<DirtyFlags>(entity);
            for (int i = 0; i < tagIds.Length; i++)
            {
                if (tagIds[i] > 0)
                {
                    _tagOps.AddTag(ref tags, ref counts, tagIds[i], ref dirty);
                }
            }
        }

        private void RemoveTags(Entity entity, ReadOnlySpan<int> tagIds)
        {
            if (!_world.IsAlive(entity) || tagIds.Length == 0)
            {
                return;
            }

            EnsureTagState(entity);
            ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(entity);
            ref TagCountContainer counts = ref _world.Get<TagCountContainer>(entity);
            ref DirtyFlags dirty = ref _world.Get<DirtyFlags>(entity);
            for (int i = 0; i < tagIds.Length; i++)
            {
                if (tagIds[i] > 0)
                {
                    _tagOps.RemoveTag(ref tags, ref counts, tagIds[i], ref dirty);
                }
            }
        }

        private bool TryResolveTeamEntity(Entity entity, out Entity teamEntity)
        {
            teamEntity = Entity.Null;
            if (!_world.IsAlive(entity) || !_world.Has<Ludots.Core.Gameplay.Components.Team>(entity))
            {
                return false;
            }

            int teamId = _world.Get<Ludots.Core.Gameplay.Components.Team>(entity).Id;
            if (teamId == 0)
            {
                return false;
            }

            teamEntity = _teamLookup.Get(teamId);
            return teamEntity != Entity.Null && _world.IsAlive(teamEntity);
        }

        private void EnsureTagState(Entity entity)
        {
            TagStateInstaller.EnsureInstalled(_world, entity);
        }

        private static ScriptContext CreateContext(GameEngine engine)
        {
            ScriptContext context = engine.CreateContext();
            if (!context.TryGet(CoreServiceKeys.MapId, out _))
            {
                throw new InvalidOperationException("Relationship callback events require an active MapId service.");
            }

            return context;
        }

        private static void FireEvent(GameEngine engine, EventKey eventKey, in RelationshipChangeRecord change)
        {
            if (string.IsNullOrWhiteSpace(eventKey.Value))
            {
                return;
            }

            ScriptContext context = CreateContext(engine);
            context.Set(CoreServiceKeys.RelationshipEventSource, change.Source);
            context.Set(CoreServiceKeys.RelationshipEventTarget, change.Target);
            context.Set(CoreServiceKeys.RelationshipEventTypeId, change.TypeId);
            context.Set(CoreServiceKeys.RelationshipEventMetricId, change.MetricId);
            context.Set(CoreServiceKeys.RelationshipEventMetricValue, change.NewValue);
            context.Set(CoreServiceKeys.RelationshipEventReasonId, change.ReasonId);
            engine.TriggerManager.FireEvent(eventKey, context);
        }
    }
}

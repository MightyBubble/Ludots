using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Publishes entity lifecycle facts into the presentation event stream.
    /// This is the SSOT bridge from ECS existence to performer observation.
    /// </summary>
    public sealed class PresentationEntityLifecycleSystem : BaseSystem<World, float>
    {
        private readonly PresentationEventStream _events;
        private readonly PerformerEntityRuntime? _performerRuntime;
        private readonly PerformerDefinitionRegistry? _definitions;

        private readonly QueryDescription _spawnedQuery = new QueryDescription()
            .WithAll<PresentationStableId>()
            .WithNone<PresentationLifecycleState>();

        private readonly QueryDescription _pendingDestroyQuery = new QueryDescription()
            .WithAll<PresentationStableId, PresentationDestroyPending>()
            .WithNone<PresentationDestroyEventPublished>();

        public PresentationEntityLifecycleSystem(
            World world,
            PresentationEventStream events,
            PerformerEntityRuntime? performerRuntime = null,
            PerformerDefinitionRegistry? definitions = null)
            : base(world)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _performerRuntime = performerRuntime;
            _definitions = definitions;
        }

        public override void Update(in float dt)
        {
            EmitSpawned();
            EmitDestroyed();
        }

        private void EmitSpawned()
        {
            var query = World.Query(in _spawnedQuery);
            foreach (var chunk in query)
            {
                var stableIds = chunk.GetArray<PresentationStableId>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity entity = chunk.Entity(i);
                    int templateKeyId = World.Has<EntityTemplateKeyCm>(entity)
                        ? World.Get<EntityTemplateKeyCm>(entity).TemplateKeyId
                        : 0;

                    if (!TryPublish(PresentationEventKind.EntitySpawned, entity, stableIds[i].Value, templateKeyId))
                    {
                        throw new InvalidOperationException("PresentationEventStream is full while publishing EntitySpawned.");
                    }

                    if (World.Has<ProjectileState>(entity))
                    {
                        ref readonly var projectile = ref World.Get<ProjectileState>(entity);
                        int effectTemplateId = projectile.PresentationEffectTemplateId > 0
                            ? projectile.PresentationEffectTemplateId
                            : projectile.ImpactEffectTemplateId;
                        if (effectTemplateId > 0 &&
                            !TryPublish(PresentationEventKind.ProjectileSpawned, entity, stableIds[i].Value, effectTemplateId))
                        {
                            throw new InvalidOperationException("PresentationEventStream is full while publishing ProjectileSpawned.");
                        }
                    }
                }
            }

            World.Add(_spawnedQuery, new PresentationLifecycleState { Spawned = true });
        }

        private void EmitDestroyed()
        {
            var query = World.Query(in _pendingDestroyQuery);
            foreach (var chunk in query)
            {
                var stableIds = chunk.GetArray<PresentationStableId>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity entity = chunk.Entity(i);
                    int templateKeyId = World.Has<EntityTemplateKeyCm>(entity)
                        ? World.Get<EntityTemplateKeyCm>(entity).TemplateKeyId
                        : 0;

                    if (!TryPublish(PresentationEventKind.EntityDestroyed, entity, stableIds[i].Value, templateKeyId))
                    {
                        throw new InvalidOperationException("PresentationEventStream is full while publishing EntityDestroyed.");
                    }

                    TryDestroyBootstrappedPerformers(entity, stableIds[i].Value, templateKeyId);
                }
            }

            World.Add<PresentationDestroyEventPublished>(in _pendingDestroyQuery);
        }

        private bool TryPublish(PresentationEventKind kind, Entity entity, int stableId, int templateKeyId)
        {
            return _events.TryAdd(new PresentationEvent
            {
                Kind = kind,
                Source = entity,
                Target = entity,
                KeyId = templateKeyId,
                PayloadA = stableId,
            });
        }

        private void TryDestroyBootstrappedPerformers(Entity entity, int stableId, int templateKeyId)
        {
            if (_performerRuntime == null || _definitions == null || templateKeyId <= 0)
            {
                return;
            }

            var bootstrap = _definitions.BootstrapRegistry;
            if (!bootstrap.TryGetEntityDestroyedDestroys(templateKeyId, out CompiledPerformerBootstrapRegistry.BootstrapDestroyRule[] rules))
            {
                return;
            }

            for (int i = 0; i < rules.Length; i++)
            {
                int scopeId = rules[i].ResolveScopeTag(stableId);
                if (scopeId > 0)
                {
                    _performerRuntime.DestroyScope(scopeId);
                }
            }
        }
    }
}

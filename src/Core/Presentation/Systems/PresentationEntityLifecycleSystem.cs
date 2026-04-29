using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Commands;
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
        private readonly PresentationStableIdAllocator? _stableIds;
        private readonly CommandBuffer _commandBuffer = new();
        private readonly List<SpawnBootstrapWork> _spawnBootstrapWork = new(256);
        private readonly List<DestroyBootstrapWork> _destroyBootstrapWork = new(256);

        private readonly QueryDescription _spawnedQuery = new QueryDescription()
            .WithAll<PresentationStableId>()
            .WithNone<PresentationLifecycleState>();

        private readonly QueryDescription _pendingDestroyQuery = new QueryDescription()
            .WithAll<PresentationStableId, PresentationDestroyPending>()
            .WithNone<PresentationDestroyEventPublished>();

        private readonly struct SpawnBootstrapWork
        {
            public readonly Entity Entity;
            public readonly int StableId;
            public readonly int TemplateKeyId;

            public SpawnBootstrapWork(Entity entity, int stableId, int templateKeyId)
            {
                Entity = entity;
                StableId = stableId;
                TemplateKeyId = templateKeyId;
            }
        }

        private readonly struct DestroyBootstrapWork
        {
            public readonly int StableId;
            public readonly int TemplateKeyId;

            public DestroyBootstrapWork(int stableId, int templateKeyId)
            {
                StableId = stableId;
                TemplateKeyId = templateKeyId;
            }
        }

        public PresentationEntityLifecycleSystem(
            World world,
            PresentationEventStream events,
            PerformerEntityRuntime? performerRuntime = null,
            PerformerDefinitionRegistry? definitions = null,
            PresentationStableIdAllocator? stableIds = null)
            : base(world)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _performerRuntime = performerRuntime;
            _definitions = definitions;
            _stableIds = stableIds;
        }

        public override void Update(in float dt)
        {
            EmitSpawned();
            EmitDestroyed();
            PlaybackStructuralChanges();
        }

        private void EmitSpawned()
        {
            _spawnBootstrapWork.Clear();
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

                    if (ShouldCreateBootstrappedPerformers(entity, templateKeyId))
                    {
                        _spawnBootstrapWork.Add(new SpawnBootstrapWork(entity, stableIds[i].Value, templateKeyId));
                    }

                    _commandBuffer.Add(entity, new PresentationLifecycleState { Spawned = true });

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

            ProcessSpawnBootstrapWork();
        }

        private bool ShouldCreateBootstrappedPerformers(Entity entity, int templateKeyId)
        {
            if (_performerRuntime == null || _definitions == null || templateKeyId <= 0)
            {
                return false;
            }

            if (World.Has<PerformerRootBootstrapHandled>(entity))
            {
                return false;
            }

            var bootstrap = _definitions.BootstrapRegistry;
            return bootstrap.TryGetEntitySpawnCreates(templateKeyId, out _);
        }

        private void ProcessSpawnBootstrapWork()
        {
            if (_performerRuntime == null || _definitions == null)
            {
                return;
            }

            for (int workIndex = 0; workIndex < _spawnBootstrapWork.Count; workIndex++)
            {
                SpawnBootstrapWork work = _spawnBootstrapWork[workIndex];
                if (!World.IsAlive(work.Entity) ||
                    World.Has<PerformerRootBootstrapHandled>(work.Entity) ||
                    !_definitions.BootstrapRegistry.TryGetEntitySpawnCreates(work.TemplateKeyId, out CompiledPerformerBootstrapRegistry.BootstrapCreateRule[] rules))
                {
                    continue;
                }

                bool createdAny = false;
                for (int i = 0; i < rules.Length; i++)
                {
                    ref readonly CompiledPerformerBootstrapRegistry.BootstrapCreateRule rule = ref rules[i];
                    if (!EvaluateBootstrapCondition(work.Entity, rule.InlineCondition))
                    {
                        continue;
                    }

                    int scopeId = rule.ResolveScopeTag(work.StableId);
                    if (!_definitions.TryGet(rule.PerformerDefinitionId, out PerformerDefinition definition))
                    {
                        throw new InvalidOperationException(
                            $"Compiled performer bootstrap references missing definition id={rule.PerformerDefinitionId}.");
                    }

                    _performerRuntime.CreateHierarchy(
                        _definitions,
                        rule.PerformerDefinitionId,
                        work.Entity,
                        scopeId,
                        PresentationAnchorKind.Entity,
                        Vector3.Zero,
                        AllocatePerformerStableId(work.StableId),
                        Entity.Null,
                        definition,
                        () => AllocatePerformerStableId(work.StableId));
                    createdAny = true;
                }

                if (createdAny)
                {
                    _commandBuffer.Add(work.Entity, new PerformerRootBootstrapHandled());
                }
            }
        }

        private int AllocatePerformerStableId(int ownerStableId)
        {
            return _stableIds?.Allocate() ?? ownerStableId;
        }

        private bool EvaluateBootstrapCondition(Entity entity, InlineConditionKind condition)
        {
            return condition switch
            {
                InlineConditionKind.None => true,
                InlineConditionKind.SourceHasVisualTransform => World.Has<VisualTransform>(entity),
                InlineConditionKind.SourceHasAttributes => World.Has<AttributeBuffer>(entity),
                _ => false,
            };
        }

        private void EmitDestroyed()
        {
            _destroyBootstrapWork.Clear();
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

                    if (templateKeyId > 0)
                    {
                        _destroyBootstrapWork.Add(new DestroyBootstrapWork(stableIds[i].Value, templateKeyId));
                    }

                    _commandBuffer.Add<PresentationDestroyEventPublished>(in entity);
                }
            }

            ProcessDestroyBootstrapWork();
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

        private void ProcessDestroyBootstrapWork()
        {
            if (_performerRuntime == null || _definitions == null)
            {
                return;
            }

            for (int workIndex = 0; workIndex < _destroyBootstrapWork.Count; workIndex++)
            {
                DestroyBootstrapWork work = _destroyBootstrapWork[workIndex];
                if (!_definitions.BootstrapRegistry.TryGetEntityDestroyedDestroys(work.TemplateKeyId, out CompiledPerformerBootstrapRegistry.BootstrapDestroyRule[] rules))
                {
                    continue;
                }

                for (int i = 0; i < rules.Length; i++)
                {
                    int scopeId = rules[i].ResolveScopeTag(work.StableId);
                    if (scopeId > 0)
                    {
                        _performerRuntime.DestroyScope(scopeId);
                    }
                }
            }
        }

        private void PlaybackStructuralChanges()
        {
            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }
    }
}

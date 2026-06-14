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
    /// This is the SSOT projection from ECS existence to performer observation.
    /// </summary>
    public sealed class PresentationEntityLifecycleSystem : BaseSystem<World, float>
    {
        private readonly PresentationEventStream _events;
        private readonly PerformerEntityRuntime? _performerRuntime;
        private readonly PerformerDefinitionRegistry? _definitions;
        private readonly PresentationStableIdAllocator? _stableIds;
        private readonly bool _createBootstrappedPerformers;
        private readonly CommandBuffer _commandBuffer = new();
        private readonly List<SpawnBootstrapWork> _spawnBootstrapWork = new(256);

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

        public PresentationEntityLifecycleSystem(
            World world,
            PresentationEventStream events)
            : this(world, events, null, null, null, createBootstrappedPerformers: false)
        {
        }

        public PresentationEntityLifecycleSystem(
            World world,
            PresentationEventStream events,
            PerformerEntityRuntime performerRuntime,
            PerformerDefinitionRegistry definitions,
            PresentationStableIdAllocator stableIds)
            : this(world, events, performerRuntime, definitions, stableIds, createBootstrappedPerformers: true)
        {
        }

        private PresentationEntityLifecycleSystem(
            World world,
            PresentationEventStream events,
            PerformerEntityRuntime? performerRuntime,
            PerformerDefinitionRegistry? definitions,
            PresentationStableIdAllocator? stableIds,
            bool createBootstrappedPerformers)
            : base(world)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _performerRuntime = performerRuntime;
            _definitions = definitions;
            _stableIds = stableIds;
            _createBootstrappedPerformers = createBootstrappedPerformers;
            if (_createBootstrappedPerformers)
            {
                ArgumentNullException.ThrowIfNull(_performerRuntime);
                ArgumentNullException.ThrowIfNull(_definitions);
                ArgumentNullException.ThrowIfNull(_stableIds);
            }
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
            if (!_createBootstrappedPerformers || templateKeyId <= 0)
            {
                return false;
            }

            if (World.Has<PerformerRootBootstrapHandled>(entity))
            {
                return false;
            }

            var bootstrap = _definitions!.BootstrapRegistry;
            return bootstrap.TryGetEntitySpawnCreates(templateKeyId, out _);
        }

        private void ProcessSpawnBootstrapWork()
        {
            if (!_createBootstrappedPerformers)
            {
                return;
            }

            PerformerEntityRuntime performerRuntime = _performerRuntime!;
            PerformerDefinitionRegistry definitions = _definitions!;
            for (int workIndex = 0; workIndex < _spawnBootstrapWork.Count; workIndex++)
            {
                SpawnBootstrapWork work = _spawnBootstrapWork[workIndex];
                if (!World.IsAlive(work.Entity) ||
                    World.Has<PerformerRootBootstrapHandled>(work.Entity) ||
                    !definitions.BootstrapRegistry.TryGetEntitySpawnCreates(work.TemplateKeyId, out CompiledPerformerBootstrapRegistry.BootstrapCreateRule[] rules))
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
                    if (!definitions.TryGet(rule.PerformerDefinitionId, out PerformerDefinition definition))
                    {
                        throw new InvalidOperationException(
                            $"Compiled performer bootstrap references missing definition id={rule.PerformerDefinitionId}.");
                    }

                    Entity rootPerformer = performerRuntime.CreateHierarchy(
                        definitions,
                        rule.PerformerDefinitionId,
                        work.Entity,
                        scopeId,
                        PresentationAnchorKind.Entity,
                        Vector3.Zero,
                        AllocatePerformerStableId(),
                        Entity.Null,
                        definition,
                        AllocatePerformerStableId);
                    if (definition.RequiresBootstrapProcessing &&
                        !World.Has<PerformerBootstrapPending>(rootPerformer))
                    {
                        _commandBuffer.Add(rootPerformer, new PerformerBootstrapPending());
                    }

                    createdAny = true;
                }

                if (createdAny)
                {
                    _commandBuffer.Add(work.Entity, new PerformerRootBootstrapHandled());
                }
            }
        }

        private int AllocatePerformerStableId()
        {
            return _stableIds!.Allocate();
        }

        private bool EvaluateBootstrapCondition(Entity entity, InlineConditionKind condition)
        {
            return condition switch
            {
                InlineConditionKind.None => true,
                InlineConditionKind.SourceHasVisualTransform => World.Has<VisualTransform>(entity),
                InlineConditionKind.SourceHasAttributes => World.Has<AttributeBuffer>(entity),
                _ => throw new InvalidOperationException($"Unsupported performer bootstrap inline condition '{condition}'."),
            };
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

                    _commandBuffer.Add<PresentationDestroyEventPublished>(in entity);
                }
            }
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

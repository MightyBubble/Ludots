using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Gameplay.Spawning
{
    public sealed class RuntimeEntitySpawnSystem : BaseSystem<World, float>
    {
        private readonly RuntimeEntitySpawnQueue _requests;
        private readonly EffectRequestQueue _effectRequests;
        private readonly DataRegistry<EntityTemplate> _templateRegistry;
        private readonly Dictionary<string, EntityTemplate> _cachedTemplates = new(StringComparer.OrdinalIgnoreCase);
        private readonly EntityBuilder _builder;

        public RuntimeEntitySpawnSystem(
            World world,
            RuntimeEntitySpawnQueue requests,
            DataRegistry<EntityTemplate> templateRegistry,
            EffectRequestQueue effectRequests = null)
            : base(world)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _templateRegistry = templateRegistry ?? throw new ArgumentNullException(nameof(templateRegistry));
            _effectRequests = effectRequests;
            _builder = new EntityBuilder(world, _cachedTemplates);
        }

        public override void Update(in float dt)
        {
            while (_requests.TryDequeue(out var request))
            {
                var spawned = request.Kind switch
                {
                    RuntimeEntitySpawnKind.UnitType => SpawnUnitType(request),
                    RuntimeEntitySpawnKind.Template => SpawnTemplate(request),
                    _ => throw new InvalidOperationException($"Unsupported runtime spawn kind '{request.Kind}'."),
                };

                PublishOnSpawnEffect(in request, spawned);
            }
        }

        private Entity SpawnUnitType(in RuntimeEntitySpawnRequest request)
        {
            if (request.UnitTypeId <= 0)
            {
                throw new InvalidOperationException("Runtime unit spawn requires a positive UnitTypeId.");
            }

            var entity = World.Create(
                new WorldPositionCm { Value = request.WorldPositionCm },
                new PreviousWorldPositionCm { Value = request.WorldPositionCm },
                VisualTransform.Default,
                new CullState { IsVisible = true, LOD = LODLevel.High },
                new AttributeBuffer());

            string typeName = UnitTypeRegistry.GetName(request.UnitTypeId);
            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new InvalidOperationException($"Runtime unit spawn references unknown UnitTypeId '{request.UnitTypeId}'.");
            }

            World.Add(entity, new Name { Value = "Unit:" + typeName });
            TryApplySourceTeam(in request, entity);
            TryApplyMapOwnership(in request, entity);
            return entity;
        }

        private Entity SpawnTemplate(in RuntimeEntitySpawnRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TemplateId))
            {
                throw new InvalidOperationException("Runtime template spawn requires a non-empty TemplateId.");
            }

            EnsureTemplateLoaded(request.TemplateId);
            var entity = _builder.UseTemplate(request.TemplateId).Build();

            ApplyWorldPosition(entity, request.WorldPositionCm);
            TryApplySourceTeam(in request, entity);
            TryApplyMapOwnership(in request, entity);
            return entity;
        }

        private void EnsureTemplateLoaded(string templateId)
        {
            if (_cachedTemplates.ContainsKey(templateId))
            {
                return;
            }

            var template = _templateRegistry.Get(templateId);
            if (template == null)
            {
                throw new InvalidOperationException($"Runtime template spawn references unknown template '{templateId}'.");
            }

            _cachedTemplates[templateId] = template;
        }

        private void ApplyWorldPosition(Entity entity, in Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 worldPositionCm)
        {
            var position = new WorldPositionCm { Value = worldPositionCm };
            var previous = new PreviousWorldPositionCm { Value = worldPositionCm };

            if (World.Has<WorldPositionCm>(entity))
            {
                World.Set(entity, position);
            }
            else
            {
                World.Add(entity, position);
            }

            if (World.Has<PreviousWorldPositionCm>(entity))
            {
                World.Set(entity, previous);
            }
            else
            {
                World.Add(entity, previous);
            }

            if (!World.Has<VisualTransform>(entity))
            {
                World.Add(entity, VisualTransform.Default);
            }

            if (!World.Has<CullState>(entity))
            {
                World.Add(entity, new CullState { IsVisible = true, LOD = LODLevel.High });
            }
        }

        private void TryApplySourceTeam(in RuntimeEntitySpawnRequest request, Entity entity)
        {
            if (request.CopySourceTeam == 0)
            {
                return;
            }

            if (!World.IsAlive(request.Source) || !World.Has<Team>(request.Source))
            {
                return;
            }

            var team = World.Get<Team>(request.Source);
            if (World.Has<Team>(entity))
            {
                World.Set(entity, team);
            }
            else
            {
                World.Add(entity, team);
            }
        }

        private void TryApplyMapOwnership(in RuntimeEntitySpawnRequest request, Entity entity)
        {
            var mapId = request.MapId;
            if (string.IsNullOrWhiteSpace(mapId.Value) &&
                World.IsAlive(request.Source) &&
                World.Has<MapEntity>(request.Source))
            {
                mapId = World.Get<MapEntity>(request.Source).MapId;
            }

            if (string.IsNullOrWhiteSpace(mapId.Value))
            {
                return;
            }

            var mapEntity = new MapEntity { MapId = mapId };
            if (World.Has<MapEntity>(entity))
            {
                World.Set(entity, mapEntity);
            }
            else
            {
                World.Add(entity, mapEntity);
            }
        }

        private void PublishOnSpawnEffect(in RuntimeEntitySpawnRequest request, Entity spawned)
        {
            if (_effectRequests == null || request.OnSpawnEffectTemplateId <= 0)
            {
                return;
            }

            _effectRequests.Publish(new EffectRequest
            {
                RootId = 0,
                Source = request.Source,
                Target = spawned,
                TargetContext = request.TargetContext,
                TemplateId = request.OnSpawnEffectTemplateId,
            });
        }
    }
}

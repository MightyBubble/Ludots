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
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Spatial;

namespace Ludots.Core.Gameplay.Spawning
{
    public sealed class RuntimeEntitySpawnSystem : BaseSystem<World, float>
    {
        private const int BatchEntityScratchCapacity = 32768;
        private readonly RuntimeEntitySpawnQueue _requests;
        private readonly EffectRequestQueue _effectRequests;
        private readonly DataRegistry<EntityTemplate> _templateRegistry;
        private readonly EntityTemplateKeyRegistry _templateKeys;
        private readonly Dictionary<string, EntityTemplate> _cachedTemplates = new(StringComparer.OrdinalIgnoreCase);
        private readonly EntityBuilder _builder;
        private readonly PresentationStableIdAllocator _stableIds;
        private readonly RuntimeEntitySpawnRequest[] _batchRequests = new RuntimeEntitySpawnRequest[BatchEntityScratchCapacity];
        private readonly TemplateEntityBatchSpawner.TemplateBatchSpawnRequest[] _templateBatchRequests = new TemplateEntityBatchSpawner.TemplateBatchSpawnRequest[BatchEntityScratchCapacity];
        private readonly Entity[] _performerBatchOwners = new Entity[BatchEntityScratchCapacity];
        private readonly int[] _performerBatchScopeIds = new int[BatchEntityScratchCapacity];
        private readonly int[] _performerBatchStableIds = new int[BatchEntityScratchCapacity];
        private readonly Entity[] _performerBatchCreated = new Entity[BatchEntityScratchCapacity];
        private readonly VisualTransform[] _performerBatchOwnerTransforms = new VisualTransform[BatchEntityScratchCapacity];
        private readonly CullState[] _performerBatchOwnerCulls = new CullState[BatchEntityScratchCapacity];
        private readonly TemplateEntityBatchSpawner _templateBatchSpawner;
        private readonly PerformerEntityRuntime? _performerRuntime;
        private readonly PerformerDefinitionRegistry? _performerDefinitions;
        private readonly CompiledPerformerBootstrapRegistry? _performerBootstrap;
        private readonly PresentationEventStream? _presentationEvents;
        private readonly ISpatialPartitionWorld? _spatialPartition;
        private readonly WorldSizeSpec _worldSizeSpec;

        public RuntimeEntitySpawnSystem(
            World world,
            RuntimeEntitySpawnQueue requests,
            DataRegistry<EntityTemplate> templateRegistry,
            EntityTemplateKeyRegistry templateKeys,
            PresentationStableIdAllocator stableIds,
            EffectRequestQueue effectRequests = null,
            PerformerEntityRuntime? performerRuntime = null,
            PerformerDefinitionRegistry? performerDefinitions = null,
            PresentationEventStream? presentationEvents = null,
            ISpatialPartitionWorld? spatialPartition = null,
            WorldSizeSpec worldSizeSpec = default)
            : base(world)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _templateRegistry = templateRegistry ?? throw new ArgumentNullException(nameof(templateRegistry));
            _templateKeys = templateKeys ?? throw new ArgumentNullException(nameof(templateKeys));
            _effectRequests = effectRequests;
            _builder = new EntityBuilder(world, _cachedTemplates);
            _stableIds = stableIds ?? throw new ArgumentNullException(nameof(stableIds));
            _spatialPartition = spatialPartition;
            _worldSizeSpec = worldSizeSpec;
            _templateBatchSpawner = new TemplateEntityBatchSpawner(
                world,
                templateKeys,
                stableIds,
                spatialPartition,
                worldSizeSpec,
                BatchEntityScratchCapacity);
            _performerRuntime = performerRuntime;
            _performerDefinitions = performerDefinitions;
            _performerBootstrap = performerDefinitions?.BootstrapRegistry;
            _presentationEvents = presentationEvents;
        }

        public override void Update(in float dt)
        {
            while (_requests.TryPeek(out var peek))
            {
                if (peek.Kind == RuntimeEntitySpawnKind.Template &&
                    !string.IsNullOrWhiteSpace(peek.TemplateId) &&
                    TryGetTemplate(peek.TemplateId, out EntityTemplate template) &&
                    _templateBatchSpawner.IsBatchCompatible(peek.TemplateId, template))
                {
                    if (!TryDrainTemplateBatch(peek.TemplateId, out int batchCount))
                    {
                        break;
                    }

                    if (batchCount > 1)
                    {
                        if (!TrySpawnTemplateBatch(peek.TemplateId, template, batchCount))
                        {
                            for (int i = 0; i < batchCount; i++)
                            {
                                var spawnedFallback = SpawnTemplate(_batchRequests[i]);
                                PublishOnSpawnEffect(in _batchRequests[i], spawnedFallback);
                            }
                        }

                        continue;
                    }

                    var singleRequest = _batchRequests[0];
                    var spawnedSingle = SpawnTemplate(singleRequest);
                    PublishOnSpawnEffect(in singleRequest, spawnedSingle);
                    continue;
                }

                if (!_requests.TryDequeue(out var request))
                {
                    break;
                }

                var spawned = request.Kind switch
                {
                    RuntimeEntitySpawnKind.UnitType => SpawnUnitType(request),
                    RuntimeEntitySpawnKind.Template => SpawnTemplate(request),
                    RuntimeEntitySpawnKind.Assembly => SpawnAssembly(request),
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
                new CullState { IsVisible = false, LOD = LODLevel.Culled },
                new AttributeBuffer());
            EnsurePresentationStableId(entity);

            string typeName = UnitTypeRegistry.GetName(request.UnitTypeId);
            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new InvalidOperationException($"Runtime unit spawn references unknown UnitTypeId '{request.UnitTypeId}'.");
            }

            World.Add(entity, new Name { Value = "Unit:" + typeName });
            TryApplyFacing(in request, entity);
            TryApplySourceTeam(in request, entity);
            TryApplySourcePlayerOwner(in request, entity);
            TryApplyMapOwnership(in request, entity);
            TryApplyParentLink(in request, entity);
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
            ApplyTemplateKey(entity, request.TemplateId);

            if (request.HasWorldPosition != 0)
            {
                ApplyWorldPosition(entity, request.WorldPositionCm);
            }
            else
            {
                EnsurePresentationStableId(entity);
            }

            TryApplyFacing(in request, entity);
            TryApplySourceTeam(in request, entity);
            TryApplySourcePlayerOwner(in request, entity);
            TryApplyMapOwnership(in request, entity);
            TryApplyParentLink(in request, entity);
            TryBootstrapPerformer(entity, request.TemplateId);
            return entity;
        }

        private bool TryDrainTemplateBatch(string templateId, out int count)
        {
            count = 0;
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return false;
            }

            while (count < _batchRequests.Length &&
                   _requests.TryPeek(out var next) &&
                   next.Kind == RuntimeEntitySpawnKind.Template &&
                   string.Equals(next.TemplateId, templateId, StringComparison.Ordinal))
            {
                if (!_requests.TryDequeue(out _batchRequests[count]))
                {
                    break;
                }

                count++;
            }

            return count > 0;
        }

        private bool TrySpawnTemplateBatch(string templateId, EntityTemplate template, int count)
        {
            bool allHaveMapEntity = true;
            for (int i = 0; i < count; i++)
            {
                ref readonly var request = ref _batchRequests[i];
                MapEntity? mapEntity = TryResolveMapEntity(in request);
                allHaveMapEntity &= mapEntity.HasValue;
                _templateBatchRequests[i] = new TemplateEntityBatchSpawner.TemplateBatchSpawnRequest(
                    request.WorldPositionCm,
                    hasWorldPosition: request.HasWorldPosition != 0,
                    facingAngleRad: request.FacingAngleRad,
                    hasFacing: request.HasFacing != 0,
                    mapEntity: mapEntity ?? default,
                    hasMapEntity: mapEntity.HasValue);
            }

            int templateKeyId = ResolveOrRegisterTemplateKeyId(templateId);
            bool hasDirectBootstrap = HasDirectEntitySpawnBootstrap(templateKeyId);
            bool publishSpawnedEvent = ShouldPublishSpawnedEvent(templateKeyId, hasDirectBootstrap);

            TemplateBatchSpawnFeatures features =
                TemplateBatchSpawnFeatures.PresentationStableId |
                TemplateBatchSpawnFeatures.PresentationLifecycleState;
            if (allHaveMapEntity)
            {
                features |= TemplateBatchSpawnFeatures.MapEntity;
            }

            if (!_templateBatchSpawner.TryCreateBatch(
                templateId,
                template,
                _templateBatchRequests.AsSpan(0, count),
                features,
                out ReadOnlySpan<Entity> created,
                _performerBatchStableIds.AsSpan(0, count),
                _performerBatchOwnerTransforms.AsSpan(0, count),
                _performerBatchOwnerCulls.AsSpan(0, count)))
            {
                return false;
            }

            int onSpawnEffectTemplateId = _templateBatchSpawner.GetOnSpawnEffectTemplateId(templateId, template);
            for (int i = 0; i < created.Length; i++)
            {
                Entity entity = created[i];
                ref readonly var request = ref _batchRequests[i];
                TryApplySourceTeam(in request, entity);
                TryApplySourcePlayerOwner(in request, entity);
                if (!allHaveMapEntity)
                {
                    TryApplyMapOwnership(in request, entity);
                }

                TryApplyParentLink(in request, entity);
                if (publishSpawnedEvent)
                {
                    PublishSpawnedPresentationEvent(entity);
                }

                if (onSpawnEffectTemplateId > 0)
                {
                    PublishOnSpawnEffect(in request, entity, onSpawnEffectTemplateId);
                }
            }

            if (hasDirectBootstrap)
            {
                TryBootstrapPerformerBatch(
                    templateKeyId,
                    created,
                    _performerBatchStableIds.AsSpan(0, created.Length),
                    _performerBatchOwnerTransforms.AsSpan(0, created.Length),
                    _performerBatchOwnerCulls.AsSpan(0, created.Length));
            }

            return true;
        }

        private Entity SpawnAssembly(in RuntimeEntitySpawnRequest request)
        {
            var entity = base.World.Create();

            if (request.HasProjectileState != 0)
            {
                base.World.Add(entity, request.Projectile);
            }

            if (request.HasWorldPosition != 0)
            {
                ApplyWorldPosition(entity, request.WorldPositionCm);
            }

            TryApplyFacing(in request, entity);
            TryApplySourceTeam(in request, entity);
            TryApplySourcePlayerOwner(in request, entity);
            TryApplyMapOwnership(in request, entity);
            TryApplyParentLink(in request, entity);
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

        private bool TryGetTemplate(string templateId, out EntityTemplate template)
        {
            template = null;
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return false;
            }

            EnsureTemplateLoaded(templateId);
            template = _cachedTemplates[templateId];
            return template != null;
        }

        private void ApplyTemplateKey(Entity entity, string templateId)
        {
            int templateKeyId = _templateKeys.GetId(templateId);
            if (templateKeyId <= 0)
            {
                templateKeyId = _templateKeys.Register(templateId);
            }

            var templateKey = new EntityTemplateKeyCm { TemplateKeyId = templateKeyId };
            if (World.Has<EntityTemplateKeyCm>(entity))
            {
                World.Set(entity, templateKey);
            }
            else
            {
                World.Add(entity, templateKey);
            }
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
                World.Add(entity, new CullState { IsVisible = false, LOD = LODLevel.Culled });
            }

            EnsurePresentationStableId(entity);
        }

        private void EnsurePresentationStableId(Entity entity)
        {
            if (World.Has<PresentationStableId>(entity))
            {
                return;
            }

            World.Add(entity, new PresentationStableId { Value = _stableIds.Allocate() });
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

        private void TryApplySourcePlayerOwner(in RuntimeEntitySpawnRequest request, Entity entity)
        {
            if (request.CopySourcePlayerOwner == 0)
            {
                return;
            }

            if (!World.IsAlive(request.Source) || !World.Has<PlayerOwner>(request.Source))
            {
                return;
            }

            var owner = World.Get<PlayerOwner>(request.Source);
            if (World.Has<PlayerOwner>(entity))
            {
                World.Set(entity, owner);
            }
            else
            {
                World.Add(entity, owner);
            }
        }

        private void TryApplyFacing(in RuntimeEntitySpawnRequest request, Entity entity)
        {
            if (request.HasFacing == 0)
            {
                return;
            }

            var facing = new FacingDirection { AngleRad = request.FacingAngleRad };
            if (World.Has<FacingDirection>(entity))
            {
                World.Set(entity, facing);
            }
            else
            {
                World.Add(entity, facing);
            }
        }

        private void TryApplyMapOwnership(in RuntimeEntitySpawnRequest request, Entity entity)
        {
            MapEntity? mapEntity = TryResolveMapEntity(in request);
            if (!mapEntity.HasValue)
            {
                return;
            }

            if (World.Has<MapEntity>(entity))
            {
                World.Set(entity, mapEntity.Value);
            }
            else
            {
                World.Add(entity, mapEntity.Value);
            }
        }

        private MapEntity? TryResolveMapEntity(in RuntimeEntitySpawnRequest request)
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
                return null;
            }

            return new MapEntity { MapId = mapId };
        }

        private void TryApplyParentLink(in RuntimeEntitySpawnRequest request, Entity entity)
        {
            Entity parent = request.LinkSourceAsParent != 0 ? request.Source : request.Parent;
            if (!World.IsAlive(parent))
            {
                return;
            }

            RelationOps.SetParent(World, entity, parent);
        }

        private void PublishOnSpawnEffect(in RuntimeEntitySpawnRequest request, Entity spawned)
        {
            PublishOnSpawnEffect(in request, spawned, 0);
        }

        private void PublishSpawnedPresentationEvent(Entity entity)
        {
            if (_presentationEvents == null ||
                !World.IsAlive(entity) ||
                !World.Has<PresentationStableId>(entity))
            {
                return;
            }

            int templateKeyId = World.Has<EntityTemplateKeyCm>(entity)
                ? World.Get<EntityTemplateKeyCm>(entity).TemplateKeyId
                : 0;
            if (!_presentationEvents.TryAdd(new PresentationEvent
                {
                    Kind = PresentationEventKind.EntitySpawned,
                    Source = entity,
                    Target = entity,
                    KeyId = templateKeyId,
                    PayloadA = World.Get<PresentationStableId>(entity).Value,
                }))
            {
                throw new InvalidOperationException("PresentationEventStream is full while publishing batch EntitySpawned.");
            }
        }

        private void PublishOnSpawnEffect(in RuntimeEntitySpawnRequest request, Entity spawned, int cachedTemplateOnSpawnEffectId)
        {
            if (_effectRequests == null)
            {
                return;
            }

            int effectTemplateId = request.OnSpawnEffectTemplateId > 0
                ? request.OnSpawnEffectTemplateId
                : cachedTemplateOnSpawnEffectId;
            bool useSpawnedAsSource = false;
            if (effectTemplateId <= 0 &&
                request.Kind == RuntimeEntitySpawnKind.Template &&
                !string.IsNullOrWhiteSpace(request.TemplateId))
            {
                var template = _templateRegistry.Get(request.TemplateId);
                if (template != null && !string.IsNullOrWhiteSpace(template.OnSpawnEffect))
                {
                    effectTemplateId = EffectTemplateIdRegistry.GetId(template.OnSpawnEffect);
                    if (effectTemplateId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Entity template '{request.TemplateId}' references unknown onSpawnEffect '{template.OnSpawnEffect}'.");
                    }
                }
            }

            if (effectTemplateId <= 0)
            {
                return;
            }

            useSpawnedAsSource = request.OnSpawnEffectTemplateId <= 0;
            _effectRequests.Publish(new EffectRequest
            {
                RootId = 0,
                Source = useSpawnedAsSource ? spawned : request.Source,
                Target = spawned,
                TargetContext = useSpawnedAsSource ? spawned : request.TargetContext,
                TemplateId = effectTemplateId,
            });
        }

        private void TryBootstrapPerformer(Entity owner, string templateId)
        {
            if (_performerRuntime == null || _performerDefinitions == null || _performerBootstrap == null)
            {
                return;
            }

            int templateKeyId = ResolveTemplateKeyId(templateId, owner);
            if (templateKeyId <= 0 ||
                !_performerBootstrap.TryGetEntitySpawnCreates(templateKeyId, out CompiledPerformerBootstrapRegistry.BootstrapCreateRule[] rules))
            {
                return;
            }

            int stableId = World.Has<PresentationStableId>(owner)
                ? World.Get<PresentationStableId>(owner).Value
                : 0;
            for (int i = 0; i < rules.Length; i++)
            {
                ref readonly var rule = ref rules[i];
                if (!PassesBootstrapCondition(rule, owner))
                {
                    continue;
                }

                if (!_performerDefinitions.TryGet(rule.PerformerDefinitionId, out PerformerDefinition definition))
                {
                    throw new InvalidOperationException($"Performer definition id={rule.PerformerDefinitionId} is not registered.");
                }

                int scopeTag = rule.ResolveScopeTag(stableId);
                if (scopeTag <= 0)
                {
                    continue;
                }

                if (_performerRuntime.HasActiveScopedInstance(rule.PerformerDefinitionId, owner, scopeTag, PresentationAnchorKind.Entity, default))
                {
                    continue;
                }

                Entity root = _performerRuntime.CreateHierarchy(
                    _performerDefinitions,
                    rule.PerformerDefinitionId,
                    owner,
                    scopeTag,
                    PresentationAnchorKind.Entity,
                    default,
                    _stableIds.Allocate(),
                    Entity.Null,
                    definition,
                    _stableIds.Allocate);
                MarkHierarchyForBootstrapIfNeeded(root);
                MarkOwnerBootstrapHandled(owner);
            }
        }

        private void TryBootstrapPerformerBatch(
            int templateKeyId,
            ReadOnlySpan<Entity> owners,
            ReadOnlySpan<int> stableIds,
            ReadOnlySpan<VisualTransform> ownerTransforms,
            ReadOnlySpan<CullState> ownerCulls)
        {
            if (_performerRuntime == null || _performerDefinitions == null || _performerBootstrap == null || owners.Length == 0)
            {
                return;
            }

            if (owners.Length != stableIds.Length ||
                owners.Length != ownerTransforms.Length ||
                owners.Length != ownerCulls.Length)
            {
                throw new ArgumentException("Performer bootstrap batch spans must have matching lengths.");
            }

            if (templateKeyId <= 0 ||
                !_performerBootstrap.TryGetEntitySpawnCreates(templateKeyId, out CompiledPerformerBootstrapRegistry.BootstrapCreateRule[] rules))
            {
                return;
            }

            for (int ri = 0; ri < rules.Length; ri++)
            {
                ref readonly var rule = ref rules[ri];
                if (!_performerDefinitions.TryGet(rule.PerformerDefinitionId, out PerformerDefinition definition))
                {
                    throw new InvalidOperationException($"Performer definition id={rule.PerformerDefinitionId} is not registered.");
                }

                int createCount = 0;
                for (int oi = 0; oi < owners.Length; oi++)
                {
                    Entity owner = owners[oi];
                    if (!PassesBootstrapCondition(rule, owner))
                    {
                        continue;
                    }

                    int stableId = stableIds[oi];
                    int scopeTag = rule.ResolveScopeTag(stableId);
                    if (scopeTag <= 0)
                    {
                        continue;
                    }

                    _performerBatchOwners[createCount] = owner;
                    _performerBatchScopeIds[createCount] = scopeTag;
                    _performerBatchStableIds[createCount] = _stableIds.Allocate();
                    _performerBatchOwnerTransforms[createCount] = ownerTransforms[oi];
                    _performerBatchOwnerCulls[createCount] = ownerCulls[oi];
                    createCount++;
                }

                if (createCount == 0)
                {
                    continue;
                }

                _performerRuntime.CreateEntityAnchoredRootBatch(
                    _performerDefinitions,
                    rule.PerformerDefinitionId,
                    _performerBatchOwners.AsSpan(0, createCount),
                    _performerBatchScopeIds.AsSpan(0, createCount),
                    _performerBatchStableIds.AsSpan(0, createCount),
                    _performerBatchOwnerTransforms.AsSpan(0, createCount),
                    _performerBatchOwnerCulls.AsSpan(0, createCount),
                    definition,
                    _performerBatchCreated.AsSpan(0, createCount),
                    _stableIds.Allocate);

                for (int i = 0; i < createCount; i++)
                {
                    MarkHierarchyForBootstrapIfNeeded(_performerBatchCreated[i]);
                }
            }
        }

        private int ResolveTemplateKeyId(string templateId, Entity owner)
        {
            if (!string.IsNullOrWhiteSpace(templateId))
            {
                int templateKeyId = _templateKeys.GetId(templateId);
                if (templateKeyId > 0)
                {
                    return templateKeyId;
                }
            }

            return World.Has<EntityTemplateKeyCm>(owner)
                ? World.Get<EntityTemplateKeyCm>(owner).TemplateKeyId
                : 0;
        }

        private bool PassesBootstrapCondition(CompiledPerformerBootstrapRegistry.BootstrapCreateRule rule, Entity owner)
        {
            return rule.InlineCondition switch
            {
                InlineConditionKind.None => true,
                InlineConditionKind.SourceHasVisualTransform => World.Has<VisualTransform>(owner),
                InlineConditionKind.SourceHasAttributes => World.Has<AttributeBuffer>(owner),
                _ => false,
            };
        }

        private int ResolveOrRegisterTemplateKeyId(string templateId)
        {
            int templateKeyId = _templateKeys.GetId(templateId);
            return templateKeyId > 0 ? templateKeyId : _templateKeys.Register(templateId);
        }

        private bool HasDirectEntitySpawnBootstrap(int templateKeyId)
        {
            if (_performerBootstrap == null)
            {
                return false;
            }

            return templateKeyId > 0 &&
                   _performerBootstrap.TryGetEntitySpawnCreates(templateKeyId, out CompiledPerformerBootstrapRegistry.BootstrapCreateRule[] rules) &&
                   rules.Length > 0;
        }

        private bool ShouldPublishSpawnedEvent(int templateKeyId, bool hasDirectBootstrap)
        {
            if (_presentationEvents == null)
            {
                return false;
            }

            if (!hasDirectBootstrap || _performerBootstrap == null)
            {
                return true;
            }

            return _performerBootstrap.HasNonBootstrapEntitySpawnRules(templateKeyId);
        }

        private void MarkHierarchyForBootstrap(Entity root)
        {
            if (!World.IsAlive(root) || !World.Has<PerformerState>(root))
            {
                return;
            }

            MarkPerformer(root);
            ref PerformerChildren children = ref World.Get<PerformerChildren>(root);
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                if (World.IsAlive(child))
                {
                    MarkHierarchyForBootstrap(child);
                }
            }
        }

        private void MarkPerformer(Entity performer)
        {
            if (World.Has<PerformerBootstrapPending>(performer))
            {
                return;
            }

            World.Add(performer, new PerformerBootstrapPending());
        }

        private void MarkHierarchyForBootstrapIfNeeded(Entity root)
        {
            if (!World.IsAlive(root) || !World.Has<PerformerState>(root))
            {
                return;
            }

            ref readonly PerformerState state = ref World.Get<PerformerState>(root);
            if (_performerDefinitions != null &&
                _performerDefinitions.TryGet(state.DefId, out PerformerDefinition definition) &&
                definition.RequiresBootstrapProcessing)
            {
                MarkPerformer(root);
            }

            ref PerformerChildren children = ref World.Get<PerformerChildren>(root);
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                if (World.IsAlive(child))
                {
                    MarkHierarchyForBootstrapIfNeeded(child);
                }
            }
        }

        private void MarkOwnerBootstrapHandled(Entity owner)
        {
            if (World.Has<PerformerRootBootstrapHandled>(owner))
            {
                return;
            }

            World.Add(owner, new PerformerRootBootstrapHandled());
        }
    }
}

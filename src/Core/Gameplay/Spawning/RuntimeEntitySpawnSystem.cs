using System;
using System.Collections.Generic;
using System.Diagnostics;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Map;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Spatial;
using CoreComponentRegistry = Ludots.Core.Config.ComponentRegistry;

namespace Ludots.Core.Gameplay.Spawning
{
    public sealed class RuntimeEntitySpawnSystem : BaseSystem<World, float>
    {
        private const int BatchEntityScratchCapacity = 32768;
        private readonly RuntimeEntitySpawnQueue _requests;
        private readonly RuntimeEntitySpawnReceiptQueue? _receipts;
        private readonly EffectRequestQueue _effectRequests;
        private readonly DataRegistry<EntityTemplate> _templateRegistry;
        private readonly EntityTemplateKeyRegistry _templateKeys;
        private readonly Dictionary<string, EntityTemplate> _cachedTemplates = new(StringComparer.Ordinal);
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
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private readonly ComponentAuthoringContext _authoringContext;
        private readonly OwnershipResolver? _ownership;
        private readonly PlayerEntityLookup? _playerLookup;
        private readonly TeamEntityLookup? _teamLookup;
        private readonly RelationshipRuntime? _relationships;
        private readonly int _memberOfTypeId;

        public RuntimeEntitySpawnSystem(
            World world,
            RuntimeEntitySpawnQueue requests,
            DataRegistry<EntityTemplate> templateRegistry,
            EntityTemplateKeyRegistry templateKeys,
            PresentationStableIdAllocator stableIds,
            EffectRequestQueue effectRequests = null,
            RuntimeEntitySpawnReceiptQueue? receipts = null,
            PerformerEntityRuntime? performerRuntime = null,
            PerformerDefinitionRegistry? performerDefinitions = null,
            PresentationEventStream? presentationEvents = null,
            ISpatialPartitionWorld? spatialPartition = null,
            WorldSizeSpec worldSizeSpec = default,
            PresentationTimingDiagnostics? timingDiagnostics = null,
            ComponentAuthoringContext? authoringContext = null,
            OwnershipResolver? ownership = null,
            PlayerEntityLookup? playerLookup = null,
            TeamEntityLookup? teamLookup = null,
            RelationshipRuntime? relationships = null,
            int memberOfTypeId = -1)
            : base(world)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _receipts = receipts;
            _templateRegistry = templateRegistry ?? throw new ArgumentNullException(nameof(templateRegistry));
            _templateKeys = templateKeys ?? throw new ArgumentNullException(nameof(templateKeys));
            _effectRequests = effectRequests;
            _authoringContext = authoringContext ?? ComponentAuthoringContext.Empty;
            _builder = new EntityBuilder(world, _cachedTemplates, _authoringContext);
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
            _timingDiagnostics = timingDiagnostics;
            _ownership = ownership;
            _playerLookup = playerLookup;
            _teamLookup = teamLookup;
            _relationships = relationships;
            _memberOfTypeId = memberOfTypeId;
        }

        public override void Update(in float dt)
        {
            while (_requests.TryPeek(out var peek))
            {
                if (peek.Kind == RuntimeEntitySpawnKind.Template &&
                    !HasComponentPatches(in peek) &&
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
                            throw new InvalidOperationException(
                                $"Runtime template batch spawn failed after template '{peek.TemplateId}' was classified as batch-compatible. " +
                                "The production path must stay on the validated bulk lane.");
                        }

                        continue;
                    }

                    var singleRequest = _batchRequests[0];
                    var spawnedSingle = SpawnTemplate(singleRequest);
                    PublishSpawnReceipt(in singleRequest, spawnedSingle);
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
                PublishSpawnReceipt(in request, spawned);
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
                new CullState { IsVisible = false, LOD = LODLevel.Low },
                new AttributeBuffer());
            EnsurePresentationStableId(entity);

            string typeName = UnitTypeRegistry.GetName(request.UnitTypeId);
            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new InvalidOperationException($"Runtime unit spawn references unknown UnitTypeId '{request.UnitTypeId}'.");
            }

            World.Add(entity, new Name { Value = "Unit:" + typeName });
            TryApplyFacing(in request, entity);
            TryApplyTeam(in request, entity);
            TryApplyPlayerOwner(in request, entity);
            ApplyComponentPatches(in request, entity);
            TryApplyMapOwnership(in request, entity);
            TryApplyParentLink(in request, entity);
            TryLinkOwnershipEdge(entity);
            TryLinkExplicitRelationships(in request, entity);
            return entity;
        }

        private Entity SpawnTemplate(in RuntimeEntitySpawnRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TemplateId))
            {
                throw new InvalidOperationException("Runtime template spawn requires a non-empty TemplateId.");
            }

            EnsureTemplateLoaded(request.TemplateId);
            var builder = _builder
                .UseTemplate(request.TemplateId)
                .WithEntityContext($"RuntimeEntitySpawn template '{request.TemplateId}'");
            ApplyTemplateComponentPatches(builder, in request);
            var entity = builder.Build();
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
            TryApplyTeam(in request, entity);
            TryApplyPlayerOwner(in request, entity);
            TryApplyMapOwnership(in request, entity);
            TryApplyParentLink(in request, entity);
            TryLinkOwnershipEdge(entity);
            TryLinkExplicitRelationships(in request, entity);
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
                   !HasComponentPatches(in next) &&
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
            long prepareStart = Stopwatch.GetTimestamp();
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
            bool hasTeamWork = false;
            bool hasPlayerOwnerWork = false;
            bool hasParentWork = false;
            bool hasRequestOnSpawnEffect = false;
            bool hasReceiptWork = false;
            for (int i = 0; i < count; i++)
            {
                ref readonly var request = ref _batchRequests[i];
                hasTeamWork |= request.TeamIdOverride > 0 || request.CopySourceTeam != 0;
                hasPlayerOwnerWork |= request.PlayerOwnerIdOverride > 0 || request.CopySourcePlayerOwner != 0;
                hasParentWork |= request.LinkSourceAsParent != 0 || World.IsAlive(request.Parent);
                hasRequestOnSpawnEffect |= request.OnSpawnEffectTemplateId > 0;
                hasReceiptWork |= request.EmitReceipt != 0;
            }

            TemplateBatchSpawnFeatures features =
                TemplateBatchSpawnFeatures.PresentationStableId |
                TemplateBatchSpawnFeatures.PresentationLifecycleState;
            if (allHaveMapEntity)
            {
                features |= TemplateBatchSpawnFeatures.MapEntity;
            }

            if (TemplateBatchOwnerPayloadPreseedPolicy.CanPreseedOwnerPayloadMarker(_performerBootstrap, template, templateKeyId))
            {
                features |= TemplateBatchSpawnFeatures.PresentationOwnerHasPerformerPayload;
            }
            double prepareMs = ElapsedMs(prepareStart);

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

            double postSpawnMs = 0d;
            int onSpawnEffectTemplateId = _templateBatchSpawner.GetOnSpawnEffectTemplateId(templateId, template);
            if (_effectRequests != null && (hasRequestOnSpawnEffect || onSpawnEffectTemplateId > 0))
            {
                _effectRequests.Reserve(_effectRequests.Count + _effectRequests.OverflowCount + created.Length);
            }

            bool requiresPostSpawnLoop =
                hasTeamWork ||
                hasPlayerOwnerWork ||
                hasParentWork ||
                publishSpawnedEvent ||
                hasRequestOnSpawnEffect ||
                hasReceiptWork ||
                onSpawnEffectTemplateId > 0 ||
                !allHaveMapEntity;
            if (requiresPostSpawnLoop)
            {
                long postSpawnStart = Stopwatch.GetTimestamp();
                for (int i = 0; i < created.Length; i++)
                {
                    Entity entity = created[i];
                    ref readonly var request = ref _batchRequests[i];
                    if (hasTeamWork)
                    {
                        TryApplyTeam(in request, entity);
                    }

                    if (hasPlayerOwnerWork)
                    {
                        TryApplyPlayerOwner(in request, entity);
                    }

                    if (!allHaveMapEntity)
                    {
                        TryApplyMapOwnership(in request, entity);
                    }

                    if (hasParentWork)
                    {
                        TryApplyParentLink(in request, entity);
                    }

                    if (publishSpawnedEvent)
                    {
                        PublishSpawnedPresentationEvent(entity);
                    }

                    PublishSpawnReceipt(in request, entity);
                    TryLinkExplicitRelationships(in request, entity);

                    if (hasRequestOnSpawnEffect || onSpawnEffectTemplateId > 0)
                    {
                        PublishOnSpawnEffect(in request, entity, onSpawnEffectTemplateId);
                    }
                }

                postSpawnMs = ElapsedMs(postSpawnStart);
            }

            if (_ownership != null && _playerLookup != null)
            {
                // Template-authored PlayerOwner components bypass the per-request owner work flags,
                // so ownership edges are linked per created entity regardless of the post-spawn loop.
                for (int i = 0; i < created.Length; i++)
                {
                    OwnershipEdgeBuilder.TryLinkSpawnedEntity(World, _ownership, _playerLookup, created[i]);
                }
            }

            double performerBatchMs = 0d;
            double performerCreateMs = 0d;
            double performerBootstrapMarkMs = 0d;
            double performerCreateSetupMs = 0d;
            double performerWorldCreateMs = 0d;
            double performerComponentFillMs = 0d;
            double performerIndexWriteMs = 0d;
            double performerOwnerPayloadMs = 0d;
            double performerPostCreateMs = 0d;
            double performerChildSetupMs = 0d;
            double performerChildWorldCreateMs = 0d;
            double performerChildComponentFillMs = 0d;
            double performerChildIndexWriteMs = 0d;
            double performerChildStableIdMs = 0d;
            int performerCreated = 0;
            if (hasDirectBootstrap)
            {
                long performerBatchStart = Stopwatch.GetTimestamp();
                TryBootstrapPerformerBatch(
                    templateKeyId,
                    created,
                    _performerBatchStableIds.AsSpan(0, created.Length),
                    _performerBatchOwnerTransforms.AsSpan(0, created.Length),
                    _performerBatchOwnerCulls.AsSpan(0, created.Length),
                    out performerCreated,
                    out performerCreateMs,
                    out performerBootstrapMarkMs,
                    out performerCreateSetupMs,
                    out performerWorldCreateMs,
                    out performerComponentFillMs,
                    out performerIndexWriteMs,
                    out performerOwnerPayloadMs,
                    out performerPostCreateMs,
                    out performerChildSetupMs,
                    out performerChildWorldCreateMs,
                    out performerChildComponentFillMs,
                    out performerChildIndexWriteMs,
                    out performerChildStableIdMs);
                performerBatchMs = ElapsedMs(performerBatchStart);
            }

            _timingDiagnostics?.ObserveRuntimeSpawnBatch(
                count,
                performerCreated,
                prepareMs,
                _templateBatchSpawner.LastWorldCreateMs,
                _templateBatchSpawner.LastFillCreatedBatchMs,
                postSpawnMs,
                performerBatchMs,
                performerCreateMs,
                performerBootstrapMarkMs,
                performerCreateSetupMs,
                performerWorldCreateMs,
                performerComponentFillMs,
                performerIndexWriteMs,
                performerOwnerPayloadMs,
                performerPostCreateMs,
                performerChildSetupMs,
                performerChildWorldCreateMs,
                performerChildComponentFillMs,
                performerChildIndexWriteMs,
                performerChildStableIdMs);

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
            TryApplyTeam(in request, entity);
            TryApplyPlayerOwner(in request, entity);
            ApplyComponentPatches(in request, entity);
            TryApplyMapOwnership(in request, entity);
            TryApplyParentLink(in request, entity);
            TryLinkOwnershipEdge(entity);
            TryLinkExplicitRelationships(in request, entity);
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

            if (!string.Equals(template.Id, templateId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Runtime template spawn requires exact template id '{template.Id}', got '{templateId}'.");
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

            var templateKey = new EntityTemplateKeyRef { TemplateKeyId = templateKeyId };
            if (World.Has<EntityTemplateKeyRef>(entity))
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

            if (World.Has<Position2D>(entity))
            {
                World.Set(entity, new Position2D { Value = worldPositionCm });
            }

            if (World.Has<PreviousPosition2D>(entity))
            {
                World.Set(entity, new PreviousPosition2D { Value = worldPositionCm });
            }

            if (!World.Has<VisualTransform>(entity))
            {
                World.Add(entity, VisualTransform.Default);
            }

            if (!World.Has<CullState>(entity))
            {
                World.Add(entity, new CullState { IsVisible = false, LOD = LODLevel.Low });
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

        private void TryApplyTeam(in RuntimeEntitySpawnRequest request, Entity entity)
        {
            Team team;
            if (request.TeamIdOverride > 0)
            {
                team = new Team { Id = request.TeamIdOverride };
            }
            else
            {
                if (request.CopySourceTeam == 0 ||
                    !World.IsAlive(request.Source) ||
                    !World.Has<Team>(request.Source))
                {
                    return;
                }

                team = World.Get<Team>(request.Source);
            }

            if (World.Has<Team>(entity))
            {
                World.Set(entity, team);
            }
            else
            {
                World.Add(entity, team);
            }
        }

        private void TryApplyPlayerOwner(in RuntimeEntitySpawnRequest request, Entity entity)
        {
            PlayerOwner owner;
            if (request.PlayerOwnerIdOverride > 0)
            {
                owner = new PlayerOwner { PlayerId = request.PlayerOwnerIdOverride };
            }
            else
            {
                if (request.CopySourcePlayerOwner == 0 ||
                    !World.IsAlive(request.Source) ||
                    !World.Has<PlayerOwner>(request.Source))
                {
                    return;
                }

                owner = World.Get<PlayerOwner>(request.Source);
            }

            if (World.Has<PlayerOwner>(entity))
            {
                World.Set(entity, owner);
            }
            else
            {
                World.Add(entity, owner);
            }
        }

        /// <summary>RFC-0065 CTRL-2: runtime spawns join the ownership topology exactly like map-load binding.</summary>
        private void TryLinkOwnershipEdge(Entity entity)
        {
            if (_ownership == null || _playerLookup == null)
            {
                return;
            }

            OwnershipEdgeBuilder.TryLinkSpawnedEntity(World, _ownership, _playerLookup, entity);
        }

        private void TryLinkExplicitRelationships(in RuntimeEntitySpawnRequest request, Entity entity)
        {
            if (request.HasOwnershipSource != 0)
            {
                if (_ownership == null || !World.IsAlive(request.OwnershipSource))
                {
                    throw new InvalidOperationException("Runtime spawn explicit OwnershipSource requires a live source and OwnershipResolver.");
                }

                _ownership.EnsureOwnership(request.OwnershipSource, entity);
            }

            if (request.HasMembershipTarget != 0)
            {
                if (_relationships == null || _memberOfTypeId < 0 || !World.IsAlive(request.MembershipTarget))
                {
                    throw new InvalidOperationException("Runtime spawn explicit MembershipTarget requires a live target and member-of relationship runtime.");
                }

                _relationships.EnsureLink(entity, request.MembershipTarget, _memberOfTypeId);
                return;
            }

            if (_relationships != null &&
                _teamLookup != null &&
                _memberOfTypeId >= 0 &&
                World.TryGet(entity, out Team team) &&
                team.Id > 0 &&
                !World.Has<PlayerIdentity>(entity) &&
                !World.Has<TeamIdentity>(entity))
            {
                if (!_teamLookup.TryGet(team.Id, out Entity teamRep) || !World.IsAlive(teamRep))
                {
                    throw new InvalidOperationException(
                        $"Runtime spawned entity {entity.Id} authors Team {team.Id}, but no live team relationship representative exists.");
                }

                _relationships.EnsureLink(entity, teamRep, _memberOfTypeId);
            }
        }

        private void ApplyTemplateComponentPatches(EntityBuilder builder, in RuntimeEntitySpawnRequest request)
        {
            if (!HasComponentPatches(in request))
            {
                return;
            }

            RuntimeEntitySpawnComponentPatch[] patches = request.ComponentPatches;
            for (int i = 0; i < patches.Length; i++)
            {
                RuntimeEntitySpawnComponentPatch patch = patches[i];
                ValidateComponentPatch(in patch, i);
                builder.WithOverride(patch.ComponentName, patch.Data);
            }
        }

        private void ApplyComponentPatches(in RuntimeEntitySpawnRequest request, Entity entity)
        {
            if (!HasComponentPatches(in request))
            {
                return;
            }

            RuntimeEntitySpawnComponentPatch[] patches = request.ComponentPatches;
            for (int i = 0; i < patches.Length; i++)
            {
                RuntimeEntitySpawnComponentPatch patch = patches[i];
                ValidateComponentPatch(in patch, i);
                if (!CoreComponentRegistry.TryGetComponentType(patch.ComponentName, out var componentType))
                {
                    throw new InvalidOperationException($"Runtime entity spawn component patch '{patch.ComponentName}' is not registered.");
                }

                if (World.Has(entity, componentType))
                {
                    throw new InvalidOperationException(
                        $"Runtime entity spawn component patch '{patch.ComponentName}' cannot overwrite an existing component outside the template override path.");
                }

                CoreComponentRegistry.Apply(entity, patch.ComponentName, patch.Data);
            }
        }

        private static bool HasComponentPatches(in RuntimeEntitySpawnRequest request)
        {
            return request.ComponentPatches is { Length: > 0 };
        }

        private static void ValidateComponentPatch(in RuntimeEntitySpawnComponentPatch patch, int index)
        {
            if (string.IsNullOrWhiteSpace(patch.ComponentName))
            {
                throw new InvalidOperationException($"Runtime entity spawn component patch at index {index} requires a non-empty component name.");
            }

            if (patch.Data == null)
            {
                throw new InvalidOperationException($"Runtime entity spawn component patch '{patch.ComponentName}' requires non-null data.");
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

        private void PublishSpawnReceipt(in RuntimeEntitySpawnRequest request, Entity spawned)
        {
            if (request.EmitReceipt == 0)
            {
                return;
            }

            if (_receipts == null)
            {
                throw new InvalidOperationException("RuntimeEntitySpawnRequest requested a receipt but RuntimeEntitySpawnReceiptQueue is not registered.");
            }

            if (!_receipts.TryEnqueue(new RuntimeEntitySpawnReceipt
                {
                    ReceiptChannelId = request.ReceiptChannelId,
                    ReceiptId = request.ReceiptId,
                    Kind = request.Kind,
                    Entity = spawned,
                    TemplateId = request.TemplateId,
                    MapId = request.MapId,
                }))
            {
                throw new InvalidOperationException("RuntimeEntitySpawnReceiptQueue capacity exceeded.");
            }
        }

        private void PublishSpawnedPresentationEvent(Entity entity)
        {
            if (_presentationEvents == null ||
                !World.IsAlive(entity) ||
                !World.Has<PresentationStableId>(entity))
            {
                return;
            }

            int templateKeyId = World.Has<EntityTemplateKeyRef>(entity)
                ? World.Get<EntityTemplateKeyRef>(entity).TemplateKeyId
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
            TryBootstrapPerformerBatch(
                templateKeyId,
                owners,
                stableIds,
                ownerTransforms,
                ownerCulls,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _);
        }

        private void TryBootstrapPerformerBatch(
            int templateKeyId,
            ReadOnlySpan<Entity> owners,
            ReadOnlySpan<int> stableIds,
            ReadOnlySpan<VisualTransform> ownerTransforms,
            ReadOnlySpan<CullState> ownerCulls,
            out int totalCreated,
            out double performerCreateMs,
            out double bootstrapMarkMs,
            out double performerCreateSetupMs,
            out double performerWorldCreateMs,
            out double performerComponentFillMs,
            out double performerIndexWriteMs,
            out double performerOwnerPayloadMs,
            out double performerPostCreateMs,
            out double performerChildSetupMs,
            out double performerChildWorldCreateMs,
            out double performerChildComponentFillMs,
            out double performerChildIndexWriteMs,
            out double performerChildStableIdMs)
        {
            totalCreated = 0;
            performerCreateMs = 0d;
            bootstrapMarkMs = 0d;
            performerCreateSetupMs = 0d;
            performerWorldCreateMs = 0d;
            performerComponentFillMs = 0d;
            performerIndexWriteMs = 0d;
            performerOwnerPayloadMs = 0d;
            performerPostCreateMs = 0d;
            performerChildSetupMs = 0d;
            performerChildWorldCreateMs = 0d;
            performerChildComponentFillMs = 0d;
            performerChildIndexWriteMs = 0d;
            performerChildStableIdMs = 0d;
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

                long createStart = Stopwatch.GetTimestamp();
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
                performerCreateMs += ElapsedMs(createStart);
                performerCreateSetupMs += _performerRuntime.LastRootBatchSetupMs;
                performerWorldCreateMs += _performerRuntime.LastRootBatchWorldCreateMs;
                performerComponentFillMs += _performerRuntime.LastRootBatchComponentFillMs;
                performerIndexWriteMs += _performerRuntime.LastRootBatchIndexWriteMs;
                performerOwnerPayloadMs += _performerRuntime.LastRootBatchOwnerPayloadMs;
                performerPostCreateMs += _performerRuntime.LastRootBatchPostCreateMs;
                performerChildSetupMs += _performerRuntime.LastChildBatchSetupMs;
                performerChildWorldCreateMs += _performerRuntime.LastChildBatchWorldCreateMs;
                performerChildComponentFillMs += _performerRuntime.LastChildBatchComponentFillMs;
                performerChildIndexWriteMs += _performerRuntime.LastChildBatchIndexWriteMs;
                performerChildStableIdMs += _performerRuntime.LastChildBatchStableIdMs;

                if (PerformerEntityRuntime.RequiresDeferredBootstrapAfterBatchCreateHierarchy(definition, _performerDefinitions))
                {
                    long markStart = Stopwatch.GetTimestamp();
                    for (int i = 0; i < createCount; i++)
                    {
                        MarkHierarchyForBootstrapAfterBatchCreateIfNeeded(_performerBatchCreated[i]);
                    }

                    bootstrapMarkMs += ElapsedMs(markStart);
                }

                totalCreated += createCount;
            }
        }

        private static double ElapsedMs(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
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

            return World.Has<EntityTemplateKeyRef>(owner)
                ? World.Get<EntityTemplateKeyRef>(owner).TemplateKeyId
                : 0;
        }

        private bool PassesBootstrapCondition(CompiledPerformerBootstrapRegistry.BootstrapCreateRule rule, Entity owner)
        {
            return rule.InlineCondition switch
            {
                InlineConditionKind.None => true,
                InlineConditionKind.SourceHasVisualTransform => World.Has<VisualTransform>(owner),
                InlineConditionKind.SourceHasAttributes => World.Has<AttributeBuffer>(owner),
                _ => throw new InvalidOperationException($"Unsupported performer bootstrap inline condition '{rule.InlineCondition}'."),
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

        private void MarkHierarchyForBootstrapAfterBatchCreateIfNeeded(Entity root)
        {
            if (!World.IsAlive(root) || !World.Has<PerformerState>(root))
            {
                return;
            }

            ref readonly PerformerState state = ref World.Get<PerformerState>(root);
            if (_performerDefinitions != null &&
                _performerDefinitions.TryGet(state.DefId, out PerformerDefinition definition) &&
                PerformerEntityRuntime.RequiresDeferredBootstrapAfterBatchCreate(definition))
            {
                MarkPerformer(root);
            }

            ref PerformerChildren children = ref World.Get<PerformerChildren>(root);
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                if (World.IsAlive(child))
                {
                    MarkHierarchyForBootstrapAfterBatchCreateIfNeeded(child);
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

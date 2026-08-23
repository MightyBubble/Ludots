using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Nodes;
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
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Spatial;
using CoreComponentRegistry = Ludots.Core.Config.ComponentRegistry;
using Ludots.Platform.Abstractions;

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
        private readonly SpawnRelationshipPlan[] _batchRelationshipPlans = new SpawnRelationshipPlan[BatchEntityScratchCapacity];
        private readonly Entity[] _presenterBatchOwners = new Entity[BatchEntityScratchCapacity];
        private readonly int[] _presenterBatchScopeIds = new int[BatchEntityScratchCapacity];
        private readonly int[] _presenterBatchStableIds = new int[BatchEntityScratchCapacity];
        private readonly Entity[] _presenterBatchCreated = new Entity[BatchEntityScratchCapacity];
        private readonly VisualTransform[] _presenterBatchOwnerTransforms = new VisualTransform[BatchEntityScratchCapacity];
        private readonly CullState[] _presenterBatchOwnerCulls = new CullState[BatchEntityScratchCapacity];
        private readonly TemplateEntityBatchSpawner _templateBatchSpawner;
        private readonly PresenterEntityRuntime? _presenterRuntime;
        private readonly PresenterDefinitionRegistry? _presenterDefinitions;
        private readonly CompiledPresenterBootstrapRegistry? _presenterBootstrap;
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
        private readonly EntityTriggerGraphMounts? _entityTriggerGraphMounts;

        private readonly struct SpawnRelationshipPlan
        {
            public SpawnRelationshipPlan(
                Entity ownershipSource,
                Entity membershipTarget,
                Entity implicitMemberOfTarget)
            {
                OwnershipSource = ownershipSource;
                MembershipTarget = membershipTarget;
                ImplicitMemberOfTarget = implicitMemberOfTarget;
            }

            public Entity OwnershipSource { get; }
            public Entity MembershipTarget { get; }
            public Entity ImplicitMemberOfTarget { get; }
            public bool HasOwnershipSource => OwnershipSource != Entity.Null;
            public bool HasMembershipTarget => MembershipTarget != Entity.Null;
            public bool HasImplicitMemberOfTarget => ImplicitMemberOfTarget != Entity.Null;
        }

        public RuntimeEntitySpawnSystem(
            World world,
            RuntimeEntitySpawnQueue requests,
            DataRegistry<EntityTemplate> templateRegistry,
            EntityTemplateKeyRegistry templateKeys,
            PresentationStableIdAllocator stableIds,
            EffectRequestQueue effectRequests = null,
            RuntimeEntitySpawnReceiptQueue? receipts = null,
            PresenterEntityRuntime? presenterRuntime = null,
            PresenterDefinitionRegistry? presenterDefinitions = null,
            PresentationEventStream? presentationEvents = null,
            ISpatialPartitionWorld? spatialPartition = null,
            WorldSizeSpec worldSizeSpec = default,
            PresentationTimingDiagnostics? timingDiagnostics = null,
            ComponentAuthoringContext? authoringContext = null,
            OwnershipResolver? ownership = null,
            PlayerEntityLookup? playerLookup = null,
            TeamEntityLookup? teamLookup = null,
            RelationshipRuntime? relationships = null,
            int memberOfTypeId = -1,
            EntityTriggerGraphMounts? entityTriggerGraphMounts = null)
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
            _presenterRuntime = presenterRuntime;
            _presenterDefinitions = presenterDefinitions;
            _presenterBootstrap = presenterDefinitions?.BootstrapRegistry;
            _presentationEvents = presentationEvents;
            _timingDiagnostics = timingDiagnostics;
            _ownership = ownership;
            _playerLookup = playerLookup;
            _teamLookup = teamLookup;
            _relationships = relationships;
            _memberOfTypeId = memberOfTypeId;
            _entityTriggerGraphMounts = entityTriggerGraphMounts;
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
                    if (!TryCopyTemplateBatch(peek.TemplateId, out int batchCount))
                    {
                        break;
                    }

                    if (batchCount > 1)
                    {
                        PreflightTemplateBatchBeforeDrain(peek.TemplateId, template, batchCount);
                        if (!TryDrainCopiedTemplateBatch(peek.TemplateId, batchCount))
                        {
                            break;
                        }

                        if (!TrySpawnTemplateBatch(peek.TemplateId, template, batchCount))
                        {
                            throw new InvalidOperationException(
                                $"Runtime template batch spawn failed after template '{peek.TemplateId}' was classified as batch-compatible. " +
                                "The production path must stay on the validated bulk lane.");
                        }

                        continue;
                    }

                    SpawnRelationshipPlan singleRelationshipPlan = PreflightSingleSpawnBeforeDrain(in peek);
                    if (!TryDrainCopiedTemplateBatch(peek.TemplateId, batchCount))
                    {
                        break;
                    }

                    var singleRequest = _batchRequests[0];
                    var spawnedSingle = SpawnTemplate(singleRequest, in singleRelationshipPlan);
                    PublishSpawnReceipt(in singleRequest, spawnedSingle);
                    PublishOnSpawnEffect(in singleRequest, spawnedSingle);
                    MountTemplateTriggerGraphs(spawnedSingle, peek.TemplateId, template);
                    continue;
                }

                SpawnRelationshipPlan relationshipPlan = PreflightSingleSpawnBeforeDrain(in peek);
                if (!_requests.TryDequeue(out var request))
                {
                    break;
                }

                var spawned = request.Kind switch
                {
                    RuntimeEntitySpawnKind.UnitType => SpawnUnitType(request, in relationshipPlan),
                    RuntimeEntitySpawnKind.Template => SpawnTemplate(request, in relationshipPlan),
                    RuntimeEntitySpawnKind.Assembly => SpawnAssembly(request, in relationshipPlan),
                    _ => throw new InvalidOperationException($"Unsupported runtime spawn kind '{request.Kind}'."),
                };

                PublishOnSpawnEffect(in request, spawned);
                PublishSpawnReceipt(in request, spawned);
                if (request.Kind == RuntimeEntitySpawnKind.Template &&
                    TryGetTemplate(request.TemplateId, out EntityTemplate spawnedTemplate))
                {
                    MountTemplateTriggerGraphs(spawned, request.TemplateId, spawnedTemplate);
                }
            }
        }

        private void MountTemplateTriggerGraphs(Entity spawned, string templateId, EntityTemplate template)
        {
            if (_entityTriggerGraphMounts == null || template.TriggerGraphs is not { Count: > 0 })
            {
                return;
            }

            _entityTriggerGraphMounts.MountRuntimeSpawned(spawned, templateId, template.TriggerGraphs);
        }

        private Entity SpawnUnitType(in RuntimeEntitySpawnRequest request, in SpawnRelationshipPlan relationshipPlan)
        {
            if (request.UnitTypeId <= 0)
            {
                throw new InvalidOperationException("Runtime unit spawn requires a positive UnitTypeId.");
            }

            string typeName = UnitTypeRegistry.GetName(request.UnitTypeId);
            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new InvalidOperationException($"Runtime unit spawn references unknown UnitTypeId '{request.UnitTypeId}'.");
            }

            var entity = World.Create(
                new WorldPositionCm { Value = request.WorldPositionCm },
                new PreviousWorldPositionCm { Value = request.WorldPositionCm },
                VisualTransform.Default,
                new CullState { IsVisible = false, LOD = LODLevel.Low },
                new AttributeBuffer(),
                new DirtyFlags());
            EnsurePresentationStableId(entity);

            World.Add(entity, new Name { Value = "Unit:" + typeName });
            TryApplyFacing(in request, entity);
            TryApplyTeam(in request, entity);
            TryApplyPlayerOwner(in request, entity);
            ApplyComponentPatches(in request, entity);
            EnsureRuntimeState(entity, in request);
            TryApplyMapOwnership(in request, entity);
            TryApplyParentLink(in request, entity);
            TryLinkOwnershipEdge(entity);
            ApplyRelationshipPlan(in relationshipPlan, entity);
            return entity;
        }

        private Entity SpawnTemplate(in RuntimeEntitySpawnRequest request, in SpawnRelationshipPlan relationshipPlan)
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
            ApplyRelationshipPlan(in relationshipPlan, entity);
            TryBootstrapPresenter(entity, request.TemplateId);
            return entity;
        }

        private bool TryCopyTemplateBatch(string templateId, out int count)
        {
            count = 0;
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return false;
            }

            while (count < _batchRequests.Length &&
                   _requests.TryPeekAt(count, out var next) &&
                   IsTemplateBatchMember(templateId, in next))
            {
                _batchRequests[count] = next;
                count++;
            }

            return count > 0;
        }

        private bool TryDrainCopiedTemplateBatch(string templateId, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!_requests.TryDequeue(out RuntimeEntitySpawnRequest drained))
                {
                    return false;
                }

                if (!IsTemplateBatchMember(templateId, in drained))
                {
                    throw new InvalidOperationException(
                        $"Runtime template batch queue changed during preflight: template='{templateId}', row={i}.");
                }

                _batchRequests[i] = drained;
            }

            return true;
        }

        private static bool IsTemplateBatchMember(string templateId, in RuntimeEntitySpawnRequest request)
        {
            return request.Kind == RuntimeEntitySpawnKind.Template &&
                   !HasComponentPatches(in request) &&
                   !string.IsNullOrWhiteSpace(request.TemplateId) &&
                   string.Equals(request.TemplateId, templateId, StringComparison.Ordinal);
        }

        private void PreflightTemplateBatchBeforeDrain(string templateId, EntityTemplate template, int count)
        {
            int templateKeyId = ResolveOrRegisterTemplateKeyId(templateId);
            bool hasDirectBootstrap = HasDirectEntitySpawnBootstrap(templateKeyId);
            bool publishSpawnedEvent = ShouldPublishSpawnedEvent(templateKeyId, hasDirectBootstrap);
            bool hasRequestOnSpawnEffect = false;
            bool templateAuthorsTeam = _templateBatchSpawner.TryGetAuthoredTeam(templateId, template, out Team templateTeam);
            bool templateAuthorsRelationshipDomainIdentity = TemplateAuthorsRelationshipDomainIdentity(template);

            for (int i = 0; i < count; i++)
            {
                ref readonly var request = ref _batchRequests[i];
                hasRequestOnSpawnEffect |= request.OnSpawnEffectTemplateId > 0;
            }

            PreflightTemplateBatchRelationships(
                templateId,
                templateAuthorsTeam,
                in templateTeam,
                templateAuthorsRelationshipDomainIdentity,
                count);
            PreflightTemplateBatchSuccessSignals(count, publishSpawnedEvent);

            int onSpawnEffectTemplateId = _templateBatchSpawner.GetOnSpawnEffectTemplateId(templateId, template);
            if (_effectRequests != null && (hasRequestOnSpawnEffect || onSpawnEffectTemplateId > 0))
            {
                _effectRequests.RequireAvailable(count, "RuntimeEntitySpawnSystem.TemplateBatchOnSpawn");
            }
            else if (_effectRequests == null && (hasRequestOnSpawnEffect || onSpawnEffectTemplateId > 0))
            {
                throw new InvalidOperationException(
                    $"Runtime template batch '{templateId}' requires EffectRequestQueue for on-spawn effects.");
            }
        }

        private SpawnRelationshipPlan PreflightSingleSpawnBeforeDrain(in RuntimeEntitySpawnRequest request)
        {
            SpawnRelationshipPlan relationshipPlan = request.Kind switch
            {
                RuntimeEntitySpawnKind.UnitType => PreflightSingleUnitTypeBeforeDrain(in request),
                RuntimeEntitySpawnKind.Template => PreflightSingleTemplateBeforeDrain(in request),
                RuntimeEntitySpawnKind.Assembly => PreflightSingleAssemblyBeforeDrain(in request),
                _ => throw new InvalidOperationException($"Unsupported runtime spawn kind '{request.Kind}'."),
            };

            PreflightSingleSpawnSuccessSignals(in request);
            return relationshipPlan;
        }

        private SpawnRelationshipPlan PreflightSingleUnitTypeBeforeDrain(in RuntimeEntitySpawnRequest request)
        {
            if (request.UnitTypeId <= 0)
            {
                throw new InvalidOperationException("Runtime unit spawn requires a positive UnitTypeId.");
            }

            string typeName = UnitTypeRegistry.GetName(request.UnitTypeId);
            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new InvalidOperationException($"Runtime unit spawn references unknown UnitTypeId '{request.UnitTypeId}'.");
            }

            return PreflightSingleSpawnRelationships(
                "Runtime unit spawn",
                in request,
                templateAuthorsTeam: false,
                templateTeam: default,
                authorsRelationshipDomainIdentity: RequestPatchesAuthorRelationshipDomainIdentity(in request));
        }

        private SpawnRelationshipPlan PreflightSingleTemplateBeforeDrain(in RuntimeEntitySpawnRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TemplateId))
            {
                throw new InvalidOperationException("Runtime template spawn requires a non-empty TemplateId.");
            }

            EnsureTemplateLoaded(request.TemplateId);
            EntityTemplate template = _cachedTemplates[request.TemplateId];
            bool templateAuthorsTeam = TryGetTemplateAuthoredTeam(request.TemplateId, template, out Team templateTeam);
            return PreflightSingleSpawnRelationships(
                $"Runtime template '{request.TemplateId}'",
                in request,
                templateAuthorsTeam,
                in templateTeam,
                TemplateOrRequestPatchesAuthorRelationshipDomainIdentity(template, in request));
        }

        private SpawnRelationshipPlan PreflightSingleAssemblyBeforeDrain(in RuntimeEntitySpawnRequest request)
        {
            return PreflightSingleSpawnRelationships(
                "Runtime assembly spawn",
                in request,
                templateAuthorsTeam: false,
                templateTeam: default,
                authorsRelationshipDomainIdentity: RequestPatchesAuthorRelationshipDomainIdentity(in request));
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
            bool hasExplicitRelationshipWork = false;
            bool hasOwnershipEdgeWork = _ownership != null && _playerLookup != null;
            bool templateAuthorsTeam = _templateBatchSpawner.TryGetAuthoredTeam(templateId, template, out Team templateTeam);
            for (int i = 0; i < count; i++)
            {
                ref readonly var request = ref _batchRequests[i];
                hasTeamWork |= request.TeamIdOverride > 0 || request.CopySourceTeam != 0;
                hasPlayerOwnerWork |= request.PlayerOwnerIdOverride > 0 || request.CopySourcePlayerOwner != 0;
                hasParentWork |= request.LinkSourceAsParent != 0 || World.IsAlive(request.Parent);
                hasRequestOnSpawnEffect |= request.OnSpawnEffectTemplateId > 0;
                hasReceiptWork |= request.EmitReceipt != 0;
                hasExplicitRelationshipWork |= request.HasOwnershipSource != 0 || request.HasMembershipTarget != 0;
            }

            int onSpawnEffectTemplateId = _templateBatchSpawner.GetOnSpawnEffectTemplateId(templateId, template);
            TemplateBatchSpawnFeatures features =
                TemplateBatchSpawnFeatures.PresentationStableId |
                TemplateBatchSpawnFeatures.PresentationLifecycleState;
            if (allHaveMapEntity)
            {
                features |= TemplateBatchSpawnFeatures.MapEntity;
            }

            if (TemplateBatchOwnerPayloadPreseedPolicy.CanPreseedOwnerPayloadMarker(_presenterBootstrap, template, templateKeyId))
            {
                features |= TemplateBatchSpawnFeatures.PresentationOwnerHasPresenterPayload;
            }
            double prepareMs = ElapsedMs(prepareStart);

            if (!_templateBatchSpawner.TryCreateBatch(
                templateId,
                template,
                _templateBatchRequests.AsSpan(0, count),
                features,
                out ReadOnlySpan<Entity> created,
                _presenterBatchStableIds.AsSpan(0, count),
                _presenterBatchOwnerTransforms.AsSpan(0, count),
                _presenterBatchOwnerCulls.AsSpan(0, count)))
            {
                return false;
            }

            double postSpawnMs = 0d;
            if (_effectRequests != null && (hasRequestOnSpawnEffect || onSpawnEffectTemplateId > 0))
            {
                _effectRequests.RequireAvailable(created.Length, "RuntimeEntitySpawnSystem.TemplateBatchOnSpawnPostCreate");
            }

            bool requiresPostSpawnLoop =
                hasTeamWork ||
                templateAuthorsTeam ||
                hasPlayerOwnerWork ||
                hasParentWork ||
                hasExplicitRelationshipWork ||
                hasOwnershipEdgeWork ||
                publishSpawnedEvent ||
                hasRequestOnSpawnEffect ||
                hasReceiptWork ||
                onSpawnEffectTemplateId > 0 ||
                template.TriggerGraphs is { Count: > 0 } ||
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

                    TryLinkOwnershipEdge(entity);
                    ApplyRelationshipPlan(in _batchRelationshipPlans[i], entity);

                    if (publishSpawnedEvent)
                    {
                        PublishSpawnedPresentationEvent(entity);
                    }

                    PublishSpawnReceipt(in request, entity);

                    if (hasRequestOnSpawnEffect || onSpawnEffectTemplateId > 0)
                    {
                        PublishOnSpawnEffect(in request, entity, onSpawnEffectTemplateId);
                    }

                    MountTemplateTriggerGraphs(entity, templateId, template);
                }

                postSpawnMs = ElapsedMs(postSpawnStart);
            }

            double presenterBatchMs = 0d;
            double presenterCreateMs = 0d;
            double presenterBootstrapMarkMs = 0d;
            double presenterCreateSetupMs = 0d;
            double presenterWorldCreateMs = 0d;
            double presenterComponentFillMs = 0d;
            double presenterIndexWriteMs = 0d;
            double presenterOwnerPayloadMs = 0d;
            double presenterPostCreateMs = 0d;
            double presenterChildSetupMs = 0d;
            double presenterChildWorldCreateMs = 0d;
            double presenterChildComponentFillMs = 0d;
            double presenterChildIndexWriteMs = 0d;
            double presenterChildStableIdMs = 0d;
            int presenterCreated = 0;
            if (hasDirectBootstrap)
            {
                long presenterBatchStart = Stopwatch.GetTimestamp();
                TryBootstrapPresenterBatch(
                    templateKeyId,
                    created,
                    _presenterBatchStableIds.AsSpan(0, created.Length),
                    _presenterBatchOwnerTransforms.AsSpan(0, created.Length),
                    _presenterBatchOwnerCulls.AsSpan(0, created.Length),
                    out presenterCreated,
                    out presenterCreateMs,
                    out presenterBootstrapMarkMs,
                    out presenterCreateSetupMs,
                    out presenterWorldCreateMs,
                    out presenterComponentFillMs,
                    out presenterIndexWriteMs,
                    out presenterOwnerPayloadMs,
                    out presenterPostCreateMs,
                    out presenterChildSetupMs,
                    out presenterChildWorldCreateMs,
                    out presenterChildComponentFillMs,
                    out presenterChildIndexWriteMs,
                    out presenterChildStableIdMs);
                presenterBatchMs = ElapsedMs(presenterBatchStart);
            }

            _timingDiagnostics?.ObserveRuntimeSpawnBatch(
                count,
                presenterCreated,
                prepareMs,
                _templateBatchSpawner.LastWorldCreateMs,
                _templateBatchSpawner.LastFillCreatedBatchMs,
                postSpawnMs,
                presenterBatchMs,
                presenterCreateMs,
                presenterBootstrapMarkMs,
                presenterCreateSetupMs,
                presenterWorldCreateMs,
                presenterComponentFillMs,
                presenterIndexWriteMs,
                presenterOwnerPayloadMs,
                presenterPostCreateMs,
                presenterChildSetupMs,
                presenterChildWorldCreateMs,
                presenterChildComponentFillMs,
                presenterChildIndexWriteMs,
                presenterChildStableIdMs);

            return true;
        }

        private Entity SpawnAssembly(in RuntimeEntitySpawnRequest request, in SpawnRelationshipPlan relationshipPlan)
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
            EnsureRuntimeState(entity, in request);
            TryApplyMapOwnership(in request, entity);
            TryApplyParentLink(in request, entity);
            TryLinkOwnershipEdge(entity);
            ApplyRelationshipPlan(in relationshipPlan, entity);
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

        private void ApplyRelationshipPlan(in SpawnRelationshipPlan plan, Entity entity)
        {
            if (plan.HasOwnershipSource)
            {
                _ownership!.EnsureOwnership(plan.OwnershipSource, entity);
            }

            if (plan.HasMembershipTarget)
            {
                _relationships!.EnsureLink(entity, plan.MembershipTarget, _memberOfTypeId);
                return;
            }

            if (plan.HasImplicitMemberOfTarget)
            {
                _relationships!.EnsureLink(entity, plan.ImplicitMemberOfTarget, _memberOfTypeId);
            }
        }

        private void PreflightTemplateBatchRelationships(
            string templateId,
            bool templateAuthorsTeam,
            in Team templateTeam,
            bool authorsRelationshipDomainIdentity,
            int count)
        {
            for (int i = 0; i < count; i++)
            {
                ref readonly RuntimeEntitySpawnRequest request = ref _batchRequests[i];
                _batchRelationshipPlans[i] = PreflightSpawnRelationships(
                    $"Runtime template batch '{templateId}'",
                    in request,
                    templateAuthorsTeam,
                    in templateTeam,
                    authorsRelationshipDomainIdentity);
            }
        }

        private void PreflightExplicitRelationship(in RuntimeEntitySpawnRequest request)
        {
            if (request.HasOwnershipSource != 0 &&
                (_ownership == null || !IsAliveInCurrentWorld(request.OwnershipSource)))
            {
                throw new InvalidOperationException("Runtime spawn explicit OwnershipSource requires a live source and OwnershipResolver.");
            }

            if (request.HasMembershipTarget != 0 &&
                (!HasRegisteredMemberOfRelationshipType() || !IsAliveInCurrentWorld(request.MembershipTarget)))
            {
                throw new InvalidOperationException(
                    "Runtime spawn explicit MembershipTarget requires a live target, a registered MemberOf relationship type, and a MemberOf relationship runtime.");
            }
        }

        private SpawnRelationshipPlan PreflightSingleSpawnRelationships(
            string context,
            in RuntimeEntitySpawnRequest request,
            bool templateAuthorsTeam,
            in Team templateTeam,
            bool authorsRelationshipDomainIdentity)
        {
            return PreflightSpawnRelationships(
                context,
                in request,
                templateAuthorsTeam,
                in templateTeam,
                authorsRelationshipDomainIdentity);
        }

        private SpawnRelationshipPlan PreflightSpawnRelationships(
            string context,
            in RuntimeEntitySpawnRequest request,
            bool templateAuthorsTeam,
            in Team templateTeam,
            bool authorsRelationshipDomainIdentity)
        {
            PreflightExplicitRelationship(in request);
            int teamId = ResolveTemplateFinalTeamId(
                context,
                in request,
                templateAuthorsTeam ? templateTeam.Id : 0);
            Entity ownershipSource = request.HasOwnershipSource != 0
                ? request.OwnershipSource
                : Entity.Null;
            if (request.HasMembershipTarget != 0)
            {
                if (teamId > 0)
                {
                    PreflightMembershipTargetMatchesTeam(context, in request, teamId);
                }

                return new SpawnRelationshipPlan(ownershipSource, request.MembershipTarget, Entity.Null);
            }

            if (teamId <= 0)
            {
                return new SpawnRelationshipPlan(ownershipSource, Entity.Null, Entity.Null);
            }

            if (authorsRelationshipDomainIdentity)
            {
                return new SpawnRelationshipPlan(ownershipSource, Entity.Null, Entity.Null);
            }

            Entity implicitMemberOfTarget = PreflightImplicitMemberOfTarget(context, teamId);
            return new SpawnRelationshipPlan(ownershipSource, Entity.Null, implicitMemberOfTarget);
        }

        private Entity PreflightImplicitMemberOfTarget(string context, int teamId)
        {
            if (_relationships == null || _teamLookup == null || !HasRegisteredMemberOfRelationshipType())
            {
                throw new InvalidOperationException(
                    $"{context} authors Team {teamId}, but implicit MemberOf linking requires RelationshipRuntime, TeamEntityLookup, and a registered MemberOf relationship type.");
            }

            if (!_teamLookup.TryGet(teamId, out Entity teamRepresentative) || !IsAliveInCurrentWorld(teamRepresentative))
            {
                throw new InvalidOperationException(
                    $"{context} authors Team {teamId}, but no live team relationship representative exists.");
            }

            return teamRepresentative;
        }

        private bool IsAliveInCurrentWorld(Entity entity)
        {
            return entity != Entity.Null && entity.WorldId == World.Id && World.IsAlive(entity);
        }

        private bool HasRegisteredMemberOfRelationshipType()
        {
            return _relationships != null &&
                _memberOfTypeId >= 0 &&
                _memberOfTypeId < _relationships.TypeRegistry.Count;
        }

        private void PreflightMembershipTargetMatchesTeam(
            string context,
            in RuntimeEntitySpawnRequest request,
            int teamId)
        {
            if (teamId <= 0)
            {
                return;
            }

            if (!World.TryGet(request.MembershipTarget, out TeamIdentity targetTeam))
            {
                throw new InvalidOperationException(
                    $"{context} authors Team {teamId}, but explicit MembershipTarget is not a team representative.");
            }

            if (targetTeam.TeamId != teamId)
            {
                throw new InvalidOperationException(
                    $"{context} authors Team {teamId}, but explicit MembershipTarget team {targetTeam.TeamId} conflicts.");
            }
        }

        private void PreflightTemplateBatchSuccessSignals(int count, bool publishSpawnedEvent)
        {
            int receiptCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (_batchRequests[i].EmitReceipt != 0)
                {
                    receiptCount++;
                }
            }

            if (receiptCount > 0)
            {
                if (_receipts == null)
                {
                    throw new InvalidOperationException("RuntimeEntitySpawnRequest requested a receipt but RuntimeEntitySpawnReceiptQueue is not registered.");
                }

                if (receiptCount > _receipts.FreeCapacity)
                {
                    throw new InvalidOperationException("RuntimeEntitySpawnReceiptQueue capacity exceeded.");
                }
            }

            if (publishSpawnedEvent &&
                _presentationEvents != null &&
                count > _presentationEvents.Capacity - _presentationEvents.Count)
            {
                throw new InvalidOperationException("PresentationEventStream is full while publishing batch EntitySpawned.");
            }
        }

        private void PreflightSingleSpawnSuccessSignals(in RuntimeEntitySpawnRequest request)
        {
            if (request.EmitReceipt != 0)
            {
                if (_receipts == null)
                {
                    throw new InvalidOperationException("RuntimeEntitySpawnRequest requested a receipt but RuntimeEntitySpawnReceiptQueue is not registered.");
                }

                if (_receipts.FreeCapacity < 1)
                {
                    throw new InvalidOperationException("RuntimeEntitySpawnReceiptQueue capacity exceeded.");
                }
            }

            if (!TryResolveOnSpawnEffectTemplateId(in request, cachedTemplateOnSpawnEffectId: 0, out int effectTemplateId, out _))
            {
                return;
            }

            if (_effectRequests == null)
            {
                throw new InvalidOperationException(
                    $"Runtime spawn on-spawn effect requires EffectRequestQueue: kind={request.Kind}, templateId={request.TemplateId}, effectTemplateId={effectTemplateId}.");
            }

            _effectRequests.RequireAvailable(1, "RuntimeEntitySpawnSystem.OnSpawnEffect");
        }

        private int ResolveTemplateFinalTeamId(
            string context,
            in RuntimeEntitySpawnRequest request,
            int templateTeamId)
        {
            int teamId = 0;
            AddTeamSource(context, "template Team", templateTeamId, ref teamId);

            if (request.TeamIdOverride > 0)
            {
                AddTeamSource(context, "TeamIdOverride", request.TeamIdOverride, ref teamId);
            }

            if (request.CopySourceTeam != 0 &&
                World.IsAlive(request.Source) &&
                World.TryGet(request.Source, out Team sourceTeam))
            {
                AddTeamSource(context, "source Team", sourceTeam.Id, ref teamId);
            }

            if (TryGetRequestPatchedTeam(in request, out Team patchedTeam))
            {
                AddTeamSource(context, "component patch Team", patchedTeam.Id, ref teamId);
            }

            return teamId;
        }

        private static void AddTeamSource(string context, string sourceName, int sourceTeamId, ref int teamId)
        {
            if (sourceTeamId <= 0)
            {
                return;
            }

            if (teamId <= 0)
            {
                teamId = sourceTeamId;
                return;
            }

            if (teamId != sourceTeamId)
            {
                throw new InvalidOperationException(
                    $"{context} has conflicting Team sources: resolved Team {teamId} conflicts with {sourceName} {sourceTeamId}.");
            }
        }

        private static bool TemplateOrRequestPatchesAuthorRelationshipDomainIdentity(
            EntityTemplate template,
            in RuntimeEntitySpawnRequest request)
        {
            return TemplateAuthorsRelationshipDomainIdentity(template) ||
                   RequestPatchesAuthorRelationshipDomainIdentity(in request);
        }

        private static bool TemplateAuthorsRelationshipDomainIdentity(EntityTemplate template)
        {
            return template.Components != null &&
                   (template.Components.ContainsKey("PlayerIdentity") ||
                    template.Components.ContainsKey("TeamIdentity"));
        }

        private static bool RequestPatchesAuthorRelationshipDomainIdentity(in RuntimeEntitySpawnRequest request)
        {
            RuntimeEntitySpawnComponentPatch[] patches = request.ComponentPatches;
            if (patches == null || patches.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < patches.Length; i++)
            {
                if (string.Equals(patches[i].ComponentName, "PlayerIdentity", StringComparison.Ordinal) ||
                    string.Equals(patches[i].ComponentName, "TeamIdentity", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetTemplateAuthoredTeam(string templateId, EntityTemplate template, out Team team)
        {
            if (template.Components != null &&
                template.Components.TryGetValue("Team", out JsonNode teamNode))
            {
                team = ParseTeamComponent($"Entity template '{templateId}' Team", teamNode);
                return true;
            }

            team = default;
            return false;
        }

        private static bool TryGetRequestPatchedTeam(in RuntimeEntitySpawnRequest request, out Team team)
        {
            RuntimeEntitySpawnComponentPatch[] patches = request.ComponentPatches;
            if (patches != null)
            {
                for (int i = 0; i < patches.Length; i++)
                {
                    if (string.Equals(patches[i].ComponentName, "Team", StringComparison.Ordinal))
                    {
                        team = ParseTeamComponent("Runtime spawn component patch Team", patches[i].Data);
                        return true;
                    }
                }
            }

            team = default;
            return false;
        }

        private static Team ParseTeamComponent(string context, JsonNode node)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"{context} requires an object payload.");
            }

            if (!obj.TryGetPropertyValue("Id", out JsonNode idNode) ||
                idNode == null ||
                !idNode.AsValue().TryGetValue(out int id))
            {
                throw new InvalidOperationException($"{context}.Id requires an integer value.");
            }

            return new Team { Id = id };
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

        private void EnsureRuntimeState(Entity entity, in RuntimeEntitySpawnRequest request)
        {
            EntityRuntimeStatePlan.EnsureInstalledForAuthoredEntity(
                World,
                entity,
                _authoringContext,
                $"RuntimeEntitySpawn kind '{request.Kind}' entity {entity.Id}");
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
            if (!TryResolveOnSpawnEffectTemplateId(in request, cachedTemplateOnSpawnEffectId, out int effectTemplateId, out bool useSpawnedAsSource))
            {
                return;
            }

            if (_effectRequests == null)
            {
                throw new InvalidOperationException(
                    $"Runtime spawn on-spawn effect requires EffectRequestQueue: kind={request.Kind}, templateId={request.TemplateId}, effectTemplateId={effectTemplateId}.");
            }

            _effectRequests.Publish(new EffectRequest
            {
                RootId = request.RootId,
                Source = useSpawnedAsSource ? spawned : request.Source,
                Target = spawned,
                TargetContext = useSpawnedAsSource ? spawned : request.TargetContext,
                TemplateId = effectTemplateId,
            });
        }

        private bool TryResolveOnSpawnEffectTemplateId(
            in RuntimeEntitySpawnRequest request,
            int cachedTemplateOnSpawnEffectId,
            out int effectTemplateId,
            out bool useSpawnedAsSource)
        {
            effectTemplateId = request.OnSpawnEffectTemplateId > 0
                ? request.OnSpawnEffectTemplateId
                : cachedTemplateOnSpawnEffectId;
            useSpawnedAsSource = false;

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
                return false;
            }

            useSpawnedAsSource = request.OnSpawnEffectTemplateId <= 0;
            return true;
        }

        private void TryBootstrapPresenter(Entity owner, string templateId)
        {
            if (_presenterRuntime == null || _presenterDefinitions == null || _presenterBootstrap == null)
            {
                return;
            }

            int templateKeyId = ResolveTemplateKeyId(templateId, owner);
            if (templateKeyId <= 0 ||
                !_presenterBootstrap.TryGetEntitySpawnCreates(templateKeyId, out CompiledPresenterBootstrapRegistry.BootstrapCreateRule[] rules))
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

                if (!_presenterDefinitions.TryGet(rule.PresenterDefinitionId, out PresenterDefinition definition))
                {
                    throw new InvalidOperationException($"Presenter definition id={rule.PresenterDefinitionId} is not registered.");
                }

                int scopeTag = rule.ResolveScopeTag(stableId);
                if (scopeTag <= 0)
                {
                    continue;
                }

                if (_presenterRuntime.HasActiveScopedInstance(rule.PresenterDefinitionId, owner, scopeTag, PresentationAnchorKind.Entity, default))
                {
                    continue;
                }

                Entity root = _presenterRuntime.CreateHierarchy(
                    _presenterDefinitions,
                    rule.PresenterDefinitionId,
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

        private void TryBootstrapPresenterBatch(
            int templateKeyId,
            ReadOnlySpan<Entity> owners,
            ReadOnlySpan<int> stableIds,
            ReadOnlySpan<VisualTransform> ownerTransforms,
            ReadOnlySpan<CullState> ownerCulls)
        {
            TryBootstrapPresenterBatch(
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

        private void TryBootstrapPresenterBatch(
            int templateKeyId,
            ReadOnlySpan<Entity> owners,
            ReadOnlySpan<int> stableIds,
            ReadOnlySpan<VisualTransform> ownerTransforms,
            ReadOnlySpan<CullState> ownerCulls,
            out int totalCreated,
            out double presenterCreateMs,
            out double bootstrapMarkMs,
            out double presenterCreateSetupMs,
            out double presenterWorldCreateMs,
            out double presenterComponentFillMs,
            out double presenterIndexWriteMs,
            out double presenterOwnerPayloadMs,
            out double presenterPostCreateMs,
            out double presenterChildSetupMs,
            out double presenterChildWorldCreateMs,
            out double presenterChildComponentFillMs,
            out double presenterChildIndexWriteMs,
            out double presenterChildStableIdMs)
        {
            totalCreated = 0;
            presenterCreateMs = 0d;
            bootstrapMarkMs = 0d;
            presenterCreateSetupMs = 0d;
            presenterWorldCreateMs = 0d;
            presenterComponentFillMs = 0d;
            presenterIndexWriteMs = 0d;
            presenterOwnerPayloadMs = 0d;
            presenterPostCreateMs = 0d;
            presenterChildSetupMs = 0d;
            presenterChildWorldCreateMs = 0d;
            presenterChildComponentFillMs = 0d;
            presenterChildIndexWriteMs = 0d;
            presenterChildStableIdMs = 0d;
            if (_presenterRuntime == null || _presenterDefinitions == null || _presenterBootstrap == null || owners.Length == 0)
            {
                return;
            }

            if (owners.Length != stableIds.Length ||
                owners.Length != ownerTransforms.Length ||
                owners.Length != ownerCulls.Length)
            {
                throw new ArgumentException("Presenter bootstrap batch spans must have matching lengths.");
            }

            if (templateKeyId <= 0 ||
                !_presenterBootstrap.TryGetEntitySpawnCreates(templateKeyId, out CompiledPresenterBootstrapRegistry.BootstrapCreateRule[] rules))
            {
                return;
            }

            for (int ri = 0; ri < rules.Length; ri++)
            {
                ref readonly var rule = ref rules[ri];
                if (!_presenterDefinitions.TryGet(rule.PresenterDefinitionId, out PresenterDefinition definition))
                {
                    throw new InvalidOperationException($"Presenter definition id={rule.PresenterDefinitionId} is not registered.");
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

                    _presenterBatchOwners[createCount] = owner;
                    _presenterBatchScopeIds[createCount] = scopeTag;
                    _presenterBatchStableIds[createCount] = _stableIds.Allocate();
                    _presenterBatchOwnerTransforms[createCount] = ownerTransforms[oi];
                    _presenterBatchOwnerCulls[createCount] = ownerCulls[oi];
                    createCount++;
                }

                if (createCount == 0)
                {
                    continue;
                }

                long createStart = Stopwatch.GetTimestamp();
                _presenterRuntime.CreateEntityAnchoredRootBatch(
                    _presenterDefinitions,
                    rule.PresenterDefinitionId,
                    _presenterBatchOwners.AsSpan(0, createCount),
                    _presenterBatchScopeIds.AsSpan(0, createCount),
                    _presenterBatchStableIds.AsSpan(0, createCount),
                    _presenterBatchOwnerTransforms.AsSpan(0, createCount),
                    _presenterBatchOwnerCulls.AsSpan(0, createCount),
                    definition,
                    _presenterBatchCreated.AsSpan(0, createCount),
                    _stableIds.Allocate);
                presenterCreateMs += ElapsedMs(createStart);
                presenterCreateSetupMs += _presenterRuntime.LastRootBatchSetupMs;
                presenterWorldCreateMs += _presenterRuntime.LastRootBatchWorldCreateMs;
                presenterComponentFillMs += _presenterRuntime.LastRootBatchComponentFillMs;
                presenterIndexWriteMs += _presenterRuntime.LastRootBatchIndexWriteMs;
                presenterOwnerPayloadMs += _presenterRuntime.LastRootBatchOwnerPayloadMs;
                presenterPostCreateMs += _presenterRuntime.LastRootBatchPostCreateMs;
                presenterChildSetupMs += _presenterRuntime.LastChildBatchSetupMs;
                presenterChildWorldCreateMs += _presenterRuntime.LastChildBatchWorldCreateMs;
                presenterChildComponentFillMs += _presenterRuntime.LastChildBatchComponentFillMs;
                presenterChildIndexWriteMs += _presenterRuntime.LastChildBatchIndexWriteMs;
                presenterChildStableIdMs += _presenterRuntime.LastChildBatchStableIdMs;

                if (PresenterEntityRuntime.RequiresDeferredBootstrapAfterBatchCreateHierarchy(definition, _presenterDefinitions))
                {
                    long markStart = Stopwatch.GetTimestamp();
                    for (int i = 0; i < createCount; i++)
                    {
                        MarkHierarchyForBootstrapAfterBatchCreateIfNeeded(_presenterBatchCreated[i]);
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

        private bool PassesBootstrapCondition(CompiledPresenterBootstrapRegistry.BootstrapCreateRule rule, Entity owner)
        {
            return rule.InlineCondition switch
            {
                InlineConditionKind.None => true,
                InlineConditionKind.SourceHasVisualTransform => World.Has<VisualTransform>(owner),
                InlineConditionKind.SourceHasAttributes => World.Has<AttributeBuffer>(owner),
                _ => throw new InvalidOperationException($"Unsupported presenter bootstrap inline condition '{rule.InlineCondition}'."),
            };
        }

        private int ResolveOrRegisterTemplateKeyId(string templateId)
        {
            int templateKeyId = _templateKeys.GetId(templateId);
            return templateKeyId > 0 ? templateKeyId : _templateKeys.Register(templateId);
        }

        private bool HasDirectEntitySpawnBootstrap(int templateKeyId)
        {
            if (_presenterBootstrap == null)
            {
                return false;
            }

            return templateKeyId > 0 &&
                   _presenterBootstrap.TryGetEntitySpawnCreates(templateKeyId, out CompiledPresenterBootstrapRegistry.BootstrapCreateRule[] rules) &&
                   rules.Length > 0;
        }

        private bool ShouldPublishSpawnedEvent(int templateKeyId, bool hasDirectBootstrap)
        {
            if (_presentationEvents == null)
            {
                return false;
            }

            if (!hasDirectBootstrap || _presenterBootstrap == null)
            {
                return true;
            }

            return _presenterBootstrap.HasNonBootstrapEntitySpawnRules(templateKeyId);
        }

        private void MarkHierarchyForBootstrap(Entity root)
        {
            if (!World.IsAlive(root) || !World.Has<PresenterState>(root))
            {
                return;
            }

            MarkPresenter(root);
            ref PresenterChildren children = ref World.Get<PresenterChildren>(root);
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                if (World.IsAlive(child))
                {
                    MarkHierarchyForBootstrap(child);
                }
            }
        }

        private void MarkPresenter(Entity presenter)
        {
            if (World.Has<PresenterBootstrapPending>(presenter))
            {
                return;
            }

            World.Add(presenter, new PresenterBootstrapPending());
        }

        private void MarkHierarchyForBootstrapIfNeeded(Entity root)
        {
            if (!World.IsAlive(root) || !World.Has<PresenterState>(root))
            {
                return;
            }

            ref readonly PresenterState state = ref World.Get<PresenterState>(root);
            if (_presenterDefinitions != null &&
                _presenterDefinitions.TryGet(state.DefId, out PresenterDefinition definition) &&
                definition.RequiresBootstrapProcessing)
            {
                MarkPresenter(root);
            }

            ref PresenterChildren children = ref World.Get<PresenterChildren>(root);
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
            if (!World.IsAlive(root) || !World.Has<PresenterState>(root))
            {
                return;
            }

            ref readonly PresenterState state = ref World.Get<PresenterState>(root);
            if (_presenterDefinitions != null &&
                _presenterDefinitions.TryGet(state.DefId, out PresenterDefinition definition) &&
                PresenterEntityRuntime.RequiresDeferredBootstrapAfterBatchCreate(definition))
            {
                MarkPresenter(root);
            }

            ref PresenterChildren children = ref World.Get<PresenterChildren>(root);
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
            if (World.Has<PresenterRootBootstrapHandled>(owner))
            {
                return;
            }

            World.Add(owner, new PresenterRootBootstrapHandled());
        }
    }
}

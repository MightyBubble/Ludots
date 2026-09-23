using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Instancing;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Systems
{
    public class MapLoader
    {
        private const string InitialInteractionContextOverrideKey = "initialInteractionContext";
        private const int TemplateBatchScratchCapacity = 4096;

        private readonly World _world;
        private readonly WorldMap _worldMap;
        private EffectRequestQueue _effectRequests;
        private EntityTriggerGraphMounts? _entityTriggerGraphMounts;
        private Ludots.Core.Input.Interaction.InteractionContextProfileRegistry? _initialInteractionContexts;
        private TemplateEntityBatchSpawner _templateBatchSpawner;
        private PresentationStableIdAllocator _stableIds;
        private PresenterEntityRuntime _presenterRuntime;
        private PresenterDefinitionRegistry _presenterDefinitions;
        private CompiledPresenterBootstrapRegistry _presenterBootstrap;
        private MeshAssetRegistry _meshAssets;
        private InstancedBatchAssetRegistry _instancedBatchAssets;
        private readonly Entity[] _presenterBatchOwners = new Entity[TemplateBatchScratchCapacity];
        private readonly int[] _presenterBatchScopeIds = new int[TemplateBatchScratchCapacity];
        private readonly int[] _presenterBatchStableIds = new int[TemplateBatchScratchCapacity];
        private readonly Entity[] _presenterBatchCreated = new Entity[TemplateBatchScratchCapacity];
        private readonly int[] _ownerBatchStableIds = new int[TemplateBatchScratchCapacity];
        private readonly VisualTransform[] _ownerBatchTransforms = new VisualTransform[TemplateBatchScratchCapacity];
        private readonly CullState[] _ownerBatchCulls = new CullState[TemplateBatchScratchCapacity];
        private readonly ParamDefault[][] _ownerBatchParamOverrides = new ParamDefault[TemplateBatchScratchCapacity][];
        private readonly ParamDefault[][] _presenterBatchParamOverrides = new ParamDefault[TemplateBatchScratchCapacity][];
        private ComponentAuthoringContext _authoringContext = ComponentAuthoringContext.Empty;
        
        // New Registry
        public DataRegistry<EntityTemplate> TemplateRegistry { get; private set; }
        public EntityTemplateKeyRegistry EntityTemplateKeys { get; }
        private readonly Dictionary<string, string> _templateSources = new Dictionary<string, string>(StringComparer.Ordinal);

        public MapLoader(World world, WorldMap worldMap, ConfigPipeline pipeline)
        {
            _world = world;
            _worldMap = worldMap;
            TemplateRegistry = new DataRegistry<EntityTemplate>(pipeline);
            EntityTemplateKeys = new EntityTemplateKeyRegistry();
            _templateBatchSpawner = new TemplateEntityBatchSpawner(world, EntityTemplateKeys, scratchCapacity: TemplateBatchScratchCapacity);
        }

        public void SetEffectRequestQueue(EffectRequestQueue effectRequests)
        {
            _effectRequests = effectRequests;
        }

        public void SetEntityTriggerGraphMounts(EntityTriggerGraphMounts entityTriggerGraphMounts)
        {
            _entityTriggerGraphMounts = entityTriggerGraphMounts ?? throw new ArgumentNullException(nameof(entityTriggerGraphMounts));
        }

        /// <summary>
        /// Binds the installed interaction context profiles so map-load spawns mount their
        /// template's initialInteractionContext; unbound registries fail the
        /// first template declaring one.
        /// </summary>
        public void SetInitialInteractionContexts(Ludots.Core.Input.Interaction.InteractionContextProfileRegistry profiles)
        {
            _initialInteractionContexts = profiles ?? throw new ArgumentNullException(nameof(profiles));
        }

        public void SetComponentAuthoringContext(ComponentAuthoringContext authoringContext)
        {
            _authoringContext = authoringContext ?? ComponentAuthoringContext.Empty;
        }

        public ComponentAuthoringContext RequireComponentAuthoringContext()
        {
            if (ReferenceEquals(_authoringContext, ComponentAuthoringContext.Empty))
            {
                throw new InvalidOperationException(
                    "MapLoader ComponentAuthoringContext has not been configured by the engine.");
            }

            return _authoringContext;
        }

        public void SetPresentationRuntime(
            PresentationStableIdAllocator stableIds,
            PresenterEntityRuntime presenterRuntime,
            PresenterDefinitionRegistry presenterDefinitions,
            ISpatialPartitionWorld spatialPartition,
            WorldSizeSpec worldSizeSpec,
            MeshAssetRegistry meshAssets = null,
            InstancedBatchAssetRegistry instancedBatchAssets = null)
        {
            _stableIds = stableIds;
            _presenterRuntime = presenterRuntime;
            _presenterDefinitions = presenterDefinitions;
            _presenterBootstrap = presenterDefinitions?.BootstrapRegistry;
            _meshAssets = meshAssets;
            _instancedBatchAssets = instancedBatchAssets;
            _templateBatchSpawner = new TemplateEntityBatchSpawner(
                _world,
                EntityTemplateKeys,
                stableIds,
                spatialPartition,
                worldSizeSpec,
                TemplateBatchScratchCapacity);
        }

        /// <summary>
        /// Builds the deterministic set of map-owned presentation assets before entities are
        /// materialized. Only assets reachable from map templates, bootstrap presenter roots,
        /// compiled presenter child plans, and authored asset-swap entries are included;
        /// transient runtime VFX are intentionally outside this contract.
        /// </summary>
        public MapPresentationAssetManifest BuildPresentationAssetManifest(MapConfig mapConfig)
        {
            if (mapConfig == null)
            {
                throw new ArgumentNullException(nameof(mapConfig));
            }

            var manifest = new MapPresentationAssetManifest();
            if (_meshAssets == null || _presenterDefinitions == null || _presenterBootstrap == null || mapConfig.Entities == null)
            {
                manifest.SealManifest();
                return manifest;
            }

            var visitedTemplates = new HashSet<string>(StringComparer.Ordinal);
            var visitedDefinitions = new HashSet<int>();
            var reachableDefinitionIds = new List<int>();
            var instanceAssetIds = new Dictionary<int, HashSet<int>>();

            for (int i = 0; i < mapConfig.Entities.Count; i++)
            {
                EntitySpawnData? entity = mapConfig.Entities[i];
                if (entity?.PresenterParamOverrides == null)
                {
                    continue;
                }

                for (int overrideIndex = 0; overrideIndex < entity.PresenterParamOverrides.Count; overrideIndex++)
                {
                    ParamOverrideData? item = entity.PresenterParamOverrides[overrideIndex];
                    if (item == null || item.Lane != ParamLane.Int || string.IsNullOrWhiteSpace(item.ParamKey))
                    {
                        continue;
                    }

                    AddInstanceAssetOverride(PresenterParamKeyRegistry.Register(item.ParamKey), item.IntValue);
                }
            }

            for (int i = 0; i < mapConfig.Entities.Count; i++)
            {
                EntitySpawnData? entity = mapConfig.Entities[i];
                if (entity == null || string.IsNullOrWhiteSpace(entity.Template))
                {
                    continue;
                }

                AddTemplate(entity.Template);
            }

            for (int i = 0; i < reachableDefinitionIds.Count; i++)
            {
                int definitionId = reachableDefinitionIds[i];
                if (!_presenterDefinitions.TryGet(definitionId, out PresenterDefinition definition))
                {
                    continue;
                }

                AddBehaviors(definition.Behaviors);
                PresenterCreatePlan plan = _presenterDefinitions.GetOrCreateCreatePlan(definitionId);
                PresenterCreatePlanNode[] nodes = plan.Nodes ?? Array.Empty<PresenterCreatePlanNode>();
                for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
                {
                    AddBehaviors(nodes[nodeIndex].InstanceOverride?.InstanceBehaviors);
                }
            }

            manifest.SealManifest();
            return manifest;

            void AddTemplate(string templateId)
            {
                if (!visitedTemplates.Add(templateId))
                {
                    return;
                }

                EntityTemplate? template = TemplateRegistry.Get(templateId);
                if (template == null)
                {
                    return;
                }

                int templateKeyId = EntityTemplateKeys.GetId(templateId);
                if (templateKeyId > 0 &&
                    _presenterBootstrap.TryGetEntitySpawnCreates(
                        templateKeyId,
                        out CompiledPresenterBootstrapRegistry.BootstrapCreateRule[] rules))
                {
                    for (int i = 0; i < rules.Length; i++)
                    {
                        CollectDefinition(rules[i].PresenterDefinitionId);
                    }
                }

                if (template.Children == null)
                {
                    return;
                }

                for (int i = 0; i < template.Children.Count; i++)
                {
                    EntityTemplateChild? child = template.Children[i];
                    if (child != null && !string.IsNullOrWhiteSpace(child.Template))
                    {
                        AddTemplate(child.Template);
                    }
                }
            }

            void CollectDefinition(int definitionId)
            {
                if (definitionId <= 0 || !visitedDefinitions.Add(definitionId) ||
                    !_presenterDefinitions.TryGet(definitionId, out PresenterDefinition definition))
                {
                    return;
                }

                reachableDefinitionIds.Add(definitionId);
                PresenterCreatePlan plan = _presenterDefinitions.GetOrCreateCreatePlan(definitionId);
                PresenterCreatePlanNode[] nodes = plan.Nodes ?? Array.Empty<PresenterCreatePlanNode>();
                for (int i = 0; i < nodes.Length; i++)
                {
                    AddParamOverrides(nodes[i].ParamOverrides);
                }

                AddParamOverrides(definition.ParamDefaults);
                for (int i = 0; i < nodes.Length; i++)
                {
                    CollectDefinition(nodes[i].DefinitionId);
                }
            }

            void AddBehaviors(BehaviorSlot[]? behaviors)
            {
                if (behaviors == null)
                {
                    return;
                }

                for (int i = 0; i < behaviors.Length; i++)
                {
                    ref readonly BehaviorSlot behavior = ref behaviors[i];
                    if (behavior.Kind == BehaviorKind.AssetBinding)
                    {
                        AddAsset(in behavior.AssetBinding);
                    }
                    else if (behavior.Kind == BehaviorKind.InstancedBatch)
                    {
                        AddInstancedBatch(behavior.InstancedBatch.BatchAssetId);
                    }
                }
            }

            void AddParamOverrides(ParamDefault[]? overrides)
            {
                if (overrides == null)
                {
                    return;
                }

                for (int i = 0; i < overrides.Length; i++)
                {
                    ref readonly ParamDefault item = ref overrides[i];
                    if (item.Lane == ParamLane.Int)
                    {
                        AddInstanceAssetOverride(item.ParamKey, item.IntValue);
                    }
                }
            }

            void AddInstanceAssetOverride(int paramKey, int assetId)
            {
                if (paramKey < 0 || assetId <= 0)
                {
                    return;
                }

                if (!instanceAssetIds.TryGetValue(paramKey, out HashSet<int>? values))
                {
                    values = new HashSet<int>();
                    instanceAssetIds.Add(paramKey, values);
                }

                values.Add(assetId);
            }

            void AddAsset(in AssetBindingConfig binding)
            {
                AddAssetId(binding.AssetKind, binding.AssetId, binding.RenderPath);
                AssetSwapEntry[] swaps = binding.AssetSwapTable ?? Array.Empty<AssetSwapEntry>();
                for (int i = 0; i < swaps.Length; i++)
                {
                    AddAssetId(binding.AssetKind, swaps[i].AssetId, binding.RenderPath);
                }

                if (binding.AssetIdParamKey >= 0 &&
                    instanceAssetIds.TryGetValue(binding.AssetIdParamKey, out HashSet<int>? overrides))
                {
                    foreach (int assetId in overrides)
                    {
                        AddAssetId(binding.AssetKind, assetId, binding.RenderPath);
                    }
                }
            }

            void AddInstancedBatch(int batchAssetId)
            {
                if (batchAssetId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' reached an InstancedBatch presenter with an invalid batch asset id {batchAssetId}.");
                }

                if (_instancedBatchAssets == null)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' requires instanced batch asset id {batchAssetId}, but the InstancedBatchAssetRegistry is not installed.");
                }

                if (!_instancedBatchAssets.TryGet(batchAssetId, out InstancedBatchAsset batch))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' references unknown instanced batch asset id {batchAssetId}.");
                }

                InstancedBatchGroup[] groups = batch.Groups ?? Array.Empty<InstancedBatchGroup>();
                for (int i = 0; i < groups.Length; i++)
                {
                    AddAssetId(AssetKind.Mesh, groups[i].MeshAssetId, batch.RenderPath);
                }
            }

            void AddAssetId(AssetKind assetKind, int assetId, VisualRenderPath renderPath)
            {
                if (assetKind is not (AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Decal or AssetKind.VFX or AssetKind.Surface) ||
                    assetId <= 0 || !_meshAssets.TryGetDescriptor(assetId, out MeshAssetDescriptor descriptor))
                {
                    return;
                }

                if (renderPath == VisualRenderPath.GpuSkinnedInstance && descriptor.GpuSkinnedLod.IsConfigured)
                {
                    AddRequiredGpuSkinnedAsset(assetKind, assetId, renderPath);
                    AddGpuSkinnedLodPass(assetKind, descriptor.GpuSkinnedLod.Main, renderPath);
                    AddGpuSkinnedLodPass(assetKind, descriptor.GpuSkinnedLod.Shadow, renderPath);
                    return;
                }

                if (descriptor.SourceUris == null || descriptor.SourceUris.Length == 0)
                {
                    return;
                }

                manifest.Add(MapPresentationAsset.Create(assetKind, assetId, renderPath, descriptor.SourceUris));
            }

            void AddGpuSkinnedLodPass(AssetKind assetKind, MeshLodAssetIds lods, VisualRenderPath renderPath)
            {
                AddRequiredGpuSkinnedAsset(assetKind, lods.High, renderPath);
                AddRequiredGpuSkinnedAsset(assetKind, lods.Medium, renderPath);
                AddRequiredGpuSkinnedAsset(assetKind, lods.Low, renderPath);
            }

            void AddRequiredGpuSkinnedAsset(AssetKind assetKind, int assetId, VisualRenderPath renderPath)
            {
                if (assetId <= 0 || !_meshAssets.TryGetDescriptor(assetId, out MeshAssetDescriptor descriptor))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' GPU-skinned LOD references unknown mesh asset id {assetId}.");
                }

                if (descriptor.Type != MeshAssetType.Model)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' GPU-skinned LOD mesh asset '{_meshAssets.GetName(assetId)}' must be a Model, but has type '{descriptor.Type}'.");
                }

                if (descriptor.SourceUris == null || descriptor.SourceUris.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' GPU-skinned LOD mesh asset '{_meshAssets.GetName(assetId)}' has no host sourceUris.");
                }

                manifest.Add(MapPresentationAsset.Create(assetKind, assetId, renderPath, descriptor.SourceUris));
            }
        }

        public void LoadTemplates(ConfigCatalog catalog, ConfigConflictReport report = null)
        {
            // This loads "Entities/templates.json" from Core and all Mods
            // Merging them with priority
            TemplateRegistry.Load("Entities/templates.json", catalog, report);
            ExpandTemplateInheritance(report);
            EntityTemplateKeys.Clear();
            _templateSources.Clear();
            var templateIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var template in TemplateRegistry.GetAll())
            {
                ValidateTemplateTriggerGraphs(template);
                ValidateTemplateInitialInteractionContext(template);
                templateIds.Add(template.Id);
            }
            ValidateTemplateChildrenGraph(templateIds);
            foreach (var template in TemplateRegistry.GetAll())
            {
                EntityTemplateKeys.Register(template.Id);
                if (report != null && report.TryGetWinner("Entities/templates.json", template.Id, out string sourceUri))
                {
                    _templateSources[template.Id] = sourceUri;
                }
            }
        }

        /// <summary>
        /// extends/uses 展开插在跨 mod 同 id 合并之后、装载校验之前：子模板可以引用
        /// 任意 mod 贡献的父模板/块，而 children/TriggerGraphs 校验看到的是展开后的
        /// 完整组合。原地展开保证所有消费方（spawn/batch/lifecycle/离线烘焙）
        /// 经由同一注册表读到同一份结果；report 接住 uses 折叠的组件覆盖链。
        /// </summary>
        private void ExpandTemplateInheritance(ConfigConflictReport report)
        {
            var byId = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal);
            foreach (var template in TemplateRegistry.GetAll())
            {
                byId[template.Id] = template;
            }

            EntityTemplateInheritance.ExpandAll(byId, report);
        }

        /// <summary>
        /// 模板 children 引用图装载期校验：子模板引用必须可解析、内联 children 递归展开、
        /// 同层 localId 唯一、localPose 严格解析、图无环；被用作 child 的模板默认禁止声明
        /// MovementParticipation（spawn 管线不授予写权）——attach:false 的可动成员豁免。
        /// </summary>
        private void ValidateTemplateChildrenGraph(HashSet<string> templateIds)
        {
            foreach (var template in TemplateRegistry.GetAll())
            {
                ValidateChildNodes(template.Id, template.Children, templateIds);
            }

            foreach (var template in TemplateRegistry.GetAll())
            {
                DetectTemplateChildrenCycle(template.Id, new HashSet<string>(StringComparer.Ordinal), "root");
            }
        }

        private void ValidateChildNodes(string ownerTemplateId, System.Collections.Generic.List<EntityTemplateChild>? children, HashSet<string> templateIds)
        {
            if (children == null)
            {
                return;
            }

            var seenLocalIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < children.Count; i++)
            {
                EntityTemplateChild child = children[i];
                string context = $"Entity template '{ownerTemplateId}' children[{i}]";
                if (child == null || string.IsNullOrWhiteSpace(child.Template))
                {
                    throw new InvalidOperationException($"{context}: template 引用缺失。");
                }
                if (!templateIds.Contains(child.Template))
                {
                    throw new InvalidOperationException(
                        $"{context}: 引用未知子模板 '{child.Template}'。");
                }

                if (child.LocalId != null)
                {
                    if (string.IsNullOrWhiteSpace(child.LocalId) ||
                        !string.Equals(child.LocalId, child.LocalId.Trim(), StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"{context}: localId 必须是非空且首尾无空白的字符串。");
                    }
                    if (!seenLocalIds.Add(child.LocalId))
                    {
                        throw new InvalidOperationException(
                            $"{context}: 同级 localId '{child.LocalId}' 重复——同一父 children 内 localId 必须唯一。");
                    }
                }

                EntityTemplate childTemplate = TemplateRegistry.Get(child.Template);
                if (child.Attach != false &&
                    childTemplate.Components != null &&
                    childTemplate.Components.ContainsKey("MovementParticipation"))
                {
                    throw new InvalidOperationException(
                        $"{context}: 子模板 '{child.Template}' 声明了 MovementParticipation——attach:true 的模板 children 是结构件，会自由移动的单位必须 attach:false 或经 AttachOp 挂接。");
                }
                Ludots.Core.Gameplay.Attachment.AttachedLocalPoseAuthoring.Parse(child.LocalPose, context);

                ValidateChildNodes(ownerTemplateId, child.Children, templateIds);
            }
        }

        private void DetectTemplateChildrenCycle(string templateId, HashSet<string> visiting, string chain)
        {
            if (!visiting.Add(templateId))
            {
                throw new InvalidOperationException(
                    $"Entity template children 图存在环: {chain} -> {templateId}。");
            }

            EntityTemplate template = TemplateRegistry.Get(templateId);
            DetectInlineChildrenCycle(template?.Children, visiting, chain, templateId);
            visiting.Remove(templateId);
        }

        private void DetectInlineChildrenCycle(System.Collections.Generic.List<EntityTemplateChild>? children, HashSet<string> visiting, string chain, string ownerTemplateId)
        {
            if (children == null)
            {
                return;
            }

            for (int i = 0; i < children.Count; i++)
            {
                EntityTemplateChild child = children[i];
                DetectTemplateChildrenCycle(child.Template, visiting, $"{chain} -> {ownerTemplateId}[{i}]");
                DetectInlineChildrenCycle(child.Children, visiting, $"{chain} -> {ownerTemplateId}[{i}]", ownerTemplateId);
            }
        }

        private static void ValidateTemplateTriggerGraphs(EntityTemplate template)
        {
            List<string>? graphs = template.TriggerGraphs;
            if (graphs == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < graphs.Count; i++)
            {
                string? name = graphs[i];
                if (string.IsNullOrWhiteSpace(name) || !string.Equals(name, name.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Entity template '{template.Id}' TriggerGraphs[{i}] must be a trimmed non-empty graph id string.");
                }

                if (!seen.Add(name))
                {
                    throw new InvalidOperationException(
                        $"Entity template '{template.Id}' TriggerGraphs[{i}] repeats graph id '{name}'; each graph may be mounted only once per entity.");
                }
            }
        }

        private static void ValidateTemplateInitialInteractionContext(EntityTemplate template)
        {
            string? profileName = template.InitialInteractionContext;
            if (profileName == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(profileName) || !string.Equals(profileName, profileName.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Entity template '{template.Id}' initialInteractionContext must be a trimmed non-empty context profile id.");
            }
        }

        public void LoadEntities(MapConfig mapConfig)
        {
            LoadEntitiesAndIndex(mapConfig);
        }


        public MapLoadEntityIndex LoadEntitiesAndIndex(MapConfig mapConfig)
        {
            if (mapConfig == null)
            {
                throw new ArgumentNullException(nameof(mapConfig));
            }

            if (mapConfig.Entities == null)
            {
                throw new InvalidOperationException($"Map '{mapConfig.Id}' requires an explicit entities list.");
            }


            // We need to extract the dictionary from the registry to pass to EntityBuilder
            // Or better, update EntityBuilder to accept DataRegistry or just the Interface.
            // For now, let's just create a dictionary snapshot or pass the lookup function.
            
            // Current EntityBuilder expects Dictionary<string, EntityTemplate>.
            // We can convert DataRegistry content to Dictionary easily.
            var templates = new System.Collections.Generic.Dictionary<string, EntityTemplate>();
            foreach(var t in TemplateRegistry.GetAll())
            {
                templates[t.Id] = t;
            }

            var builder = new EntityBuilder(_world, templates, _templateSources, _authoringContext);
            var mapEntityTag = new MapEntity { MapId = new MapId(mapConfig.Id) };
            var entityIndex = new MapLoadEntityIndex();
            var pendingBatchRequests = new List<TemplateEntityBatchSpawner.TemplateBatchSpawnRequest>(_templateBatchSpawner.ScratchCapacity);
            var pendingBatchEntityData = new List<EntitySpawnData>(_templateBatchSpawner.ScratchCapacity);
            string? activeBatchTemplateId = null;

            void FlushPendingTemplateBatch()
            {
                if (activeBatchTemplateId == null || pendingBatchRequests.Count == 0)
                {
                    pendingBatchRequests.Clear();
                    pendingBatchEntityData.Clear();
                    activeBatchTemplateId = null;
                    return;
                }

                int templateKeyId = ResolveTemplateKeyId(activeBatchTemplateId);
                EntityTemplate activeBatchTemplate = templates[activeBatchTemplateId];
                bool hasDirectBootstrap = HasDirectEntitySpawnBootstrap(templateKeyId);
                bool publishSpawnedEvent = ShouldPublishSpawnedEvent(templateKeyId, hasDirectBootstrap);

                TemplateBatchSpawnFeatures features =
                    TemplateBatchSpawnFeatures.MapEntity | TemplateBatchSpawnFeatures.PlacedInstanceId;
                if (_stableIds != null)
                {
                    features |= TemplateBatchSpawnFeatures.PresentationStableId;
                    if (!publishSpawnedEvent)
                    {
                        features |= TemplateBatchSpawnFeatures.PresentationLifecycleState;
                    }
                }

                if (hasDirectBootstrap)
                {
                    features |= TemplateBatchSpawnFeatures.PresenterRootBootstrapHandled;
                    if (TemplateBatchOwnerPayloadPreseedPolicy.CanPreseedOwnerPayloadMarker(_presenterBootstrap, activeBatchTemplate, templateKeyId))
                    {
                        features |= TemplateBatchSpawnFeatures.PresentationOwnerHasPresenterPayload;
                    }
                }

                int batchCount = pendingBatchRequests.Count;
                Span<int> stableIds = hasDirectBootstrap ? _ownerBatchStableIds.AsSpan(0, batchCount) : default;
                Span<VisualTransform> ownerTransforms = hasDirectBootstrap ? _ownerBatchTransforms.AsSpan(0, batchCount) : default;
                Span<CullState> ownerCulls = hasDirectBootstrap ? _ownerBatchCulls.AsSpan(0, batchCount) : default;
                bool hasPresenterParamOverrides = BatchContainsPresenterParamOverrides(pendingBatchRequests);
                if (hasPresenterParamOverrides && !CanApplyPresenterParamOverrides())
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' template '{activeBatchTemplateId}' declares PresenterParamOverrides but presentation runtime is not installed.");
                }

                if (hasPresenterParamOverrides && !hasDirectBootstrap)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' template '{activeBatchTemplateId}' declares PresenterParamOverrides but has no direct presenter bootstrap.");
                }

                if (!_templateBatchSpawner.TryCreateBatch(
                    activeBatchTemplateId,
                    activeBatchTemplate,
                    CollectionsMarshal.AsSpan(pendingBatchRequests),
                    features,
                    out var created,
                    stableIds,
                    ownerTransforms,
                    ownerCulls))
                {
                    throw new InvalidOperationException(
                        $"Map template batch spawn failed after template '{activeBatchTemplateId}' was classified as batch-compatible.");
                }

                for (int i = 0; i < created.Length; i++)
                {
                    entityIndex.Register(mapConfig.Id, pendingBatchEntityData[i].InstanceId, created[i]);
                    PublishTemplateOnSpawnEffect(created[i], activeBatchTemplateId);
                    MountInitialInteractionContext(
                        created[i], activeBatchTemplateId, activeBatchTemplate, pendingBatchEntityData[i].Overrides);
                    BufferEntityTriggerGraphs(created[i], activeBatchTemplateId, activeBatchTemplate);
                    // 注意：batch lane 不展开 children——TemplateSpawnDescriptor.Create 按合同
                    // 把带 children 的模板判为 Incompatible（逐子挂接只能走单实体 lane），
                    // 因此能进本 flush 的模板必无 children，无需在此展开。
                }

                if (hasDirectBootstrap)
                {
                    for (int i = 0; i < batchCount; i++)
                    {
                        _ownerBatchParamOverrides[i] = pendingBatchRequests[i].PresenterParamOverrides;
                    }

                    try
                    {
                        TryBootstrapPresenterBatch(
                            templateKeyId,
                            created,
                            stableIds,
                            ownerTransforms,
                            ownerCulls,
                            _ownerBatchParamOverrides.AsSpan(0, batchCount));
                    }
                    finally
                    {
                        for (int i = 0; i < batchCount; i++)
                        {
                            _ownerBatchParamOverrides[i] = null!;
                        }
                    }
                }

                pendingBatchRequests.Clear();
                pendingBatchEntityData.Clear();
                activeBatchTemplateId = null;
            }
            
            foreach (var entityData in mapConfig.Entities)
            {
                if (entityData == null)
                {
                    throw new InvalidOperationException($"Map '{mapConfig.Id}' contains a null entity entry.");
                }
                if (string.IsNullOrWhiteSpace(entityData.Template))
                {
                    throw new InvalidOperationException($"Map '{mapConfig.Id}' contains an entity entry without a template.");
                }

                if (!templates.ContainsKey(entityData.Template))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' references unknown entity template '{entityData.Template}'.");
                }

                // 可寻址路径 = 摆放实例根 instanceId + localId 链；缺 instanceId 的后代会
                // 落进根名字空间与兄弟撞名。此门必须盖住 batch 与非 batch 两条路径（S3-a）。
                if (string.IsNullOrWhiteSpace(entityData.InstanceId) &&
                    EntityTemplate.HasAddressableDescendant(templates[entityData.Template].Children, TemplateRegistry))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' entity template '{entityData.Template}' has addressable descendants (localId) but no InstanceId; " +
                        "the placed instance root must declare a non-empty, trimmed InstanceId to prefix addressable paths.");
                }

                if (entityData.OverridePaths is { Count: > 0 })
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' entity '{ResolveMapEntityContextId(entityData)}' overridePaths is not loaded. " +
                        "Instance titles belong in EntityInfo/insight_profiles.json.");
                }

                bool isBatchCompatible = _templateBatchSpawner.IsBatchCompatible(entityData.Template, templates[entityData.Template]);
                if (isBatchCompatible && TryBuildBatchRequest(
                        mapConfig.Id,
                        entityData,
                        templates[entityData.Template],
                        mapEntityTag,
                        out var batchRequest))
                {
                    if (!string.Equals(activeBatchTemplateId, entityData.Template, StringComparison.Ordinal) ||
                        pendingBatchRequests.Count >= _templateBatchSpawner.ScratchCapacity)
                    {
                        FlushPendingTemplateBatch();
                    }

                    activeBatchTemplateId = entityData.Template;
                    pendingBatchRequests.Add(batchRequest);
                    pendingBatchEntityData.Add(entityData);
                    continue;
                }

                if (HasPresenterParamOverrides(entityData))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' entity template '{entityData.Template}' declares PresenterParamOverrides but is not compatible with the map template batch path.");
                }

                FlushPendingTemplateBatch();

                builder
                    .UseTemplate(entityData.Template)
                    .WithEntityContext($"Map '{mapConfig.Id}' entity '{ResolveMapEntityContextId(entityData)}'");
                
                if (entityData.Overrides != null)
                {
                    foreach (var kvp in entityData.Overrides)
                    {
                        if (!string.Equals(kvp.Key, InitialInteractionContextOverrideKey, System.StringComparison.Ordinal))
                        {
                            builder.WithOverride(kvp.Key, kvp.Value);
                        }
                    }
                }

                // Placement position is the anchor of last resort: it lands only when
                // neither the template nor an explicit override supplies WorldPositionCm.
                if (entityData.PositionXCm.HasValue != entityData.PositionYCm.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' entity '{ResolveMapEntityContextId(entityData)}' authors PositionXCm/PositionYCm partially; set both or neither.");
                }

                bool hasAuthoredWorldPosition =
                    templates[entityData.Template].Components.ContainsKey("WorldPositionCm") ||
                    (entityData.Overrides != null && entityData.Overrides.ContainsKey("WorldPositionCm"));
                if (entityData.PositionXCm.HasValue && !hasAuthoredWorldPosition)
                {
                    builder.WithOverride(
                        "WorldPositionCm",
                        new JsonObject
                        {
                            ["Value"] = new JsonObject
                            {
                                ["X"] = entityData.PositionXCm.Value,
                                ["Y"] = entityData.PositionYCm.Value,
                            },
                        });
                }

                var entity = builder.Build();
                TryApplyTemplateKey(entity, entityData.Template);
                _world.Add(entity, mapEntityTag);
                entityIndex.Register(mapConfig.Id, entityData.InstanceId, entity);
                StampPlacedInstanceId(entity, entityData.InstanceId);
                PublishTemplateOnSpawnEffect(entity, entityData.Template);
                MountInitialInteractionContext(
                    entity, entityData.Template, templates[entityData.Template], entityData.Overrides);
                BufferEntityTriggerGraphs(entity, entityData.Template, templates[entityData.Template]);
                SpawnTemplateChildrenAtMapLoad(
                    builder,
                    templates,
                    mapConfig.Id,
                    entityData.Template,
                    entity,
                    mapEntityTag,
                    entityIndex,
                    string.IsNullOrWhiteSpace(entityData.InstanceId) ? null : entityData.InstanceId);
            }

            FlushPendingTemplateBatch();
            return entityIndex;
        }

        private void BufferEntityTriggerGraphs(Entity entity, string templateId, EntityTemplate template)
        {
            if (_entityTriggerGraphMounts == null || template.TriggerGraphs is not { Count: > 0 })
            {
                return;
            }

            _entityTriggerGraphMounts.BufferMapLoadSpawn(entity, templateId, template.TriggerGraphs);
        }

        private void MountInitialInteractionContext(
            Entity entity,
            string templateId,
            EntityTemplate template,
            Dictionary<string, JsonNode>? overrides)
        {
            string? profileName = template.InitialInteractionContext;
            if (overrides != null &&
                overrides.TryGetValue(InitialInteractionContextOverrideKey, out JsonNode? overrideNode) &&
                overrideNode.GetValueKind() == JsonValueKind.String)
            {
                string? overrideName = overrideNode.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(overrideName))
                {
                    profileName = overrideName;
                }
            }

            if (string.IsNullOrWhiteSpace(profileName))
            {
                return;
            }

            Ludots.Core.Input.Interaction.InteractionContextProfileRegistry? profiles = _initialInteractionContexts
                ?? throw new InvalidOperationException(
                    $"Entity template '{templateId}' declares initialInteractionContext '{profileName}' but no interaction context profile registry is bound to the map loader.");
            Ludots.Core.Input.Interaction.TemplateInteractionContextMounting.MountInitialContext(
                _world,
                profiles,
                entity,
                templateId,
                profileName);
        }

        /// <summary>
        /// map 装载 lane 的模板 children 物化：与 runtime spawn 队列同一 EntityBuilder 物化路径、
        /// 同一 AttachedPoseMath 落位数学，仅时序不同（map 装载是同步 lane）。
        /// 装载期已校验引用与无环，此处递归必然终止。带 localId 的节点以摆放实例根路径
        /// 前缀累积成可寻址路径，登记进 entityIndex（切A 只留形状，供切D/切F 消费）。
        /// </summary>
        private void SpawnTemplateChildrenAtMapLoad(
            EntityBuilder builder,
            System.Collections.Generic.Dictionary<string, EntityTemplate> templates,
            string mapId,
            string parentTemplateId,
            Entity parent,
            MapEntity mapEntityTag,
            MapLoadEntityIndex entityIndex,
            string? parentLocalPath)
        {
            EntityTemplate parentTemplate = templates[parentTemplateId];
            SpawnTemplateChildNodes(
                builder,
                templates,
                mapId,
                parentTemplateId,
                parentTemplate.Children,
                parent,
                mapEntityTag,
                entityIndex,
                parentLocalPath);
        }

        private void SpawnTemplateChildNodes(
            EntityBuilder builder,
            System.Collections.Generic.Dictionary<string, EntityTemplate> templates,
            string mapId,
            string ownerTemplateId,
            System.Collections.Generic.List<EntityTemplateChild>? children,
            Entity parent,
            MapEntity mapEntityTag,
            MapLoadEntityIndex entityIndex,
            string? parentLocalPath)
        {
            if (children is not { Count: > 0 })
            {
                return;
            }

            int index = 0;
            while (index < children.Count)
            {
                if (!TryGetNameOnlyRun(templates, children, index, out int run))
                {
                    SpawnTemplateChildSlow(
                        builder,
                        templates,
                        mapId,
                        ownerTemplateId,
                        children[index],
                        index,
                        parent,
                        mapEntityTag,
                        entityIndex,
                        parentLocalPath);
                    index++;
                    continue;
                }

                SpawnNameOnlyChildRun(
                    templates,
                    mapId,
                    ownerTemplateId,
                    children,
                    index,
                    run,
                    parent,
                    mapEntityTag,
                    entityIndex,
                    parentLocalPath,
                    builder);
                index += run;
            }
        }

        private static bool TryGetNameOnlyRun(
            System.Collections.Generic.Dictionary<string, EntityTemplate> templates,
            System.Collections.Generic.List<EntityTemplateChild> children,
            int start,
            out int run)
        {
            run = 0;
            while (start + run < children.Count && IsNameOnlyChild(templates, children[start + run]))
            {
                run++;
            }

            return run > 0;
        }

        private static bool IsNameOnlyChild(
            System.Collections.Generic.Dictionary<string, EntityTemplate> templates,
            EntityTemplateChild child)
        {
            if (child == null || string.IsNullOrWhiteSpace(child.Template) || !templates.TryGetValue(child.Template, out EntityTemplate template))
            {
                return false;
            }

            if (template.Components == null ||
                template.Components.Count != 1 ||
                !template.Components.ContainsKey("Name"))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(template.OnSpawnEffect) ||
                template.TriggerGraphs is { Count: > 0 } ||
                !string.IsNullOrWhiteSpace(template.InitialInteractionContext))
            {
                return false;
            }

            if (child.Overrides == null)
            {
                return true;
            }

            foreach (string key in child.Overrides.Keys)
            {
                if (!string.Equals(key, "Name", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private void SpawnNameOnlyChildRun(
            System.Collections.Generic.Dictionary<string, EntityTemplate> templates,
            string mapId,
            string ownerTemplateId,
            System.Collections.Generic.List<EntityTemplateChild> children,
            int start,
            int run,
            Entity parent,
            MapEntity mapEntityTag,
            MapLoadEntityIndex entityIndex,
            string? parentLocalPath,
            EntityBuilder builder)
        {
            var names = new Name[run];
            var templateKeyIds = new int[run];
            var addressablePaths = new string[run];
            for (int offset = 0; offset < run; offset++)
            {
                EntityTemplateChild child = children[start + offset];
                EntityTemplate template = templates[child.Template];
                string context = $"Map template children '{ownerTemplateId}'[{start + offset}] '{child.Template}'";
                string? relativePath = ChildRelativePath(parentLocalPath, child.LocalId);
                JsonNode childName = null;
                if (child.Overrides != null && child.Overrides.TryGetValue("Name", out JsonNode authored))
                {
                    childName = authored;
                }

                names[offset] = TemplateEntityBatchSpawner.ResolveAuthoredName(context, template, childName, instanceNameOverride: null);
                templateKeyIds[offset] = ResolveTemplateKeyId(child.Template);
                addressablePaths[offset] = relativePath;
            }

            var created = new Entity[run];
            _templateBatchSpawner.CreateNameOnlyChildren(names, templateKeyIds, addressablePaths, in mapEntityTag, created);
            for (int offset = 0; offset < run; offset++)
            {
                EntityTemplateChild child = children[start + offset];
                string context = $"Map template children '{ownerTemplateId}'[{start + offset}] '{child.Template}'";
                Entity childEntity = created[offset];
                string? childLocalPath = addressablePaths[offset];
                if (!string.IsNullOrEmpty(childLocalPath))
                {
                    entityIndex.RegisterLocalPath(mapId, childLocalPath, childEntity);
                }

                Ludots.Core.Gameplay.Attachment.AttachmentOps.Attach(
                    _world,
                    arbiter: null,
                    childEntity,
                    parent,
                    Ludots.Core.Gameplay.Attachment.AttachedLocalPoseAuthoring.Parse(child.LocalPose, context));

                SpawnTemplateChildrenAtMapLoad(
                    builder,
                    templates,
                    mapId,
                    child.Template,
                    childEntity,
                    mapEntityTag,
                    entityIndex,
                    childLocalPath);
                SpawnTemplateChildNodes(
                    builder,
                    templates,
                    mapId,
                    ownerTemplateId,
                    child.Children,
                    childEntity,
                    mapEntityTag,
                    entityIndex,
                    childLocalPath);
            }
        }

        private void SpawnTemplateChildSlow(
            EntityBuilder builder,
            System.Collections.Generic.Dictionary<string, EntityTemplate> templates,
            string mapId,
            string ownerTemplateId,
            EntityTemplateChild child,
            int index,
            Entity parent,
            MapEntity mapEntityTag,
            MapLoadEntityIndex entityIndex,
            string? parentLocalPath)
        {
            string context = $"Map template children '{ownerTemplateId}'[{index}] '{child.Template}'";
            builder
                .UseTemplate(child.Template)
                .WithEntityContext(context);
            string? childLocalPath = ChildRelativePath(parentLocalPath, child.LocalId);
            if (child.Overrides != null)
            {
                foreach (var kvp in child.Overrides)
                {
                    builder.WithOverride(kvp.Key, kvp.Value);
                }
            }

            var childEntity = builder.Build();
            TryApplyTemplateKey(childEntity, child.Template);
            _world.Add(childEntity, mapEntityTag);
            PublishTemplateOnSpawnEffect(childEntity, child.Template);
            BufferEntityTriggerGraphs(childEntity, child.Template, templates[child.Template]);

            if (!string.IsNullOrEmpty(childLocalPath))
            {
                entityIndex.RegisterLocalPath(mapId, childLocalPath, childEntity);
                StampPlacedInstanceId(childEntity, childLocalPath);
            }

            // attach:false 的独立出生属切E；本切仍走结构挂接，保留标记与禁令豁免。
            Ludots.Core.Gameplay.Attachment.AttachmentOps.Attach(
                _world,
                arbiter: null,
                childEntity,
                parent,
                Ludots.Core.Gameplay.Attachment.AttachedLocalPoseAuthoring.Parse(child.LocalPose, context));

            // 先展开被引用模板自身的 children（main 既有先例），再展开本节点的内联 children。
            SpawnTemplateChildrenAtMapLoad(
                builder,
                templates,
                mapId,
                child.Template,
                childEntity,
                mapEntityTag,
                entityIndex,
                childLocalPath);
            SpawnTemplateChildNodes(
                builder,
                templates,
                mapId,
                ownerTemplateId,
                child.Children,
                childEntity,
                mapEntityTag,
                entityIndex,
                childLocalPath);
        }

        private static string? ChildRelativePath(string? parentLocalPath, string? localId)
        {
            if (string.IsNullOrWhiteSpace(localId))
            {
                return null;
            }

            return string.IsNullOrEmpty(parentLocalPath)
                ? localId
                : parentLocalPath + "." + localId;
        }

        private void StampPlacedInstanceId(Entity entity, string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                return;
            }

            _world.Add(entity, new PlacedInstanceId { Value = instanceId });
        }

        private static bool TryBuildBatchRequest(
            string mapId,
            EntitySpawnData entityData,
            EntityTemplate template,
            in MapEntity mapEntity,
            out TemplateEntityBatchSpawner.TemplateBatchSpawnRequest request)
        {
            request = default;
            var worldPosition = default(Ludots.Core.Mathematics.FixedPoint.Fix64Vec2);
            bool hasWorldPosition = false;
            float facingAngleRad = 0f;
            bool hasFacing = false;

            if (entityData.Overrides != null && entityData.Overrides.Count > 0)
            {
                bool containsWorldPosition = false;
                bool containsFacing = false;
                foreach (string key in entityData.Overrides.Keys)
                {
                    switch (key)
                    {
                        case "WorldPositionCm":
                            containsWorldPosition = true;
                            break;
                        case "FacingDirection":
                            containsFacing = true;
                            break;
                        case "Name":
                        case "Team":
                        case "PlayerOwner":
                        case "AttributeBuffer":
                            if (template?.Components == null || !template.Components.ContainsKey(key))
                            {
                                return false;
                            }

                            break;
                        default:
                            return false;
                    }
                }

                if (containsWorldPosition)
                {
                    worldPosition = ParseWorldPositionOverride(
                        mapId,
                        entityData,
                        entityData.Overrides["WorldPositionCm"]);
                    hasWorldPosition = true;
                }

                if (containsFacing)
                {
                    facingAngleRad = ParseFacingOverride(
                        mapId,
                        entityData,
                        entityData.Overrides["FacingDirection"]);
                    hasFacing = true;
                }
            }

            // Placement yaw stays FacingDirection. Per-instance Name / Team / PlayerOwner /
            // AttributeBuffer stay on this lane when the template already has that component,
            // so the row archetype does not change. Any other component still leaves the lane.
            request = TemplateEntityBatchSpawner.CreatePlacementRequest(
                $"Map '{mapId}' entity template '{entityData.Template}'",
                template,
                worldPosition,
                hasWorldPosition,
                facingAngleRad,
                hasFacing,
                mapEntity,
                ParsePresenterParamOverrides(mapId, entityData),
                entityData.Overrides,
                entityData.InstanceId);
            return true;
        }

        private static string ResolveMapEntityContextId(EntitySpawnData entityData)
        {
            return string.IsNullOrWhiteSpace(entityData.InstanceId)
                ? $"template:{entityData.Template}"
                : entityData.InstanceId;
        }

        private static bool BatchContainsPresenterParamOverrides(List<TemplateEntityBatchSpawner.TemplateBatchSpawnRequest> requests)
        {
            for (int i = 0; i < requests.Count; i++)
            {
                if (requests[i].PresenterParamOverrides.Length != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasPresenterParamOverrides(EntitySpawnData entityData)
        {
            return entityData.PresenterParamOverrides != null && entityData.PresenterParamOverrides.Count != 0;
        }

        private bool CanApplyPresenterParamOverrides()
        {
            return _presenterRuntime != null &&
                   _presenterDefinitions != null &&
                   _presenterBootstrap != null &&
                   _stableIds != null;
        }

        private static ParamDefault[] ParsePresenterParamOverrides(string mapId, EntitySpawnData entityData)
        {
            List<ParamOverrideData> overrides = entityData.PresenterParamOverrides;
            if (overrides == null || overrides.Count == 0)
            {
                return Array.Empty<ParamDefault>();
            }

            var result = new ParamDefault[overrides.Count];
            for (int i = 0; i < overrides.Count; i++)
            {
                ParamOverrideData item = overrides[i];
                if (item == null)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' entity template '{entityData.Template}' PresenterParamOverrides[{i}] requires an object payload.");
                }

                string paramKey = item.ParamKey;
                if (string.IsNullOrWhiteSpace(paramKey) ||
                    !string.Equals(paramKey, paramKey.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' entity template '{entityData.Template}' PresenterParamOverrides[{i}].ParamKey must be a trimmed semantic string.");
                }

                if (!item.Lane.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' entity template '{entityData.Template}' PresenterParamOverrides[{i}].Lane requires an explicit param lane.");
                }

                ParamLane lane = item.Lane.Value;
                var parsed = new ParamDefault
                {
                    ParamKey = PresenterParamKeyRegistry.Register(paramKey),
                    Lane = lane,
                };

                switch (lane)
                {
                    case ParamLane.Float:
                        parsed.FloatValue = item.FloatValue;
                        break;
                    case ParamLane.Int:
                        parsed.IntValue = item.IntValue;
                        break;
                    case ParamLane.Vector:
                        parsed.VectorValue = ParsePresenterParamOverrideVector(mapId, entityData, item, i);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Map '{mapId}' entity template '{entityData.Template}' PresenterParamOverrides[{i}].Lane '{lane}' is unsupported.");
                }

                result[i] = parsed;
            }

            return result;
        }

        private static Vector4 ParsePresenterParamOverrideVector(
            string mapId,
            EntitySpawnData entityData,
            ParamOverrideData item,
            int index)
        {
            float[] values = item.VectorValue;
            if (values == null || values.Length != 4)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity template '{entityData.Template}' PresenterParamOverrides[{index}].VectorValue requires four numeric values.");
            }

            return new Vector4(values[0], values[1], values[2], values[3]);
        }

        private static Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 ParseWorldPositionOverride(
            string mapId,
            EntitySpawnData entityData,
            JsonNode worldPositionNode)
        {
            if (worldPositionNode is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm override requires an object payload.");
            }

            ValidateProperties(obj, $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm", "Value");
            JsonNode valueNode = RequireProperty(
                obj,
                "Value",
                $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm");
            if (valueNode is not JsonObject valueObj)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm.Value requires an object payload.");
            }

            ValidateProperties(valueObj, $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm.Value", "X", "Y");
            JsonNode xNode = RequireProperty(
                valueObj,
                "X",
                $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm.Value");
            JsonNode yNode = RequireProperty(
                valueObj,
                "Y",
                $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm.Value");
            if (xNode.GetValueKind() != System.Text.Json.JsonValueKind.Number)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm.Value.X requires an integer value.");
            }

            if (yNode.GetValueKind() != System.Text.Json.JsonValueKind.Number)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm.Value.Y requires an integer value.");
            }

            int x = xNode.GetValue<int>();
            int y = yNode.GetValue<int>();
            return Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(x, y);
        }

        private static float ParseFacingOverride(
            string mapId,
            EntitySpawnData entityData,
            JsonNode facingNode)
        {
            if (facingNode is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity template '{entityData.Template}' FacingDirection override requires an object payload.");
            }

            ValidateProperties(obj, $"Map '{mapId}' entity template '{entityData.Template}' FacingDirection", "AngleRad");
            JsonNode angleNode = RequireProperty(
                obj,
                "AngleRad",
                $"Map '{mapId}' entity template '{entityData.Template}' FacingDirection");
            if (angleNode.GetValueKind() != System.Text.Json.JsonValueKind.Number)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity template '{entityData.Template}' FacingDirection.AngleRad requires a numeric value.");
            }

            return angleNode.GetValue<float>();
        }

        private static void ValidateProperties(JsonObject obj, string context, params string[] allowedNames)
        {
            foreach (var kvp in obj)
            {
                bool allowed = false;
                for (int i = 0; i < allowedNames.Length; i++)
                {
                    if (string.Equals(kvp.Key, allowedNames[i], StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    throw new InvalidOperationException($"{context} contains unsupported property '{kvp.Key}'.");
                }
            }
        }

        private static JsonNode RequireProperty(JsonObject obj, string name, string context)
        {
            if (!obj.TryGetPropertyValue(name, out JsonNode node) || node == null)
            {
                throw new InvalidOperationException($"{context} requires explicit '{name}'.");
            }

            return node;
        }

        private void TryApplyTemplateKey(Entity entity, string templateId)
        {
            int templateKeyId = EntityTemplateKeys.GetId(templateId);
            if (templateKeyId <= 0)
            {
                throw new InvalidOperationException($"Entity template key '{templateId}' is not registered.");
            }

            var templateKey = new EntityTemplateKeyRef { TemplateKeyId = templateKeyId };
            if (_world.Has<EntityTemplateKeyRef>(entity))
            {
                _world.Set(entity, templateKey);
            }
            else
            {
                _world.Add(entity, templateKey);
            }
        }

        private int ResolveTemplateKeyId(string templateId)
        {
            int templateKeyId = EntityTemplateKeys.GetId(templateId);
            return templateKeyId > 0 ? templateKeyId : EntityTemplateKeys.Register(templateId);
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
            if (!hasDirectBootstrap || _presenterBootstrap == null)
            {
                return true;
            }

            return _presenterBootstrap.HasNonBootstrapEntitySpawnRules(templateKeyId);
        }

        private void TryBootstrapPresenterBatch(
            int templateKeyId,
            ReadOnlySpan<Entity> owners,
            ReadOnlySpan<int> ownerStableIds,
            ReadOnlySpan<VisualTransform> ownerTransforms,
            ReadOnlySpan<CullState> ownerCulls,
            ReadOnlySpan<ParamDefault[]> ownerParamOverrides)
        {
            if (_presenterRuntime == null ||
                _presenterDefinitions == null ||
                _presenterBootstrap == null ||
                _stableIds == null ||
                owners.Length == 0)
            {
                return;
            }

            if (owners.Length != ownerStableIds.Length ||
                owners.Length != ownerTransforms.Length ||
                owners.Length != ownerCulls.Length ||
                owners.Length != ownerParamOverrides.Length)
            {
                throw new ArgumentException("Map presenter bootstrap batch spans must have matching lengths.");
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

                    int scopeTag = rule.ResolveScopeTag(ownerStableIds[oi]);
                    if (scopeTag <= 0)
                    {
                        continue;
                    }

                    if (_presenterRuntime.HasActiveScopedInstance(
                            rule.PresenterDefinitionId,
                            owner,
                            scopeTag,
                            PresentationAnchorKind.Entity,
                            default))
                    {
                        continue;
                    }

                    _presenterBatchOwners[createCount] = owner;
                    _presenterBatchScopeIds[createCount] = scopeTag;
                    _presenterBatchStableIds[createCount] = _stableIds.Allocate();
                    _ownerBatchTransforms[createCount] = ownerTransforms[oi];
                    _ownerBatchCulls[createCount] = ownerCulls[oi];
                    _presenterBatchParamOverrides[createCount] = ownerParamOverrides[oi] ?? Array.Empty<ParamDefault>();
                    createCount++;
                }

                if (createCount == 0)
                {
                    continue;
                }

                _presenterRuntime.CreateEntityAnchoredRootBatch(
                    _presenterDefinitions,
                    rule.PresenterDefinitionId,
                    _presenterBatchOwners.AsSpan(0, createCount),
                    _presenterBatchScopeIds.AsSpan(0, createCount),
                    _presenterBatchStableIds.AsSpan(0, createCount),
                    _ownerBatchTransforms.AsSpan(0, createCount),
                    _ownerBatchCulls.AsSpan(0, createCount),
                    definition,
                    _presenterBatchCreated.AsSpan(0, createCount),
                    _stableIds.Allocate,
                    _presenterBatchParamOverrides.AsSpan(0, createCount));

                for (int i = 0; i < createCount; i++)
                {
                    _presenterBatchParamOverrides[i] = null!;
                    MarkHierarchyForBootstrapIfNeeded(_presenterBatchCreated[i]);
                }
            }
        }

        private bool PassesBootstrapCondition(CompiledPresenterBootstrapRegistry.BootstrapCreateRule rule, Entity owner)
        {
            return rule.InlineCondition switch
            {
                InlineConditionKind.None => true,
                InlineConditionKind.SourceHasVisualTransform => _world.Has<VisualTransform>(owner),
                InlineConditionKind.SourceHasAttributes => _world.Has<AttributeBuffer>(owner),
                _ => throw new InvalidOperationException($"Unsupported presenter bootstrap inline condition '{rule.InlineCondition}'."),
            };
        }

        private void MarkHierarchyForBootstrapIfNeeded(Entity root)
        {
            if (!_world.IsAlive(root) || !_world.Has<PresenterState>(root))
            {
                return;
            }

            ref readonly PresenterState state = ref _world.Get<PresenterState>(root);
            if (_presenterDefinitions != null &&
                _presenterDefinitions.TryGet(state.DefId, out PresenterDefinition definition) &&
                definition.RequiresBootstrapProcessing)
            {
                MarkPresenter(root);
            }

            ref PresenterChildren children = ref _world.Get<PresenterChildren>(root);
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                if (_world.IsAlive(child))
                {
                    MarkHierarchyForBootstrapIfNeeded(child);
                }
            }
        }

        private void MarkPresenter(Entity presenter)
        {
            if (_world.Has<PresenterBootstrapPending>(presenter))
            {
                return;
            }

            _world.Add(presenter, new PresenterBootstrapPending());
        }

        private void PublishTemplateOnSpawnEffect(Entity entity, string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return;
            }

            EntityTemplate template = TemplateRegistry.Get(templateId);
            if (template == null || string.IsNullOrWhiteSpace(template.OnSpawnEffect))
            {
                return;
            }

            if (_effectRequests == null)
            {
                throw new InvalidOperationException(
                    $"MapLoader has no EffectRequestQueue; cannot publish onSpawnEffect '{template.OnSpawnEffect}' for template '{templateId}'.");
            }

            int effectTemplateId = EffectTemplateIdRegistry.GetId(template.OnSpawnEffect);
            if (effectTemplateId <= 0)
            {
                throw new InvalidOperationException(
                    $"Entity template '{templateId}' references unknown onSpawnEffect '{template.OnSpawnEffect}'.");
            }

            _effectRequests.Publish(new EffectRequest
            {
                Source = entity,
                Target = entity,
                TargetContext = entity,
                TemplateId = effectTemplateId,
            });
        }
        
    }
}

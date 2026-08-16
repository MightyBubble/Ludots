using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Instancing;
using Ludots.Core.Presentation.Presenters;

namespace Ludots.Core.Presentation.Config
{
    public sealed class InstancedBatchAssetConfigLoader
    {
        public const string DefaultRelativePath = "Presentation/instanced_batches.json";

        private readonly ConfigPipeline _configs;
        private readonly InstancedBatchAssetRegistry _batches;
        private readonly MeshAssetRegistry _meshes;
        private readonly PresentationMaterialRegistry _materials;
        private readonly Func<string, int> _resolveAttributeKey;
        private readonly Func<PresentationEventKind, string, int> _resolveGasEventKey;
        private readonly Func<PresentationEventKind, string, int> _resolvePresentationEventKey;

        public InstancedBatchAssetConfigLoader(
            ConfigPipeline configs,
            InstancedBatchAssetRegistry batches,
            MeshAssetRegistry meshes,
            PresentationMaterialRegistry materials,
            Func<string, int>? resolveAttributeKey = null,
            Func<PresentationEventKind, string, int>? resolveGasEventKey = null,
            Func<PresentationEventKind, string, int>? resolvePresentationEventKey = null)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _batches = batches ?? throw new ArgumentNullException(nameof(batches));
            _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
            _materials = materials ?? throw new ArgumentNullException(nameof(materials));
            _resolveAttributeKey = resolveAttributeKey ?? (_ => 0);
            _resolveGasEventKey = resolveGasEventKey ?? ((_, _) => 0);
            _resolvePresentationEventKey = resolvePresentationEventKey ?? ((_, _) => 0);
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, DefaultRelativePath, ConfigMergePolicy.ArrayById, "id");
            var fragments = PresentationAssetConfigIdGuard.CollectUniqueArrayByIdFragments(_configs, in entry);
            var merged = ConfigMerger.MergeArrayByIdToEntries(fragments, in entry, report);

            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].Node is not JsonObject obj)
                {
                    throw new InvalidOperationException(
                        $"{DefaultRelativePath} entry '{merged[i].Id}' must merge to a JSON object.");
                }

                ValidateObjectFields(
                    obj,
                    $"{DefaultRelativePath} entry '{merged[i].Id}'",
                    "id",
                    "renderPath",
                    "ownerStableId",
                    "groups",
                    "customDataChannels",
                    "behaviors",
                    "progressiveSubmission");

                string key = RequireString(obj["id"], $"{DefaultRelativePath} entry id");
                var asset = new InstancedBatchAsset
                {
                    Key = key,
                    RenderPath = ParseRenderPath(obj["renderPath"], key),
                    OwnerStableId = RequireString(obj["ownerStableId"], $"Instanced batch '{key}' ownerStableId"),
                    Groups = ParseGroups(obj["groups"], key),
                    CustomDataChannels = ParseCustomDataChannels(obj["customDataChannels"], key),
                    Behaviors = ParseBehaviors(obj["behaviors"], key),
                    ProgressiveSubmission = ParseProgressiveSubmission(obj["progressiveSubmission"], key),
                };

                int batchId = _batches.Register(key, asset);
                asset.AddressTable = BuildAddressTable(batchId, asset);
                CompileGroupAddresses(asset);
                CompileAndValidateBehaviors(asset);
            }
        }

        private InstancedBatchGroup[] ParseGroups(JsonNode? node, string batchKey)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' must declare a non-empty groups array.");
            }

            var groups = new InstancedBatchGroup[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException(
                        $"Instanced batch '{batchKey}' groups[{i}] must be an object.");
                }

                ValidateObjectFields(
                    obj,
                    $"Instanced batch '{batchKey}' groups[{i}]",
                    "id",
                    "meshAssetId",
                    "materialId",
                    "bucketId",
                    "instanceSpanId",
                    "transforms",
                    "source");

                string groupId = RequireString(obj["id"], $"Instanced batch '{batchKey}' groups[{i}].id");
                string meshKey = RequireString(obj["meshAssetId"], $"Instanced batch '{batchKey}' group '{groupId}' meshAssetId");
                int meshAssetId = ResolveMeshId(meshKey, batchKey, groupId);
                int materialId = ResolveOptionalMaterialId(obj["materialId"], batchKey, groupId);

                groups[i] = new InstancedBatchGroup
                {
                    Id = groupId,
                    MeshAssetId = meshAssetId,
                    MaterialId = materialId,
                    BucketId = ResolveBucketId(obj["bucketId"], batchKey, groupId),
                    InstanceSpanId = RequireString(obj["instanceSpanId"], $"Instanced batch '{batchKey}' group '{groupId}' instanceSpanId"),
                    Transforms = ParseTransforms(obj["transforms"], obj["source"], batchKey, groupId),
                    Source = ParseInstanceSource(obj["source"], obj["transforms"], batchKey, groupId),
                };
            }

            return groups;
        }

        private static string ResolveBucketId(JsonNode? node, string batchKey, string groupId)
        {
            return RequireString(node, $"Instanced batch '{batchKey}' group '{groupId}' bucketId");
        }

        private static InstancedBatchAddressTable BuildAddressTable(int batchId, InstancedBatchAsset asset)
        {
            InstancedBatchGroup[] groups = asset.Groups ?? Array.Empty<InstancedBatchGroup>();
            var inputs = new InstancedBatchAddressGroupInput[groups.Length];
            for (int i = 0; i < groups.Length; i++)
            {
                inputs[i] = new InstancedBatchAddressGroupInput(
                    groups[i].Id,
                    groups[i].BucketId,
                    groups[i].InstanceSpanId);
            }

            return InstancedBatchAddressTable.Build(batchId, asset.OwnerStableId, inputs);
        }

        private static void CompileGroupAddresses(InstancedBatchAsset asset)
        {
            InstancedBatchGroup[] groups = asset.Groups ?? Array.Empty<InstancedBatchGroup>();
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i].Address = asset.AddressTable.Resolve(
                    groups[i].Id,
                    groups[i].BucketId,
                    groups[i].InstanceSpanId);
            }
        }

        private InstancedBatchTransform[] ParseTransforms(JsonNode? node, JsonNode? sourceNode, string batchKey, string groupId)
        {
            if (node == null)
            {
                if (sourceNode != null)
                {
                    return Array.Empty<InstancedBatchTransform>();
                }

                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' group '{groupId}' must declare either a non-empty transforms array or a source object.");
            }

            if (sourceNode != null)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' group '{groupId}' must not declare both transforms and source.");
            }

            if (node is not JsonArray arr || arr.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' group '{groupId}' must declare a non-empty transforms array.");
            }

            var transforms = new InstancedBatchTransform[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException(
                        $"Instanced batch '{batchKey}' group '{groupId}' transforms[{i}] must be an object.");
                }

                ValidateObjectFields(
                    obj,
                    $"Instanced batch '{batchKey}' group '{groupId}' transforms[{i}]",
                    "positionCm",
                    "rotation",
                    "scale");

                transforms[i] = new InstancedBatchTransform
                {
                    PositionCm = ParseRequiredVector3(obj["positionCm"], $"Instanced batch '{batchKey}' group '{groupId}' transforms[{i}].positionCm"),
                    Rotation = ParseQuaternionWithDefault(obj["rotation"], Quaternion.Identity, $"Instanced batch '{batchKey}' group '{groupId}' transforms[{i}].rotation"),
                    Scale = ParseVector3WithDefault(obj["scale"], Vector3.One, $"Instanced batch '{batchKey}' group '{groupId}' transforms[{i}].scale"),
                };
            }

            return transforms;
        }

        private static InstancedBatchInstanceSource ParseInstanceSource(JsonNode? node, JsonNode? transformsNode, string batchKey, string groupId)
        {
            if (node == null)
            {
                return default;
            }

            if (transformsNode != null)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' group '{groupId}' must not declare both transforms and source.");
            }

            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' group '{groupId}' source must be an object.");
            }

            ValidateObjectFields(
                obj,
                $"Instanced batch '{batchKey}' group '{groupId}' source",
                "format",
                "assetUri",
                "setId",
                "instanceCount",
                "groundToVisualHeightmap");

            string format = RequireString(obj["format"], $"Instanced batch '{batchKey}' group '{groupId}' source.format");
            string assetUri = RequireString(obj["assetUri"], $"Instanced batch '{batchKey}' group '{groupId}' source.assetUri");
            string setId = RequireString(obj["setId"], $"Instanced batch '{batchKey}' group '{groupId}' source.setId");
            int instanceCount = ParseRequiredInt(obj["instanceCount"], $"Instanced batch '{batchKey}' group '{groupId}' source.instanceCount");
            if (instanceCount <= 0)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' group '{groupId}' source.instanceCount must be positive.");
            }

            bool groundToVisualHeightmap = ParseOptionalBool(
                obj["groundToVisualHeightmap"],
                defaultValue: false,
                $"Instanced batch '{batchKey}' group '{groupId}' source.groundToVisualHeightmap");
            return new InstancedBatchInstanceSource(format, assetUri, setId, instanceCount, groundToVisualHeightmap);
        }

        private InstancedBatchCustomDataChannel[] ParseCustomDataChannels(JsonNode? node, string batchKey)
        {
            if (node == null)
            {
                return Array.Empty<InstancedBatchCustomDataChannel>();
            }

            if (node is not JsonArray arr)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' customDataChannels must be an array.");
            }

            if (arr.Count > MaterialCustomDataBinding.MaxSlots)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' customDataChannels supports at most {MaterialCustomDataBinding.MaxSlots} slots.");
            }

            var channels = new InstancedBatchCustomDataChannel[arr.Count];
            bool[] seen = new bool[MaterialCustomDataBinding.MaxSlots];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException(
                        $"Instanced batch '{batchKey}' customDataChannels[{i}] must be an object.");
                }

                ValidateObjectFields(
                    obj,
                    $"Instanced batch '{batchKey}' customDataChannels[{i}]",
                    "key",
                    "slot",
                    "type");

                int slot = ParseRequiredInt(obj["slot"], $"Instanced batch '{batchKey}' customDataChannels[{i}].slot");
                if ((uint)slot >= MaterialCustomDataBinding.MaxSlots)
                {
                    throw new InvalidOperationException(
                        $"Instanced batch '{batchKey}' customDataChannels[{i}].slot must be between 0 and {MaterialCustomDataBinding.MaxSlots - 1}.");
                }

                if (seen[slot])
                {
                    throw new InvalidOperationException(
                        $"Instanced batch '{batchKey}' customDataChannels[{i}].slot duplicates slot {slot}.");
                }

                seen[slot] = true;
                channels[i] = new InstancedBatchCustomDataChannel
                {
                    Key = RequireString(obj["key"], $"Instanced batch '{batchKey}' customDataChannels[{i}].key"),
                    Slot = slot,
                    Lane = ParseCustomDataLane(obj["type"], $"Instanced batch '{batchKey}' customDataChannels[{i}].type"),
                };
            }

            SortCustomDataChannels(channels);
            for (int i = 0; i < channels.Length; i++)
            {
                if (channels[i].Slot != i)
                {
                    throw new InvalidOperationException(
                        $"Instanced batch '{batchKey}' customDataChannels slots must be contiguous starting at 0.");
                }
            }

            return channels;
        }

        private static InstancedBatchProgressiveSubmissionPolicy ParseProgressiveSubmission(JsonNode? node, string batchKey)
        {
            if (node == null)
            {
                return InstancedBatchProgressiveSubmissionPolicy.None;
            }

            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' progressiveSubmission must be an object.");
            }

            ValidateObjectFields(
                obj,
                $"Instanced batch '{batchKey}' progressiveSubmission",
                "maxInstancesPerFlush");

            int maxInstancesPerFlush = ParseRequiredInt(
                obj["maxInstancesPerFlush"],
                $"Instanced batch '{batchKey}' progressiveSubmission.maxInstancesPerFlush");
            if (maxInstancesPerFlush <= 0)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' progressiveSubmission.maxInstancesPerFlush must be positive.");
            }

            return new InstancedBatchProgressiveSubmissionPolicy(maxInstancesPerFlush);
        }

        private InstancedBatchBehaviorBinding[] ParseBehaviors(JsonNode? node, string batchKey)
        {
            if (node == null)
            {
                return Array.Empty<InstancedBatchBehaviorBinding>();
            }

            if (node is not JsonArray arr)
            {
                throw new InvalidOperationException($"Instanced batch '{batchKey}' behaviors must be an array.");
            }

            var bindings = new InstancedBatchBehaviorBinding[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Instanced batch '{batchKey}' behaviors[{i}] must be an object.");
                }

                ValidateObjectFields(
                    obj,
                    $"Instanced batch '{batchKey}' behaviors[{i}]",
                    "id",
                    "source",
                    "target",
                    "mapping",
                    "order",
                    "coalescing",
                    "lifecycle");

                string key = RequireString(obj["id"], $"Instanced batch '{batchKey}' behaviors[{i}].id");
                (InstancedBatchSourceKind sourceKind, int sourceKeyId, PresentationEventKind sourceEventKind) = ParseBehaviorSource(obj["source"], batchKey, key);
                (InstancedBatchOperationKind operationKind, string groupId, string bucketId, string spanId, int customDataSlot, int targetPayloadId) =
                    ParseBehaviorTarget(obj["target"], batchKey, key);
                (InstancedBatchValueMappingKind mappingKind, float inputMin, float inputMax, float outputMin, float outputMax, float constantValue) =
                    ParseBehaviorMapping(obj["mapping"], batchKey, key);
                int order = ParseOptionalInt(obj["order"], defaultValue: i, $"Instanced batch '{batchKey}' behavior '{key}' order");
                InstancedBatchCoalescingMode coalescing = ParseOptionalEnum(
                    obj["coalescing"],
                    InstancedBatchCoalescingMode.LastWriteWins,
                    $"Instanced batch '{batchKey}' behavior '{key}' coalescing");
                InstancedBatchLifecycleMode lifecycle = ParseOptionalEnum(
                    obj["lifecycle"],
                    InstancedBatchLifecycleMode.UntilOwnerDestroyed,
                    $"Instanced batch '{batchKey}' behavior '{key}' lifecycle");

                bindings[i] = new InstancedBatchBehaviorBinding(
                    key,
                    sourceKind,
                    sourceKeyId,
                    sourceEventKind,
                    operationKind,
                    groupId,
                    bucketId,
                    spanId,
                    customDataSlot,
                    mappingKind,
                    inputMin,
                    inputMax,
                    outputMin,
                    outputMax,
                    constantValue,
                    order,
                    coalescing,
                    lifecycle,
                    targetPayloadId);
            }

            SortBehaviorsByOrder(bindings);
            return bindings;
        }

        private (InstancedBatchSourceKind Kind, int KeyId, PresentationEventKind EventKind) ParseBehaviorSource(JsonNode? node, string batchKey, string behaviorKey)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' behavior '{behaviorKey}' source must be an object.");
            }

            ValidateObjectFields(
                obj,
                $"Instanced batch '{batchKey}' behavior '{behaviorKey}' source",
                "kind",
                "eventKind",
                "key");

            InstancedBatchSourceKind kind = ParseRequiredEnum<InstancedBatchSourceKind>(
                obj["kind"],
                $"Instanced batch '{batchKey}' behavior '{behaviorKey}' source.kind");
            PresentationEventKind eventKind = ResolveSourceEventKind(obj["eventKind"], kind, batchKey, behaviorKey);
            string key = RequireString(obj["key"], $"Instanced batch '{batchKey}' behavior '{behaviorKey}' source.key");
            int keyId = kind switch
            {
                InstancedBatchSourceKind.Attribute => _resolveAttributeKey(key),
                InstancedBatchSourceKind.GasEvent => _resolveGasEventKey(eventKind, key),
                InstancedBatchSourceKind.PresentationEvent => _resolvePresentationEventKey(eventKind, key),
                _ => 0,
            };

            if (IsInvalidSourceKey(kind, eventKind, keyId))
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' behavior '{behaviorKey}' references unknown {kind} source key '{key}'.");
            }

            return (kind, keyId, eventKind);
        }

        private static bool IsInvalidSourceKey(
            InstancedBatchSourceKind kind,
            PresentationEventKind eventKind,
            int keyId)
        {
            if (kind == InstancedBatchSourceKind.Attribute)
            {
                return keyId < 0;
            }

            if (kind == InstancedBatchSourceKind.PresentationEvent &&
                eventKind is PresentationEventKind.PresenterCreated or PresentationEventKind.PresenterDestroyed &&
                keyId == -1)
            {
                return false;
            }

            return keyId <= 0;
        }

        private static PresentationEventKind ResolveSourceEventKind(
            JsonNode? node,
            InstancedBatchSourceKind sourceKind,
            string batchKey,
            string behaviorKey)
        {
            if (sourceKind == InstancedBatchSourceKind.Attribute)
            {
                if (node != null)
                {
                    throw new InvalidOperationException(
                        $"Instanced batch '{batchKey}' behavior '{behaviorKey}' source.eventKind is not valid for Attribute sources.");
                }

                return PresentationEventKind.None;
            }

            PresentationEventKind eventKind = ParseRequiredEnum<PresentationEventKind>(
                node,
                $"Instanced batch '{batchKey}' behavior '{behaviorKey}' source.eventKind");
            if (sourceKind == InstancedBatchSourceKind.GasEvent)
            {
                if (eventKind is not (
                    PresentationEventKind.EffectApplied or
                    PresentationEventKind.EffectActivated or
                    PresentationEventKind.CastCommitted or
                    PresentationEventKind.CastFailed))
                {
                    throw new InvalidOperationException(
                        $"Instanced batch '{batchKey}' behavior '{behaviorKey}' GasEvent source.eventKind must be EffectApplied, EffectActivated, CastCommitted, or CastFailed.");
                }

                return eventKind;
            }

            if (eventKind is PresentationEventKind.None or PresentationEventKind.AttributeValueChanged)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' behavior '{behaviorKey}' PresentationEvent source.eventKind must be a concrete non-attribute event kind.");
            }

            return eventKind;
        }

        private (InstancedBatchOperationKind Kind, string GroupId, string BucketId, string SpanId, int CustomDataSlot, int TargetPayloadId)
            ParseBehaviorTarget(JsonNode? node, string batchKey, string behaviorKey)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' behavior '{behaviorKey}' target must be an object.");
            }

            ValidateObjectFields(
                obj,
                $"Instanced batch '{batchKey}' behavior '{behaviorKey}' target",
                "operation",
                "group",
                "bucket",
                "span",
                "customDataSlot",
                "presentationStateId",
                "effectAssetId");

            InstancedBatchOperationKind kind = ParseRequiredEnum<InstancedBatchOperationKind>(
                obj["operation"],
                $"Instanced batch '{batchKey}' behavior '{behaviorKey}' target.operation");
            ValidateBehaviorTargetFields(obj, kind, batchKey, behaviorKey);
            string groupId = RequireString(obj["group"], $"Instanced batch '{batchKey}' behavior '{behaviorKey}' target.group");
            string bucketId = RequireString(obj["bucket"], $"Instanced batch '{batchKey}' behavior '{behaviorKey}' target.bucket");
            string spanId = RequireString(obj["span"], $"Instanced batch '{batchKey}' behavior '{behaviorKey}' target.span");
            int customDataSlot = kind == InstancedBatchOperationKind.WriteCustomData
                ? ParseRequiredInt(obj["customDataSlot"], $"Instanced batch '{batchKey}' behavior '{behaviorKey}' target.customDataSlot")
                : -1;
            if (customDataSlot >= MaterialCustomDataBinding.MaxSlots)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' behavior '{behaviorKey}' target.customDataSlot must be between 0 and {MaterialCustomDataBinding.MaxSlots - 1}.");
            }

            int targetPayloadId = kind switch
            {
                InstancedBatchOperationKind.SetPresentationState => ParseRequiredInt(
                    obj["presentationStateId"],
                    $"Instanced batch '{batchKey}' behavior '{behaviorKey}' target.presentationStateId"),
                InstancedBatchOperationKind.AttachEffect => ResolveEffectAssetId(obj["effectAssetId"], batchKey, behaviorKey),
                InstancedBatchOperationKind.UpdateEffect => ResolveEffectAssetId(obj["effectAssetId"], batchKey, behaviorKey),
                InstancedBatchOperationKind.RemoveEffect => ResolveEffectAssetId(obj["effectAssetId"], batchKey, behaviorKey),
                _ => 0,
            };

            if (targetPayloadId < 0)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' behavior '{behaviorKey}' target payload id must not be negative.");
            }

            return (kind, groupId, bucketId, spanId, customDataSlot, targetPayloadId);
        }

        private int ResolveEffectAssetId(JsonNode? node, string batchKey, string behaviorKey)
        {
            string effectKey = RequireString(
                node,
                $"Instanced batch '{batchKey}' behavior '{behaviorKey}' target.effectAssetId");
            int effectAssetId = _meshes.GetId(effectKey);
            if (effectAssetId <= 0 || !_meshes.TryGetDescriptor(effectAssetId, out MeshAssetDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' behavior '{behaviorKey}' references unknown effect asset '{effectKey}'.");
            }

            if (!descriptor.VfxData.IsValid)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' behavior '{behaviorKey}' references effect asset '{effectKey}' without VFX particle data.");
            }

            return effectAssetId;
        }

        private static (InstancedBatchValueMappingKind Kind, float InputMin, float InputMax, float OutputMin, float OutputMax, float ConstantValue)
            ParseBehaviorMapping(JsonNode? node, string batchKey, string behaviorKey)
        {
            if (node == null)
            {
                return (InstancedBatchValueMappingKind.Identity, 0f, 1f, 0f, 1f, 0f);
            }

            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' behavior '{behaviorKey}' mapping must be an object.");
            }

            ValidateObjectFields(
                obj,
                $"Instanced batch '{batchKey}' behavior '{behaviorKey}' mapping",
                "kind",
                "inputMin",
                "inputMax",
                "outputMin",
                "outputMax",
                "constantValue");

            InstancedBatchValueMappingKind kind = ParseRequiredEnum<InstancedBatchValueMappingKind>(
                obj["kind"],
                $"Instanced batch '{batchKey}' behavior '{behaviorKey}' mapping.kind");
            return (
                kind,
                obj["inputMin"]?.GetValue<float>() ?? 0f,
                obj["inputMax"]?.GetValue<float>() ?? 1f,
                obj["outputMin"]?.GetValue<float>() ?? 0f,
                obj["outputMax"]?.GetValue<float>() ?? 1f,
                obj["constantValue"]?.GetValue<float>() ?? 0f);
        }

        private int ResolveMeshId(string meshKey, string batchKey, string groupId)
        {
            int meshAssetId = _meshes.GetId(meshKey);
            if (meshAssetId <= 0 || !_meshes.TryGetDescriptor(meshAssetId, out _))
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' group '{groupId}' references unknown mesh asset '{meshKey}'.");
            }

            return meshAssetId;
        }

        private static void CompileAndValidateBehaviors(InstancedBatchAsset asset)
        {
            InstancedBatchBehaviorBinding[] behaviors = asset.Behaviors ?? Array.Empty<InstancedBatchBehaviorBinding>();
            if (behaviors.Length == 0)
            {
                return;
            }

            var compiled = new InstancedBatchBehaviorBinding[behaviors.Length];
            for (int i = 0; i < behaviors.Length; i++)
            {
                InstancedBatchBehaviorBinding behavior = behaviors[i];
                InstancedBatchAddress address = asset.AddressTable.Resolve(
                    behavior.GroupId,
                    behavior.BucketId,
                    behavior.SpanId);
                ValidateBehaviorTarget(asset, in behavior);
                compiled[i] = behavior.WithCompiledAddress(address);
            }

            asset.Behaviors = compiled;
        }

        private static void ValidateBehaviorTarget(InstancedBatchAsset asset, in InstancedBatchBehaviorBinding behavior)
        {
            if (behavior.OperationKind == InstancedBatchOperationKind.WriteCustomData)
            {
                if (behavior.CustomDataSlot < 0 || behavior.CustomDataSlot >= MaterialCustomDataBinding.MaxSlots)
                {
                    throw new InvalidOperationException(
                        $"Instanced batch '{asset.Key}' behavior '{behavior.Key}' target.customDataSlot must be between 0 and {MaterialCustomDataBinding.MaxSlots - 1}.");
                }

                InstancedBatchCustomDataChannel[] channels = asset.CustomDataChannels ?? Array.Empty<InstancedBatchCustomDataChannel>();
                if (behavior.CustomDataSlot >= channels.Length || channels[behavior.CustomDataSlot].Slot != behavior.CustomDataSlot)
                {
                    throw new InvalidOperationException(
                        $"Instanced batch '{asset.Key}' behavior '{behavior.Key}' writes undeclared customDataSlot {behavior.CustomDataSlot}.");
                }
            }
            else if (behavior.CustomDataSlot >= 0)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{asset.Key}' behavior '{behavior.Key}' target.customDataSlot is only valid for WriteCustomData.");
            }

            if (RequiresPayload(behavior.OperationKind) && behavior.TargetPayloadId <= 0)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{asset.Key}' behavior '{behavior.Key}' operation '{behavior.OperationKind}' requires a positive payload id.");
            }
        }

        private static bool RequiresPayload(InstancedBatchOperationKind kind)
        {
            return kind is InstancedBatchOperationKind.SetPresentationState
                or InstancedBatchOperationKind.AttachEffect
                or InstancedBatchOperationKind.UpdateEffect
                or InstancedBatchOperationKind.RemoveEffect;
        }

        private int ResolveOptionalMaterialId(JsonNode? node, string batchKey, string groupId)
        {
            string materialKey = ReadOptionalString(node, $"Instanced batch '{batchKey}' group '{groupId}' materialId");
            if (materialKey.Length == 0)
            {
                return 0;
            }

            int materialId = _materials.GetId(materialKey);
            if (materialId <= 0 || !_materials.TryGet(materialId, out _))
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' group '{groupId}' references unknown material asset '{materialKey}'.");
            }

            return materialId;
        }

        private static VisualRenderPath ParseRenderPath(JsonNode? node, string batchKey)
        {
            VisualRenderPath renderPath = ParseRequiredEnum<VisualRenderPath>(node, $"Instanced batch '{batchKey}' renderPath");
            if (renderPath != VisualRenderPath.InstancedStaticMesh &&
                renderPath != VisualRenderPath.HierarchicalInstancedStaticMesh)
            {
                throw new InvalidOperationException(
                    $"Instanced batch '{batchKey}' renderPath must be 'InstancedStaticMesh' or 'HierarchicalInstancedStaticMesh', not '{renderPath}'.");
            }

            return renderPath;
        }

        private static MaterialCustomDataLane ParseCustomDataLane(JsonNode? node, string context)
        {
            return ParseRequiredEnum<MaterialCustomDataLane>(node, context);
        }

        private static Vector3 ParseRequiredVector3(JsonNode? node, string context)
        {
            if (node == null)
            {
                throw new InvalidOperationException($"{context} requires explicit x/y/z values.");
            }

            return ParseVector3WithDefault(node, Vector3.Zero, context);
        }

        private static Vector3 ParseVector3WithDefault(JsonNode? node, Vector3 defaultValue, string context)
        {
            if (node == null)
            {
                return defaultValue;
            }

            if (node is JsonObject obj)
            {
                ValidateVectorObjectFields(obj, context, requireW: false);
                return new Vector3(
                    ParseRequiredFiniteFloat(obj["x"], $"{context}.x"),
                    ParseRequiredFiniteFloat(obj["y"], $"{context}.y"),
                    ParseRequiredFiniteFloat(obj["z"], $"{context}.z"));
            }

            if (node is JsonArray arr)
            {
                if (arr.Count != 3)
                {
                    throw new InvalidOperationException($"{context} requires exactly 3 numeric array entries.");
                }

                return new Vector3(
                    ParseRequiredFiniteFloat(arr[0], $"{context}[0]"),
                    ParseRequiredFiniteFloat(arr[1], $"{context}[1]"),
                    ParseRequiredFiniteFloat(arr[2], $"{context}[2]"));
            }

            throw new InvalidOperationException($"{context} must be an object with x/y/z or a 3-component array.");
        }

        private static Quaternion ParseQuaternionWithDefault(JsonNode? node, Quaternion defaultValue, string context)
        {
            if (node == null)
            {
                return defaultValue;
            }

            if (node is JsonObject obj)
            {
                ValidateVectorObjectFields(obj, context, requireW: true);
                return new Quaternion(
                    ParseRequiredFiniteFloat(obj["x"], $"{context}.x"),
                    ParseRequiredFiniteFloat(obj["y"], $"{context}.y"),
                    ParseRequiredFiniteFloat(obj["z"], $"{context}.z"),
                    ParseRequiredFiniteFloat(obj["w"], $"{context}.w"));
            }

            if (node is JsonArray arr)
            {
                if (arr.Count != 4)
                {
                    throw new InvalidOperationException($"{context} requires exactly 4 numeric array entries.");
                }

                return new Quaternion(
                    ParseRequiredFiniteFloat(arr[0], $"{context}[0]"),
                    ParseRequiredFiniteFloat(arr[1], $"{context}[1]"),
                    ParseRequiredFiniteFloat(arr[2], $"{context}[2]"),
                    ParseRequiredFiniteFloat(arr[3], $"{context}[3]"));
            }

            throw new InvalidOperationException($"{context} must be an object with x/y/z/w or a 4-component array.");
        }

        private static void ValidateVectorObjectFields(JsonObject obj, string context, bool requireW)
        {
            foreach ((string propertyName, _) in obj)
            {
                if (propertyName != "x" &&
                    propertyName != "y" &&
                    propertyName != "z" &&
                    (!requireW || propertyName != "w"))
                {
                    string expected = requireW ? "x, y, z, w" : "x, y, z";
                    throw new InvalidOperationException(
                        $"{context} object uses unsupported field '{propertyName}'. Expected exact fields {expected}.");
                }
            }
        }

        private static void ValidateObjectFields(JsonObject obj, string context, params string[] allowedNames)
        {
            foreach ((string propertyName, _) in obj)
            {
                bool allowed = false;
                for (int i = 0; i < allowedNames.Length; i++)
                {
                    if (string.Equals(propertyName, allowedNames[i], StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    throw new InvalidOperationException(
                        $"{context} uses unsupported field '{propertyName}'.");
                }
            }
        }

        private static void ValidateBehaviorTargetFields(
            JsonObject obj,
            InstancedBatchOperationKind kind,
            string batchKey,
            string behaviorKey)
        {
            string context = $"Instanced batch '{batchKey}' behavior '{behaviorKey}' target";
            if (kind != InstancedBatchOperationKind.WriteCustomData && obj.ContainsKey("customDataSlot"))
            {
                throw new InvalidOperationException(
                    $"{context}.customDataSlot is only valid for WriteCustomData.");
            }

            if (kind != InstancedBatchOperationKind.SetPresentationState && obj.ContainsKey("presentationStateId"))
            {
                throw new InvalidOperationException(
                    $"{context}.presentationStateId is only valid for SetPresentationState.");
            }

            bool effectOperation = kind is InstancedBatchOperationKind.AttachEffect
                or InstancedBatchOperationKind.UpdateEffect
                or InstancedBatchOperationKind.RemoveEffect;
            if (!effectOperation && obj.ContainsKey("effectAssetId"))
            {
                throw new InvalidOperationException(
                    $"{context}.effectAssetId is only valid for AttachEffect, UpdateEffect, or RemoveEffect.");
            }
        }

        private static string RequireString(JsonNode? node, string context)
        {
            string value = ReadStringValue(node, context);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context} must be a non-empty string.");
            }

            RequireNoBoundaryWhitespace(value, context);
            return value;
        }

        private static string ReadOptionalString(JsonNode? node, string context)
        {
            if (node == null)
            {
                return string.Empty;
            }

            string value = ReadStringValue(node, context);
            if (value.Length == 0)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context} must not be whitespace.");
            }

            RequireNoBoundaryWhitespace(value, context);
            return value;
        }

        private static string ReadStringValue(JsonNode? node, string context)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out string? parsed))
            {
                return parsed ?? string.Empty;
            }

            throw new InvalidOperationException($"{context} must be a string.");
        }

        private static int ParseRequiredInt(JsonNode? node, string context)
        {
            if (node is not JsonValue value || !value.TryGetValue<int>(out int parsed))
            {
                throw new InvalidOperationException($"{context} requires an explicit integer field.");
            }

            return parsed;
        }

        private static int ParseOptionalInt(JsonNode? node, int defaultValue, string context)
        {
            if (node == null)
            {
                return defaultValue;
            }

            return ParseRequiredInt(node, context);
        }

        private static bool ParseOptionalBool(JsonNode? node, bool defaultValue, string context)
        {
            if (node == null)
            {
                return defaultValue;
            }

            if (node is JsonValue value && value.TryGetValue<bool>(out bool parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException($"{context} must be a boolean.");
        }

        private static float ParseRequiredFiniteFloat(JsonNode? node, string context)
        {
            if (node is not JsonValue value || !value.TryGetValue<float>(out float parsed))
            {
                throw new InvalidOperationException($"{context} requires an explicit numeric field.");
            }

            if (!float.IsFinite(parsed))
            {
                throw new InvalidOperationException($"{context} must be finite.");
            }

            return parsed;
        }

        private static T ParseRequiredEnum<T>(JsonNode? node, string context) where T : struct, Enum
        {
            if (node is not JsonValue value)
            {
                throw new InvalidOperationException($"{context} requires a non-empty enum string.");
            }

            if (value.TryGetValue<int>(out int numericValue))
            {
                throw new InvalidOperationException(
                    $"{context} must be an enum string, not numeric value {numericValue}.");
            }

            if (!value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{context} requires a non-empty enum string.");
            }

            if (!TryParseDefinedEnum(text, out T parsed))
            {
                throw new InvalidOperationException($"{context} has invalid value '{text}'.");
            }

            return parsed;
        }

        private static T ParseOptionalEnum<T>(JsonNode? node, T defaultValue, string context) where T : struct, Enum
        {
            if (node == null)
            {
                return defaultValue;
            }

            return ParseRequiredEnum<T>(node, context);
        }

        private static bool TryParseDefinedEnum<T>(string text, out T parsed) where T : struct, Enum
        {
            parsed = default;
            if (!string.Equals(text, text.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }

            return Enum.TryParse(text, ignoreCase: false, out parsed) && Enum.IsDefined(typeof(T), parsed);
        }

        private static void SortCustomDataChannels(InstancedBatchCustomDataChannel[] channels)
        {
            for (int i = 1; i < channels.Length; i++)
            {
                InstancedBatchCustomDataChannel channel = channels[i];
                int j = i - 1;
                while (j >= 0 && channels[j].Slot > channel.Slot)
                {
                    channels[j + 1] = channels[j];
                    j--;
                }

                channels[j + 1] = channel;
            }
        }

        private static void SortBehaviorsByOrder(InstancedBatchBehaviorBinding[] behaviors)
        {
            for (int i = 1; i < behaviors.Length; i++)
            {
                InstancedBatchBehaviorBinding behavior = behaviors[i];
                int j = i - 1;
                while (j >= 0 && behaviors[j].Order > behavior.Order)
                {
                    behaviors[j + 1] = behaviors[j];
                    j--;
                }

                behaviors[j + 1] = behavior;
            }
        }

        private static void RequireNoBoundaryWhitespace(string value, string context)
        {
            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{context} must not include leading or trailing whitespace.");
            }
        }
    }
}

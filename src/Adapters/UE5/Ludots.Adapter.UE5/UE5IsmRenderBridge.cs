using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;

namespace Ludots.Adapter.UE5
{
    public readonly struct StaticDrawItem
    {
        public int StableId { get; init; }
        public VisualRenderPath RenderPath { get; init; }
        public Vector3 Translation { get; init; }
        public Quaternion Rotation { get; init; }
        public Vector3 Scale { get; init; }
        public VisualVisibility Visibility { get; init; }
    }

    public readonly struct AllegroDrawItem
    {
        public int StableId { get; init; }
        public int MeshAssetId { get; init; }
        public int AnimationProfileId { get; init; }
        public VisualRenderPath RenderPath { get; init; }
        public Vector3 Position { get; init; }
        public Quaternion Rotation { get; init; }
        public Vector3 Scale { get; init; }
        public Vector4 Color { get; init; }
        public AnimatorPackedState Animator { get; init; }
        public AnimationOverlayRequest AnimationOverlay { get; init; }
        public VisualVisibility Visibility { get; init; }
    }

    public sealed class IsmBucket
    {
        public int MeshAssetId { get; }
        public VisualRenderPath RenderPath { get; }
        public List<StaticDrawItem> Instances { get; } = new();

        public IsmBucket(int meshAssetId, VisualRenderPath renderPath)
        {
            MeshAssetId = meshAssetId;
            RenderPath = renderPath;
        }

        internal void Clear() => Instances.Clear();
    }

    public sealed class UE5IsmRenderBridge
    {
        private const float MetersToUECm = 100f;

        private readonly Dictionary<long, IsmBucket> _buckets = new();
        private readonly List<IsmBucket> _bucketList = new();
        private readonly List<AllegroDrawItem> _allegroItems = new();
        private readonly PrefabFinalizedLeafBuffer _prefabLeaves = new();
        private readonly PrefabFinalizedVisualBuffer _prefabVisuals = new();

        public IReadOnlyList<IsmBucket> HismBuckets => _bucketList;
        public IReadOnlyList<AllegroDrawItem> AllegroItems => _allegroItems;

        public void CollectBuckets(GameEngine engine)
        {
            ResetFrame();

            if (engine == null)
            {
                return;
            }

            MeshAssetRegistry? meshRegistry = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
            PresentationVisualRequestBuffer? visualRequests = engine.GetService(CoreServiceKeys.PresentationVisualRequestBuffer);
            PresentationAdapterCapabilities? capabilities = engine.GetService(CoreServiceKeys.PresentationAdapterCapabilities);
            var visualHeightmap = engine.GetService(CoreServiceKeys.VisualHeightmap);
            var finalizationContext = new PrefabFinalizationContext(visualHeightmap);

            PrimitiveDrawBuffer? snapshot =
                engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer) ??
                engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);

            if (snapshot != null)
            {
                CollectStaticBuckets(snapshot, meshRegistry, visualRequests, capabilities, in finalizationContext);
            }

            SkinnedVisualBatchBuffer? skinnedBatch = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer);
            if (skinnedBatch != null)
            {
                CollectSkinnedItems(skinnedBatch, meshRegistry, visualRequests, capabilities, in finalizationContext);
                return;
            }

            if (snapshot != null)
            {
                CollectSkinnedFallback(snapshot, meshRegistry, visualRequests, capabilities, in finalizationContext);
            }
        }

        public static Vector3 ToUEPosition(Vector3 ludots)
        {
            return new Vector3(
                ludots.X * MetersToUECm,
                ludots.Z * MetersToUECm,
                ludots.Y * MetersToUECm);
        }

        public static Vector3 ToUEScale(Vector3 ludotsScale)
        {
            return new Vector3(ludotsScale.X, ludotsScale.Z, ludotsScale.Y);
        }

        public static Quaternion ToUERotation(Quaternion ludotsRotation)
        {
            return new Quaternion(ludotsRotation.X, ludotsRotation.Z, ludotsRotation.Y, ludotsRotation.W);
        }

        private void ResetFrame()
        {
            foreach (var bucket in _buckets.Values)
            {
                bucket.Clear();
            }

            _bucketList.Clear();
            _allegroItems.Clear();
        }

        private void CollectStaticBuckets(
            PrimitiveDrawBuffer buffer,
            MeshAssetRegistry? meshRegistry,
            PresentationVisualRequestBuffer? visualRequests,
            PresentationAdapterCapabilities? capabilities,
            in PrefabFinalizationContext finalizationContext)
        {
            ReadOnlySpan<PrimitiveDrawItem> span = buffer.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (!item.RenderPath.IsStaticInstanceLane())
                {
                    continue;
                }

                AddStaticItemRecursive(
                    item.MeshAssetId,
                    item.RenderPath,
                    item.StableId,
                    item.Position,
                    item.Rotation,
                    item.Scale,
                    item.Visibility,
                    meshRegistry,
                    visualRequests,
                    capabilities,
                    in finalizationContext);
            }
        }

        private void AddStaticItemRecursive(
            int meshAssetId,
            VisualRenderPath renderPath,
            int stableId,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            VisualVisibility visibility,
            MeshAssetRegistry? meshRegistry,
            PresentationVisualRequestBuffer? visualRequests,
            PresentationAdapterCapabilities? capabilities,
            in PrefabFinalizationContext finalizationContext)
        {
            if (meshRegistry == null)
            {
                return;
            }

            _prefabLeaves.Clear();
            _prefabVisuals.Clear();
            PrefabFinalizationPipeline.FinalizeVisuals(
                meshRegistry,
                meshAssetId,
                stableId,
                position,
                rotation,
                scale,
                Vector4.One,
                finalizationContext,
                _prefabLeaves,
                _prefabVisuals);

            foreach (ref readonly var leaf in _prefabLeaves.GetSpan())
            {
                long key = BuildBucketKey(leaf.MeshAssetId, renderPath);
                if (!_buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new IsmBucket(leaf.MeshAssetId, renderPath);
                    _buckets.Add(key, bucket);
                }

                if (bucket.Instances.Count == 0)
                {
                    _bucketList.Add(bucket);
                }

                bucket.Instances.Add(new StaticDrawItem
                {
                    StableId = leaf.StableId,
                    RenderPath = renderPath,
                    Translation = ToUEPosition(leaf.Position),
                    Rotation = ToUERotation(leaf.Rotation),
                    Scale = ToUEScale(leaf.Scale),
                    Visibility = visibility,
                });
            }

            AppendTypedPrefabVisualRequests(visualRequests, capabilities, _prefabVisuals);
        }

        private void CollectSkinnedItems(
            SkinnedVisualBatchBuffer buffer,
            MeshAssetRegistry? meshRegistry,
            PresentationVisualRequestBuffer? visualRequests,
            PresentationAdapterCapabilities? capabilities,
            in PrefabFinalizationContext finalizationContext)
        {
            ReadOnlySpan<SkinnedVisualBatchItem> span = buffer.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                AddSkinnedItemRecursive(
                    item.MeshAssetId,
                    item.StableId,
                    item.AnimationProfileId,
                    item.RenderPath,
                    item.Position,
                    item.Rotation,
                    item.Scale,
                    item.Color,
                    item.Animator,
                    item.AnimationOverlay,
                    item.Visibility,
                    meshRegistry,
                    visualRequests,
                    capabilities,
                    in finalizationContext);
            }
        }

        private void CollectSkinnedFallback(
            PrimitiveDrawBuffer buffer,
            MeshAssetRegistry? meshRegistry,
            PresentationVisualRequestBuffer? visualRequests,
            PresentationAdapterCapabilities? capabilities,
            in PrefabFinalizationContext finalizationContext)
        {
            ReadOnlySpan<PrimitiveDrawItem> span = buffer.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (!item.RenderPath.IsSkinnedLane())
                {
                    continue;
                }

                AddSkinnedItemRecursive(
                    item.MeshAssetId,
                    item.StableId,
                    item.AnimationProfileId,
                    item.RenderPath,
                    item.Position,
                    item.Rotation,
                    item.Scale,
                    item.Color,
                    item.Animator,
                    item.AnimationOverlay,
                    item.Visibility,
                    meshRegistry,
                    visualRequests,
                    capabilities,
                    in finalizationContext);
            }
        }

        private void AddSkinnedItemRecursive(
            int meshAssetId,
            int stableId,
            int animationProfileId,
            VisualRenderPath renderPath,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            Vector4 color,
            AnimatorPackedState animator,
            AnimationOverlayRequest animationOverlay,
            VisualVisibility visibility,
            MeshAssetRegistry? meshRegistry,
            PresentationVisualRequestBuffer? visualRequests,
            PresentationAdapterCapabilities? capabilities,
            in PrefabFinalizationContext finalizationContext)
        {
            if (meshRegistry == null)
            {
                return;
            }

            _prefabLeaves.Clear();
            _prefabVisuals.Clear();
            PrefabFinalizationPipeline.FinalizeVisuals(
                meshRegistry,
                meshAssetId,
                stableId,
                position,
                rotation,
                scale,
                color,
                finalizationContext,
                _prefabLeaves,
                _prefabVisuals);

            foreach (ref readonly var leaf in _prefabLeaves.GetSpan())
            {
                AddFinalizedSkinnedLeaf(
                    leaf.StableId,
                    leaf.MeshAssetId,
                    animationProfileId,
                    renderPath,
                    leaf.Position,
                    leaf.Rotation,
                    leaf.Scale,
                    leaf.Color,
                    animator,
                    animationOverlay,
                    visibility);
            }

            AppendTypedPrefabVisualRequests(visualRequests, capabilities, _prefabVisuals);
        }

        private static void AppendTypedPrefabVisualRequests(
            PresentationVisualRequestBuffer? visualRequests,
            PresentationAdapterCapabilities? capabilities,
            PrefabFinalizedVisualBuffer visuals)
        {
            if (visuals.Count == 0)
            {
                return;
            }

            if (visualRequests == null)
            {
                throw new InvalidOperationException(
                    $"Typed prefab finalization produced {visuals.Count} non-mesh visual request(s), but PresentationVisualRequestBuffer is not registered.");
            }

            foreach (ref readonly var visual in visuals.GetSpan())
            {
                var request = visual.ToVisualRequest();
                if (!visualRequests.TryAdd(in request))
                {
                    throw new InvalidOperationException(
                        $"Presentation visual request buffer overflowed while forwarding prefab visual stableId={visual.StableId} kind={visual.Kind}.");
                }
            }

            PresentationVisualCapabilityValidator.Validate(visualRequests, capabilities);
        }

        private void AddFinalizedSkinnedLeaf(
            int stableId,
            int meshAssetId,
            int animationProfileId,
            VisualRenderPath renderPath,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            Vector4 color,
            AnimatorPackedState animator,
            AnimationOverlayRequest animationOverlay,
            VisualVisibility visibility)
        {
            _allegroItems.Add(new AllegroDrawItem
            {
                StableId = stableId,
                MeshAssetId = meshAssetId,
                AnimationProfileId = animationProfileId,
                RenderPath = renderPath,
                Position = ToUEPosition(position),
                Rotation = ToUERotation(rotation),
                Scale = ToUEScale(scale),
                Color = color,
                Animator = animator,
                AnimationOverlay = animationOverlay,
                Visibility = visibility,
            });
        }

        private static long BuildBucketKey(int meshAssetId, VisualRenderPath renderPath)
        {
            return ((long)meshAssetId << 8) | (byte)renderPath;
        }
    }
}

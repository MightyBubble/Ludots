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

        public IReadOnlyList<IsmBucket> HismBuckets => _bucketList;
        public IReadOnlyList<AllegroDrawItem> AllegroItems => _allegroItems;

        public void CollectBuckets(GameEngine engine)
        {
            ResetFrame();

            if (engine == null)
            {
                return;
            }

            PrimitiveDrawBuffer? snapshot =
                engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer) ??
                engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);

            if (snapshot != null)
            {
                var visualHeightmap = engine.GetService(CoreServiceKeys.VisualHeightmap);
                var finalizationContext = new PrefabFinalizationContext(visualHeightmap);
                CollectStaticBuckets(snapshot, engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry), in finalizationContext);
            }

            SkinnedVisualBatchBuffer? skinnedBatch = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer);
            if (skinnedBatch != null)
            {
                CollectSkinnedItems(skinnedBatch);
                return;
            }

            if (snapshot != null)
            {
                CollectSkinnedFallback(snapshot);
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

        private void CollectStaticBuckets(PrimitiveDrawBuffer buffer, MeshAssetRegistry? meshRegistry, in PrefabFinalizationContext finalizationContext)
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
            in PrefabFinalizationContext finalizationContext)
        {
            if (meshRegistry == null)
            {
                return;
            }

            _prefabLeaves.Clear();
            PrefabFinalizationPipeline.FinalizeLeaves(
                meshRegistry,
                meshAssetId,
                stableId,
                position,
                rotation,
                scale,
                Vector4.One,
                finalizationContext,
                _prefabLeaves);

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
        }

        private void CollectSkinnedItems(SkinnedVisualBatchBuffer buffer)
        {
            ReadOnlySpan<SkinnedVisualBatchItem> span = buffer.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                _allegroItems.Add(new AllegroDrawItem
                {
                    StableId = item.StableId,
                    MeshAssetId = item.MeshAssetId,
                    AnimationProfileId = item.AnimationProfileId,
                    RenderPath = item.RenderPath,
                    Position = ToUEPosition(item.Position),
                    Rotation = ToUERotation(item.Rotation),
                    Scale = ToUEScale(item.Scale),
                    Color = item.Color,
                    Animator = item.Animator,
                    AnimationOverlay = item.AnimationOverlay,
                    Visibility = item.Visibility,
                });
            }
        }

        private void CollectSkinnedFallback(PrimitiveDrawBuffer buffer)
        {
            ReadOnlySpan<PrimitiveDrawItem> span = buffer.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (!item.RenderPath.IsSkinnedLane())
                {
                    continue;
                }

                _allegroItems.Add(new AllegroDrawItem
                {
                    StableId = item.StableId,
                    MeshAssetId = item.MeshAssetId,
                    AnimationProfileId = item.AnimationProfileId,
                    RenderPath = item.RenderPath,
                    Position = ToUEPosition(item.Position),
                    Rotation = ToUERotation(item.Rotation),
                    Scale = ToUEScale(item.Scale),
                    Color = item.Color,
                    Animator = item.Animator,
                    AnimationOverlay = item.AnimationOverlay,
                    Visibility = item.Visibility,
                });
            }
        }

        private static long BuildBucketKey(int meshAssetId, VisualRenderPath renderPath)
        {
            return ((long)meshAssetId << 8) | (byte)renderPath;
        }
    }
}

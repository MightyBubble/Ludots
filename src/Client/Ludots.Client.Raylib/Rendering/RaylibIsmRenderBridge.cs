using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Ludots.Core.Presentation.AdapterSync;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed class RaylibIsmRenderBridge
    {
        private enum BucketCacheOwner : byte
        {
            None = 0,
            PersistentSync = 1,
            BenchmarkScene = 2,
        }

        public sealed class Bucket
        {
            public Bucket(in StaticMeshLaneKey lane)
            {
                Lane = lane;
            }

            public StaticMeshLaneKey Lane { get; }

            public List<PrimitiveDrawItem> Items { get; } = new();

            public bool HasVisibleItems => Items.Count > 0;

            public int Revision { get; private set; }

            public void MarkDirty()
            {
                unchecked
                {
                    Revision++;
                }
            }
        }

        private readonly struct BucketSlot
        {
            public BucketSlot(Bucket bucket, int itemIndex)
            {
                Bucket = bucket;
                ItemIndex = itemIndex;
            }

            public Bucket Bucket { get; }

            public int ItemIndex { get; }
        }

        private readonly StaticMeshAdapterSyncPlanner _planner = new();
        private readonly Dictionary<StaticMeshLaneKey, Bucket> _bucketMap = new();
        private readonly List<Bucket> _activeBuckets = new();
        private readonly Dictionary<int, BucketSlot> _bucketSlotsByStableId = new();
        private readonly Dictionary<int, Vector4> _materialColors = new();
        private RaylibBenchmarkScene _benchmarkScene;
        private RaylibBenchmarkStats _lastStats;
        private int _benchmarkActiveInstanceCount;
        private int _lastPersistentSnapshotRevision = -1;
        private double _lastBenchmarkBucketRebuildMs;
        private BucketCacheOwner _bucketOwner;

        public StaticMeshAdapterSyncPlanner Planner => _planner;

        public IReadOnlyDictionary<int, StaticMeshAdapterBindingState> ActiveBindings => _planner.ActiveBindings;

        public IReadOnlyList<Bucket> ActiveBuckets => _activeBuckets;

        public RaylibBenchmarkStats LastStats => _lastStats;

        public void SyncPersistentLanes(PrimitiveDrawBuffer? snapshot)
        {
            if (snapshot == null)
            {
                _planner.Sync(snapshot);
                ClearBucketCache();
                _lastPersistentSnapshotRevision = -1;
                return;
            }

            bool requiresFullRebuild = _bucketOwner != BucketCacheOwner.PersistentSync;
            int snapshotRevision = snapshot.Revision;
            if (!requiresFullRebuild && snapshotRevision == _lastPersistentSnapshotRevision)
            {
                return;
            }

            _planner.Sync(snapshot);
            _lastPersistentSnapshotRevision = snapshotRevision;
            if (requiresFullRebuild)
            {
                RebuildBuckets(_planner.ActiveBindings.Values);
                _bucketOwner = BucketCacheOwner.PersistentSync;
                return;
            }

            ApplySyncOperations(_planner.Operations);
        }

        public void SetBenchmarkScene(in RaylibBenchmarkScene scene)
        {
            _benchmarkScene = scene;
            _materialColors.Clear();
            foreach (RaylibBenchmarkMaterialColor color in scene.Palette.Colors.Span)
            {
                _materialColors[color.MaterialId] = color.Color;
            }

            if (scene.Enabled)
            {
                _benchmarkActiveInstanceCount = Math.Clamp(scene.InitialActiveInstanceCount, 0, scene.Instances.Length);
                BuildCachedBenchmarkBuckets(scene);
            }
            else
            {
                _benchmarkActiveInstanceCount = 0;
                ClearBucketCache();
            }

            if (!scene.Enabled)
            {
                _lastBenchmarkBucketRebuildMs = 0d;
                _lastStats = default;
            }
        }

        public RaylibBenchmarkScene GetBenchmarkScene() => _benchmarkScene;

        public bool SetBenchmarkActiveInstanceCount(int count)
        {
            if (!_benchmarkScene.Enabled)
            {
                return false;
            }

            int clamped = Math.Clamp(count, 0, _benchmarkScene.Instances.Length);
            if (clamped == _benchmarkActiveInstanceCount)
            {
                return false;
            }

            _benchmarkActiveInstanceCount = clamped;
            BuildCachedBenchmarkBuckets(_benchmarkScene);
            return true;
        }

        public int GetBenchmarkActiveInstanceCount()
        {
            return _benchmarkActiveInstanceCount;
        }

        public RaylibBenchmarkStats BuildBenchmarkBuckets()
        {
            if (!_benchmarkScene.Enabled)
            {
                ClearBucketCache();
                _materialColors.Clear();
                _lastBenchmarkBucketRebuildMs = 0d;
                _lastStats = default;
                return _lastStats;
            }

            _lastStats = new RaylibBenchmarkStats(
                active: true,
                instanceCount: _benchmarkActiveInstanceCount,
                bucketCount: _activeBuckets.Count,
                visibleCount: _benchmarkActiveInstanceCount,
                cpuBuildMs: _lastBenchmarkBucketRebuildMs,
                cpuDrawMs: _lastStats.CpuDrawMs);
            return _lastStats;
        }

        public void CompleteBenchmarkDraw(double drawMs, int visibleCount)
        {
            if (!_benchmarkScene.Enabled)
            {
                _lastStats = default;
                return;
            }

            _lastStats = new RaylibBenchmarkStats(
                active: true,
                instanceCount: _lastStats.InstanceCount,
                bucketCount: _lastStats.BucketCount,
                visibleCount: visibleCount,
                cpuBuildMs: _lastStats.CpuBuildMs,
                cpuDrawMs: drawMs);
        }

        private void ClearBucketCache()
        {
            _activeBuckets.Clear();
            _bucketMap.Clear();
            _bucketSlotsByStableId.Clear();
            _bucketOwner = BucketCacheOwner.None;
            _lastPersistentSnapshotRevision = -1;
        }

        private void RebuildBuckets(IEnumerable<StaticMeshAdapterBindingState> bindings)
        {
            _activeBuckets.Clear();
            _bucketMap.Clear();
            _bucketSlotsByStableId.Clear();
            _bucketSlotsByStableId.EnsureCapacity(_planner.ActiveBindings.Count);

            foreach (StaticMeshAdapterBindingState binding in bindings)
            {
                AddVisibleBinding(binding);
            }
        }

        private void ApplySyncOperations(IReadOnlyList<StaticMeshAdapterSyncOp> operations)
        {
            for (int i = 0; i < operations.Count; i++)
            {
                StaticMeshAdapterSyncOp operation = operations[i];
                switch (operation.Kind)
                {
                    case StaticMeshAdapterSyncOpKind.Create:
                        AddVisibleBinding(operation.Binding);
                        break;
                    case StaticMeshAdapterSyncOpKind.Update:
                        UpdateVisibleBinding(operation.Binding);
                        break;
                    case StaticMeshAdapterSyncOpKind.Remove:
                        RemoveVisibleBinding(operation.Binding.StableId);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unknown static mesh adapter sync op kind '{operation.Kind}'.");
                }
            }
        }

        private void AddVisibleBinding(in StaticMeshAdapterBindingState binding)
        {
            if (!binding.IsVisible)
            {
                return;
            }

            Bucket bucket = GetOrCreateBucket(binding.Lane);
            int itemIndex = bucket.Items.Count;
            bucket.Items.Add(binding.Item);
            bucket.MarkDirty();
            _bucketSlotsByStableId[binding.StableId] = new BucketSlot(bucket, itemIndex);
        }

        private void UpdateVisibleBinding(in StaticMeshAdapterBindingState binding)
        {
            if (!_bucketSlotsByStableId.TryGetValue(binding.StableId, out BucketSlot slot))
            {
                AddVisibleBinding(binding);
                return;
            }

            if (!binding.IsVisible)
            {
                RemoveVisibleBinding(binding.StableId);
                return;
            }

            if (!slot.Bucket.Lane.Equals(binding.Lane))
            {
                RemoveVisibleBinding(binding.StableId);
                AddVisibleBinding(binding);
                return;
            }

            slot.Bucket.Items[slot.ItemIndex] = binding.Item;
            slot.Bucket.MarkDirty();
        }

        private void RemoveVisibleBinding(int stableId)
        {
            if (!_bucketSlotsByStableId.TryGetValue(stableId, out BucketSlot slot))
            {
                return;
            }

            Bucket bucket = slot.Bucket;
            int lastIndex = bucket.Items.Count - 1;
            if (slot.ItemIndex != lastIndex)
            {
                PrimitiveDrawItem moved = bucket.Items[lastIndex];
                bucket.Items[slot.ItemIndex] = moved;
                _bucketSlotsByStableId[moved.StableId] = new BucketSlot(bucket, slot.ItemIndex);
            }

            bucket.Items.RemoveAt(lastIndex);
            bucket.MarkDirty();
            _bucketSlotsByStableId.Remove(stableId);
            if (!bucket.HasVisibleItems)
            {
                RemoveBucket(bucket);
            }
        }

        private Bucket GetOrCreateBucket(in StaticMeshLaneKey lane)
        {
            if (_bucketMap.TryGetValue(lane, out Bucket? bucket))
            {
                return bucket;
            }

            bucket = new Bucket(lane);
            _bucketMap.Add(lane, bucket);
            _activeBuckets.Add(bucket);
            return bucket;
        }

        private void RemoveBucket(Bucket bucket)
        {
            _bucketMap.Remove(bucket.Lane);
            int index = _activeBuckets.IndexOf(bucket);
            if (index >= 0)
            {
                _activeBuckets.RemoveAt(index);
            }
        }

        private void BuildCachedBenchmarkBuckets(in RaylibBenchmarkScene scene)
        {
            long rebuildStart = Stopwatch.GetTimestamp();
            _activeBuckets.Clear();
            _bucketMap.Clear();
            _bucketSlotsByStableId.Clear();
            ReadOnlySpan<RaylibBenchmarkInstance> instances = scene.Instances.Span;
            int activeCount = Math.Min(_benchmarkActiveInstanceCount, instances.Length);
            _bucketSlotsByStableId.EnsureCapacity(activeCount);
            _bucketOwner = BucketCacheOwner.BenchmarkScene;
            for (int i = 0; i < activeCount; i++)
            {
                ref readonly RaylibBenchmarkInstance instance = ref instances[i];
                PrimitiveDrawItem item = new PrimitiveDrawItem
                {
                    MeshAssetId = instance.MeshAssetId,
                    Position = instance.Position,
                    Rotation = instance.Rotation,
                    Scale = instance.Scale,
                    Color = ResolveMaterialColor(instance.MaterialId, instance.Color),
                    MaterialId = instance.MaterialId,
                    StableId = i + 1,
                    TemplateId = 0,
                    RenderPath = VisualRenderPath.InstancedStaticMesh,
                    Mobility = VisualMobility.Static,
                    Flags = VisualRuntimeFlags.None,
                    Visibility = VisualVisibility.Visible,
                };

                StaticMeshLaneKey lane = StaticMeshLaneKey.FromItem(item);
                if (!_bucketMap.TryGetValue(lane, out Bucket? bucket))
                {
                    bucket = new Bucket(lane);
                    _bucketMap.Add(lane, bucket);
                    _activeBuckets.Add(bucket);
                }

                bucket.Items.Add(item);
                bucket.MarkDirty();
                _bucketSlotsByStableId[item.StableId] = new BucketSlot(bucket, bucket.Items.Count - 1);
            }

            _lastBenchmarkBucketRebuildMs = (Stopwatch.GetTimestamp() - rebuildStart) * 1000.0 / Stopwatch.Frequency;
        }

        private Vector4 ResolveMaterialColor(int materialId, Vector4 fallback)
        {
            if (materialId > 0 && _materialColors.TryGetValue(materialId, out Vector4 color))
            {
                return color;
            }

            return fallback == Vector4.Zero ? _benchmarkScene.Palette.DefaultColor : fallback;
        }
    }
}

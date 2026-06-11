using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Ludots.Core.Presentation.AdapterSync;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed class RaylibIsmRenderBridge
    {
        public sealed class Bucket
        {
            public Bucket(in StaticMeshLaneKey lane)
            {
                Lane = lane;
            }

            public StaticMeshLaneKey Lane { get; }

            public List<PrimitiveDrawItem> Items { get; } = new();

            internal void Clear() => Items.Clear();
        }

        private readonly StaticMeshAdapterSyncPlanner _planner = new();
        private readonly Dictionary<StaticMeshLaneKey, Bucket> _bucketMap = new();
        private readonly List<Bucket> _activeBuckets = new();
        private readonly List<SurfaceDrawItem> _surfaceItems = new();
        private readonly Dictionary<int, Vector4> _materialColors = new();
        private RaylibBenchmarkScene _benchmarkScene;
        private RaylibBenchmarkStats _lastStats;

        public IReadOnlyDictionary<int, StaticMeshAdapterBindingState> ActiveBindings => _planner.ActiveBindings;

        public IReadOnlyList<Bucket> ActiveBuckets => _activeBuckets;

        public IReadOnlyList<SurfaceDrawItem> SurfaceItems => _surfaceItems;

        public StaticMeshAdapterSyncPlanner Planner => _planner;

        public RaylibBenchmarkStats LastStats => _lastStats;

        public void SyncPersistentLanes(PrimitiveDrawBuffer? snapshot)
        {
            CollectSurfaceItems(snapshot);
            _planner.Sync(snapshot);
            RebuildBucketsFromBindings(_planner.ActiveBindings.Values);
        }

        public void SetBenchmarkScene(in RaylibBenchmarkScene scene)
        {
            _benchmarkScene = scene;
            if (!scene.Enabled)
            {
                _lastStats = default;
            }
        }

        public RaylibBenchmarkScene GetBenchmarkScene() => _benchmarkScene;

        public RaylibBenchmarkStats BuildBenchmarkBuckets()
        {
            if (!_benchmarkScene.Enabled)
            {
                _activeBuckets.Clear();
                _bucketMap.Clear();
                _materialColors.Clear();
                _lastStats = default;
                return _lastStats;
            }

            long buildStart = Stopwatch.GetTimestamp();
            _materialColors.Clear();
            foreach (RaylibBenchmarkMaterialColor color in _benchmarkScene.Palette.Colors.Span)
            {
                _materialColors[color.MaterialId] = color.Color;
            }

            _activeBuckets.Clear();
            _bucketMap.Clear();

            ReadOnlySpan<RaylibBenchmarkInstance> instances = _benchmarkScene.Instances.Span;
            for (int i = 0; i < instances.Length; i++)
            {
                ref readonly RaylibBenchmarkInstance instance = ref instances[i];
                Vector4 color = ResolveMaterialColor(instance.MaterialId, instance.Color);
                var item = new PrimitiveDrawItem
                {
                    AssetKind = AssetKind.Mesh,
                    MeshAssetId = instance.MeshAssetId,
                    Position = instance.Position,
                    Rotation = instance.Rotation,
                    Scale = instance.Scale,
                    Color = color,
                    StableId = i + 1,
                    MaterialId = instance.MaterialId,
                    TemplateId = 0,
                    RenderPath = VisualRenderPath.InstancedStaticMesh,
                    Mobility = VisualMobility.Static,
                    Flags = VisualRuntimeFlags.Visible,
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
            }

            double buildMs = (Stopwatch.GetTimestamp() - buildStart) * 1000.0 / Stopwatch.Frequency;
            _lastStats = new RaylibBenchmarkStats(
                active: true,
                instanceCount: instances.Length,
                bucketCount: _activeBuckets.Count,
                visibleCount: instances.Length,
                cpuBuildMs: buildMs,
                cpuDrawMs: _lastStats.CpuDrawMs);
            return _lastStats;
        }

        private void CollectSurfaceItems(PrimitiveDrawBuffer? snapshot)
        {
            _surfaceItems.Clear();
            if (snapshot == null)
            {
                return;
            }

            ReadOnlySpan<PrimitiveDrawItem> span = snapshot.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PrimitiveDrawItem item = ref span[i];
                if (item.RenderPath == VisualRenderPath.Surface && item.AssetKind != AssetKind.Surface)
                {
                    throw new InvalidOperationException(
                        $"RaylibIsmRenderBridge received render path 'Surface' for non-Surface asset kind '{item.AssetKind}' stableId={item.StableId}.");
                }

                if (item.AssetKind != AssetKind.Surface)
                {
                    continue;
                }

                if (item.RenderPath != VisualRenderPath.Surface)
                {
                    throw new InvalidOperationException(
                        $"RaylibIsmRenderBridge received Surface visual stableId={item.StableId} on render path '{item.RenderPath}'.");
                }

                _surfaceItems.Add(new SurfaceDrawItem
                {
                    StableId = item.StableId,
                    MeshAssetId = item.MeshAssetId,
                    MaterialId = item.MaterialId,
                    SurfaceLayerKey = item.SurfaceLayerKey,
                    SortId = item.SortId,
                    Position = item.Position,
                    Rotation = item.Rotation,
                    Scale = item.Scale,
                    Visibility = item.Visibility,
                    MaterialCustomData = item.MaterialCustomData,
                });
            }
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

        private void RebuildBucketsFromBindings(IEnumerable<StaticMeshAdapterBindingState> bindings)
        {
            _activeBuckets.Clear();
            _bucketMap.Clear();

            foreach (StaticMeshAdapterBindingState binding in bindings)
            {
                if (!binding.IsVisible)
                {
                    continue;
                }

                PrimitiveDrawItem item = binding.Item;
                StaticMeshLaneKey lane = binding.Lane;
                if (!_bucketMap.TryGetValue(lane, out Bucket? bucket))
                {
                    bucket = new Bucket(lane);
                    _bucketMap.Add(lane, bucket);
                    _activeBuckets.Add(bucket);
                }

                bucket.Items.Add(item);
            }
        }

        private Vector4 ResolveMaterialColor(int materialId, Vector4 instanceColor)
        {
            if (materialId > 0 && _materialColors.TryGetValue(materialId, out Vector4 color))
            {
                return color;
            }

            if (materialId > 0)
            {
                throw new InvalidOperationException(
                    $"Raylib benchmark materialId '{materialId}' is not declared in the benchmark material palette.");
            }

            return instanceColor == Vector4.Zero ? _benchmarkScene.Palette.DefaultColor : instanceColor;
        }
    }
}

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
        public sealed class Bucket
        {
            public Bucket(in StaticMeshLaneKey lane)
            {
                Lane = lane;
            }

            public StaticMeshLaneKey Lane { get; }

            public List<PrimitiveDrawItem> Items { get; } = new();
        }

        private readonly StaticMeshAdapterSyncPlanner _planner = new();
        private readonly Dictionary<StaticMeshLaneKey, Bucket> _bucketMap = new();
        private readonly List<Bucket> _activeBuckets = new();
        private readonly Dictionary<int, Vector4> _materialColors = new();
        private RaylibBenchmarkScene _benchmarkScene;
        private RaylibBenchmarkStats _lastStats;
        private int _benchmarkActiveInstanceCount;
        private double _lastBenchmarkBucketRebuildMs;

        public StaticMeshAdapterSyncPlanner Planner => _planner;

        public IReadOnlyDictionary<int, StaticMeshAdapterBindingState> ActiveBindings => _planner.ActiveBindings;

        public IReadOnlyList<Bucket> ActiveBuckets => _activeBuckets;

        public RaylibBenchmarkStats LastStats => _lastStats;

        public void SyncPersistentLanes(PrimitiveDrawBuffer? snapshot)
        {
            _planner.Sync(snapshot);
            RebuildBuckets(_planner.ActiveBindings.Values);
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
                _activeBuckets.Clear();
                _bucketMap.Clear();
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
            _activeBuckets.Clear();
            _bucketMap.Clear();
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

        private void RebuildBuckets(IEnumerable<StaticMeshAdapterBindingState> bindings)
        {
            _activeBuckets.Clear();
            _bucketMap.Clear();

            foreach (StaticMeshAdapterBindingState binding in bindings)
            {
                if (!binding.IsVisible)
                {
                    continue;
                }

                if (!_bucketMap.TryGetValue(binding.Lane, out Bucket? bucket))
                {
                    bucket = new Bucket(binding.Lane);
                    _bucketMap.Add(binding.Lane, bucket);
                    _activeBuckets.Add(bucket);
                }

                bucket.Items.Add(binding.Item);
            }
        }

        private void BuildCachedBenchmarkBuckets(in RaylibBenchmarkScene scene)
        {
            long rebuildStart = Stopwatch.GetTimestamp();
            _activeBuckets.Clear();
            _bucketMap.Clear();
            ReadOnlySpan<RaylibBenchmarkInstance> instances = scene.Instances.Span;
            int activeCount = Math.Min(_benchmarkActiveInstanceCount, instances.Length);
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

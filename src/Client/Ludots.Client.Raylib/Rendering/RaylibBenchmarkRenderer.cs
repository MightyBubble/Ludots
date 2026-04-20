using System;
using System.Diagnostics;
using Ludots.Core.Presentation.Assets;
using Ludots.Platform.Abstractions;
using Raylib_cs;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed class RaylibBenchmarkRenderer : IRaylibBenchmarkRenderer
    {
        private readonly RaylibPrimitiveRenderer _primitiveRenderer;
        private readonly MeshAssetRegistry _meshes;

        public RaylibBenchmarkRenderer(RaylibPrimitiveRenderer primitiveRenderer, MeshAssetRegistry meshes)
        {
            _primitiveRenderer = primitiveRenderer ?? throw new ArgumentNullException(nameof(primitiveRenderer));
            _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        }

        public RaylibBenchmarkStats LastStats => _primitiveRenderer.IsmBridge.LastStats;

        public RaylibBenchmarkScene CurrentScene => _primitiveRenderer.IsmBridge.GetBenchmarkScene();

        public void SetScene(in RaylibBenchmarkScene scene)
        {
            _primitiveRenderer.IsmBridge.SetBenchmarkScene(scene);
        }

        public bool SetActiveInstanceCount(int count)
        {
            return _primitiveRenderer.IsmBridge.SetBenchmarkActiveInstanceCount(count);
        }

        public int GetActiveInstanceCount()
        {
            return _primitiveRenderer.IsmBridge.GetBenchmarkActiveInstanceCount();
        }

        public bool Draw(Camera3D fallbackCamera)
        {
            RaylibBenchmarkScene scene = _primitiveRenderer.IsmBridge.GetBenchmarkScene();
            if (!scene.Enabled)
            {
                return false;
            }

            RaylibBenchmarkStats buildStats = _primitiveRenderer.IsmBridge.BuildBenchmarkBuckets();
            Camera3D camera = fallbackCamera;
            camera.position = scene.Camera.Position;
            camera.target = scene.Camera.Target;
            if (scene.Camera.FovY > 0.01f)
            {
                camera.fovy = scene.Camera.FovY;
            }

            long drawStart = Stopwatch.GetTimestamp();
            int visibleCount = 0;
            _primitiveRenderer.ResetInstancedStats();
            foreach (RaylibIsmRenderBridge.Bucket bucket in _primitiveRenderer.IsmBridge.ActiveBuckets)
            {
                visibleCount += bucket.Items.Count;
                _primitiveRenderer.DrawInstancedBucket(bucket, _meshes);
            }

            double drawMs = (Stopwatch.GetTimestamp() - drawStart) * 1000.0 / Stopwatch.Frequency;
            _primitiveRenderer.IsmBridge.CompleteBenchmarkDraw(drawMs, visibleCount);
            return buildStats.Active;
        }
    }
}

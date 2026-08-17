using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using Ludots.Raylib.Render;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    [Category("benchmark")]
    public sealed class RaylibIsmBenchmarkShowcaseTests
    {
        private const string RendererServiceKey = "Platform.RaylibBenchmarkRenderer";
        private const string BenchmarkMapId = "raylib_ism_benchmark_showcase";
        private static readonly string[] BlacksmithMeshKeys =
        [
            "blacksmith.building.north.intact",
            "blacksmith.building.south.intact",
            "blacksmith.building.damaged",
            "blacksmith.building.ruined",
            "blacksmith.furnace",
            "blacksmith.worker.knight"
        ];

        [Test]
        public void MapLoad_SubmitsThirtyThousandInstancesIntoRaylibIsmBuckets()
        {
            string repoRoot = FindRepoRoot();
            var modPaths = RepoModPaths.ResolveExplicit(
                repoRoot,
                new[] { "LudotsCoreMod", "CoreInputMod", "RaylibPlatformMeshesMod", "RaylibIsmBenchmarkShowcaseMod" });

            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, Path.Combine(repoRoot, "assets"));

            var renderer = new CapturingRaylibBenchmarkRenderer();
            engine.SetService(new ServiceKey<IRaylibBenchmarkRenderer>(RendererServiceKey), renderer);

            engine.Start();
            engine.LoadMap(BenchmarkMapId);

            Assert.That(renderer.Scene.Enabled, Is.True, "Benchmark showcase should enable the platform benchmark scene on map load.");
            Assert.That(renderer.Scene.Instances.Length, Is.EqualTo(300_000), "This showcase should preload the full 300K instance pool for the slider-driven stress test.");
            Assert.That(renderer.Scene.InitialActiveInstanceCount, Is.EqualTo(30_000), "Benchmark should start from the 30K active baseline.");

            MeshAssetRegistry meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
                ?? throw new InvalidOperationException("PresentationMeshAssetRegistry missing.");
            int[] expectedMeshIds = BlacksmithMeshKeys
                .Select(key => meshes.GetId(key))
                .ToArray();
            Assert.That(expectedMeshIds.All(id => id > 0), Is.True, "Benchmark must use registered blacksmith third-party model assets.");

            ReadOnlySpan<RaylibBenchmarkInstance> instances = renderer.Scene.Instances.Span;
            int[] actualMeshIds = instances
                .ToArray()
                .Select(instance => instance.MeshAssetId)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();
            Assert.That(actualMeshIds, Is.EqualTo(expectedMeshIds.OrderBy(id => id).ToArray()));
            foreach (int meshId in expectedMeshIds)
            {
                Assert.That(meshes.TryGetDescriptor(meshId, out var descriptor), Is.True);
                Assert.That(descriptor.Type, Is.EqualTo(MeshAssetType.Model));
            }

            RaylibBenchmarkStats stats = renderer.BuildStats;
            Assert.That(stats.Active, Is.True);
            Assert.That(stats.InstanceCount, Is.EqualTo(30_000));
            Assert.That(stats.VisibleCount, Is.EqualTo(30_000));
            Assert.That(stats.BucketCount, Is.EqualTo(6), "Benchmark should create one ISM bucket per blacksmith model/material lane.");
            Assert.That(renderer.RenderPaths, Is.EquivalentTo(new[] { VisualRenderPath.InstancedStaticMesh }));
            Assert.That(renderer.Mobilities, Is.EquivalentTo(new[] { VisualMobility.Static }));
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current)!;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }

        private sealed class CapturingRaylibBenchmarkRenderer : IRaylibBenchmarkRenderer
        {
            private readonly RaylibIsmRenderBridge _bridge = new();

            public RaylibBenchmarkScene Scene { get; private set; }

            public RaylibBenchmarkStats BuildStats { get; private set; }

            public RaylibBenchmarkStats LastStats => BuildStats;

            public IReadOnlyCollection<VisualRenderPath> RenderPaths { get; private set; } = Array.Empty<VisualRenderPath>();

            public IReadOnlyCollection<VisualMobility> Mobilities { get; private set; } = Array.Empty<VisualMobility>();

            public void SetScene(in RaylibBenchmarkScene scene)
            {
                Scene = scene;
                _bridge.SetBenchmarkScene(scene);
                BuildStats = _bridge.BuildBenchmarkBuckets();
                RenderPaths = _bridge.ActiveBuckets
                    .SelectMany(bucket => bucket.Items)
                    .Select(item => item.RenderPath)
                    .Distinct()
                    .ToArray();
                Mobilities = _bridge.ActiveBuckets
                    .SelectMany(bucket => bucket.Items)
                    .Select(item => item.Mobility)
                    .Distinct()
                    .ToArray();
            }

            public bool SetActiveInstanceCount(int count)
            {
                bool changed = _bridge.SetBenchmarkActiveInstanceCount(count);
                BuildStats = _bridge.BuildBenchmarkBuckets();
                return changed;
            }

            public int GetActiveInstanceCount()
            {
                return _bridge.GetBenchmarkActiveInstanceCount();
            }
        }
    }
}

using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using System.Collections.Generic;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PrefabFinalizationAndVisualHeightmapTests
    {
        [Test]
        public void PrefabFinalizationPipeline_ExpandsNestedPrefabsIntoLeafRecords()
        {
            var meshes = new MeshAssetRegistry();
            int cubeId = meshes.GetId(WellKnownMeshKeys.Cube);
            int sphereId = meshes.GetId(WellKnownMeshKeys.Sphere);

            int childPrefabId = meshes.Register(
                "prefab.child",
                MeshAssetDescriptor.Prefab(
                    0,
                    new PrefabPart
                    {
                        MeshAssetId = sphereId,
                        LocalPosition = new Vector3(0f, 1f, 2f),
                        LocalRotation = Quaternion.Identity,
                        LocalScale = new Vector3(2f, 2f, 2f),
                        ColorTint = new Vector4(0.5f, 1f, 0.5f, 1f),
                    }));

            int rootPrefabId = meshes.Register(
                "prefab.root",
                MeshAssetDescriptor.Prefab(
                    0,
                    new PrefabPart
                    {
                        MeshAssetId = cubeId,
                        LocalPosition = new Vector3(1f, 0f, 0f),
                        LocalRotation = Quaternion.Identity,
                        LocalScale = new Vector3(1f, 2f, 3f),
                        ColorTint = new Vector4(0.25f, 0.5f, 1f, 1f),
                    },
                    new PrefabPart
                    {
                        MeshAssetId = childPrefabId,
                        LocalPosition = new Vector3(0f, 5f, 0f),
                        LocalRotation = Quaternion.Identity,
                        LocalScale = Vector3.One,
                        ColorTint = new Vector4(1f, 0.5f, 1f, 1f),
                    }));

            var output = new PrefabFinalizedLeafBuffer();
            PrefabFinalizationPipeline.FinalizeLeaves(
                meshes,
                rootPrefabId,
                stableId: 17,
                position: new Vector3(10f, 20f, 30f),
                rotation: Quaternion.Identity,
                scale: Vector3.One,
                color: Vector4.One,
                output);

            Assert.That(output.Count, Is.EqualTo(2));

            var leaves = output.GetSpan();
            Assert.That(leaves[0].MeshAssetId, Is.EqualTo(cubeId));
            Assert.That(leaves[0].Descriptor.Type, Is.EqualTo(MeshAssetType.Primitive));
            Assert.That(leaves[0].Position, Is.EqualTo(new Vector3(11f, 20f, 30f)));
            Assert.That(leaves[0].Scale, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(leaves[0].Color, Is.EqualTo(new Vector4(0.25f, 0.5f, 1f, 1f)));
            Assert.That(
                leaves[0].StableId,
                Is.EqualTo(PrefabTransformUtility.BuildChildStableId(17, depth: 0, childIndex: 0, meshAssetId: cubeId)));

            int childRootStableId = PrefabTransformUtility.BuildChildStableId(17, depth: 0, childIndex: 1, meshAssetId: childPrefabId);
            int childLeafStableId = PrefabTransformUtility.BuildChildStableId(childRootStableId, depth: 1, childIndex: 0, meshAssetId: sphereId);
            Assert.That(leaves[1].MeshAssetId, Is.EqualTo(sphereId));
            Assert.That(leaves[1].Descriptor.Type, Is.EqualTo(MeshAssetType.Primitive));
            Assert.That(leaves[1].Position, Is.EqualTo(new Vector3(10f, 26f, 32f)));
            Assert.That(leaves[1].Scale, Is.EqualTo(new Vector3(2f, 2f, 2f)));
            Assert.That(leaves[1].Color, Is.EqualTo(new Vector4(0.5f, 0.5f, 0.5f, 1f)));
            Assert.That(leaves[1].StableId, Is.EqualTo(childLeafStableId));
        }

        [Test]
        public void VisualHeightmapRuntime_SupportsBatchSamplingAndRaycast()
        {
            var runtime = CreateRuntime();

            Assert.That(runtime.TrySampleHeightCm(500f, 500f, out float heightCm), Is.True);
            Assert.That(heightCm, Is.EqualTo(100f).Within(0.001f));

            float[] xs = { 0f, 500f, 1000f };
            float[] ys = { 0f, 500f, 1000f };
            float[] heights = new float[3];
            Assert.That(runtime.SampleHeightsCm(xs, ys, heights), Is.True);
            Assert.That(heights[0], Is.EqualTo(0f).Within(0.001f));
            Assert.That(heights[1], Is.EqualTo(100f).Within(0.001f));
            Assert.That(heights[2], Is.EqualTo(200f).Within(0.001f));

            var ray = new ScreenRay(new Vector3(5f, 10f, 5f), -Vector3.UnitY);
            Assert.That(runtime.TryRaycastGround(in ray, out VisualGroundHit hit), Is.True);
            Assert.That(hit.WorldXCm, Is.EqualTo(500f).Within(0.001f));
            Assert.That(hit.WorldYCm, Is.EqualTo(500f).Within(0.001f));
            Assert.That(hit.HeightCm, Is.EqualTo(100f).Within(0.001f));

            float[] ox = { 5f, 15f };
            float[] oy = { 10f, 10f };
            float[] oz = { 5f, 15f };
            float[] dx = { 0f, 0f };
            float[] dy = { -1f, -1f };
            float[] dz = { 0f, 0f };
            var hits = new VisualGroundHit[2];
            byte[] hitMask = new byte[2];
            Assert.That(runtime.RaycastGroundBatch(ox, oy, oz, dx, dy, dz, hits, hitMask), Is.True);
            Assert.That(hitMask[0], Is.EqualTo((byte)1));
            Assert.That(hitMask[1], Is.EqualTo((byte)0));
        }

        [Test]
        public void TerrainHeightSyncSystem_PrefersVisualHeightmap_AndGroundRaycastUsesSameTruth()
        {
            using var world = World.Create();
            world.Create(
                new PresentationFrameState
                {
                    InterpolationAlpha = 0.25f,
                    Enabled = true,
                },
                new PresentationFrameStateTag());

            Entity entity = world.Create(
                WorldPositionCm.FromCm(400, 800),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(0, 400) },
                new VisualTransform
                {
                    Position = new Vector3(1f, 0f, 5f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                });

            var heightmap = CreateRuntime();
            var projector = new CountingGroundProjector();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.VisualHeightmap.Name] = heightmap,
                [CoreServiceKeys.VisualGroundProjector.Name] = projector,
            };

            using var system = new TerrainHeightSyncSystem(world, globals);
            system.Update(0.016f);

            Assert.That(projector.InvocationCount, Is.EqualTo(0));

            VisualTransform visual = world.Get<VisualTransform>(entity);
            Assert.That(visual.Position.Y, Is.EqualTo(60f * 0.01f).Within(0.001f));

            var ray = new ScreenRay(new Vector3(2f, 10f, 6f), -Vector3.UnitY);
            Assert.That(GroundRaycastUtil.TryGetGroundWorldCm(in ray, heightmap, out var worldCm), Is.True);
            Assert.That(worldCm, Is.EqualTo(new WorldCmInt2(200, 600)));
        }

        private static VisualHeightmapRuntime CreateRuntime()
        {
            return new VisualHeightmapRuntime(
                VisualHeightmapAsset.CreateSingleLayer(
                    new WorldAabbCm(0, 0, 1000, 1000),
                    sampleColumns: 2,
                    sampleRows: 2,
                    new short[]
                    {
                        0, 100,
                        100, 200,
                    }));
        }

        private sealed class CountingGroundProjector : IVisualGroundProjector
        {
            public int InvocationCount { get; private set; }

            public bool TryProjectHeights(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm)
            {
                InvocationCount++;
                return false;
            }
        }
    }
}

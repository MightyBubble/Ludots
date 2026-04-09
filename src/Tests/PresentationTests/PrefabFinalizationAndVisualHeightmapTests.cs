using System;
using System.IO;
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
        public void PrefabFinalizationPipeline_GroundsPerPartAgainstVisualHeightmap_AndAlignsToGroundNormal()
        {
            var meshes = new MeshAssetRegistry();
            int cubeId = meshes.GetId(WellKnownMeshKeys.Cube);
            int sphereId = meshes.GetId(WellKnownMeshKeys.Sphere);
            int groundedPrefabId = meshes.Register(
                "prefab.grounded",
                MeshAssetDescriptor.Prefab(
                    0,
                    new PrefabPart
                    {
                        MeshAssetId = cubeId,
                        LocalPosition = new Vector3(1f, 99f, 1f),
                        LocalRotation = Quaternion.Identity,
                        LocalScale = Vector3.One,
                        ColorTint = Vector4.One,
                        Grounding = new PrefabPartGrounding(
                            PrefabPartGroundingMode.VisualHeightmap,
                            verticalOffsetMeters: 0.25f),
                    },
                    new PrefabPart
                    {
                        MeshAssetId = sphereId,
                        LocalPosition = new Vector3(5f, -12f, 5f),
                        LocalRotation = Quaternion.Identity,
                        LocalScale = Vector3.One,
                        ColorTint = Vector4.One,
                        Grounding = new PrefabPartGrounding(
                            PrefabPartGroundingMode.VisualHeightmap,
                            alignToGroundNormal: true),
                    }));

            var output = new PrefabFinalizedLeafBuffer();
            var runtime = CreateRuntime();
            var context = new PrefabFinalizationContext(runtime);

            PrefabFinalizationPipeline.FinalizeLeaves(
                meshes,
                groundedPrefabId,
                stableId: 100,
                position: Vector3.Zero,
                rotation: Quaternion.Identity,
                scale: Vector3.One,
                color: Vector4.One,
                context,
                output);

            Assert.That(output.Count, Is.EqualTo(2));

            var leaves = output.GetSpan();
            Assert.That(leaves[0].Position.X, Is.EqualTo(1f).Within(0.001f));
            Assert.That(leaves[0].Position.Z, Is.EqualTo(1f).Within(0.001f));
            Assert.That(leaves[0].Position.Y, Is.EqualTo(0.45f).Within(0.001f), "Grounded part must use heightmap truth plus vertical offset.");

            var groundRay = new ScreenRay(new Vector3(5f, 10f, 5f), -Vector3.UnitY);
            Assert.That(runtime.TryRaycastGround(in groundRay, out VisualGroundHit hit), Is.True);
            Vector3 alignedUp = Vector3.Transform(Vector3.UnitY, leaves[1].Rotation);
            Assert.That(leaves[1].Position.Y, Is.EqualTo(1f).Within(0.001f));
            Assert.That(alignedUp.X, Is.EqualTo(hit.Normal.X).Within(0.001f));
            Assert.That(alignedUp.Y, Is.EqualTo(hit.Normal.Y).Within(0.001f));
            Assert.That(alignedUp.Z, Is.EqualTo(hit.Normal.Z).Within(0.001f));
        }

        [Test]
        public void PrefabFinalizationPipeline_WhenGroundingRequestsVisualHeightmapWithoutTruth_ThrowsExplicitly()
        {
            var meshes = new MeshAssetRegistry();
            int cubeId = meshes.GetId(WellKnownMeshKeys.Cube);
            int groundedPrefabId = meshes.Register(
                "prefab.requires_heightmap",
                MeshAssetDescriptor.Prefab(
                    0,
                    new PrefabPart
                    {
                        MeshAssetId = cubeId,
                        LocalPosition = Vector3.Zero,
                        LocalRotation = Quaternion.Identity,
                        LocalScale = Vector3.One,
                        ColorTint = Vector4.One,
                        Grounding = new PrefabPartGrounding(PrefabPartGroundingMode.VisualHeightmap),
                    }));

            var output = new PrefabFinalizedLeafBuffer();

            Assert.Throws<InvalidOperationException>(() =>
                PrefabFinalizationPipeline.FinalizeLeaves(
                    meshes,
                    groundedPrefabId,
                    stableId: 12,
                    position: Vector3.Zero,
                    rotation: Quaternion.Identity,
                    scale: Vector3.One,
                    color: Vector4.One,
                    output));
        }

        [Test]
        public void VisualHeightmapBinary_RoundTripsAssetMetadataAndSamples()
        {
            var asset = new VisualHeightmapAsset(
                new WorldAabbCm(-500, 250, 1500, 2000),
                sampleColumns: 3,
                sampleRows: 2,
                new short[]
                {
                    10, 20, 30,
                    40, 50, 60,
                    70, 80, 90,
                    100, 110, 120,
                },
                new[]
                {
                    new VisualHeightmapLayerDefinition(10, "base", sampleOffset: 0, sampleCount: 6),
                    new VisualHeightmapLayerDefinition(11, "detail", sampleOffset: 6, sampleCount: 6),
                },
                VisualHeightmapStorageLayout.RowMajorInt16Centimeters,
                defaultLayerIndex: 1);

            using var stream = new MemoryStream();
            VisualHeightmapBinary.Write(stream, asset);
            stream.Position = 0;

            VisualHeightmapAsset roundTripped = VisualHeightmapBinary.Read(stream);

            Assert.That(roundTripped.Bounds, Is.EqualTo(asset.Bounds));
            Assert.That(roundTripped.SampleColumns, Is.EqualTo(asset.SampleColumns));
            Assert.That(roundTripped.SampleRows, Is.EqualTo(asset.SampleRows));
            Assert.That(roundTripped.StorageLayout, Is.EqualTo(asset.StorageLayout));
            Assert.That(roundTripped.DefaultLayerIndex, Is.EqualTo(asset.DefaultLayerIndex));
            Assert.That(roundTripped.Layers.Length, Is.EqualTo(2));
            Assert.That(roundTripped.Layers[0].Name, Is.EqualTo("base"));
            Assert.That(roundTripped.Layers[1].Name, Is.EqualTo("detail"));
            Assert.That(roundTripped.HeightSamplesCm, Is.EqualTo(asset.HeightSamplesCm));
        }

        [Test]
        public void VisualHeightmapBinary_RoundTripsScaledUInt16ImportMetadata()
        {
            var asset = new VisualHeightmapAsset(
                new WorldAabbCm(0, 0, 2000, 1000),
                sampleColumns: 2,
                sampleRows: 2,
                new ushort[] { 0, 100, 200, 300 },
                new[]
                {
                    new VisualHeightmapLayerDefinition(20, "imported", sampleOffset: 0, sampleCount: 4),
                },
                new VisualHeightSampleScale(OffsetCm: 50, UnitsPerSampleNumeratorCm: 2, UnitsPerSampleDenominator: 1),
                VisualHeightmapStorageLayout.RowMajorUInt16Scaled,
                defaultLayerIndex: 0,
                interpolationMode: VisualHeightmapInterpolationMode.TriangleHeightfield);

            using var stream = new MemoryStream();
            VisualHeightmapBinary.Write(stream, asset);
            stream.Position = 0;

            VisualHeightmapAsset roundTripped = VisualHeightmapBinary.Read(stream);

            Assert.That(roundTripped.StorageLayout, Is.EqualTo(VisualHeightmapStorageLayout.RowMajorUInt16Scaled));
            Assert.That(roundTripped.InterpolationMode, Is.EqualTo(VisualHeightmapInterpolationMode.TriangleHeightfield));
            Assert.That(roundTripped.SampleScale, Is.EqualTo(asset.SampleScale));
            Assert.That(roundTripped.HeightSamplesRaw, Is.EqualTo(asset.HeightSamplesRaw));
            Assert.That(roundTripped.UsesRawUInt16Samples, Is.True);
        }

        [Test]
        public void VisualHeightmapRuntime_SupportsBatchSamplingAndSoaRaycast()
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
            var hitWorldX = new float[2];
            var hitWorldY = new float[2];
            var hitHeight = new float[2];
            var hitDistance = new float[2];
            var hitNormalX = new float[2];
            var hitNormalY = new float[2];
            var hitNormalZ = new float[2];
            var hitLayer = new int[2];
            byte[] hitMask = new byte[2];
            Assert.That(
                runtime.RaycastGroundBatch(
                    ox,
                    oy,
                    oz,
                    dx,
                    dy,
                    dz,
                    hitWorldX,
                    hitWorldY,
                    hitHeight,
                    hitDistance,
                    hitNormalX,
                    hitNormalY,
                    hitNormalZ,
                    hitLayer,
                    hitMask),
                Is.True);
            Assert.That(hitMask[0], Is.EqualTo((byte)1));
            Assert.That(hitWorldX[0], Is.EqualTo(500f).Within(0.001f));
            Assert.That(hitWorldY[0], Is.EqualTo(500f).Within(0.001f));
            Assert.That(hitHeight[0], Is.EqualTo(100f).Within(0.001f));
            Assert.That(hitDistance[0], Is.EqualTo(9f).Within(0.001f));
            Assert.That(hitNormalY[0], Is.GreaterThan(0.9f));
            Assert.That(hitLayer[0], Is.EqualTo(0));
            Assert.That(hitMask[1], Is.EqualTo((byte)0));
            Assert.That(float.IsNaN(hitWorldX[1]), Is.True);
            Assert.That(hitLayer[1], Is.EqualTo(-1));
        }

        [Test]
        public void VisualHeightmapRuntime_TriangleInterpolation_UsesExactTriangleTruth_ForSamplingAndRaycast()
        {
            var runtime = new VisualHeightmapRuntime(
                VisualHeightmapAsset.CreateSingleLayer(
                    new WorldAabbCm(0, 0, 100, 100),
                    sampleColumns: 2,
                    sampleRows: 2,
                    new short[]
                    {
                        0, 100,
                        0, 0,
                    },
                    interpolationMode: VisualHeightmapInterpolationMode.TriangleHeightfield));

            Assert.That(runtime.TrySampleHeightCm(75f, 75f, out float heightCm), Is.True);
            Assert.That(heightCm, Is.EqualTo(25f).Within(0.001f));

            var ray = new ScreenRay(new Vector3(0.75f, 1f, 0.75f), -Vector3.UnitY);
            Assert.That(runtime.TryRaycastGround(in ray, out VisualGroundHit hit), Is.True);
            Assert.That(hit.WorldXCm, Is.EqualTo(75f).Within(0.001f));
            Assert.That(hit.WorldYCm, Is.EqualTo(75f).Within(0.001f));
            Assert.That(hit.HeightCm, Is.EqualTo(25f).Within(0.001f));
            Assert.That(hit.DistanceMeters, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(hit.Normal.Y, Is.GreaterThan(0.6f));
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

        [Test]
        public void ChunkedVisualHeightmapRuntime_MatchesScalarAndBatchAcrossSharedSeam()
        {
            var runtime = CreateChunkedRuntime(includeRightChunk: true);

            Assert.That(runtime.TrySampleHeightCm(25f, 50f, out float leftHeightCm), Is.True);
            Assert.That(leftHeightCm, Is.EqualTo(25f).Within(0.001f));
            Assert.That(runtime.TrySampleHeightCm(100f, 50f, out float seamHeightCm), Is.True);
            Assert.That(seamHeightCm, Is.EqualTo(100f).Within(0.001f));
            Assert.That(runtime.TrySampleHeightCm(175f, 50f, out float rightHeightCm), Is.True);
            Assert.That(rightHeightCm, Is.EqualTo(175f).Within(0.001f));

            float[] xs = { 25f, 100f, 175f };
            float[] ys = { 50f, 50f, 50f };
            float[] heights = new float[3];
            Assert.That(runtime.SampleHeightsCm(xs, ys, heights), Is.True);
            Assert.That(heights[0], Is.EqualTo(25f).Within(0.001f));
            Assert.That(heights[1], Is.EqualTo(100f).Within(0.001f));
            Assert.That(heights[2], Is.EqualTo(175f).Within(0.001f));

            var seamRay = new ScreenRay(new Vector3(1f, 3f, 0.5f), -Vector3.UnitY);
            Assert.That(runtime.TryRaycastGround(in seamRay, out VisualGroundHit seamHit), Is.True);
            Assert.That(seamHit.WorldXCm, Is.EqualTo(100f).Within(0.001f));
            Assert.That(seamHit.WorldYCm, Is.EqualTo(50f).Within(0.001f));
            Assert.That(seamHit.HeightCm, Is.EqualTo(100f).Within(0.001f));
            Assert.That(seamHit.DistanceMeters, Is.EqualTo(2f).Within(0.001f));

            float[] ox = { 0.25f, 1.75f };
            float[] oy = { 3f, 3f };
            float[] oz = { 0.5f, 0.5f };
            float[] dx = { 0f, 0f };
            float[] dy = { -1f, -1f };
            float[] dz = { 0f, 0f };
            var hitWorldX = new float[2];
            var hitWorldY = new float[2];
            var hitHeight = new float[2];
            var hitDistance = new float[2];
            var hitNormalX = new float[2];
            var hitNormalY = new float[2];
            var hitNormalZ = new float[2];
            var hitLayer = new int[2];
            byte[] hitMask = new byte[2];

            Assert.That(
                runtime.RaycastGroundBatch(
                    ox,
                    oy,
                    oz,
                    dx,
                    dy,
                    dz,
                    hitWorldX,
                    hitWorldY,
                    hitHeight,
                    hitDistance,
                    hitNormalX,
                    hitNormalY,
                    hitNormalZ,
                    hitLayer,
                    hitMask),
                Is.True);

            Assert.That(hitMask[0], Is.EqualTo((byte)1));
            Assert.That(hitMask[1], Is.EqualTo((byte)1));
            Assert.That(hitWorldX[0], Is.EqualTo(25f).Within(0.001f));
            Assert.That(hitHeight[0], Is.EqualTo(25f).Within(0.001f));
            Assert.That(hitDistance[0], Is.EqualTo(2.75f).Within(0.001f));
            Assert.That(hitLayer[0], Is.EqualTo(0));
            Assert.That(hitNormalY[0], Is.GreaterThan(0.7f));
            Assert.That(hitWorldX[1], Is.EqualTo(175f).Within(0.001f));
            Assert.That(hitHeight[1], Is.EqualTo(175f).Within(0.001f));
            Assert.That(hitDistance[1], Is.EqualTo(1.25f).Within(0.001f));
            Assert.That(hitLayer[1], Is.EqualTo(0));
            Assert.That(hitNormalY[1], Is.GreaterThan(0.7f));
        }

        [Test]
        public void ChunkedVisualHeightmapRuntime_MissingChunksFailScalarQueries_AndBatchMarksMisses()
        {
            var runtime = CreateChunkedRuntime(includeRightChunk: false);

            Assert.That(runtime.TrySampleHeightCm(25f, 50f, out float leftHeightCm), Is.True);
            Assert.That(leftHeightCm, Is.EqualTo(25f).Within(0.001f));
            Assert.That(runtime.TrySampleHeightCm(150f, 50f, out _), Is.False);

            var missingRay = new ScreenRay(new Vector3(1.5f, 3f, 0.5f), -Vector3.UnitY);
            Assert.That(runtime.TryRaycastGround(in missingRay, out _), Is.False);

            float[] xs = { 25f, 150f };
            float[] ys = { 50f, 50f };
            float[] heights = new float[2];
            Assert.That(runtime.SampleHeightsCm(xs, ys, heights), Is.True);
            Assert.That(heights[0], Is.EqualTo(25f).Within(0.001f));
            Assert.That(float.IsNaN(heights[1]), Is.True);

            float[] ox = { 0.25f, 1.5f };
            float[] oy = { 3f, 3f };
            float[] oz = { 0.5f, 0.5f };
            float[] dx = { 0f, 0f };
            float[] dy = { -1f, -1f };
            float[] dz = { 0f, 0f };
            var hitWorldX = new float[2];
            var hitWorldY = new float[2];
            var hitHeight = new float[2];
            var hitDistance = new float[2];
            var hitNormalX = new float[2];
            var hitNormalY = new float[2];
            var hitNormalZ = new float[2];
            var hitLayer = new int[2];
            byte[] hitMask = new byte[2];

            Assert.That(
                runtime.RaycastGroundBatch(
                    ox,
                    oy,
                    oz,
                    dx,
                    dy,
                    dz,
                    hitWorldX,
                    hitWorldY,
                    hitHeight,
                    hitDistance,
                    hitNormalX,
                    hitNormalY,
                    hitNormalZ,
                    hitLayer,
                    hitMask),
                Is.True);

            Assert.That(hitMask[0], Is.EqualTo((byte)1));
            Assert.That(hitMask[1], Is.EqualTo((byte)0));
            Assert.That(hitHeight[0], Is.EqualTo(25f).Within(0.001f));
            Assert.That(float.IsNaN(hitHeight[1]), Is.True);
            Assert.That(hitLayer[1], Is.EqualTo(-1));
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

        private static ChunkedVisualHeightmapRuntime CreateChunkedRuntime(bool includeRightChunk)
        {
            var descriptor = ChunkedVisualHeightmapDescriptor.CreateSingleLayer(
                new WorldAabbCm(0, 0, 200, 100),
                chunkColumns: 2,
                chunkRows: 1,
                samplesPerChunkColumn: 3,
                samplesPerChunkRow: 2);
            var store = new ChunkedVisualHeightmapStore(descriptor);

            store.SetChunk(new ChunkedVisualHeightmapChunk(
                chunkX: 0,
                chunkY: 0,
                new short[]
                {
                    0, 50, 100,
                    0, 50, 100,
                }));

            if (includeRightChunk)
            {
                store.SetChunk(new ChunkedVisualHeightmapChunk(
                    chunkX: 1,
                    chunkY: 0,
                    new short[]
                    {
                        100, 150, 200,
                        100, 150, 200,
                    }));
            }

            return new ChunkedVisualHeightmapRuntime(descriptor, store);
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

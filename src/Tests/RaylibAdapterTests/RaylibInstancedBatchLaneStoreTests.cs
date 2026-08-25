using System;
using System.Numerics;
using Ludots.Adapter.Raylib.Rendering;
using Ludots.Core.Presentation.Instancing;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter
{
    [TestFixture]
    public sealed class RaylibInstancedBatchLaneStoreTests
    {
        [Test]
        public void ApplyRequests_AccumulatesProgressiveChunksUntilFinalChunk()
        {
            InstancedBatchTransform[] transforms = BuildGridTransforms(count: 10);
            InstancedBatchAssetRegistry registry = BuildRegistry("demo.batch", VisualRenderPath.InstancedStaticMesh, transforms);
            InstancedBatchRequestBuffer requests = new();
            var store = new RaylibInstancedBatchLaneStore();
            InstancedBatchRequest chunk = BuildCreateRequest(registry, presenterStableId: 7, start: 0, count: 4, finalChunk: false);

            requests.Add(chunk);
            store.ApplyRequests(requests.GetSpan(), registry);
            requests.Clear();

            Assert.That(store.ResidentLaneCount, Is.EqualTo(1));
            RaylibInstancedBatchLane partial = store.GetResidentLane(0);
            Assert.That(partial.Count, Is.EqualTo(4));
            int revisionAfterFirstChunk = partial.Revision;

            requests.Add(BuildCreateRequest(registry, presenterStableId: 7, start: 4, count: 6, finalChunk: true));
            store.ApplyRequests(requests.GetSpan(), registry);

            Assert.That(store.ResidentLaneCount, Is.EqualTo(1));
            RaylibInstancedBatchLane complete = store.GetResidentLane(0);
            Assert.That(complete.Count, Is.EqualTo(10));
            Assert.That(complete.Revision, Is.GreaterThan(revisionAfterFirstChunk));
            Assert.That(complete.MeshAssetId, Is.EqualTo(chunk.MeshAssetId));
            Assert.That(complete.MaterialAssetId, Is.EqualTo(chunk.MaterialAssetId));
            Assert.That(complete.RenderPath, Is.EqualTo(VisualRenderPath.InstancedStaticMesh));
            Assert.That(complete.Visible, Is.True);
        }

        [Test]
        public void ApplyRequests_ConvertsPositionCmToVisualMeterMatrices()
        {
            InstancedBatchTransform[] transforms =
            {
                new()
                {
                    PositionCm = new Vector3(200f, 50f, -300f),
                    Rotation = Quaternion.Identity,
                    Scale = new Vector3(2f, 2f, 2f),
                },
            };
            InstancedBatchAssetRegistry registry = BuildRegistry("demo.batch", VisualRenderPath.InstancedStaticMesh, transforms);
            InstancedBatchRequestBuffer requests = new();
            var store = new RaylibInstancedBatchLaneStore();

            requests.Add(BuildCreateRequest(registry, presenterStableId: 1, start: 0, count: 1, finalChunk: true));
            store.ApplyRequests(requests.GetSpan(), registry);

            Matrix4x4 expected = Matrix4x4.CreateScale(2f) * Matrix4x4.CreateTranslation(new Vector3(2f, 0.5f, -3f));
            Matrix4x4 actual = store.GetResidentLane(0).Matrices[0];
            AssertMatrixNear(actual, expected);
        }

        [Test]
        public void ApplyRequests_RemoveIsIdempotentForMissingAndExistingLanes()
        {
            InstancedBatchAssetRegistry registry = BuildRegistry("demo.batch", VisualRenderPath.InstancedStaticMesh, BuildGridTransforms(count: 4));
            InstancedBatchRequestBuffer requests = new();
            var store = new RaylibInstancedBatchLaneStore();
            InstancedBatchRequest create = BuildCreateRequest(registry, presenterStableId: 9, start: 0, count: 4, finalChunk: true);
            InstancedBatchRequest remove = BuildRemoveRequest(registry, presenterStableId: 9);

            requests.Add(remove);
            Assert.DoesNotThrow(() => store.ApplyRequests(requests.GetSpan(), registry));
            Assert.That(store.ResidentLaneCount, Is.EqualTo(0));
            requests.Clear();

            requests.Add(create);
            store.ApplyRequests(requests.GetSpan(), registry);
            requests.Clear();
            Assert.That(store.ResidentLaneCount, Is.EqualTo(1));

            requests.Add(remove);
            store.ApplyRequests(requests.GetSpan(), registry);
            requests.Clear();
            Assert.That(store.ResidentLaneCount, Is.EqualTo(0));

            requests.Add(remove);
            Assert.DoesNotThrow(() => store.ApplyRequests(requests.GetSpan(), registry));
            Assert.That(store.ResidentLaneCount, Is.EqualTo(0));
        }

        [Test]
        public void ApplyRequests_ThrowsWhenChunkExceedsDeclaredCapacity()
        {
            InstancedBatchAssetRegistry registry = BuildRegistry("demo.batch", VisualRenderPath.InstancedStaticMesh, BuildGridTransforms(count: 4));
            InstancedBatchRequestBuffer requests = new();
            var store = new RaylibInstancedBatchLaneStore();

            requests.Add(BuildCreateRequest(registry, presenterStableId: 3, start: 2, count: 3, finalChunk: false));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => store.ApplyRequests(requests.GetSpan(), registry))!;
            Assert.That(ex.Message, Does.Contain("exceeds the declared capacity 4"));
        }

        [Test]
        public void ApplyRequests_ResetsLaneWhenDeclaredCapacityShrinks()
        {
            const string batchKey = "demo.batch";
            InstancedBatchAssetRegistry registry = BuildRegistry(batchKey, VisualRenderPath.InstancedStaticMesh, BuildGridTransforms(count: 8));
            InstancedBatchRequestBuffer requests = new();
            var store = new RaylibInstancedBatchLaneStore();

            requests.Add(BuildCreateRequest(registry, presenterStableId: 5, start: 0, count: 8, finalChunk: true));
            store.ApplyRequests(requests.GetSpan(), registry);
            requests.Clear();
            Assert.That(store.GetResidentLane(0).Count, Is.EqualTo(8));

            // Hot re-registration of the same batch key with fewer inline transforms.
            InstancedBatchAsset shrunk = BuildAsset(VisualRenderPath.InstancedStaticMesh, BuildGridTransforms(count: 4));
            int batchAssetId = registry.Register(batchKey, shrunk);
            CompileAddresses(registry, batchAssetId, shrunk);

            requests.Add(BuildCreateRequest(registry, presenterStableId: 5, start: 0, count: 4, finalChunk: true));
            store.ApplyRequests(requests.GetSpan(), registry);

            RaylibInstancedBatchLane lane = store.GetResidentLane(0);
            Assert.That(lane.Count, Is.EqualTo(4));
            Assert.That(lane.Matrices.Length, Is.EqualTo(4));
        }

        [Test]
        public void ApplyRequests_ThrowsForUnknownBatchAsset()
        {
            InstancedBatchAssetRegistry registry = BuildRegistry("demo.batch", VisualRenderPath.InstancedStaticMesh, BuildGridTransforms(count: 2));
            InstancedBatchRequestBuffer requests = new();
            var store = new RaylibInstancedBatchLaneStore();

            InstancedBatchRequest orphan = BuildCreateRequest(registry, presenterStableId: 1, start: 0, count: 2, finalChunk: true, batchAssetIdOverride: 4242);
            requests.Add(orphan);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => store.ApplyRequests(requests.GetSpan(), registry))!;
            Assert.That(ex.Message, Does.Contain("cannot resolve batchAssetId=4242"));
        }

        [Test]
        public void ApplyRequests_ThrowsForExternalSourceGroups()
        {
            InstancedBatchAsset asset = BuildAsset(VisualRenderPath.InstancedStaticMesh, BuildGridTransforms(count: 2));
            asset.Groups[0].Source = new InstancedBatchInstanceSource(
                "ludots.instanced_transform_factorized.v1",
                "vfs://demo/transforms.json",
                "set.0",
                instanceCount: 2,
                groundToVisualHeightmap: false);
            InstancedBatchAssetRegistry registry = new();
            int batchAssetId = registry.Register("demo.batch", asset);
            CompileAddresses(registry, batchAssetId, asset);

            InstancedBatchRequestBuffer requests = new();
            var store = new RaylibInstancedBatchLaneStore();
            requests.Add(BuildCreateRequest(registry, presenterStableId: 1, start: 0, count: 2, finalChunk: true));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => store.ApplyRequests(requests.GetSpan(), registry))!;
            Assert.That(ex.Message, Does.Contain("#1152"));
        }

        [Test]
        public void ApplyRequests_TracksLastAppliedRequestCount()
        {
            InstancedBatchAssetRegistry registry = BuildRegistry("demo.batch", VisualRenderPath.InstancedStaticMesh, BuildGridTransforms(count: 4));
            InstancedBatchRequestBuffer requests = new();
            var store = new RaylibInstancedBatchLaneStore();
            Assert.That(store.LastAppliedRequestCount, Is.EqualTo(0));

            requests.Add(BuildCreateRequest(registry, presenterStableId: 2, start: 0, count: 4, finalChunk: true));
            store.ApplyRequests(requests.GetSpan(), registry);
            Assert.That(store.LastAppliedRequestCount, Is.EqualTo(1));

            store.ApplyRequests(ReadOnlySpan<InstancedBatchRequest>.Empty, registry);
            Assert.That(store.LastAppliedRequestCount, Is.EqualTo(0));
        }

        [Test]
        public void GetResidentLane_ThrowsForOutOfRangeIndex()
        {
            var store = new RaylibInstancedBatchLaneStore();
            Assert.Throws<ArgumentOutOfRangeException>(() => store.GetResidentLane(0));
        }

        [Test]
        public void ApplyRequests_KeepsLanesOfDistinctPresentersSeparate()
        {
            InstancedBatchAssetRegistry registry = BuildRegistry("demo.batch", VisualRenderPath.InstancedStaticMesh, BuildGridTransforms(count: 4));
            InstancedBatchRequestBuffer requests = new();
            var store = new RaylibInstancedBatchLaneStore();

            requests.Add(BuildCreateRequest(registry, presenterStableId: 11, start: 0, count: 4, finalChunk: true));
            requests.Add(BuildCreateRequest(registry, presenterStableId: 12, start: 0, count: 4, finalChunk: true));
            store.ApplyRequests(requests.GetSpan(), registry);

            Assert.That(store.ResidentLaneCount, Is.EqualTo(2));
            Assert.That(store.GetResidentLane(0).LaneId, Is.Not.EqualTo(store.GetResidentLane(1).LaneId));
        }

        private static InstancedBatchTransform[] BuildGridTransforms(int count)
        {
            var transforms = new InstancedBatchTransform[count];
            for (int i = 0; i < count; i++)
            {
                transforms[i] = new InstancedBatchTransform
                {
                    PositionCm = new Vector3((i % 4) * 100f, 0f, (i / 4) * 100f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                };
            }

            return transforms;
        }

        private static InstancedBatchAsset BuildAsset(VisualRenderPath renderPath, InstancedBatchTransform[] transforms)
        {
            return new InstancedBatchAsset
            {
                Key = "demo.batch",
                RenderPath = renderPath,
                OwnerStableId = "demo.owner",
                Groups = new[]
                {
                    new InstancedBatchGroup
                    {
                        Id = "grid",
                        MeshAssetId = 101,
                        MaterialId = 202,
                        BucketId = "grid.bucket",
                        InstanceSpanId = "grid.span",
                        Transforms = transforms,
                    },
                },
            };
        }

        private static InstancedBatchAssetRegistry BuildRegistry(
            string batchKey,
            VisualRenderPath renderPath,
            InstancedBatchTransform[] transforms)
        {
            InstancedBatchAssetRegistry registry = new();
            InstancedBatchAsset asset = BuildAsset(renderPath, transforms);
            int batchAssetId = registry.Register(batchKey, asset);
            CompileAddresses(registry, batchAssetId, asset);
            return registry;
        }

        private static void CompileAddresses(InstancedBatchAssetRegistry registry, int batchAssetId, InstancedBatchAsset asset)
        {
            var inputs = new InstancedBatchAddressGroupInput[asset.Groups.Length];
            for (int i = 0; i < asset.Groups.Length; i++)
            {
                inputs[i] = new InstancedBatchAddressGroupInput(
                    asset.Groups[i].Id,
                    asset.Groups[i].BucketId,
                    asset.Groups[i].InstanceSpanId);
            }

            asset.AddressTable = InstancedBatchAddressTable.Build(batchAssetId, asset.OwnerStableId, inputs);
            for (int i = 0; i < asset.Groups.Length; i++)
            {
                asset.Groups[i].Address = asset.AddressTable.Resolve(
                    asset.Groups[i].Id,
                    asset.Groups[i].BucketId,
                    asset.Groups[i].InstanceSpanId);
            }
        }

        private static InstancedBatchRequest BuildCreateRequest(
            InstancedBatchAssetRegistry registry,
            int presenterStableId,
            int start,
            int count,
            bool finalChunk,
            int batchAssetIdOverride = 0)
        {
            int batchAssetId = batchAssetIdOverride > 0 ? batchAssetIdOverride : registry.GetId("demo.batch");
            InstancedBatchAddress address = registry.TryGet(batchAssetId, out InstancedBatchAsset asset)
                ? asset.Groups[0].Address
                : new InstancedBatchAddress(1, new InstancedBatchOwnerId(1), new InstancedBatchGroupId(1), new InstancedBatchBucketId(1), new InstancedBatchSpanId(1));

            return new InstancedBatchRequest(
                InstancedBatchRequestKind.CreateOrUpdate,
                batchAssetId,
                presenterStableId,
                default,
                default,
                address,
                VisualRenderPath.InstancedStaticMesh,
                meshAssetId: 101,
                materialAssetId: 202,
                instanceStart: start,
                instanceCount: count,
                finalChunk: finalChunk);
        }

        private static InstancedBatchRequest BuildRemoveRequest(InstancedBatchAssetRegistry registry, int presenterStableId)
        {
            int batchAssetId = registry.GetId("demo.batch");
            InstancedBatchAddress address = registry.TryGet(batchAssetId, out InstancedBatchAsset asset)
                ? asset.Groups[0].Address
                : new InstancedBatchAddress(1, new InstancedBatchOwnerId(1), new InstancedBatchGroupId(1), new InstancedBatchBucketId(1), new InstancedBatchSpanId(1));
            return new InstancedBatchRequest(
                InstancedBatchRequestKind.Remove,
                batchAssetId,
                presenterStableId,
                default,
                default,
                address,
                VisualRenderPath.InstancedStaticMesh,
                meshAssetId: 101,
                materialAssetId: 202,
                instanceStart: 0,
                instanceCount: 0,
                finalChunk: true);
        }

        private static void AssertMatrixNear(Matrix4x4 actual, Matrix4x4 expected, float epsilon = 0.0001f)
        {
            Assert.That(actual.M11, Is.EqualTo(expected.M11).Within(epsilon));
            Assert.That(actual.M12, Is.EqualTo(expected.M12).Within(epsilon));
            Assert.That(actual.M13, Is.EqualTo(expected.M13).Within(epsilon));
            Assert.That(actual.M21, Is.EqualTo(expected.M21).Within(epsilon));
            Assert.That(actual.M22, Is.EqualTo(expected.M22).Within(epsilon));
            Assert.That(actual.M23, Is.EqualTo(expected.M23).Within(epsilon));
            Assert.That(actual.M31, Is.EqualTo(expected.M31).Within(epsilon));
            Assert.That(actual.M32, Is.EqualTo(expected.M32).Within(epsilon));
            Assert.That(actual.M33, Is.EqualTo(expected.M33).Within(epsilon));
            Assert.That(actual.M41, Is.EqualTo(expected.M41).Within(epsilon));
            Assert.That(actual.M42, Is.EqualTo(expected.M42).Within(epsilon));
            Assert.That(actual.M43, Is.EqualTo(expected.M43).Within(epsilon));
        }
    }
}

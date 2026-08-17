using System;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Surfaces;
using Ludots.Core.Presentation.Systems;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class ChunkSurfaceBakeSystemTests
    {
        [Test]
        public void Update_WhenSurfaceReferencesUnknownLodProfile_Throws()
        {
            using var world = World.Create();
            var runtime = new SurfaceSourceRuntimeRegistry();
            var meshes = new MeshAssetRegistry();
            var materials = new PresentationMaterialRegistry();
            var lodProfiles = new PresentationLodProfileRegistry();
            var definitions = new PresenterDefinitionRegistry();
            var commands = new PresenterCommandBuffer();
            var presenters = new PresenterEntityRuntime(world);
            int materialId = materials.GetId(PresentationMaterialRegistry.DefaultSurfaceKey);
            runtime.Upsert(
                CreateRawSurfaceRequest("missing_lod"),
                CreateRawSurfacePayload(materialId),
                frame: 1);

            using var system = new ChunkSurfaceBakeSystem(
                world,
                runtime,
                meshes,
                materials,
                lodProfiles,
                definitions,
                commands,
                presenters);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0.016f))!;
            Assert.That(ex.Message, Does.Contain("unknown lodProfileId 'missing_lod'"));
        }

        [Test]
        public void Update_WhenSurfaceReferencesRegisteredLodProfile_WritesProfileToBakedEntity()
        {
            using var world = World.Create();
            var runtime = new SurfaceSourceRuntimeRegistry();
            var meshes = new MeshAssetRegistry();
            var materials = new PresentationMaterialRegistry();
            var lodProfiles = new PresentationLodProfileRegistry();
            lodProfiles.Register(
                "custom_lod",
                new PresentationLodProfile(
                    new PresentationLodEntry(1500f, 0.7f),
                    new PresentationLodEntry(6000f, 0.2f),
                    new PresentationLodEntry(24000f, 0.01f)));
            var definitions = new PresenterDefinitionRegistry();
            var commands = new PresenterCommandBuffer();
            var presenters = new PresenterEntityRuntime(world);
            int materialId = materials.GetId(PresentationMaterialRegistry.DefaultSurfaceKey);
            runtime.Upsert(
                CreateRawSurfaceRequest("custom_lod"),
                CreateRawSurfacePayload(materialId),
                frame: 1);

            using var system = new ChunkSurfaceBakeSystem(
                world,
                runtime,
                meshes,
                materials,
                lodProfiles,
                definitions,
                commands,
                presenters);

            system.Update(0.016f);

            SurfaceSourceRecord record = runtime.Records.Single();
            Assert.That(record.Entity, Is.Not.EqualTo(Entity.Null));
            ref readonly PresentationLodProfile profile = ref world.Get<PresentationLodProfile>(record.Entity);
            Assert.That(profile.High.MaxDistanceCm, Is.EqualTo(1500f));
            Assert.That(profile.High.MinScreenCoverage01, Is.EqualTo(0.7f));
            Assert.That(profile.Medium.MaxDistanceCm, Is.EqualTo(6000f));
            Assert.That(profile.Low.MaxDistanceCm, Is.EqualTo(24000f));
        }

        private static SurfaceSourceRequest CreateRawSurfaceRequest(string lodProfileId)
        {
            return new SurfaceSourceRequest
            {
                StableId = 1001,
                PresenterDefinitionId = 2001,
                ScopeId = 3001,
                SurfaceKind = PresenterSurfaceKind.RawProceduralMesh,
                Authoring = new SurfaceAuthoringBlock
                {
                    Kind = PresenterSurfaceKind.RawProceduralMesh,
                    MaterialSet = new PresenterSurfaceMaterialSet
                    {
                        PrimaryMaterialId = PresentationMaterialRegistry.DefaultSurfaceKey,
                    },
                    LodProfileId = lodProfileId,
                },
            };
        }

        private static SurfacePayloadSnapshot CreateRawSurfacePayload(int materialId)
        {
            ProceduralMeshAssetData mesh = CreateTriangleMesh(materialId);
            return new SurfacePayloadSnapshot(
                PresenterSurfaceKind.RawProceduralMesh,
                version: 1,
                splineRibbon: default,
                closedArea: default,
                rawProceduralMesh: new SurfaceRawProceduralMeshPayload(mesh, Vector3.Zero));
        }

        private static ProceduralMeshAssetData CreateTriangleMesh(int materialId)
        {
            var mesh = new ProceduralMeshAssetData(maxVertexCount: 3, maxIndexCount: 3);
            SetVertex(mesh, 0, new Vector3(0f, 0f, 0f), new Vector2(0f, 0f));
            SetVertex(mesh, 1, new Vector3(1f, 0f, 0f), new Vector2(1f, 0f));
            SetVertex(mesh, 2, new Vector3(0f, 0f, 1f), new Vector2(0f, 1f));
            mesh.Indices[0] = 0;
            mesh.Indices[1] = 1;
            mesh.Indices[2] = 2;
            mesh.Commit(
                vertexCount: 3,
                indexCount: 3,
                new[] { new ProceduralSubmeshDescriptor(0, 3, materialId) },
                new ProceduralMeshBounds(new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0.01f, 0.5f)),
                ProceduralMeshUsageHint.Static);
            return mesh;
        }

        private static void SetVertex(ProceduralMeshAssetData mesh, int index, in Vector3 position, in Vector2 uv)
        {
            int p = index * 3;
            mesh.Positions[p + 0] = position.X;
            mesh.Positions[p + 1] = position.Y;
            mesh.Positions[p + 2] = position.Z;
            mesh.Normals[p + 0] = 0f;
            mesh.Normals[p + 1] = 1f;
            mesh.Normals[p + 2] = 0f;

            int t = index * 4;
            mesh.Tangents[t + 0] = 1f;
            mesh.Tangents[t + 1] = 0f;
            mesh.Tangents[t + 2] = 0f;
            mesh.Tangents[t + 3] = 1f;

            int u = index * 2;
            mesh.Uv0[u + 0] = uv.X;
            mesh.Uv0[u + 1] = uv.Y;
        }
    }
}

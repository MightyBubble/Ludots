using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Surfaces;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class SplineSurfaceUatTests
    {
        private const string MapId = "spline_surface_uat";

        [Test]
        public void SplineSurfaceUat_LoadMap_BakesAllPerformerSourcedSurfaceKinds()
        {
            using var engine = CreateEngine();
            engine.LoadMap(MapId);
            Tick(engine, 5);

            var runtime = engine.GetService(CoreServiceKeys.SurfaceSourceRuntimeRegistry) as SurfaceSourceRuntimeRegistry;
            Assert.That(runtime, Is.Not.Null);

            var records = runtime!.Records.ToArray();
            Assert.That(records.Length, Is.EqualTo(4));
            Assert.That(records.Count(record => record.Request.SurfaceKind == PerformerSurfaceKind.SplineRibbon), Is.EqualTo(2));
            Assert.That(records.Any(record => record.Request.SurfaceKind == PerformerSurfaceKind.ClosedArea), Is.True);
            Assert.That(records.Any(record => record.Request.SurfaceKind == PerformerSurfaceKind.RawProceduralMesh), Is.True);

            var performers = engine.GetService(CoreServiceKeys.PerformerEntityRuntime)
                ?? throw new InvalidOperationException("PerformerEntityRuntime missing.");
            int bakedPerformerCount = 0;
            var perfQuery = new QueryDescription().WithAll<PerformerState>();
            engine.World.Query(in perfQuery, (Entity entity, ref PerformerState state) =>
            {
                if (state.DefId > 0)
                {
                    bakedPerformerCount++;
                }
            });

            Assert.That(bakedPerformerCount, Is.GreaterThanOrEqualTo(4), "Spline surface UAT should bootstrap performer-backed surface sources instead of legacy visual runtime state.");
        }

        [Test]
        public void SplineSurfaceUat_SplineRibbonMesh_WeldsSegmentJoinWithoutDuplicateSampleRow()
        {
            using var engine = CreateEngine();
            engine.LoadMap(MapId);
            Tick(engine, 5);

            var meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry) as MeshAssetRegistry;
            var runtime = engine.GetService(CoreServiceKeys.SurfaceSourceRuntimeRegistry) as SurfaceSourceRuntimeRegistry;
            Assert.That(meshes, Is.Not.Null);
            Assert.That(runtime, Is.Not.Null);

            SurfaceSourceRecord? roadRecord = runtime!.Records
                .FirstOrDefault(record => record.Request.SurfaceKind == PerformerSurfaceKind.SplineRibbon && record.Request.ScopeId > 0);
            Assert.That(roadRecord, Is.Not.Null);
            Assert.That(roadRecord!.MeshAssetId, Is.GreaterThan(0));

            Assert.That(meshes!.TryGetDescriptor(roadRecord.MeshAssetId, out MeshAssetDescriptor descriptor), Is.True);
            Assert.That(descriptor.ProceduralMeshData, Is.Not.Null);

            ProceduralMeshAssetData mesh = descriptor.ProceduralMeshData;
            Assert.That(mesh.VertexCount, Is.EqualTo(((2 * 12) + 1) * 2));
            Assert.That(mesh.IndexCount, Is.EqualTo(2 * 12 * 6));

            Vector3 row12Left = ReadPosition(mesh, 12 * 2);
            Vector3 row13Left = ReadPosition(mesh, 13 * 2);
            Assert.That(Vector3.Distance(row12Left, row13Left), Is.GreaterThan(0.01f), "Segment join should advance to the next sampled row instead of duplicating the shared control point row.");
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = new List<string>
            {
                Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                Path.Combine(repoRoot, "mods", "CoreInputMod"),
                Path.Combine(repoRoot, "mods", "capabilities", "camera", "CameraProfilesMod"),
                Path.Combine(repoRoot, "mods", "showcases", "spline_surface_uat", "SplineSurfaceUatMod"),
            };

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            engine.Start();
            return engine;
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(1f / 60f);
            }
        }

        private static Vector3 ReadPosition(ProceduralMeshAssetData mesh, int vertexIndex)
        {
            int offset = vertexIndex * 3;
            return new Vector3(
                mesh.Positions[offset + 0],
                mesh.Positions[offset + 1],
                mesh.Positions[offset + 2]);
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (Directory.Exists(Path.Combine(dir, "assets")) &&
                    Directory.Exists(Path.Combine(dir, "mods")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new DirectoryNotFoundException("Repository root not found from test directory.");
        }
    }
}

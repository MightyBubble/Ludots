using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Ticking;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    [NonParallelizable]
    [Category("acceptance")]
    public sealed class StaticObstaclePhysicsShowcaseAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string ShowcaseModId = "StaticObstaclePhysicsShowcaseMod";
        private const string ShowcaseConfigRelativePath = "StaticObstaclePhysicsShowcaseConfig.json";

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            ShowcaseModId
        };

        [Test]
        public void StaticObstaclePhysicsShowcase_ProductionChain_SpawnsAndRetainsStaticBodies()
        {
            string repoRoot = FindRepoRoot();
            ShowcaseConfigSnapshot config = ReadShowcaseConfig(repoRoot);
            AssertShowcaseCatalog(repoRoot);
            AssertObstacleTemplateIsAuthoritative(repoRoot, config.ObstacleTemplateId, out int piecesPerObstacle);

            using var engine = CreateEngine(repoRoot);
            Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo(config.MapId));

            Physics2DSimulationSystem physics = FindSystem<Physics2DSimulationSystem>(engine, SystemGroup.InputCollection);
            RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");

            engine.LoadMap(config.MapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null);
            Assert.That(spawnQueue.Count, Is.EqualTo(config.TotalObstacleCount),
                "Map focus should enqueue configured static obstacles through RuntimeEntitySpawnQueue.");

            var frameTimesMs = new List<double>(48);
            TickUntil(engine, frameTimesMs, () => spawnQueue.Count == 0, maxFrames: 8);
            Assert.That(spawnQueue.Count, Is.EqualTo(0), "RuntimeEntitySpawnSystem should drain the showcase spawn batch.");
            Assert.That(CountMapComponents<CompoundObstacle2D>(engine.World, config.MapId), Is.EqualTo(config.TotalObstacleCount));
            Assert.That(CountMapComponents<CompoundObstacle2DState>(engine.World, config.MapId), Is.EqualTo(config.TotalObstacleCount));
            Assert.That(CountStaticMassBodies(engine.World, config.MapId), Is.EqualTo(config.TotalObstacleCount));
            Assert.That(CountMapComponents<Position2D>(engine.World, config.MapId), Is.EqualTo(config.TotalObstacleCount));
            Assert.That(CountMapComponents<Physics2DStaticBodyState>(engine.World, config.MapId), Is.EqualTo(config.TotalObstacleCount));
            Assert.That(SumCompoundPieces(engine.World, config.MapId), Is.EqualTo(config.TotalObstacleCount * piecesPerObstacle));

            TickUntil(engine, frameTimesMs, () => physics.Build.StaticBodyVersion > 0, maxFrames: 8);
            int materializedStaticVersion = physics.Build.StaticBodyVersion;
            int materializedDescriptorCount = physics.Build.StaticRigidBodyDescriptors.Count;
            Assert.That(physics.Build.DirtyStaticBodyCountLastRebuild, Is.EqualTo(config.TotalObstacleCount));
            Assert.That(materializedDescriptorCount, Is.EqualTo(config.TotalObstacleCount * piecesPerObstacle));
            Assert.That(materializedStaticVersion, Is.GreaterThan(0));

            TickMeasured(engine, 30, frameTimesMs);
            Assert.That(physics.Build.StaticBodyVersion, Is.EqualTo(materializedStaticVersion),
                "Retained static cache must not rebuild when the authored obstacle set is unchanged.");
            Assert.That(physics.Build.DirtyStaticBodyCountLastUpdate, Is.EqualTo(0));
            Assert.That(physics.Build.StaticRigidBodyDescriptors.Count, Is.EqualTo(materializedDescriptorCount));
            Assert.That(CountMapComponents<Physics2DStaticBodyDirty>(engine.World, config.MapId), Is.EqualTo(0));

            Physics2DPerfStats stats = ReadPhysicsPerfStats(engine.World);
            Assert.That(stats.PhysicsStepsLastFixedTick, Is.GreaterThanOrEqualTo(0));
            Assert.That(stats.PhysicsHz, Is.GreaterThan(0));

            WriteBenchmarkReport(
                repoRoot,
                config,
                piecesPerObstacle,
                materializedDescriptorCount,
                materializedStaticVersion,
                frameTimesMs,
                stats);
        }

        private static GameEngine CreateEngine(string repoRoot)
        {
            string assetsRoot = Path.Combine(repoRoot, "assets");
            List<string> modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            engine.Start();
            return engine;
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.AuthoritativeInput, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static void TickMeasured(GameEngine engine, int frames, List<double> frameTimesMs)
        {
            for (int i = 0; i < frames; i++)
            {
                long start = Stopwatch.GetTimestamp();
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
                frameTimesMs.Add((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
            }
        }

        private static void TickUntil(GameEngine engine, List<double> frameTimesMs, Func<bool> predicate, int maxFrames)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                TickMeasured(engine, 1, frameTimesMs);
            }

            Assert.That(predicate(), Is.True, $"Predicate was not satisfied within {maxFrames} frames.");
        }

        private static int CountMapComponents<T>(World world, string mapId)
        {
            int count = 0;
            var expected = new MapId(mapId);
            var query = new QueryDescription().WithAll<MapEntity, T>();
            world.Query(in query, (Entity _, ref MapEntity mapEntity, ref T __) =>
            {
                if (mapEntity.MapId == expected)
                {
                    count++;
                }
            });

            return count;
        }

        private static int CountStaticMassBodies(World world, string mapId)
        {
            int count = 0;
            var expected = new MapId(mapId);
            var query = new QueryDescription().WithAll<MapEntity, Mass2D>();
            world.Query(in query, (Entity _, ref MapEntity mapEntity, ref Mass2D mass) =>
            {
                if (mapEntity.MapId == expected && mass.IsStatic)
                {
                    count++;
                }
            });

            return count;
        }

        private static int SumCompoundPieces(World world, string mapId)
        {
            int count = 0;
            var expected = new MapId(mapId);
            var query = new QueryDescription().WithAll<MapEntity, CompoundObstacle2DState>();
            world.Query(in query, (Entity _, ref MapEntity mapEntity, ref CompoundObstacle2DState state) =>
            {
                if (mapEntity.MapId == expected)
                {
                    count += state.PieceCount;
                }
            });

            return count;
        }

        private static Physics2DPerfStats ReadPhysicsPerfStats(World world)
        {
            var query = new QueryDescription().WithAll<Physics2DPerfStats>();
            Physics2DPerfStats stats = default;
            bool found = false;
            world.Query(in query, (Entity _, ref Physics2DPerfStats value) =>
            {
                if (found)
                {
                    return;
                }

                stats = value;
                found = true;
            });

            Assert.That(found, Is.True, "Physics2DPerfStats should be published by the production physics system.");
            return stats;
        }

        private static T FindSystem<T>(GameEngine engine, SystemGroup group)
            where T : class, ISystem<float>
        {
            var field = typeof(GameEngine).GetField("_systemGroups", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);

            var systemGroups = field!.GetValue(engine) as Dictionary<SystemGroup, List<ISystem<float>>>;
            Assert.That(systemGroups, Is.Not.Null);
            Assert.That(systemGroups!.TryGetValue(group, out List<ISystem<float>>? systems), Is.True);

            for (int i = 0; i < systems!.Count; i++)
            {
                if (systems[i] is T typed)
                {
                    return typed;
                }
            }

            throw new InvalidOperationException($"System '{typeof(T).Name}' was not registered in group '{group}'.");
        }

        private static void AssertShowcaseCatalog(string repoRoot)
        {
            string catalogPath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "static_obstacle_physics",
                "StaticObstaclePhysicsShowcaseMod",
                "assets",
                "Configs",
                "config_catalog.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(catalogPath));
            AssertCatalogEntry(document.RootElement, ShowcaseConfigRelativePath, "Replace", null);
            AssertCatalogEntry(document.RootElement, "Entities/templates.json", "ArrayById", "id");
            AssertCatalogEntry(document.RootElement, "Presentation/presenters.json", "ArrayById", "id");
        }

        private static void AssertCatalogEntry(JsonElement catalog, string path, string policy, string? idField)
        {
            foreach (JsonElement entry in catalog.EnumerateArray())
            {
                string? entryPath = entry.GetProperty("Path").GetString();
                if (!string.Equals(entryPath, path, StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.That(entry.GetProperty("Policy").GetString(), Is.EqualTo(policy));
                if (idField != null)
                {
                    Assert.That(entry.GetProperty("IdField").GetString(), Is.EqualTo(idField));
                }

                return;
            }

            Assert.Fail($"Catalog entry '{path}' is missing.");
        }

        private static ShowcaseConfigSnapshot ReadShowcaseConfig(string repoRoot)
        {
            string configPath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "static_obstacle_physics",
                "StaticObstaclePhysicsShowcaseMod",
                "assets",
                ShowcaseConfigRelativePath);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement root = document.RootElement;
            string mapId = RequireString(root, "mapId");
            string templateId = RequireString(root, "obstacleTemplateId");
            int spawnScratchCapacity = root.GetProperty("spawnScratchCapacity").GetInt32();
            int totalObstacleCount = 0;
            int regionCount = 0;
            foreach (JsonElement region in root.GetProperty("regions").EnumerateArray())
            {
                int columns = region.GetProperty("columns").GetInt32();
                int rows = region.GetProperty("rows").GetInt32();
                totalObstacleCount = checked(totalObstacleCount + (columns * rows));
                regionCount++;
            }

            return new ShowcaseConfigSnapshot(mapId, templateId, spawnScratchCapacity, regionCount, totalObstacleCount);
        }

        private static void AssertObstacleTemplateIsAuthoritative(
            string repoRoot,
            string templateId,
            out int piecesPerObstacle)
        {
            string templatePath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "static_obstacle_physics",
                "StaticObstaclePhysicsShowcaseMod",
                "assets",
                "Entities",
                "templates.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(templatePath));
            foreach (JsonElement template in document.RootElement.EnumerateArray())
            {
                if (!string.Equals(template.GetProperty("id").GetString(), templateId, StringComparison.Ordinal))
                {
                    continue;
                }

                JsonElement components = template.GetProperty("components");
                Assert.That(components.TryGetProperty("CompoundObstacle2D", out JsonElement compound), Is.True);
                Assert.That(components.TryGetProperty("Collider2D", out _), Is.False);
                Assert.That(components.TryGetProperty("Mass2D", out _), Is.False);
                Assert.That(components.TryGetProperty("Position2D", out _), Is.False);
                Assert.That(components.TryGetProperty("Physics2DStaticBodyState", out _), Is.False);
                Assert.That(components.TryGetProperty("CompoundObstacle2DState", out _), Is.False);
                Assert.That(compound.GetProperty("sinkPhysicsCollider").GetBoolean(), Is.True);
                Assert.That(compound.GetProperty("sinkNavigationObstacle").GetBoolean(), Is.False);
                piecesPerObstacle = compound.GetProperty("pieces").GetArrayLength();
                Assert.That(piecesPerObstacle, Is.GreaterThan(0));
                return;
            }

            throw new InvalidOperationException($"Template '{templateId}' was not found.");
        }

        private static string RequireString(JsonElement root, string propertyName)
        {
            string? value = root.GetProperty(propertyName).GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Config property '{propertyName}' is required.");
            }

            return value;
        }

        private static void WriteBenchmarkReport(
            string repoRoot,
            in ShowcaseConfigSnapshot config,
            int piecesPerObstacle,
            int staticDescriptorCount,
            int staticBodyVersion,
            List<double> frameTimesMs,
            in Physics2DPerfStats stats)
        {
            string artifactDir = Path.Combine(repoRoot, "artifacts", "benchmarks", "static-obstacle-physics-showcase");
            Directory.CreateDirectory(artifactDir);
            string reportPath = Path.Combine(artifactDir, "benchmark-report.md");

            double maxMs = 0d;
            double sumMs = 0d;
            for (int i = 0; i < frameTimesMs.Count; i++)
            {
                double value = frameTimesMs[i];
                maxMs = Math.Max(maxMs, value);
                sumMs += value;
            }

            double avgMs = frameTimesMs.Count > 0 ? sumMs / frameTimesMs.Count : 0d;
            var builder = new StringBuilder();
            builder.AppendLine("# Static Obstacle Physics Showcase Benchmark");
            builder.AppendLine();
            builder.AppendLine("| Metric | Value |");
            builder.AppendLine("| --- | ---: |");
            builder.AppendLine($"| Config source | `{ShowcaseModId}:assets/{ShowcaseConfigRelativePath}` |");
            builder.AppendLine($"| Map | `{config.MapId}` |");
            builder.AppendLine($"| Regions | {config.RegionCount} |");
            builder.AppendLine($"| Obstacle entities | {config.TotalObstacleCount} |");
            builder.AppendLine($"| Pieces per obstacle | {piecesPerObstacle} |");
            builder.AppendLine($"| Static rigid body descriptors | {staticDescriptorCount} |");
            builder.AppendLine($"| Static body version after materialization | {staticBodyVersion} |");
            builder.AppendLine($"| Steady-state dirty static bodies | 0 |");
            builder.AppendLine($"| Physics Hz | {stats.PhysicsHz} |");
            builder.AppendLine($"| Last physics update ms | {stats.PhysicsUpdateMs:F4} |");
            builder.AppendLine($"| Measured frames | {frameTimesMs.Count} |");
            builder.AppendLine($"| Average frame tick ms | {avgMs:F4} |");
            builder.AppendLine($"| Max frame tick ms | {maxMs:F4} |");
            builder.AppendLine();
            builder.AppendLine("Production-chain evidence: ConfigPipeline catalog Replace entry -> map focus event -> RuntimeEntitySpawnQueue.EnqueueMany -> RuntimeEntitySpawnSystem -> ManifestationObstacleBridge2DSystem -> Physics2DSimulationSystem retained static cache.");
            File.WriteAllText(reportPath, builder.ToString());
        }

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                var candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not locate repo root.");
        }

        private readonly struct ShowcaseConfigSnapshot
        {
            public ShowcaseConfigSnapshot(
                string mapId,
                string obstacleTemplateId,
                int spawnScratchCapacity,
                int regionCount,
                int totalObstacleCount)
            {
                MapId = mapId;
                ObstacleTemplateId = obstacleTemplateId;
                SpawnScratchCapacity = spawnScratchCapacity;
                RegionCount = regionCount;
                TotalObstacleCount = totalObstacleCount;
            }

            public string MapId { get; }
            public string ObstacleTemplateId { get; }
            public int SpawnScratchCapacity { get; }
            public int RegionCount { get; }
            public int TotalObstacleCount { get; }
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public System.Numerics.Vector2 GetMousePosition() => System.Numerics.Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}

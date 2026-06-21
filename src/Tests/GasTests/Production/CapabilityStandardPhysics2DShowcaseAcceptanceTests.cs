using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Ticking;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    [NonParallelizable]
    public sealed class CapabilityStandardPhysics2DShowcaseAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string ShowcaseModId = "CapabilityStandardPhysics2DMod";
        private const string ShowcaseConfigRelativePath = "CapabilityStandardPhysics2DConfig.json";
        private const string MapId = "capability_standard_physics2d";
        private const string StoneTemplateId = "capability_standard_physics2d_bouncing_stone";
        private const string WallTemplateId = "capability_standard_physics2d_polygon_wall";
        private const string KnockbackTemplateId = "capability_standard_physics2d_knockback_target";
        private const string DampingFieldTemplateId = "capability_standard_physics2d_damping_field";
        private const string DampingProbeTemplateId = "capability_standard_physics2d_damping_probe";
        private const string DoorTemplateId = "capability_standard_physics2d_kinematic_rotating_door";

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            ShowcaseModId
        };

        [Test]
        public void CapabilityStandardPhysics2D_ProductionChain_WritesKeyframeAcceptance()
        {
            string repoRoot = FindRepoRoot();
            ShowcaseConfigSnapshot config = ReadShowcaseConfig(repoRoot);
            Assert.That(config.MapId, Is.EqualTo(MapId));
            AssertShowcaseCatalog(repoRoot);
            AssertShowcaseTemplates(repoRoot);

            using var engine = CreateEngine(repoRoot);
            Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo(MapId));
            Assert.That(engine.MergedConfig.Physics2D.Enabled, Is.True);
            Assert.That(engine.MergedConfig.Navigation2D.Enabled, Is.False);
            Assert.That(engine.GetService(CoreServiceKeys.Navigation2DRuntime), Is.Null);

            Physics2DSimulationSystem physics = FindSystem<Physics2DSimulationSystem>(engine, SystemGroup.InputCollection);
            RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");

            engine.LoadMap(config.MapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null);
            Assert.That(spawnQueue.Count, Is.EqualTo(config.SpawnCount),
                "Map focus should enqueue all configured Physics2D showcase entities through RuntimeEntitySpawnQueue.EnqueueMany.");

            var frameTimesMs = new List<double>(96);
            TickUntil(engine, frameTimesMs, () => spawnQueue.Count == 0, maxFrames: 8);
            Assert.That(spawnQueue.Count, Is.EqualTo(0), "RuntimeEntitySpawnSystem should drain the Physics2D showcase spawn batch.");

            Entity wall = FindSingleByTemplate(engine, WallTemplateId);
            Entity stone = FindSingleByTemplate(engine, StoneTemplateId);
            Entity knockback = FindSingleByTemplate(engine, KnockbackTemplateId);
            Entity dampingField = FindSingleByTemplate(engine, DampingFieldTemplateId);
            Entity dampingProbe = FindSingleByTemplate(engine, DampingProbeTemplateId);
            Entity door = FindSingleByTemplate(engine, DoorTemplateId);

            Assert.That(engine.World.Has<Position2D>(dampingField), Is.True);
            Assert.That(engine.World.Has<DampingField>(dampingField), Is.True);
            Assert.That(engine.World.Has<Collider2D>(stone), Is.True);
            Assert.That(engine.World.Has<Collider2D>(door), Is.True);
            Assert.That(engine.World.Has<ForceInput2D>(knockback), Is.True);

            TickUntil(engine, frameTimesMs, () => physics.Build.StaticBodyVersion > 0, maxFrames: 8);
            Assert.That(engine.World.Has<Physics2DStaticBodyState>(wall), Is.True);
            Assert.That(physics.Build.StaticRigidBodyDescriptors.Count, Is.GreaterThanOrEqualTo(1),
                "The polygon wall should materialize into the retained static body cache.");

            var keyframes = new List<KeyframeSnapshot>(8)
            {
                Capture(engine, frame: 0, stone, knockback, dampingProbe, door)
            };

            TickMeasured(engine, 1, frameTimesMs);
            var afterFirstFrame = Capture(engine, frame: 1, stone, knockback, dampingProbe, door);
            keyframes.Add(afterFirstFrame);
            Assert.That(afterFirstFrame.KnockbackForceX, Is.EqualTo(0f).Within(0.001f));
            Assert.That(afterFirstFrame.KnockbackForceY, Is.EqualTo(0f).Within(0.001f));
            Assert.That(afterFirstFrame.KnockbackVelocityX, Is.GreaterThan(0f));
            Assert.That(afterFirstFrame.KnockbackPositionX, Is.GreaterThan(-900f));

            TickUntil(
                engine,
                frameTimesMs,
                () => engine.World.Get<Velocity2D>(stone).Linear.X < Fix64.Zero,
                maxFrames: 90);
            keyframes.Add(Capture(engine, frame: frameTimesMs.Count, stone, knockback, dampingProbe, door));

            TickMeasured(engine, 16, frameTimesMs);
            var final = Capture(engine, frame: frameTimesMs.Count, stone, knockback, dampingProbe, door);
            keyframes.Add(final);

            Assert.That(final.StoneVelocityX, Is.LessThan(0f), "Restitution should reverse the stone after hitting the static polygon wall.");
            Assert.That(final.StonePositionX, Is.GreaterThan(-360f), "The stone should move from its configured spawn position before bouncing.");
            Assert.That(final.DampingProbeVelocityX, Is.LessThan(keyframes[0].DampingProbeVelocityX * 0.7f),
                "The damping field should reduce probe velocity through AppliedDamping.");
            Assert.That(engine.World.Has<AppliedDamping>(dampingProbe), Is.True);
            Assert.That(final.DoorRotationRad, Is.GreaterThan(keyframes[0].DoorRotationRad + 0.05f),
                "The rotating door should advance through Velocity2D.Angular without Navigation2D.");

            Physics2DPerfStats stats = ReadPhysicsPerfStats(engine.World);
            Assert.That(stats.PhysicsHz, Is.GreaterThan(0));

            WriteAcceptanceArtifacts(repoRoot, keyframes, frameTimesMs, stats, physics);
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

        private static Entity FindSingleByTemplate(GameEngine engine, string templateId)
        {
            EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
                ?? throw new InvalidOperationException("EntityTemplateKeyRegistry missing.");
            int templateKeyId = templateKeys.GetId(templateId);
            Assert.That(templateKeyId, Is.GreaterThan(0), $"Template '{templateId}' should be registered.");

            Entity found = Entity.Null;
            int count = 0;
            var expectedMap = new MapId(MapId);
            var query = new QueryDescription().WithAll<EntityTemplateKeyRef, MapEntity>();
            engine.World.Query(in query, (Entity entity, ref EntityTemplateKeyRef keyRef, ref MapEntity mapEntity) =>
            {
                if (keyRef.TemplateKeyId == templateKeyId && mapEntity.MapId == expectedMap)
                {
                    found = entity;
                    count++;
                }
            });

            Assert.That(count, Is.EqualTo(1), $"Expected exactly one entity for template '{templateId}'.");
            return found;
        }

        private static KeyframeSnapshot Capture(
            GameEngine engine,
            int frame,
            Entity stone,
            Entity knockback,
            Entity dampingProbe,
            Entity door)
        {
            Position2D stonePosition = engine.World.Get<Position2D>(stone);
            Velocity2D stoneVelocity = engine.World.Get<Velocity2D>(stone);
            Position2D knockbackPosition = engine.World.Get<Position2D>(knockback);
            Velocity2D knockbackVelocity = engine.World.Get<Velocity2D>(knockback);
            ForceInput2D knockbackForce = engine.World.Has<ForceInput2D>(knockback)
                ? engine.World.Get<ForceInput2D>(knockback)
                : default;
            Velocity2D dampingVelocity = engine.World.Get<Velocity2D>(dampingProbe);
            AppliedDamping appliedDamping = engine.World.Has<AppliedDamping>(dampingProbe)
                ? engine.World.Get<AppliedDamping>(dampingProbe)
                : new AppliedDamping { TotalFieldDamping = Fix64.OneValue };
            Rotation2D doorRotation = engine.World.Get<Rotation2D>(door);

            return new KeyframeSnapshot(
                frame,
                ToFloat(stonePosition.Value.X),
                ToFloat(stonePosition.Value.Y),
                ToFloat(stoneVelocity.Linear.X),
                ToFloat(stoneVelocity.Linear.Y),
                ToFloat(knockbackPosition.Value.X),
                ToFloat(knockbackPosition.Value.Y),
                ToFloat(knockbackVelocity.Linear.X),
                ToFloat(knockbackVelocity.Linear.Y),
                ToFloat(knockbackForce.Force.X),
                ToFloat(knockbackForce.Force.Y),
                ToFloat(dampingVelocity.Linear.X),
                ToFloat(appliedDamping.TotalFieldDamping),
                ToFloat(doorRotation.Value));
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
                "capability_standard",
                "CapabilityStandardPhysics2DMod",
                "assets",
                "Configs",
                "config_catalog.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(catalogPath));
            AssertCatalogEntry(document.RootElement, ShowcaseConfigRelativePath, "Replace", null);
            AssertCatalogEntry(document.RootElement, "Entities/templates.json", "ArrayById", "id");
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
                "capability_standard",
                "CapabilityStandardPhysics2DMod",
                "assets",
                ShowcaseConfigRelativePath);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement root = document.RootElement;
            return new ShowcaseConfigSnapshot(RequireString(root, "mapId"), root.GetProperty("spawns").GetArrayLength());
        }

        private static void AssertShowcaseTemplates(string repoRoot)
        {
            string templatePath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "capability_standard",
                "CapabilityStandardPhysics2DMod",
                "assets",
                "Entities",
                "templates.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(templatePath));
            var templateIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement template in document.RootElement.EnumerateArray())
            {
                string id = RequireString(template, "id");
                templateIds.Add(id);
                JsonElement components = template.GetProperty("components");
                Assert.That(components.TryGetProperty("NavKinematics2D", out _), Is.False);
                Assert.That(components.TryGetProperty("NavDesiredVelocity2D", out _), Is.False);
                Assert.That(components.TryGetProperty("NavObstacle2D", out _), Is.False);
            }

            Assert.That(templateIds.SetEquals(new[]
            {
                WallTemplateId,
                StoneTemplateId,
                KnockbackTemplateId,
                DampingFieldTemplateId,
                DampingProbeTemplateId,
                DoorTemplateId
            }), Is.True);
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

        private static void WriteAcceptanceArtifacts(
            string repoRoot,
            IReadOnlyList<KeyframeSnapshot> keyframes,
            IReadOnlyList<double> frameTimesMs,
            in Physics2DPerfStats stats,
            Physics2DSimulationSystem physics)
        {
            string artifactDir = Path.Combine(repoRoot, "artifacts", "showcases", "capability-standard-physics2d");
            Directory.CreateDirectory(artifactDir);
            string jsonlPath = Path.Combine(artifactDir, "keyframes.jsonl");
            string mdPath = Path.Combine(artifactDir, "acceptance.md");

            var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
            using (var writer = new StreamWriter(jsonlPath, append: false, Encoding.UTF8))
            {
                for (int i = 0; i < keyframes.Count; i++)
                {
                    writer.WriteLine(JsonSerializer.Serialize(keyframes[i], jsonOptions));
                }
            }

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
            builder.AppendLine("# Capability Standard Physics2D Acceptance");
            builder.AppendLine();
            builder.AppendLine("| Check | Evidence |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine("| Pure Physics2D startup | `physics2D.enabled=true`, `navigation2D.enabled=false`, no `Navigation2DRuntime` service |");
            builder.AppendLine("| Spawn path | `ConfigPipeline` catalog -> map focus event -> `RuntimeEntitySpawnQueue.EnqueueMany` -> `RuntimeEntitySpawnSystem` |");
            builder.AppendLine($"| Static polygon wall | Static body version `{physics.Build.StaticBodyVersion}`, descriptors `{physics.Build.StaticRigidBodyDescriptors.Count}` |");
            builder.AppendLine($"| Restitution bounce | final stone velocity X `{Format(keyframes[^1].StoneVelocityX)}` cm/s |");
            builder.AppendLine($"| ForceInput knockback | frame 1 force X/Y `{Format(keyframes[1].KnockbackForceX)}` / `{Format(keyframes[1].KnockbackForceY)}`, velocity X `{Format(keyframes[1].KnockbackVelocityX)}` cm/s |");
            builder.AppendLine($"| Damping field | final damping probe velocity X `{Format(keyframes[^1].DampingProbeVelocityX)}` cm/s, applied damping `{Format(keyframes[^1].DampingProbeAppliedDamping)}` |");
            builder.AppendLine($"| Kinematic rotating door | final rotation `{Format(keyframes[^1].DoorRotationRad)}` rad |");
            builder.AppendLine($"| Physics stats | Hz `{stats.PhysicsHz}`, potential pairs `{stats.PotentialPairs}`, contact pairs `{stats.ContactPairs}`, last update `{stats.PhysicsUpdateMs:F4}` ms |");
            builder.AppendLine($"| Test tick timings | frames `{frameTimesMs.Count}`, avg `{avgMs:F4}` ms, max `{maxMs:F4}` ms |");
            builder.AppendLine();
            builder.AppendLine("## Keyframes");
            builder.AppendLine();
            builder.AppendLine("| Frame | Stone X | Stone Vx | Knockback X | Knockback Vx | Damping Vx | Door Rot |");
            builder.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            for (int i = 0; i < keyframes.Count; i++)
            {
                KeyframeSnapshot keyframe = keyframes[i];
                builder.AppendLine(
                    $"| {keyframe.Frame} | {Format(keyframe.StonePositionX)} | {Format(keyframe.StoneVelocityX)} | {Format(keyframe.KnockbackPositionX)} | {Format(keyframe.KnockbackVelocityX)} | {Format(keyframe.DampingProbeVelocityX)} | {Format(keyframe.DoorRotationRad)} |");
            }

            File.WriteAllText(mdPath, builder.ToString(), Encoding.UTF8);
        }

        private static float ToFloat(Fix64 value)
        {
            return value.ToFloat();
        }

        private static string Format(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
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

        private readonly record struct ShowcaseConfigSnapshot(string MapId, int SpawnCount);

        private readonly record struct KeyframeSnapshot(
            int Frame,
            float StonePositionX,
            float StonePositionY,
            float StoneVelocityX,
            float StoneVelocityY,
            float KnockbackPositionX,
            float KnockbackPositionY,
            float KnockbackVelocityX,
            float KnockbackVelocityY,
            float KnockbackForceX,
            float KnockbackForceY,
            float DampingProbeVelocityX,
            float DampingProbeAppliedDamping,
            float DoorRotationRad);

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

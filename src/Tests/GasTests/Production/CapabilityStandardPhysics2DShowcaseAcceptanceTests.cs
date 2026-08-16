using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Arch.Core;
using Arch.System;
using CapabilityStandardPhysics2DShowcaseMod.Runtime;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Ticking;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    [NonParallelizable]
    [Category("acceptance")]
    public sealed class CapabilityStandardPhysics2DShowcaseAcceptanceTests
    {
        private const string BindingName = "capability_standard_physics2d_showcase";
        private const string PresetId = "capability_standard_physics2d_showcase_raylib";
        private const string ShowcaseModId = "CapabilityStandardPhysics2DShowcaseMod";
        private const string ShowcaseConfigPath = "CapabilityStandardPhysics2DShowcaseConfig.json";

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            ShowcaseModId
        };

        [Test]
        public void RootMod_UsesCapabilityLauncherAndFormalPhysics2DStartupPath()
        {
            string repoRoot = FindRepoRoot();
            AssertLauncherBinding(repoRoot);
            AssertLauncherPreset(repoRoot);
            ShowcaseConfigSnapshot config = ReadShowcaseConfig(repoRoot);
            AssertShowcaseCatalog(repoRoot);
            AssertTemplateAuthoring(repoRoot, config.StaticObstacleTemplateId);
            AssertGameJson(repoRoot, config.MapId);
            AssertMapExists(repoRoot, config.MapId);

            using var engine = CreateEngine(repoRoot);
            Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo(config.MapId));

            Physics2DSimulationSystem physics = FindSystem<Physics2DSimulationSystem>(engine, SystemGroup.InputCollection);
            Physics2DBroadphasePolicy broadphasePolicy = engine.GetService(CoreServiceKeys.Physics2DBroadphasePolicy)
                ?? throw new InvalidOperationException("Physics2DBroadphasePolicy missing.");
            Physics2DTickPolicy tickPolicy = engine.GetService(CoreServiceKeys.Physics2DTickPolicy)
                ?? throw new InvalidOperationException("Physics2DTickPolicy missing.");

            Assert.That(tickPolicy.TargetHz, Is.EqualTo(15));
            Assert.That(broadphasePolicy.Strategy, Is.EqualTo(Physics2DBroadphaseStrategyKind.UniformGrid));
            Assert.That(broadphasePolicy.CellSizeCm, Is.EqualTo(256));
            Assert.That(physics.Spatial.CurrentStrategyKind, Is.EqualTo(Physics2DBroadphaseStrategyKind.SortAndSweep),
                "Policy application happens on the first physics simulation update, after BuildPhysicsWorld runs.");

            TickUntil(
                engine,
                () => physics.Spatial.CurrentStrategyKind == Physics2DBroadphaseStrategyKind.UniformGrid,
                maxFrames: 12);

            Assert.That(physics.Spatial.CurrentStrategyKind, Is.EqualTo(Physics2DBroadphaseStrategyKind.UniformGrid));
            Assert.That(physics.Spatial.CurrentCellSizeCm, Is.EqualTo(256));

            WriteAcceptanceEvidence(repoRoot, config, tickPolicy, broadphasePolicy);
        }

        [Test]
        public void RootMod_CommandPointerDrawing_CreatesStaticPolygonObstacleThroughBridge()
        {
            string repoRoot = FindRepoRoot();
            using var engine = CreateEngine(repoRoot);
            var runtime = engine.GetService(CoreServiceKeys.BenchmarkSceneController) as CapabilityStandardPhysics2DShowcaseRuntime
                ?? throw new InvalidOperationException("Capability-standard Physics2D showcase runtime missing.");
            var control = FindSystem<CapabilityStandardPhysics2DShowcaseControlSystem>(engine, SystemGroup.InputCollection);

            engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
            Assert.That(runtime.IsActive, Is.True);
            engine.SetService(CoreServiceKeys.AuthoritativeInput, new FrozenInputActionReader());
            runtime.TogglePolygonDrawMode();
            AddPolygonVertex(control, engine, new WorldCmInt2(-300, -200));
            AddPolygonVertex(control, engine, new WorldCmInt2(300, -200));
            AddPolygonVertex(control, engine, new WorldCmInt2(0, 300));

            CapabilityStandardPhysics2DShowcasePanelState draftState = runtime.CapturePanelState(engine);
            Assert.That(draftState.PolygonDrawMode, Is.True);
            Assert.That(draftState.DrawnPolygonVertices, Is.EqualTo(3));

            runtime.CompletePolygonObstacle();
            var shapeStorage = engine.GetService(CoreServiceKeys.Physics2DShapeStorage) as ShapeDataStorage2D
                ?? throw new InvalidOperationException("Physics2D showcase test requires Physics2D shape storage.");
            new ManifestationObstacleBridge2DSystem(engine.World, shapeStorage).Update(0f);

            Entity polygonEntity = FindShowcasePolygonObstacle(engine.World);
            Assert.That(engine.World.Has<CapabilityStandardPhysics2DShowcaseStaticObstacleTag>(polygonEntity), Is.True);
            Assert.That(engine.World.Has<Collider2D>(polygonEntity), Is.True);
            Assert.That(engine.World.Get<Collider2D>(polygonEntity).Type, Is.EqualTo(ColliderType2D.Polygon));
            Assert.That(engine.World.Has<Mass2D>(polygonEntity), Is.True);
            Assert.That(engine.World.Get<Mass2D>(polygonEntity).IsStatic, Is.True);
            Assert.That(engine.World.Has<Velocity2D>(polygonEntity), Is.True);

            CapabilityStandardPhysics2DShowcasePanelState completedState = runtime.CapturePanelState(engine);
            Assert.That(completedState.PolygonDrawMode, Is.False);
            Assert.That(completedState.DrawnPolygonVertices, Is.EqualTo(0));
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

        private static void AddPolygonVertex(
            CapabilityStandardPhysics2DShowcaseControlSystem control,
            GameEngine engine,
            in WorldCmInt2 worldCm)
        {
            var input = engine.GetService(CoreServiceKeys.AuthoritativeInput)
                ?? throw new InvalidOperationException("AuthoritativeInput missing.");
            var pointerButtons = engine.GetService(CoreServiceKeys.AuthoritativePointerButtons)
                ?? throw new InvalidOperationException("AuthoritativePointerButtons missing.");
            InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(
                engine.GlobalContext,
                nameof(CapabilityStandardPhysics2DShowcaseAcceptanceTests));
            SetPointerButtonState(pointerButtons, bindings.ConfirmActionId, worldCm, isDown: false, pressed: false);
            SetPointerButtonState(pointerButtons, bindings.CancelActionId, worldCm, isDown: false, pressed: false);
            SetPointerButtonState(pointerButtons, bindings.CommandActionId, worldCm, isDown: true, pressed: true);
            if (input is FrozenInputActionReader frozen)
            {
                frozen.SetActionState(
                    AuthoritativeGroundPointerHelper.ActionId,
                    new System.Numerics.Vector3(worldCm.X, 0f, worldCm.Y),
                    isDown: true,
                    pressedThisFrame: false,
                    releasedThisFrame: false);
                frozen.SetActionValue(bindings.PointerPositionActionId, new System.Numerics.Vector3(worldCm.X, worldCm.Y, 0f));
            }
            else if (input is PlayerInputHandler playerInput)
            {
                playerInput.InjectAction(AuthoritativeGroundPointerHelper.ActionId, new System.Numerics.Vector3(worldCm.X, 0f, worldCm.Y));
                playerInput.InjectAction(bindings.PointerPositionActionId, new System.Numerics.Vector3(worldCm.X, worldCm.Y, 0f));
                playerInput.Update();
            }
            else
            {
                throw new InvalidOperationException($"Unsupported test input reader '{input.GetType().Name}'.");
            }

            control.Update(0f);
            pointerButtons.SuppressAction(bindings.CommandActionId);
        }

        private static void SetPointerButtonState(
            AuthoritativePointerButtonSnapshot pointerButtons,
            string actionId,
            in WorldCmInt2 worldCm,
            bool isDown,
            bool pressed)
        {
            pointerButtons.SetState(
                actionId,
                new PointerButtonState(
                    new System.Numerics.Vector2(worldCm.X, worldCm.Y),
                    new System.Numerics.Vector2(worldCm.X, worldCm.Y),
                    default,
                    new System.Numerics.Vector2(worldCm.X, worldCm.Y),
                    isDown: isDown,
                    pressedThisFrame: pressed,
                    releasedThisFrame: false,
                    hasPressPointer: pressed,
                    hasReleasePointer: false,
                    hasLastDownPointer: isDown));
        }

        private static Entity FindShowcasePolygonObstacle(World world)
        {
            var query = new QueryDescription().WithAll<
                CapabilityStandardPhysics2DShowcaseEntityTag,
                CapabilityStandardPhysics2DShowcaseStaticObstacleTag,
                ManifestationObstaclePolygon2D,
                Collider2D>();
            Entity found = Entity.Null;
            int count = 0;
            world.Query(in query, (Entity entity) =>
            {
                found = entity;
                count++;
            });

            Assert.That(count, Is.EqualTo(1));
            return found;
        }

        private static void AssertLauncherBinding(string repoRoot)
        {
            string launcherConfig = Path.Combine(repoRoot, "launcher.config.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(launcherConfig));
            foreach (JsonElement binding in document.RootElement.GetProperty("bindings").EnumerateArray())
            {
                if (!string.Equals(binding.GetProperty("name").GetString(), BindingName, StringComparison.Ordinal))
                {
                    continue;
                }

                JsonElement target = binding.GetProperty("target");
                Assert.That(target.GetProperty("type").GetString(), Is.EqualTo("path"));
                Assert.That(
                    target.GetProperty("value").GetString(),
                    Is.EqualTo("mods/showcases/capability_standard/CapabilityStandardPhysics2DShowcaseMod"));
                Assert.That(target.GetProperty("projectPath").GetString(), Is.EqualTo("CapabilityStandardPhysics2DShowcaseMod.csproj"));
                return;
            }

            Assert.Fail($"Launcher binding '{BindingName}' is missing.");
        }

        private static void AssertLauncherPreset(string repoRoot)
        {
            string launcherPresets = Path.Combine(repoRoot, "launcher.presets.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(launcherPresets));
            foreach (JsonElement preset in document.RootElement.GetProperty("presets").EnumerateArray())
            {
                if (!string.Equals(preset.GetProperty("id").GetString(), PresetId, StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.That(preset.GetProperty("adapterId").GetString(), Is.EqualTo("raylib"));
                JsonElement selectors = preset.GetProperty("selectors");
                Assert.That(selectors.GetArrayLength(), Is.EqualTo(1));
                Assert.That(selectors[0].GetString(), Is.EqualTo($"${BindingName}"));
                return;
            }

            Assert.Fail($"Launcher preset '{PresetId}' is missing.");
        }

        private static void AssertShowcaseCatalog(string repoRoot)
        {
            string catalogPath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "capability_standard",
                "CapabilityStandardPhysics2DShowcaseMod",
                "assets",
                "config_catalog.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(catalogPath));
            AssertCatalogEntry(document.RootElement, ShowcaseConfigPath, "Replace", null);
            AssertCatalogEntry(document.RootElement, "Physics2D/clock.json", "DeepObject", null);
            AssertCatalogEntry(document.RootElement, "Entities/templates.json", "ArrayById", "id");
        }

        private static void AssertCatalogEntry(JsonElement catalog, string path, string policy, string? idField)
        {
            foreach (JsonElement entry in catalog.EnumerateArray())
            {
                if (!string.Equals(entry.GetProperty("Path").GetString(), path, StringComparison.Ordinal))
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

        private static void AssertGameJson(string repoRoot, string mapId)
        {
            string gamePath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "capability_standard",
                "CapabilityStandardPhysics2DShowcaseMod",
                "assets",
                "game.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(gamePath));
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("startupMapId").GetString(), Is.EqualTo(mapId));
            Assert.That(root.TryGetProperty("navigation" + "2D", out _), Is.False);
            Assert.That(
                root.GetProperty("presentation").GetProperty("runtimeEntitySpawnQueueCapacity").GetInt32(),
                Is.GreaterThanOrEqualTo(100000));
            Assert.That(
                root.GetProperty("presentation").GetProperty("runtimeEntitySpawnReceiptQueueCapacity").GetInt32(),
                Is.GreaterThanOrEqualTo(100000));
        }

        private static ShowcaseConfigSnapshot ReadShowcaseConfig(string repoRoot)
        {
            string configPath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "capability_standard",
                "CapabilityStandardPhysics2DShowcaseMod",
                "assets",
                ShowcaseConfigPath);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("maxDynamicEntities").GetInt32(), Is.EqualTo(30000));
            Assert.That(root.GetProperty("maxStaticObstacles").GetInt32(), Is.EqualTo(100000));
            return new ShowcaseConfigSnapshot(
                RequireString(root, "mapId"),
                RequireString(root, "staticObstacleTemplateId"));
        }

        private static void AssertTemplateAuthoring(string repoRoot, string templateId)
        {
            string templatePath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "capability_standard",
                "CapabilityStandardPhysics2DShowcaseMod",
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
                Assert.That(compound.GetProperty("sinkPhysicsCollider").GetBoolean(), Is.True);
                Assert.That(compound.GetProperty("sinkNavigationObstacle").GetBoolean(), Is.False);
                Assert.That(compound.GetProperty("pieces").GetArrayLength(), Is.GreaterThan(0));
                return;
            }

            Assert.Fail($"Static obstacle template '{templateId}' is missing.");
        }

        private static void AssertMapExists(string repoRoot, string mapId)
        {
            string mapPath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "capability_standard",
                "CapabilityStandardPhysics2DShowcaseMod",
                "assets",
                "Maps",
                $"{mapId}.json");
            Assert.That(File.Exists(mapPath), Is.True);
        }

        private static void WriteAcceptanceEvidence(
            string repoRoot,
            ShowcaseConfigSnapshot config,
            Physics2DTickPolicy tickPolicy,
            Physics2DBroadphasePolicy broadphasePolicy)
        {
            string artifactDir = Path.Combine(
                repoRoot,
                "artifacts",
                "showcases",
                "capability-standard-physics2d-showcase");
            Directory.CreateDirectory(artifactDir);
            File.WriteAllText(
                Path.Combine(artifactDir, "acceptance.md"),
                string.Join(
                    Environment.NewLine,
                    "# Capability Standard Physics2D Showcase Acceptance",
                    "",
                    $"binding={BindingName}",
                    $"preset={PresetId}",
                    $"map={config.MapId}",
                    $"physics2D.tickHz={tickPolicy.TargetHz}",
                    $"physics2D.broadphase={broadphasePolicy.Strategy}",
                    $"physics2D.broadphaseCellSizeCm={broadphasePolicy.CellSizeCm}",
                    ""));
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

        private static void TickUntil(GameEngine engine, Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (condition())
                {
                    return;
                }

                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(1f / 60f);
            }

            Assert.That(condition(), Is.True);
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
                string staticObstacleTemplateId)
            {
                MapId = mapId;
                StaticObstacleTemplateId = staticObstacleTemplateId;
            }

            public string MapId { get; }
            public string StaticObstacleTemplateId { get; }
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

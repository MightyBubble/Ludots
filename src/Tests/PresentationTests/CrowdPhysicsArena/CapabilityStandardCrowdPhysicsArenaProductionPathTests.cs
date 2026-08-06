using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using CapabilityStandardCrowdPhysicsArenaMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Movement.Physics2DBridge;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class CapabilityStandardCrowdPhysicsArenaProductionPathTests
    {
        private const float FixedDeltaSeconds = 1f / 60f;
        private const int MaxWarmupFrames = 240;

        private static readonly string[] ShowcaseMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "MassNavigationMod",
            "CapabilityStandardCrowdPhysicsArenaMod"
        };

        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            PerformerScopeTagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            PerformerScopeTagRegistry.Clear();
        }

        [Test]
        public void Showcase_BootsArenaWithKinematicSquadsDrivenByBridge()
        {
            GC.KeepAlive(typeof(CapabilityStandardCrowdPhysicsArenaModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);

            MassNavigationSimulationRuntime simulation = RequireMassNavigationSimulation(engine);
            int expectedAgents = checked(simulation.Config.Scenario.Teams.Length * simulation.Config.Scenario.AgentsPerTeam);
            Assert.That(expectedAgents, Is.EqualTo(96));

            WaitForScenarioAgents(engine, simulation, expectedAgents);

            // Every squad agent must be a kinematic physics participant driven by the massnav bridge.
            var feedSystem = RequireService(engine, MovementPhysics2DBridgeKeys.KinematicPoseFeedSystem);
            TickFrames(engine, 2);
            Assert.That(feedSystem.LastFedParticipantCount, Is.EqualTo(expectedAgents),
                "All arena squad agents must be fed into the kinematic pose buffer every fixed step.");

            int kinematicAgents = 0;
            var agentQuery = new QueryDescription()
                .WithAll<MassNavigationAgentIndex, MovementParticipation, Mass2D, Position2D, WorldPositionCm>();
            engine.World.Query(in agentQuery, (
                Entity entity,
                ref MassNavigationAgentIndex agentIndex,
                ref MovementParticipation participation,
                ref Mass2D mass,
                ref Position2D position,
                ref WorldPositionCm worldPosition) =>
            {
                Assert.That(participation.PhysicsPresence, Is.EqualTo(PhysicsPresenceKind.Kinematic));
                Assert.That(mass.IsKinematic, Is.True);
                Vector2 bodyCm = new((float)position.Value.X, (float)position.Value.Y);
                Vector2 committedCm = worldPosition.Value.ToVector2();
                Assert.That(Vector2.Distance(bodyCm, committedCm), Is.LessThanOrEqualTo(1f),
                    $"Agent {agentIndex.Value}: kinematic body must mirror the committed WorldPositionCm.");
                kinematicAgents++;
            });
            Assert.That(kinematicAgents, Is.EqualTo(expectedAgents));
        }

        private static void WaitForScenarioAgents(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            int expectedAgents)
        {
            for (int frame = 0; frame < MaxWarmupFrames; frame++)
            {
                if (simulation.NavigationAgentCount >= expectedAgents)
                {
                    return;
                }

                TickFrames(engine, 1);
            }

            Assert.Fail(
                $"Arena scenario did not spawn {expectedAgents} agents within {MaxWarmupFrames} frames " +
                $"(current: {simulation.NavigationAgentCount}).");
        }

        private static void TickFrames(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(FixedDeltaSeconds);
                HeadlessPresentationTestHost.UpdateCamera(engine);
            }
        }

        private static void StartStartupMap(GameEngine engine)
        {
            Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo("crowd_physics_arena"));
            Assert.That(engine.MergedConfig.StartupLocalPlayerId, Is.GreaterThan(0));

            engine.Start();
            engine.LoadStartupMap();
            WaitForMassNavigationRuntimeReady(engine);
        }

        private static void WaitForMassNavigationRuntimeReady(GameEngine engine)
        {
            for (int frame = 0; frame < MaxWarmupFrames; frame++)
            {
                if (MassNavigationIds.IsCurrentNavigationRuntimeReady(engine))
                {
                    return;
                }

                TickFrames(engine, 1);
            }

            MassNavigationRuntimeBinding binding = RequireService(engine, MassNavigationKeys.RuntimeBinding);
            Assert.Fail(
                $"MassNavigation runtime did not become prepared within {MaxWarmupFrames} frames. " +
                $"currentMap={engine.CurrentMapSession?.MapId.Value ?? "<none>"}, bindingMap={binding.CurrentMapId.Value ?? "<none>"}, revision={binding.Revision}, preparedRevision={binding.PreparedRevision}.");
        }

        private static MassNavigationSimulationRuntime RequireMassNavigationSimulation(GameEngine engine)
        {
            return RequireService(engine, MassNavigationKeys.RuntimeBinding).RequireCurrent();
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, ShowcaseMods),
                Path.Combine(repoRoot, "assets"));
            ApplyHostAssets(engine);
            InstallInput(engine);
            HeadlessPresentationTestHost.Install(engine);
            return engine;
        }

        private static void ApplyHostAssets(GameEngine engine)
        {
            var meshAssets = RequireService(engine, CoreServiceKeys.PresentationMeshAssetRegistry);
            var materialAssets = RequireService(engine, CoreServiceKeys.PresentationMaterialRegistry);
            new PresentationHostAssetConfigLoader(engine.ConfigPipeline, meshAssets, materialAssets)
                .Apply("raylib", engine.ConfigCatalog, engine.ConfigConflictReport);
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new HeadlessInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static T RequireService<T>(GameEngine engine, ServiceKey<T> key)
        {
            T value = engine.GetService(key);
            return value ?? throw new InvalidOperationException($"{key.Name} service is missing.");
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

        private sealed class HeadlessInputBackend : IInputBackend
        {
            private readonly HashSet<string> _pressedButtons = new(StringComparer.Ordinal);
            private Vector2 _mousePosition;

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _pressedButtons.Contains(devicePath);
            public Vector2 GetMousePosition() => _mousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;

            public void SetMousePosition(Vector2 mousePosition)
            {
                _mousePosition = mousePosition;
            }

            public void SetButton(string devicePath, bool pressed)
            {
                if (pressed)
                {
                    _pressedButtons.Add(devicePath);
                    return;
                }

                _pressedButtons.Remove(devicePath);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Navigation2D
{
    [TestFixture]
    [NonParallelizable]
    public sealed class FormationPhysicsPlaygroundRuntimeLoadTests
    {
        private const float DeltaTime = 1f / 60f;

        private static readonly QueryDescription ScenarioQuery = new QueryDescription()
            .WithAll<NavAgent2D, NavGoal2D, WorldPositionCm, PreviousWorldPositionCm>();

        private static readonly QueryDescription PresentedScenarioQuery = new QueryDescription()
            .WithAll<NavAgent2D, NavGoal2D, VisualTemplateRef, PresentationStableId>();

        private static readonly QueryDescription VisualRuntimeScenarioQuery = new QueryDescription()
            .WithAll<NavAgent2D, NavGoal2D, VisualTemplateRef, VisualRuntimeState, PresentationStableId>();

        [Test]
        public void FormationPhysicsPlayground_LoadMap_SpawnsScenarioEntities()
        {
            using var engine = CreateEngine();

            Assert.DoesNotThrow(() => engine.LoadMap(engine.MergedConfig.StartupMapId));

            Tick(engine, frames: 5);

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "Formation map load should not leave trigger errors behind.");
            Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo("formation_physics_playground"));
            Assert.That(engine.World.CountEntities(in ScenarioQuery), Is.GreaterThan(0), "Formation acceptance lane should spawn runtime-owned scenario entities after map load.");
            Assert.That(engine.World.CountEntities(in PresentedScenarioQuery), Is.GreaterThan(0), "Formation entities should be presentation-ready so performer rendering has something to emit.");
            Assert.That(engine.World.CountEntities(in VisualRuntimeScenarioQuery), Is.GreaterThan(0), "Formation visuals should use the visual template SSOT instead of body performers.");

            VisualTemplateRegistry? templates = engine.GetService(CoreServiceKeys.PresentationVisualTemplateRegistry);
            int agentTemplateId = templates?.GetId("formation_playground.agent") ?? 0;
            int blockerTemplateId = templates?.GetId("formation_playground.blocker") ?? 0;
            Assert.That(agentTemplateId, Is.GreaterThan(0));
            Assert.That(blockerTemplateId, Is.GreaterThan(0));
            Assert.That(templates!.TryGet(agentTemplateId, out var agentTemplate), Is.True);
            Assert.That(agentTemplate.VisibleByDefault, Is.True);
            Assert.That(agentTemplate.RenderPath, Is.EqualTo(VisualRenderPath.StaticMesh));
            Assert.That(templates.TryGet(blockerTemplateId, out var blockerTemplate), Is.True);
            Assert.That(blockerTemplate.VisibleByDefault, Is.True);
            Assert.That(blockerTemplate.RenderPath, Is.EqualTo(VisualRenderPath.StaticMesh));
            Assert.That(engine.GlobalContext.TryGetValue("CoreInputMod.ActiveViewModeId", out object? activeModeObj) && activeModeObj is string activeModeId
                ? activeModeId
                : null, Is.EqualTo("FormationPhysics.Playground.Mode.Command"));
            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo("FormationPhysics.Playground.Camera.Command"));
            Assert.That(engine.GameSession.Camera.State.TargetCm.Length(), Is.GreaterThan(1000f), "Camera reset should frame the spawned formation instead of staying at the map origin.");
            Assert.That(engine.GameSession.Camera.State.DistanceCm, Is.EqualTo(18000f).Within(0.01f));
            Assert.That(engine.GameSession.Camera.State.Pitch, Is.EqualTo(65f).Within(0.01f));
            Assert.That(engine.GameSession.Camera.State.FovYDeg, Is.EqualTo(48f).Within(0.01f));
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string modsRoot = Path.Combine(repoRoot, "mods");

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                new List<string>
                {
                    Path.Combine(modsRoot, "LudotsCoreMod"),
                    Path.Combine(modsRoot, "CoreInputMod"),
                    Path.Combine(modsRoot, "FormationPhysicsPlaygroundMod"),
                },
                assetsRoot);

            InstallInput(engine);
            engine.Start();
            return engine;
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
            engine.SetService(CoreServiceKeys.InputBackend, backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(DeltaTime);
            }
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private sealed class HeadlessInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public System.Numerics.Vector2 GetMousePosition() => default;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}

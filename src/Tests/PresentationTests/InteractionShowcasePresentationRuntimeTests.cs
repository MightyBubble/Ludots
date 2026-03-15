using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using CoreInputMod.ViewMode;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using System.Numerics;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class InteractionShowcasePresentationRuntimeTests
    {
        private static readonly string[] ShowcaseMods =
        {
            "EntityInfoPanelsMod",
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "InteractionShowcaseMod",
            "EntityCommandPanelMod",
            "EntityCommandPanelShowcaseMod"
        };

        [Test]
        public void InteractionShowcaseHub_LoadsVisibleEntityPrimitives_AndCentersDefaultCameraOnEncounter()
        {
            using var engine = CreateEngine(ShowcaseMods);
            LoadMap(engine, "interaction_showcase_hub");

            int mapVisuals = 0;
            int skinnedCount = 0;
            int staticCount = 0;
            var query = new QueryDescription().WithAll<MapEntity, Name, VisualRuntimeState>();
            engine.World.Query(in query, (ref MapEntity _, ref Name _, ref VisualRuntimeState visual) =>
            {
                mapVisuals++;
                if (visual.RenderPath == VisualRenderPath.SkinnedMesh)
                {
                    skinnedCount++;
                }
                else if (visual.RenderPath == VisualRenderPath.StaticMesh)
                {
                    staticCount++;
                }
            });

            Assert.That(mapVisuals, Is.EqualTo(8), "Interaction showcase hub should spawn eight visible map entities with presentation runtime.");
            Assert.That(skinnedCount, Is.EqualTo(4), "Hero-side interaction fixtures should stay on the skinned lane.");
            Assert.That(staticCount, Is.EqualTo(4), "Enemy-side interaction fixtures should stay on the static lane.");

            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            Assert.That(primitives, Is.Not.Null);
            Assert.That(primitives!.Count, Is.EqualTo(8), "All showcase entities should emit world primitives once the map is loaded.");

            int visibleSkinned = 0;
            int visibleStatic = 0;
            foreach (ref readonly PrimitiveDrawItem item in primitives.GetSpan())
            {
                if (item.RenderPath == VisualRenderPath.SkinnedMesh)
                {
                    visibleSkinned++;
                }
                else if (item.RenderPath == VisualRenderPath.StaticMesh)
                {
                    visibleStatic++;
                }
            }

            Assert.That(visibleSkinned, Is.EqualTo(4));
            Assert.That(visibleStatic, Is.EqualTo(4));

            Vector2 target = engine.GameSession.Camera.State.TargetCm;
            Assert.That(target.X, Is.EqualTo(1630f).Within(0.1f), "Default camera should frame the showcase encounter instead of the world origin.");
            Assert.That(target.Y, Is.EqualTo(955f).Within(0.1f), "Default camera should frame the showcase encounter instead of the world origin.");
        }

        private static GameEngine CreateEngine(params string[] modIds)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, modIds);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            engine.Start();
            return engine;
        }

        private static void LoadMap(GameEngine engine, string mapId, int frames = 5)
        {
            engine.LoadMap(mapId);
            Tick(engine, frames);
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

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(1f / 60f);
            }
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

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}

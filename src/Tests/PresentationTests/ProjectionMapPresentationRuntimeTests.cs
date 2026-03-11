using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using CameraAcceptanceMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class ProjectionMapPresentationRuntimeTests
    {
        private static readonly string[] ProjectionMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraAcceptanceMod"
        };

        [Test]
        public void ProjectionMap_PopulatesVisualModels_AndEmitsWorldPrimitives()
        {
            using var engine = CreateEngine(ProjectionMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            int entitiesWithVisualModel = 0;
            var visualQuery = new QueryDescription().WithAll<Name, VisualModel>();
            engine.World.Query(in visualQuery, (ref Name _, ref VisualModel __) => entitiesWithVisualModel++);
            Assert.That(entitiesWithVisualModel, Is.EqualTo(3), "Projection fixture entities must all carry VisualModel.");

            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            Assert.That(primitives, Is.Not.Null);
            Assert.That(primitives!.Count, Is.EqualTo(3), "Entity visuals must emit one primitive draw item per visible fixture entity.");
        }

        [Test]
        public void ProjectionMap_HealthHud_UsesRegisteredAttributeValues()
        {
            using var engine = CreateEngine(ProjectionMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var hud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            Assert.That(hud, Is.Not.Null);

            int barCount = 0;
            int textCount = 0;
            foreach (ref readonly var item in hud!.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Bar)
                {
                    barCount++;
                    Assert.That(item.Value0, Is.EqualTo(1f).Within(0.001f), "Health bar fill must resolve the real Health ratio.");
                }

                if (item.Kind == WorldHudItemKind.Text)
                {
                    textCount++;
                    Assert.That(item.Value0, Is.EqualTo(100f).Within(0.001f), "Health text current value must come from AttributeBuffer.");
                    Assert.That(item.Value1, Is.EqualTo(100f).Within(0.001f), "Health text base value must come from AttributeBuffer.");
                    Assert.That(item.Id1, Is.EqualTo((int)WorldHudValueMode.AttributeCurrentOverBase));
                }
            }

            Assert.That(barCount, Is.EqualTo(3));
            Assert.That(textCount, Is.EqualTo(3));
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

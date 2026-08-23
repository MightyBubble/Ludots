using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using CoreInputMod.ViewMode;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;
using System.Numerics;
using Ludots.Tests.TestCommon;

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

            var presenters = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)
                ?? throw new InvalidOperationException("PresenterEntityRuntime missing.");

            int presenterVisuals = 0;
            var presenterQuery = new QueryDescription().WithAll<PresenterState>();
            engine.World.Query(in presenterQuery, (Entity entity, ref PresenterState state) =>
            {
                if (state.AnchorKind != PresentationAnchorKind.Entity || !engine.World.IsAlive(state.OwnerEntity) || !engine.World.Has<MapEntity>(state.OwnerEntity))
                {
                    return;
                }

                presenterVisuals++;
            });

            Assert.That(presenterVisuals, Is.GreaterThanOrEqualTo(8), "Interaction showcase hub should bootstrap presenter instances for encounter actors.");

            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
            var snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer)
                ?? throw new InvalidOperationException("PresentationVisualSnapshotBuffer missing.");
            var skinnedBatch = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer)
                ?? throw new InvalidOperationException("PresentationSkinnedVisualBatchBuffer missing.");

            Assert.That(primitives.Count, Is.EqualTo(4), "Static showcase actors should publish visible primitive draw items once the map is loaded.");
            Assert.That(snapshot.Count, Is.EqualTo(4), "Primitive snapshot should expose the visible static presenter lane.");
            Assert.That(skinnedBatch.Count, Is.EqualTo(4), "Skinned showcase actors should publish visible skinned batch items once the map is loaded.");

            int visibleSkinned = 0;
            int visibleStatic = 0;
            foreach (ref readonly PrimitiveDrawItem item in primitives.GetSpan())
            {
                Assert.That(item.StableId, Is.GreaterThan(0));
                Assert.That(item.TemplateId, Is.GreaterThan(0));
                Assert.That(item.Visibility, Is.EqualTo(VisualVisibility.Visible));
                if (item.RenderPath == VisualRenderPath.StaticMesh)
                {
                    visibleStatic++;
                }
            }

            foreach (ref readonly SkinnedVisualBatchItem item in skinnedBatch.GetSpan())
            {
                Assert.That(item.StableId, Is.GreaterThan(0));
                Assert.That(item.TemplateId, Is.GreaterThan(0));
                Assert.That(item.RenderPath, Is.EqualTo(VisualRenderPath.SkinnedMesh));
                Assert.That(item.Animator.GetControllerId(), Is.GreaterThan(0));
                Assert.That(item.Visibility, Is.EqualTo(VisualVisibility.Visible));
                visibleSkinned++;
            }

            Assert.That(visibleSkinned, Is.EqualTo(4));
            Assert.That(visibleStatic, Is.EqualTo(4));

            Vector2 target = engine.AuthorityCamera().State.TargetCm;
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
            PresentationAcceptanceUiHostInstaller.Install(engine, 1600f, 900f);
            HeadlessPresentationTestHost.Install(engine);
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
                HeadlessPresentationTestHost.UpdateCamera(engine);
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

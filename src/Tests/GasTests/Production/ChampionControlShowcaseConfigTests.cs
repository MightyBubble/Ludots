using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    public sealed class ChampionControlShowcaseConfigTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string ControlMapId = "champion_control_showcase";

        private static readonly string[] ShowcaseMods =
        {
            "LudotsCoreMod",
            "CommonControlBuffsMod",
            "CommonControlBuffsPresentationMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "DiagnosticsOverlayMod",
            "EntityCommandPanelMod",
            "ChampionSkillSandboxMod"
        };

        private static readonly string[] EntryMods =
        {
            "LudotsCoreMod",
            "CommonControlBuffsMod",
            "CommonControlBuffsPresentationMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "DiagnosticsOverlayMod",
            "EntityCommandPanelMod",
            "ChampionSkillSandboxMod",
            "ChampionControlShowcaseEntryMod"
        };

        [Test]
        public void ChampionControlShowcase_MapLoad_RegistersCommonControlEffectsAndPlayableMarshalLoadout()
        {
            using var engine = CreateEngine(ShowcaseMods);
            LoadMap(engine, ControlMapId);

            var overlays = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");
            var performers = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry missing.");

            Assert.That(EffectTemplateIdRegistry.GetId("Effect.Control.Common.Slow.Light"), Is.GreaterThan(0));
            Assert.That(EffectTemplateIdRegistry.GetId("Effect.Control.Common.Slow.Heavy"), Is.GreaterThan(0));
            Assert.That(EffectTemplateIdRegistry.GetId("Effect.Control.Common.Silence"), Is.GreaterThan(0));
            Assert.That(EffectTemplateIdRegistry.GetId("Effect.Control.Common.Root"), Is.GreaterThan(0));
            Assert.That(EffectTemplateIdRegistry.GetId("Effect.Control.Common.Stun"), Is.GreaterThan(0));
            Assert.That(EffectTemplateIdRegistry.GetId("Effect.ControlShowcase.Caster.ArcPulse"), Is.GreaterThan(0));

            Assert.That(performers.GetId("control.common.status.slowed"), Is.GreaterThan(0));
            Assert.That(performers.GetId("control.common.status.silenced"), Is.GreaterThan(0));
            Assert.That(performers.GetId("control.common.status.rooted"), Is.GreaterThan(0));
            Assert.That(performers.GetId("control.common.status.stunned"), Is.GreaterThan(0));

            Assert.That(OverlayContainsText(overlays, "Control Showcase"), Is.True);
            Assert.That(OverlayContainsText(overlays, "Q slow | W silence | E root | R stun"), Is.True);

            Entity marshal = FindEntityByName(engine.World, "Control Marshal");
            Entity runner = FindEntityByName(engine.World, "Control Runner");
            Entity caster = FindEntityByName(engine.World, "Control Caster");

            Assert.That(engine.World.Has<AbilityStateBuffer>(marshal), Is.True);
            Assert.That(engine.World.Has<GameplayTagContainer>(runner), Is.True);
            Assert.That(engine.World.Has<AbilityStateBuffer>(caster), Is.True);

            var slots = new EntityCommandPanelSlotView[8];
            int count = ResolveGasPanelSource(engine).CopySlots(marshal, 0, slots);
            Assert.That(count, Is.EqualTo(4));
            Assert.That(slots[0].DisplayLabel, Is.EqualTo("Crippling Shot"));
            Assert.That(slots[1].DisplayLabel, Is.EqualTo("Hush Seal"));
            Assert.That(slots[2].DisplayLabel, Is.EqualTo("Iron Snare"));
            Assert.That(slots[3].DisplayLabel, Is.EqualTo("Shock Jail"));
        }

        [Test]
        public void ChampionControlShowcase_EntryMod_SetsStartupMapId()
        {
            using var engine = CreateEngine(EntryMods);
            Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo(ControlMapId));
        }

        private static GameEngine CreateEngine(string[] modIds)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, modIds);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            InstallUi(engine);
            engine.Start();
            return engine;
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new NullInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static void InstallUi(GameEngine engine)
        {
            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());
        }

        private static void LoadMap(GameEngine engine, string mapId, int frames = 12)
        {
            engine.LoadMap(mapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null, $"{mapId} should create a live map session.");
            Tick(engine, frames);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "Control showcase map should load without trigger errors.");
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(DeltaTime);
            }
        }

        private static bool OverlayContainsText(ScreenOverlayBuffer overlay, string expected)
        {
            foreach (ref readonly var item in overlay.GetSpan())
            {
                if (item.Kind != ScreenOverlayItemKind.Text)
                {
                    continue;
                }

                string? text = overlay.GetString(item.StringId);
                if (!string.IsNullOrEmpty(text) &&
                    text.Contains(expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEntityCommandPanelSource ResolveGasPanelSource(GameEngine engine)
        {
            var registry = engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry)
                ?? throw new InvalidOperationException("EntityCommandPanelSourceRegistry missing.");
            Assert.That(registry.TryGet("gas.ability-slots", out IEntityCommandPanelSource source), Is.True);
            return source;
        }

        private static Entity FindEntityByName(World world, string entityName)
        {
            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (found != Entity.Null)
                {
                    return;
                }

                if (string.Equals(name.Value, entityName, StringComparison.Ordinal))
                {
                    found = entity;
                }
            });

            Assert.That(found, Is.Not.EqualTo(Entity.Null), $"Entity '{entityName}' should exist on {ControlMapId}.");
            return found;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string srcDir = Path.Combine(dir.FullName, "src");
                string assetsDir = Path.Combine(dir.FullName, "assets");
                if (Directory.Exists(srcDir) && Directory.Exists(assetsDir))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
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

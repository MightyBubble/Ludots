using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Progression.Components;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    public sealed class ProgressionScopeShowcaseAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;

        [Test]
        public void EntityScopedProgressionShowcase_UnlocksSlotsAcrossExplicitScopes()
        {
            var frames = new List<double>();
            using var engine = CreateEngine();
            engine.LoadMap("progression_scope_showcase");
            Tick(engine, 5, frames);

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Assert.That(engine.CurrentMapSession?.MapConfig?.Id, Is.EqualTo("progression_scope_showcase"));
            Assert.That(engine.CurrentMapSession?.MapConfig?.Tags, Does.Contain("progression_scope_showcase"));
            Assert.That(engine.CurrentMapSession?.MapConfig?.Tags, Does.Not.Contain("rts_showcase"));
            Assert.That(engine.CurrentMapSession?.MapConfig?.Tags, Does.Not.Contain("war3"));
            Assert.That(engine.CurrentMapSession?.MapConfig?.Tags, Does.Not.Contain("cnc"));
            Assert.That(engine.CurrentMapSession?.MapConfig?.Tags, Does.Not.Contain("sc2"));

            Assert.That(engine.GetService(CoreServiceKeys.ProgressionDefinitionRegistry), Is.Not.Null);
            Assert.That(engine.GetService(CoreServiceKeys.ProgressionRequirementRegistry), Is.Not.Null);
            Assert.That(engine.GetService(CoreServiceKeys.ProgressionScopeKeyRegistry), Is.Not.Null);
            Assert.That(engine.GetService(CoreServiceKeys.ProgressionRequirementEvaluator), Is.Not.Null);
            Assert.That(engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry), Is.Not.Null);

            World world = engine.World;
            Entity barracks = FindEntity(world, "Chang An Barracks");
            Entity cityHost = FindEntity(world, "Chang An City Scope");
            Entity factionHost = FindEntity(world, "Shu Faction Scope");
            Entity regionHost = FindEntity(world, "Jing Region Scope");
            Entity provinceHost = FindEntity(world, "Guanzhong Province Scope");
            Entity hero = FindEntity(world, "Strategist Hero");

            Assert.That(world.Has<ProgressionStateBuffer>(cityHost), Is.True);
            Assert.That(world.Has<ProgressionStateBuffer>(factionHost), Is.True);
            Assert.That(world.Has<ProgressionStateBuffer>(regionHost), Is.True);
            Assert.That(world.Has<ProgressionStateBuffer>(provinceHost), Is.True);
            Assert.That(world.Has<ProgressionScopeRefBuffer>(barracks), Is.True);
            Assert.That(world.Has<ProgressionScopeRefBuffer>(hero), Is.True);

            IReadOnlyList<EntityCommandPanelSlotView> initialSlots = ResolveSlots(engine, barracks);
            AssertBlocked(initialSlots, "Train Militia");
            AssertHidden(initialSlots, "Raise Elite");
            AssertBlocked(initialSlots, "Regional Stratagem");
            AssertBlocked(initialSlots, "Province Convoy");
            uint initialRevision = ResolveRevision(engine, barracks);

            PublishCompleteProgression(engine, barracks, cityHost, "Effect.Showcase.CompleteCityDrill");
            Tick(engine, 4, frames);
            IReadOnlyList<EntityCommandPanelSlotView> citySlots = ResolveSlots(engine, barracks);
            AssertAvailable(citySlots, "Train Militia");
            Assert.That(ResolveRevision(engine, barracks), Is.Not.EqualTo(initialRevision));

            PublishCompleteProgression(engine, barracks, factionHost, "Effect.Showcase.CompleteFactionMandate");
            Tick(engine, 4, frames);
            IReadOnlyList<EntityCommandPanelSlotView> factionSlots = ResolveSlots(engine, barracks);
            AssertAvailable(factionSlots, "Raise Elite");

            PublishCompleteProgression(engine, barracks, provinceHost, "Effect.Showcase.CompleteProvinceLogistics");
            Tick(engine, 4, frames);
            IReadOnlyList<EntityCommandPanelSlotView> provinceSlots = ResolveSlots(engine, barracks);
            AssertAvailable(provinceSlots, "Province Convoy");

            int strategistTagId = TagRegistry.GetId("Hero.Showcase.Strategist");
            Assert.That(strategistTagId, Is.GreaterThan(0));
            uint beforeHeroRevision = ResolveRevision(engine, barracks);
            AddTag(engine, hero, strategistTagId);
            Tick(engine, 2, frames);
            Assert.That(ResolveRevision(engine, barracks), Is.Not.EqualTo(beforeHeroRevision));
            IReadOnlyList<EntityCommandPanelSlotView> heroSlots = ResolveSlots(engine, barracks);
            AssertAvailable(heroSlots, "Regional Stratagem");

            var requirements = engine.GetService(CoreServiceKeys.ProgressionRequirementEvaluator)
                ?? throw new InvalidOperationException("ProgressionRequirementEvaluator service is missing.");
            int regionReqId = ProgressionRequirementIdRegistry.GetId("Req.Showcase.RegionStrategist.Use");
            var context = new ProgressionRequirementEvaluationContext(barracks, barracks);
            Assert.That(requirements.Evaluate(regionReqId, in context), Is.True);
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, new[]
            {
                "LudotsCoreMod",
                "CoreInputMod",
                "EntityCommandPanelMod",
                "ProgressionScopeShowcaseMod"
            });

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallDummyInput(engine);
            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());
            engine.Start();
            return engine;
        }

        private static IReadOnlyList<EntityCommandPanelSlotView> ResolveSlots(GameEngine engine, Entity target)
        {
            var registry = engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry)
                ?? throw new InvalidOperationException("EntityCommandPanelSourceRegistry service is missing.");
            Assert.That(registry.TryGet("gas.ability-slots", out IEntityCommandPanelSource source), Is.True);

            var slots = new EntityCommandPanelSlotView[8];
            int count = source.CopySlots(target, 0, slots);
            return slots.Take(count).ToArray();
        }

        private static uint ResolveRevision(GameEngine engine, Entity target)
        {
            var registry = engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry)
                ?? throw new InvalidOperationException("EntityCommandPanelSourceRegistry service is missing.");
            Assert.That(registry.TryGet("gas.ability-slots", out IEntityCommandPanelSource source), Is.True);
            Assert.That(source.TryGetRevision(target, out uint revision), Is.True);
            return revision;
        }

        private static void AssertBlocked(IReadOnlyList<EntityCommandPanelSlotView> slots, string label)
        {
            EntityCommandPanelSlotView slot = FindSlot(slots, label);
            Assert.That((slot.StateFlags & EntityCommandSlotStateFlags.Blocked) != 0, Is.True, $"{label} should be blocked.");
        }

        private static void AssertAvailable(IReadOnlyList<EntityCommandPanelSlotView> slots, string label)
        {
            EntityCommandPanelSlotView slot = FindSlot(slots, label);
            Assert.That((slot.StateFlags & EntityCommandSlotStateFlags.Empty) == 0, Is.True, $"{label} should be visible.");
            Assert.That((slot.StateFlags & EntityCommandSlotStateFlags.Blocked) == 0, Is.True, $"{label} should be available.");
        }

        private static void AssertHidden(IReadOnlyList<EntityCommandPanelSlotView> slots, string label)
        {
            Assert.That(slots.Any(slot => string.Equals(slot.DisplayLabel, label, StringComparison.OrdinalIgnoreCase)), Is.False);
        }

        private static EntityCommandPanelSlotView FindSlot(IReadOnlyList<EntityCommandPanelSlotView> slots, string label)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (string.Equals(slots[i].DisplayLabel, label, StringComparison.OrdinalIgnoreCase))
                {
                    return slots[i];
                }
            }

            throw new InvalidOperationException($"Missing command panel slot '{label}'.");
        }

        private static void PublishCompleteProgression(GameEngine engine, Entity source, Entity scopeHost, string effectId)
        {
            var queue = engine.GetService(CoreServiceKeys.EffectRequestQueue)
                ?? throw new InvalidOperationException("EffectRequestQueue service is missing.");
            int templateId = EffectTemplateIdRegistry.GetId(effectId);
            Assert.That(templateId, Is.GreaterThan(0));
            queue.Publish(new EffectRequest
            {
                Source = source,
                Target = source,
                TargetContext = scopeHost,
                TemplateId = templateId
            });
        }

        private static void AddTag(GameEngine engine, Entity entity, int tagId)
        {
            if (!engine.World.Has<GameplayTagContainer>(entity))
            {
                engine.World.Add(entity, new GameplayTagContainer());
            }

            if (!engine.World.Has<TagCountContainer>(entity))
            {
                engine.World.Add(entity, new TagCountContainer());
            }

            if (!engine.World.Has<DirtyFlags>(entity))
            {
                engine.World.Add(entity, new DirtyFlags());
            }

            var tagOps = engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("TagOps service is missing.");
            ref var tags = ref engine.World.Get<GameplayTagContainer>(entity);
            ref var counts = ref engine.World.Get<TagCountContainer>(entity);
            ref var dirty = ref engine.World.Get<DirtyFlags>(entity);
            Assert.That(tagOps.AddTag(ref tags, ref counts, tagId, ref dirty), Is.True);
        }

        private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
        {
            var stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
            for (int i = 0; i < frames; i++)
            {
                if (stepPolicy.Mode == GasStepMode.Manual)
                {
                    stepPolicy.RequestStep(1);
                }

                var stopwatch = Stopwatch.StartNew();
                engine.Tick(DeltaTime);
                stopwatch.Stop();
                frameTimesMs.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        private static Entity FindEntity(World world, string entityName)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });

            if (result == Entity.Null)
            {
                throw new InvalidOperationException($"Missing entity '{entityName}'.");
            }

            return result;
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                string candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not locate repository root.");
        }

        private static void InstallDummyInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
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

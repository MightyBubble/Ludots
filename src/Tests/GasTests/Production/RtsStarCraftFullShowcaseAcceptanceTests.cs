using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    public sealed class RtsStarCraftFullShowcaseAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string MapId = "rts_starcraft_full";

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "EntityCommandPanelMod",
            "RtsDemoMod",
            "BrowserRtsProductionShowcaseMod",
            "CombatStanceBehaviorMod",
            "RtsStarCraftFullShowcaseMod"
        };

        [Test]
        public void RtsStarCraftFull_ContentAssets_AreCompleteAndReferenceClean()
        {
            string root = FindRepoRoot();
            string modRoot = Path.Combine(root, "mods", "showcases", "rts_starcraft_full", "RtsStarCraftFullShowcaseMod");
            string assets = Path.Combine(modRoot, "assets");

            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(modRoot, "mod.json")));
            JsonElement dependencies = manifest.RootElement.GetProperty("dependencies");
            Assert.That(dependencies.TryGetProperty("BrowserRtsProductionShowcaseMod", out _), Is.True);
            Assert.That(dependencies.TryGetProperty("RtsDemoMod", out _), Is.True);
            Assert.That(dependencies.TryGetProperty("CombatStanceBehaviorMod", out _), Is.True);

            using JsonDocument game = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "game.json")));
            Assert.That(game.RootElement.GetProperty("startupMapId").GetString(), Is.EqualTo(MapId));
            Assert.That(game.RootElement.GetProperty("startupLocalPlayerId").GetInt32(), Is.EqualTo(1));
            JsonElement browserRuntime = game.RootElement.GetProperty("browserRuntime");
            Assert.That(browserRuntime.GetProperty("enabled").GetBoolean(), Is.True);
            Assert.That(browserRuntime.GetProperty("required").GetBoolean(), Is.True);
            Assert.That(browserRuntime.GetProperty("provider").GetString(), Is.EqualTo("cef"));

            using JsonDocument templatesDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "Entities", "templates.json")));
            using JsonDocument abilitiesDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "GAS", "abilities.json")));
            using JsonDocument formSetsDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "GAS", "ability_form_sets.json")));
            using JsonDocument effectsDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "GAS", "effects.json")));
            using JsonDocument graphsDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "GAS", "graphs.json")));
            using JsonDocument itemsDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "Items", "definitions.json")));
            using JsonDocument presentersDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "Presentation", "presenters.json")));
            using JsonDocument meshesDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "Presentation", "mesh_assets.json")));
            using JsonDocument mapDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "Maps", "rts_starcraft_full.json")));

            JsonElement templates = templatesDoc.RootElement;
            JsonElement abilities = abilitiesDoc.RootElement;
            JsonElement formSets = formSetsDoc.RootElement;
            JsonElement effects = effectsDoc.RootElement;
            JsonElement graphs = graphsDoc.RootElement;
            JsonElement items = itemsDoc.RootElement;
            JsonElement presenters = presentersDoc.RootElement;
            JsonElement meshes = meshesDoc.RootElement;
            JsonElement map = mapDoc.RootElement;

            Assert.That(templates.GetArrayLength(), Is.EqualTo(100));
            Assert.That(CountPrefix(templates, "scf_terran_"), Is.EqualTo(34));
            Assert.That(CountPrefix(templates, "scf_zerg_"), Is.EqualTo(33));
            Assert.That(CountPrefix(templates, "scf_protoss_"), Is.EqualTo(33));
            Assert.That(CountPrefix(presenters, "scf.visual.scf_"), Is.EqualTo(100));
            Assert.That(CountRootVisualPresenters(presenters), Is.EqualTo(100));
            Assert.That(meshes.GetArrayLength(), Is.EqualTo(100));
            Assert.That(map.GetProperty("Entities").GetArrayLength(), Is.EqualTo(100));
            Assert.That(graphs.GetArrayLength(), Is.GreaterThanOrEqualTo(3));
            Assert.That(items.GetArrayLength(), Is.EqualTo(12));

            HashSet<string> templateIds = AssertUniqueIds(templates, "entity templates");
            HashSet<string> abilityIds = AssertUniqueIds(abilities, "abilities");
            HashSet<string> formSetIds = AssertUniqueIds(formSets, "ability form sets");
            HashSet<string> effectIds = AssertUniqueIds(effects, "effects");
            HashSet<string> graphIds = AssertUniqueIds(graphs, "graphs");
            HashSet<string> presenterIds = AssertUniqueIds(presenters, "presenters");
            HashSet<string> meshIds = AssertUniqueIds(meshes, "mesh assets");
            AssertUniqueIds(items, "items");
            Assert.That(graphIds.Contains("Graph.Scf.Mining.Minerals"), Is.True);

            foreach (string templateId in templateIds)
            {
                Assert.That(presenterIds.Contains("scf.visual." + templateId), Is.True, $"Missing presenter for {templateId}.");
            }

            HashSet<string> mapInstances = new(StringComparer.Ordinal);
            HashSet<string> mapTemplateIds = new(StringComparer.Ordinal);
            foreach (JsonElement entity in map.GetProperty("Entities").EnumerateArray())
            {
                string templateId = entity.GetProperty("Template").GetString()!;
                Assert.That(templateIds.Contains(templateId), Is.True);
                Assert.That(mapTemplateIds.Add(templateId), Is.True, $"Map should place template {templateId} only once.");
                Assert.That(mapInstances.Add(entity.GetProperty("InstanceId").GetString()!), Is.True);
            }

            Assert.That(mapTemplateIds.SetEquals(templateIds), Is.True, "Every SCF entity template should be visible as a map entity.");

            Assert.That(map.GetProperty("Teams").GetArrayLength(), Is.EqualTo(3));
            Assert.That(map.GetProperty("Players").GetArrayLength(), Is.EqualTo(3));
            foreach (JsonElement team in map.GetProperty("Teams").EnumerateArray())
            {
                Assert.That(mapInstances.Contains(team.GetProperty("RepresentativeInstanceId").GetString()!), Is.True);
            }
            foreach (JsonElement player in map.GetProperty("Players").EnumerateArray())
            {
                Assert.That(mapInstances.Contains(player.GetProperty("RepresentativeInstanceId").GetString()!), Is.True);
            }

            foreach (string abilityRef in EnumerateTemplateAbilityRefs(templates))
            {
                if (abilityRef.StartsWith("Ability.Scf.", StringComparison.Ordinal))
                {
                    Assert.That(abilityIds.Contains(abilityRef), Is.True, $"Missing local ability {abilityRef}.");
                }
            }

            foreach (string formSetRef in EnumerateTemplateFormSetRefs(templates))
            {
                Assert.That(formSetIds.Contains(formSetRef), Is.True, $"Missing ability form set {formSetRef}.");
            }

            foreach (string effectRef in EnumerateEffectRefs(abilities, items, graphs))
            {
                Assert.That(effectIds.Contains(effectRef), Is.True, $"Missing effect {effectRef}.");
            }

            HashSet<string> presenterMeshRefs = EnumeratePresenterMeshRefs(presenters).ToHashSet(StringComparer.Ordinal);
            Assert.That(presenterMeshRefs.Count, Is.EqualTo(100));
            foreach (string templateId in templateIds)
            {
                Assert.That(meshIds.Contains("scf.prim." + templateId), Is.True, $"Missing primitive mesh for {templateId}.");
                Assert.That(presenterMeshRefs.Contains("scf.prim." + templateId), Is.True, $"Presenter for {templateId} should bind its SCF primitive mesh.");
            }

            HashSet<string> phaseGraphRefs = EnumerateEffectPhaseGraphRefs(effects).ToHashSet(StringComparer.Ordinal);
            Assert.That(graphIds.Contains("Graph.Scf.Zerg.RegenOnKill"), Is.True);
            Assert.That(graphIds.Contains("Graph.Scf.Terran.ArmorShred"), Is.True);
            Assert.That(graphIds.Contains("Graph.Scf.Protoss.ShieldRecharge"), Is.True);
            Assert.That(phaseGraphRefs.Contains("Graph.Scf.Zerg.RegenOnKill"), Is.True);
            Assert.That(phaseGraphRefs.Contains("Graph.Scf.Terran.ArmorShred"), Is.True);
            Assert.That(phaseGraphRefs.Contains("Graph.Scf.Protoss.ShieldRecharge"), Is.True);
            foreach (string graphRef in phaseGraphRefs)
            {
                Assert.That(graphIds.Contains(graphRef), Is.True, $"Missing graph {graphRef}.");
            }
        }

        [Test]
        public void RtsStarCraftFull_LoadsBrowserDataPlaneAndPlaysProductionLoop()
        {
            var frameTimesMs = new List<double>();
            using GameEngine engine = CreateEngine(out FakeBrowserRuntime browserRuntime);

            Assert.That(browserRuntime.SurfaceCreateCount, Is.EqualTo(1));
            Assert.That(browserRuntime.LastSurface?.LastNavigationUri?.ToString(), Is.EqualTo("ludots-app://app/"));

            engine.LoadMap(MapId);
            Tick(engine, 30, frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Assert.That(engine.CurrentMapSession?.MapConfig?.Id, Is.EqualTo(MapId));

            World world = engine.World;
            Entity commandCenter = FindEntity(world, "Command Center");
            Entity hatchery = FindEntity(world, "Hatchery");
            Entity nexus = FindEntity(world, "Nexus");
            Entity marine = FindEntity(world, "Marine");
            Entity zergling = FindEntity(world, "Zergling");
            Entity zealot = FindEntity(world, "Zealot");

            TickUntil(
                engine,
                frameTimesMs,
                () =>
                    CountActiveEffects(world, commandCenter, "Effect.Scf.Item.Terran.StimPack") > 0 &&
                    CountActiveEffects(world, hatchery, "Effect.Scf.Item.Zerg.MetabolicBoost") > 0 &&
                    CountActiveEffects(world, nexus, "Effect.Scf.Item.Protoss.ShieldBoost") > 0,
                180,
                "Race headquarters should equip SCF upgrade items and receive passive GAS effects.");

            TickUntil(
                engine,
                frameTimesMs,
                () => CountActiveEffects(world, marine, "Effect.Scf.AutoAttack.terran.marine") > 0,
                180,
                "Map Marines should receive the StarCraft full auto-attack buff through GAS.");

            int healthId = EnsureAttribute("Health");
            int shieldId = EnsureAttribute("Shield");
            PublishEffect(engine, marine, zergling, "Effect.Scf.Damage.terran.marine");
            TickUntil(
                engine,
                frameTimesMs,
                () => CountActiveEffects(world, zergling, "Effect.Scf.Graph.ArmorShred") > 0,
                90,
                "Terran damage should execute its OnApply graph and apply armor shred.");

            float zerglingHealthBefore = ReadAttribute(world, zergling, healthId);
            PublishEffect(engine, zergling, marine, "Effect.Scf.Damage.zerg.zergling");
            TickUntil(
                engine,
                frameTimesMs,
                () => ReadAttribute(world, zergling, healthId) > zerglingHealthBefore,
                90,
                "Zerg damage should execute its OnApply graph and regenerate the source.");

            float zealotShieldBefore = ReadAttribute(world, zealot, shieldId);
            PublishEffect(engine, zealot, zealot, "Effect.Scf.Damage.protoss.zealot");
            TickUntil(
                engine,
                frameTimesMs,
                () => ReadAttribute(world, zealot, shieldId) > zealotShieldBefore,
                90,
                "Protoss damage should execute its OnApply graph and recharge shields.");

            IEntityCommandPanelSource panelSource = ResolveGasPanelSource(engine);
            Entity scv = FindEntity(world, "SCV");
            EntityCommandPanelSlotView[] scvSlots = CopySlots(panelSource, scv);
            EntityCommandPanelSlotView harvestSlot = scvSlots.First(slot => string.Equals(slot.DisplayLabel, "Harvest Minerals", StringComparison.Ordinal));
            int mineralsId = EnsureAttribute("Minerals");
            float teamMineralsBeforeHarvest = ReadTeamAttributeTotal(world, teamId: 1, mineralsId);

            CastAbility(engine, scv, scv, slot: harvestSlot.SlotIndex);
            TickUntil(
                engine,
                frameTimesMs,
                () =>
                    CountActiveEffects(world, scv, "Effect.Scf.Mining.Minerals") > 0 &&
                    ReadTeamAttributeTotal(world, teamId: 1, mineralsId) > teamMineralsBeforeHarvest,
                180,
                "SCV should start a periodic mining GAS buff and increase the team mineral total.");

            EntityCommandPanelSlotView[] slots = CopySlots(panelSource, commandCenter);
            Assert.That(slots.Any(slot => string.Equals(slot.DisplayLabel, "Train SCV", StringComparison.Ordinal)), Is.True);
            EntityCommandPanelSlotView trainMarineSlot = slots.First(slot => string.Equals(slot.DisplayLabel, "Train Marine", StringComparison.Ordinal));
            Assert.That(trainMarineSlot.SlotIndex, Is.GreaterThanOrEqualTo(0));

            float mineralsBefore = ReadAttribute(world, commandCenter, mineralsId);
            int marineCountBefore = CountEntitiesByName(world, "Marine");

            CastAbility(engine, commandCenter, commandCenter, slot: trainMarineSlot.SlotIndex);
            TickUntil(
                engine,
                frameTimesMs,
                () => CountEntitiesByName(world, "Marine") > marineCountBefore,
                600,
                "Command Center should train a Marine through the GAS CreateUnit pipeline.");

            float mineralsAfter = ReadAttribute(world, commandCenter, mineralsId);
            Assert.That(mineralsAfter, Is.LessThan(mineralsBefore));

            TickUntil(
                engine,
                frameTimesMs,
                () => CountEntitiesByNameWithEffect(world, "Marine", "Effect.Scf.AutoAttack.terran.marine") >= marineCountBefore + 1,
                180,
                "Newly trained Marines should also receive auto-attack, not only map-placed units.");

            Entity assaultMarine = FindEntity(world, "Marine");
            EntityCommandPanelSlotView[] marineSlots = CopySlots(panelSource, assaultMarine);
            EntityCommandPanelSlotView assaultSlot = marineSlots.First(slot => string.Equals(slot.DisplayLabel, "Assault Hatchery", StringComparison.Ordinal));
            CastAbility(engine, assaultMarine, assaultMarine, slot: assaultSlot.SlotIndex);
            TickUntil(
                engine,
                frameTimesMs,
                () =>
                    engine.GlobalContext.TryGetValue("scf.scenario.victory", out object? victory) &&
                    victory is true,
                1800,
                "Assault Hatchery should drive the scenario through GAS damage effects to a victory state.");

            Assert.That(TryFindEntity(world, "Hatchery", out _), Is.False, "Victory should eliminate the Zerg Hatchery entity.");
            Assert.That(CountAliveTeamEntities(world, teamId: 2, healthId), Is.EqualTo(0), "Victory should eliminate the opponent team.");
        }

        private static GameEngine CreateEngine(out FakeBrowserRuntime browserRuntime)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallDummyInput(engine);

            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1600f, 900f);
            var textMeasurer = new SkiaTextMeasurer();
            var imageSizeProvider = new SkiaImageSizeProvider();
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)textMeasurer);
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)imageSizeProvider);
            engine.SetService(CoreServiceKeys.UiSurfaceHost, new UiSurfaceHost(uiRoot, textMeasurer, imageSizeProvider));

            browserRuntime = new FakeBrowserRuntime();
            engine.SetService(new ServiceKey<IBrowserRuntime>(BrowserRuntimeServiceNames.BrowserRuntime), browserRuntime);
            engine.Start();
            return engine;
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

        private static void TickUntil(GameEngine engine, List<double> frameTimesMs, Func<bool> condition, int maxFrames, string because)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (condition())
                {
                    return;
                }

                Tick(engine, 1, frameTimesMs);
            }

            Assert.That(condition(), Is.True, $"{because} {DescribeScenario(engine)}");
        }

        private static string DescribeScenario(GameEngine engine)
        {
            string phase = engine.GlobalContext.TryGetValue("scf.scenario.phase", out object? phaseValue)
                ? Convert.ToString(phaseValue) ?? ""
                : "";
            string lastEvent = engine.GlobalContext.TryGetValue("scf.scenario.lastEvent", out object? eventValue)
                ? Convert.ToString(eventValue) ?? ""
                : "";
            string enemyHealth = engine.GlobalContext.TryGetValue("scf.scenario.enemyHqHealth", out object? healthValue)
                ? Convert.ToString(healthValue) ?? ""
                : "";
            string army = engine.GlobalContext.TryGetValue("scf.scenario.armyCount", out object? armyValue)
                ? Convert.ToString(armyValue) ?? ""
                : "";
            return $"Scenario phase='{phase}', enemyHqHealth='{enemyHealth}', army='{army}', event='{lastEvent}'.";
        }

        private static void CastAbility(GameEngine engine, Entity actor, Entity target, int slot)
        {
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
                ?? throw new InvalidOperationException("OrderQueue service is missing.");

            bool enqueued = orderQueue.TryEnqueue(new Order
            {
                OrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["castAbility"],
                PlayerId = 1,
                Actor = actor,
                Target = target,
                Args = new OrderArgs { I0 = slot },
                SubmitMode = OrderSubmitMode.Immediate
            });

            Assert.That(enqueued, Is.True, "Ability order should enqueue.");
        }

        private static void PublishEffect(GameEngine engine, Entity source, Entity target, string effectKey)
        {
            var effectRequests = engine.GetService(CoreServiceKeys.EffectRequestQueue)
                ?? throw new InvalidOperationException("EffectRequestQueue service is missing.");
            int templateId = EffectTemplateIdRegistry.GetId(effectKey);
            Assert.That(templateId, Is.GreaterThan(0), $"Effect template should be registered: {effectKey}");

            effectRequests.Publish(new EffectRequest
            {
                Source = source,
                Target = target,
                TargetContext = target,
                TemplateId = templateId
            });
        }

        private static EntityCommandPanelSlotView[] CopySlots(IEntityCommandPanelSource source, Entity target)
        {
            var buffer = new EntityCommandPanelSlotView[8];
            int count = source.CopySlots(target, 0, buffer);
            return buffer.Take(count).ToArray();
        }

        private static IEntityCommandPanelSource ResolveGasPanelSource(GameEngine engine)
        {
            var registry = engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry)
                ?? throw new InvalidOperationException("EntityCommandPanelSourceRegistry service is missing.");
            Assert.That(registry.TryGet("gas.ability-slots", out IEntityCommandPanelSource source), Is.True);
            return source;
        }

        private static int CountEntitiesByNameWithEffect(World world, string entityName, string effectKey)
        {
            int count = 0;
            foreach (Entity entity in FindEntitiesByName(world, entityName))
            {
                if (CountActiveEffects(world, entity, effectKey) > 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountActiveEffects(World world, Entity entity, string effectKey)
        {
            int templateId = EffectTemplateIdRegistry.GetId(effectKey);
            Assert.That(templateId, Is.GreaterThan(0), $"Effect template should be registered: {effectKey}");
            if (!world.IsAlive(entity) || !world.Has<ActiveEffectContainer>(entity))
            {
                return 0;
            }

            int count = 0;
            ref ActiveEffectContainer active = ref world.Get<ActiveEffectContainer>(entity);
            for (int i = 0; i < active.Count; i++)
            {
                Entity effectEntity = active.GetEntity(i);
                if (world.IsAlive(effectEntity) &&
                    world.Has<EffectTemplateRef>(effectEntity) &&
                    world.Get<EffectTemplateRef>(effectEntity).TemplateId == templateId)
                {
                    count++;
                }
            }

            return count;
        }

        private static float ReadAttribute(World world, Entity entity, int attributeId)
        {
            return world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);
        }

        private static float ReadTeamAttributeTotal(World world, int teamId, int attributeId)
        {
            float total = 0f;
            var query = new QueryDescription().WithAll<Team, AttributeBuffer>();
            world.Query(in query, (ref Team team, ref AttributeBuffer attributes) =>
            {
                if (team.Id == teamId && attributes.HasAttribute(attributeId))
                {
                    total += attributes.GetCurrent(attributeId);
                }
            });

            return total;
        }

        private static int EnsureAttribute(string attributeName)
        {
            int id = AttributeRegistry.GetId(attributeName);
            return id != AttributeRegistry.InvalidId ? id : AttributeRegistry.Register(attributeName);
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

        private static bool TryFindEntity(World world, string entityName, out Entity found)
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

            found = result;
            return result != Entity.Null && world.IsAlive(result);
        }

        private static Entity[] FindEntitiesByName(World world, string entityName)
        {
            var matches = new List<Entity>();
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(entity);
                }
            });

            return matches.ToArray();
        }

        private static int CountEntitiesByName(World world, string entityName) => FindEntitiesByName(world, entityName).Length;

        private static int CountAliveTeamEntities(World world, int teamId, int healthId)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<Team, AttributeBuffer>();
            world.Query(in query, (ref Team team, ref AttributeBuffer attributes) =>
            {
                if (team.Id == teamId &&
                    attributes.HasAttribute(healthId) &&
                    attributes.GetCurrent(healthId) > 0f)
                {
                    count++;
                }
            });

            return count;
        }

        private static void InstallDummyInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static HashSet<string> AssertUniqueIds(JsonElement array, string label)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement item in array.EnumerateArray())
            {
                string id = item.GetProperty("id").GetString()!;
                Assert.That(ids.Add(id), Is.True, $"{label} contains duplicate id '{id}'.");
            }

            return ids;
        }

        private static int CountPrefix(JsonElement array, string prefix)
        {
            int count = 0;
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.GetProperty("id").GetString()!.StartsWith(prefix, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountRootVisualPresenters(JsonElement presenters)
        {
            int count = 0;
            foreach (JsonElement presenter in presenters.EnumerateArray())
            {
                if (!presenter.TryGetProperty("rules", out JsonElement rules))
                {
                    continue;
                }

                foreach (JsonElement rule in rules.EnumerateArray())
                {
                    if (rule.TryGetProperty("event", out JsonElement eventElement) &&
                        eventElement.TryGetProperty("kind", out JsonElement kind) &&
                        string.Equals(kind.GetString(), "EntitySpawned", StringComparison.Ordinal))
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        private static IEnumerable<string> EnumerateTemplateAbilityRefs(JsonElement templates)
        {
            foreach (JsonElement template in templates.EnumerateArray())
            {
                if (!template.GetProperty("components").TryGetProperty("AbilityStateBuffer", out JsonElement abilityBuffer) ||
                    !abilityBuffer.TryGetProperty("abilityIds", out JsonElement abilityIds))
                {
                    continue;
                }

                foreach (JsonElement abilityId in abilityIds.EnumerateArray())
                {
                    yield return abilityId.GetString()!;
                }
            }
        }

        private static IEnumerable<string> EnumerateTemplateFormSetRefs(JsonElement templates)
        {
            foreach (JsonElement template in templates.EnumerateArray())
            {
                if (template.GetProperty("components").TryGetProperty("AbilityFormSetRef", out JsonElement formSetRef))
                {
                    yield return formSetRef.GetProperty("formSetId").GetString()!;
                }
            }
        }

        private static IEnumerable<string> EnumeratePresenterMeshRefs(JsonElement presenters)
        {
            foreach (JsonElement presenter in presenters.EnumerateArray())
            {
                if (!presenter.TryGetProperty("behaviors", out JsonElement behaviors))
                {
                    continue;
                }

                foreach (JsonElement behavior in behaviors.EnumerateArray())
                {
                    if (behavior.TryGetProperty("assetBinding", out JsonElement binding) &&
                        binding.TryGetProperty("assetId", out JsonElement assetId))
                    {
                        yield return assetId.GetString()!;
                    }
                }
            }
        }

        private static IEnumerable<string> EnumerateEffectPhaseGraphRefs(JsonElement effects)
        {
            foreach (JsonElement effect in effects.EnumerateArray())
            {
                if (!effect.TryGetProperty("phaseGraphs", out JsonElement phaseGraphs))
                {
                    continue;
                }

                foreach (JsonProperty phase in phaseGraphs.EnumerateObject())
                {
                    if (phase.Value.TryGetProperty("pre", out JsonElement pre))
                    {
                        yield return pre.GetString()!;
                    }

                    if (phase.Value.TryGetProperty("post", out JsonElement post))
                    {
                        yield return post.GetString()!;
                    }
                }
            }
        }

        private static IEnumerable<string> EnumerateEffectRefs(JsonElement abilities, JsonElement items, JsonElement graphs)
        {
            foreach (JsonElement ability in abilities.EnumerateArray())
            {
                if (!ability.GetProperty("exec").TryGetProperty("items", out JsonElement execItems))
                {
                    continue;
                }

                foreach (JsonElement execItem in execItems.EnumerateArray())
                {
                    if (execItem.TryGetProperty("template", out JsonElement template))
                    {
                        yield return template.GetString()!;
                    }
                }
            }

            foreach (JsonElement item in items.EnumerateArray())
            {
                foreach (JsonElement effect in item.GetProperty("equipEffects").EnumerateArray())
                {
                    yield return effect.GetString()!;
                }
            }

            foreach (JsonElement graph in graphs.EnumerateArray())
            {
                foreach (JsonElement node in graph.GetProperty("nodes").EnumerateArray())
                {
                    if (node.TryGetProperty("effectTemplate", out JsonElement effectTemplate))
                    {
                        yield return effectTemplate.GetString()!;
                    }
                }
            }
        }

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
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

        private sealed class FakeBrowserRuntime : IBrowserRuntime
        {
            public BrowserRuntimeInfo Info { get; } = new(
                BrowserEngineKind.Cef,
                "Fake CEF",
                "1.0",
                BrowserEngineCapabilityProfiles.Cef);

            public int SurfaceCreateCount { get; private set; }
            public FakeBrowserSurface? LastSurface { get; private set; }

            public ValueTask<IBrowserSurface> CreateSurfaceAsync(
                BrowserViewport viewport,
                IBrowserResourceResolver? resourceResolver = null,
                CancellationToken cancellationToken = default)
            {
                SurfaceCreateCount++;
                LastSurface = new FakeBrowserSurface(viewport);
                return ValueTask.FromResult<IBrowserSurface>(LastSurface);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class FakeBrowserSurface : IBrowserSurface
        {
            public FakeBrowserSurface(BrowserViewport viewport)
            {
                Viewport = viewport;
            }

            public event EventHandler<BrowserFrameReadyEventArgs>? FrameReady;

            public BrowserSurfaceId Id { get; } = BrowserSurfaceId.New();
            public BrowserViewport Viewport { get; private set; }
            public IBrowserMessageBridge Messages { get; } = new FakeBrowserMessageBridge();
            public Uri? LastNavigationUri { get; private set; }

            public ValueTask NavigateAsync(BrowserNavigationRequest request, CancellationToken cancellationToken = default)
            {
                LastNavigationUri = request.Uri;
                return ValueTask.CompletedTask;
            }

            public ValueTask ResizeAsync(BrowserViewport viewport, CancellationToken cancellationToken = default)
            {
                Viewport = viewport;
                return ValueTask.CompletedTask;
            }

            public ValueTask SendInputAsync(BrowserInputEvent inputEvent, CancellationToken cancellationToken = default)
            {
                return ValueTask.CompletedTask;
            }

            public BrowserFrame? TryGetLatestFrame() => null;

            public bool TryReadLatestFrame<TState>(TState state, BrowserFrameReadAction<TState> readFrame) => false;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public void RaiseFrameReady(BrowserFrameReadyEventArgs args) => FrameReady?.Invoke(this, args);
        }

        private sealed class FakeBrowserMessageBridge : IBrowserMessageBridge
        {
            public event EventHandler<BrowserScriptMessage>? MessageReceived;

            public ValueTask PostMessageAsync(BrowserScriptMessage message, CancellationToken cancellationToken = default)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
            {
                return ValueTask.CompletedTask;
            }

            public void RaiseMessage(BrowserScriptMessage message) => MessageReceived?.Invoke(this, message);
        }
    }
}

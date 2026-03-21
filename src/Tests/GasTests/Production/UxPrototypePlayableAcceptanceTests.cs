using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    public sealed class UxPrototypePlayableAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string TestInputBackendKey = "Tests.UxPrototype.InputBackend";
        private static readonly Vector2[] HoverProbeOffsets =
        {
            Vector2.Zero,
            new Vector2(0f, -24f),
            new Vector2(0f, 24f),
            new Vector2(-24f, 0f),
            new Vector2(24f, 0f),
            new Vector2(-36f, -36f),
            new Vector2(36f, -36f),
            new Vector2(-36f, 36f),
            new Vector2(36f, 36f),
            new Vector2(0f, -48f),
            new Vector2(0f, 48f),
            new Vector2(-48f, 0f),
            new Vector2(48f, 0f),
            new Vector2(-64f, -24f),
            new Vector2(64f, -24f),
            new Vector2(-64f, 24f),
            new Vector2(64f, 24f)
        };
        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "EntityInfoPanelsMod",
            "EntityCommandPanelMod",
            "UxPrototypeMod"
        };

        [Test]
        public void UxPrototype_PlayableAcceptance_WritesArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "ux-prototype");
            Directory.CreateDirectory(artifactDir);

            var trace = new List<object>();
            var timeline = new List<string>();
            var frameTimesMs = new List<double>();

            using var engine = CreateEngine();
            LoadMap(engine, "ux_prototype_battle", frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

            var uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
                ?? throw new InvalidOperationException("UIRoot missing.");
            List<string> uiText = ExtractUiText(uiRoot);
            Assert.That(uiText.Any(text => text.Contains("Current Objective", StringComparison.Ordinal)), Is.True);
            Assert.That(uiText.Any(text => text.Contains("Faction Stockpile", StringComparison.Ordinal)), Is.True);
            Assert.That(uiText.Any(text => text.Contains("Global Panel", StringComparison.Ordinal)), Is.True);
            timeline.Add("[T+001] Prototype map loaded and Ludots-native HUD mounted.");
            trace.Add(new
            {
                Step = "hud_loaded",
                UiText = uiText.Take(12).ToArray()
            });

            int farmsBefore = CountNamedLike(engine.World, "Farm");
            int workersBefore = CountNamedLike(engine.World, "Worker");

            object state = GetPrototypeState(engine);
            InvokeState(state, "QueueConstruction", engine, "ux_farm");
            InvokeState(state, "QueueProduction", engine, "ux_worker");
            InvokeState(state, "StartNavmeshBake");
            Tick(engine, 240, frameTimesMs);

            int farmsAfter = CountNamedLike(engine.World, "Farm");
            int workersAfter = CountNamedLike(engine.World, "Worker");
            Assert.That(farmsAfter, Is.GreaterThan(farmsBefore));
            Assert.That(workersAfter, Is.GreaterThan(workersBefore));

            object snapshot = BuildSnapshot(state, engine);
            bool navmeshBakeRunning = (bool)(snapshot.GetType().GetProperty("NavmeshBakeRunning")?.GetValue(snapshot) ?? true);
            Assert.That(navmeshBakeRunning, Is.False);

            uiText = ExtractUiText(uiRoot);
            timeline.Add("[T+002] Runtime state queued construction, production, and navmesh bake; simulation completed and spawned new content.");
            trace.Add(new
            {
                Step = "queues_completed",
                FarmsBefore = farmsBefore,
                FarmsAfter = farmsAfter,
                WorkersBefore = workersBefore,
                WorkersAfter = workersAfter,
                NavmeshBakeRunning = navmeshBakeRunning,
                UiText = uiText.Take(16).ToArray()
            });

            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(trace));
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, frameTimesMs, farmsBefore, farmsAfter, workersBefore, workersAfter));
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());
        }

        [Test]
        public void UxPrototype_ContentParity_CoversUnitsBuildingsSkillsAndPanels()
        {
            var frameTimesMs = new List<double>();

            using var engine = CreateEngine();
            LoadMap(engine, "ux_prototype_battle", frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            var backend = GetInputBackend(engine);
            Vector2 cameraTarget = engine.GameSession.Camera.State.TargetCm;
            Assert.That(cameraTarget.X, Is.InRange(3200f, 4000f), "UX prototype battle camera should frame the battlefield instead of drifting back toward the world origin.");
            Assert.That(cameraTarget.Y, Is.InRange(2200f, 2900f), "UX prototype battle camera should frame the battlefield instead of drifting back toward the world origin.");

            object state = GetPrototypeState(engine);

            AssertSelectionSnapshot(state, engine, backend, "Blue City", "City", "Train Worker", "Build Farm", "Build Mine", "Build Tower");
            AssertSelectionSnapshot(state, engine, backend, "Blue Barracks", "Barracks", "Train Soldier", "Train Archer", "Train Mage", "Train Medic");
            AssertSelectionSnapshot(state, engine, backend, "Blue Stable", "Stable", "Train Heavy Cavalry", "Train Horse Archer");
            AssertSelectionSnapshot(state, engine, backend, "Blue Workshop", "Workshop", "Train Ram", "Train Catapult", "Train Well Column");
            AssertSelectionSnapshot(state, engine, backend, "Worker A", "Worker", "Build Farm", "Build Mine", "Build Barracks", "Build Stable", "Build Workshop", "Build Tower", "Build Watchtower", "Build Fort");
            AssertSelectionSnapshot(state, engine, backend, "Soldier A", "Soldier", "Leap Slash");
            AssertSelectionSnapshot(state, engine, backend, "Archer A", "Archer", "Volley");
            AssertSelectionSnapshot(state, engine, backend, "Mage A", "Mage", "Fireball");
            AssertSelectionSnapshot(state, engine, backend, "Medic A", "Medic", "Heal");
            AssertSelectionSnapshot(state, engine, backend, "Heavy Cavalry A", "Heavy Cavalry", "Charge");
            AssertSelectionSnapshot(state, engine, backend, "Horse Archer A", "Horse Archer", "Volley");
            AssertSelectionSnapshot(state, engine, backend, "Catapult A", "Catapult", "Siege Shot");
            AssertSelectionSnapshot(state, engine, backend, "Ram A", "Ram", "Command", "Queue", "Global");
            AssertSelectionSnapshot(state, engine, backend, "Well Column A", "Well Column", "Command", "Queue", "Global");
            AssertSelectionSnapshot(state, engine, backend, "East Tower", "Tower", "Command", "Queue", "Global");
            AssertSelectionSnapshot(state, engine, backend, "North Watchtower", "Watchtower", "Command", "Queue", "Global");
            AssertSelectionSnapshot(state, engine, backend, "Eastern Fort", "Fort", "Build Watchtower", "Build Tower", "Reinforce Fort");
            AssertSelectionSnapshot(state, engine, backend, "North Farm", "Farm", "Command", "Queue", "Global");
            AssertSelectionSnapshot(state, engine, backend, "Central Mine", "Mine", "Command", "Queue", "Global");

            AssertEntityUsesBillboardVisual(engine, "Blue City", "ux.billboard.city");
            AssertEntityUsesBillboardVisual(engine, "Blue Barracks", "ux.billboard.barracks");
            AssertEntityUsesBillboardVisual(engine, "Blue Stable", "ux.billboard.stable");
            AssertEntityUsesBillboardVisual(engine, "Blue Workshop", "ux.billboard.workshop");
            AssertEntityUsesBillboardVisual(engine, "North Farm", "ux.billboard.farm");
            AssertEntityUsesBillboardVisual(engine, "Central Mine", "ux.billboard.mine");
            AssertEntityUsesBillboardVisual(engine, "East Tower", "ux.billboard.tower");
            AssertEntityUsesBillboardVisual(engine, "North Watchtower", "ux.billboard.watchtower");
            AssertEntityUsesBillboardVisual(engine, "Eastern Fort", "ux.billboard.fort");
            AssertEntityUsesBillboardVisual(engine, "Worker A", "ux.billboard.worker");
            AssertEntityUsesBillboardVisual(engine, "Soldier A", "ux.billboard.soldier");
            AssertEntityUsesBillboardVisual(engine, "Archer A", "ux.billboard.archer");
            AssertEntityUsesBillboardVisual(engine, "Mage A", "ux.billboard.mage");
            AssertEntityUsesBillboardVisual(engine, "Medic A", "ux.billboard.medic");
            AssertEntityUsesBillboardVisual(engine, "Heavy Cavalry A", "ux.billboard.heavy_cavalry");
            AssertEntityUsesBillboardVisual(engine, "Horse Archer A", "ux.billboard.horse_archer");
            AssertEntityUsesBillboardVisual(engine, "Ram A", "ux.billboard.ram");
            AssertEntityUsesBillboardVisual(engine, "Catapult A", "ux.billboard.catapult");
            AssertEntityUsesBillboardVisual(engine, "Well Column A", "ux.billboard.well_column");
            AssertEntityUsesBillboardVisual(engine, "Gold Deposit North", "ux.billboard.gold_deposit");
            AssertEntityUsesBillboardVisual(engine, "Enemy Raider A", "ux.billboard.enemy_raider");

            AssertGlobalTabEntries(state, engine, "build", "build:ux_farm", "build:ux_mine", "build:ux_barracks", "build:ux_stable", "build:ux_workshop", "build:ux_tower", "build:ux_watchtower", "build:ux_fort");
            AssertGlobalTabEntries(state, engine, "units", "train:ux_worker", "train:ux_soldier", "train:ux_archer", "train:ux_mage", "train:ux_medic", "train:ux_heavy_cavalry", "train:ux_horse_archer", "train:ux_ram", "train:ux_catapult", "train:ux_well_column");
            AssertGlobalTabEntries(state, engine, "defense", "build:ux_tower", "build:ux_watchtower", "build:ux_fort");
            AssertGlobalTabEntries(state, engine, "editors", "editor:road", "editor:obstacle", "editor:navmesh");

            AssertRosterTabRows(state, engine, "economy", "Cities", "Farms", "Mines", "Workers");
            AssertRosterTabRows(state, engine, "production", "Barracks", "Stable", "Workshop");
            AssertRosterTabRows(state, engine, "defense", "Towers", "Watchtowers", "Forts");
            AssertRosterTabRows(state, engine, "units", "Soldiers", "Archers", "Mages", "Medics", "Workers");
            AssertRosterTabRows(state, engine, "siege", "Rams", "Catapults", "Well Columns");

            AssertFactionTabRows(state, engine, "diplomacy", "Declare War", "Offer Alliance", "Send Envoy");
            AssertFactionTabRows(state, engine, "tech", "Research", "Upgrade Unit", "Refit Siege");
            AssertFactionTabRows(state, engine, "trade", "Buy Food", "Sell Materials", "Caravan");

            InvokeState(state, "SwitchMode", "UxPrototype.Mode.RoadEditor");
            Assert.That(ReadProperty<string>(BuildSnapshot(state, engine), "ActiveModeId"), Is.EqualTo("UxPrototype.Mode.RoadEditor"));
            InvokeState(state, "SwitchMode", "UxPrototype.Mode.ObstacleEditor");
            Assert.That(ReadProperty<string>(BuildSnapshot(state, engine), "ActiveModeId"), Is.EqualTo("UxPrototype.Mode.ObstacleEditor"));
            InvokeState(state, "SwitchMode", "UxPrototype.Mode.Navmesh");
            Assert.That(ReadProperty<string>(BuildSnapshot(state, engine), "ActiveModeId"), Is.EqualTo("UxPrototype.Mode.Navmesh"));
            InvokeState(state, "SwitchMode", "UxPrototype.Mode.Play");
            Assert.That(ReadProperty<string>(BuildSnapshot(state, engine), "ActiveModeId"), Is.EqualTo("UxPrototype.Mode.Play"));

            AssertSkillCooldownCycle(state, engine, backend, frameTimesMs, "Mage A", "fireball", "Fireball");
            AssertSkillCooldownCycle(state, engine, backend, frameTimesMs, "Archer A", "volley", "Volley");
            AssertSkillCooldownCycle(state, engine, backend, frameTimesMs, "Medic A", "heal", "Heal");
            AssertSkillCooldownCycle(state, engine, backend, frameTimesMs, "Soldier A", "leap_slash", "Leap Slash");
            AssertSkillCooldownCycle(state, engine, backend, frameTimesMs, "Heavy Cavalry A", "charge", "Charge");
            AssertSkillCooldownCycle(state, engine, backend, frameTimesMs, "Catapult A", "siege_shot", "Siege Shot");

            ClickEntityByName(engine, backend, "Blue Barracks");
            InvokeState(state, "TriggerAction", engine, "train:ux_mage");
            object barracksSnapshot = BuildSnapshot(state, engine);
            object barracksQueue = ReadObjectProperty(barracksSnapshot, "SelectedQueue");
            Assert.That(ReadProperty<string>(barracksQueue, "Header"), Is.EqualTo("Blue Barracks"));
            IReadOnlyList<object> barracksEntries = ReadObjectList(barracksQueue, "Entries");
            Assert.That(barracksEntries.Count, Is.EqualTo(1));
            Assert.That(ReadProperty<string>(barracksEntries[0], "Label"), Is.EqualTo("Mage"));
            string barracksToken = ReadProperty<string>(barracksEntries[0], "QueueToken");
            Assert.That(barracksToken, Does.StartWith("prod:"));
            Assert.That(ReadProperty<bool>(barracksEntries[0], "CanCancel"), Is.True);
            InvokeState(state, "CancelQueueEntry", engine, barracksToken);
            Assert.That(ReadObjectList(ReadObjectProperty(BuildSnapshot(state, engine), "SelectedQueue"), "Entries"), Is.Empty);

            ClickEntityByName(engine, backend, "Worker A");
            InvokeState(state, "TriggerAction", engine, "build:ux_farm");
            object constructionSnapshot = BuildSnapshot(state, engine);
            Assert.That(ReadProperty<string?>(constructionSnapshot, "ActiveBuildTemplateId"), Is.EqualTo("ux_farm"));
            object constructionQueue = ReadObjectProperty(constructionSnapshot, "SelectedQueue");
            Assert.That(ReadProperty<string>(constructionQueue, "Header"), Is.EqualTo("Construction"));
            IReadOnlyList<object> constructionEntries = ReadObjectList(constructionQueue, "Entries");
            Assert.That(constructionEntries.Count, Is.EqualTo(1));
            Assert.That(ReadProperty<string>(constructionEntries[0], "Label"), Is.EqualTo("Farm"));
            string constructionToken = ReadProperty<string>(constructionEntries[0], "QueueToken");
            Assert.That(constructionToken, Is.EqualTo("construct:0"));
            Assert.That(ReadProperty<bool>(constructionEntries[0], "CanCancel"), Is.True);
            InvokeState(state, "CancelQueueEntry", engine, constructionToken);
            object constructionQueueAfterCancel = ReadObjectProperty(BuildSnapshot(state, engine), "SelectedQueue");
            Assert.That(ReadObjectList(constructionQueueAfterCancel, "Entries"), Is.Empty);
            Assert.That(ReadProperty<string?>(BuildSnapshot(state, engine), "ActiveBuildTemplateId"), Is.Null);
        }

        [Test]
        public void UxPrototype_CommandPanelSource_ReusesSharedEntityCommandPanelInfrastructure()
        {
            var frameTimesMs = new List<double>();

            using var engine = CreateEngine();
            LoadMap(engine, "ux_prototype_battle", frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

            var backend = GetInputBackend(engine);
            ClickEntityByName(engine, backend, "Blue Barracks");

            var registry = engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry) as IEntityCommandPanelSourceRegistry
                ?? throw new InvalidOperationException("EntityCommandPanelSourceRegistry missing.");
            Assert.That(registry.TryGet("uxprototype.entity-actions", out IEntityCommandPanelSource source), Is.True);

            Entity target = FindEntityByName(engine.World, "Blue Barracks");
            var slots = new EntityCommandPanelSlotView[8];
            int count = source.CopySlots(target, 0, slots);
            Assert.That(count, Is.EqualTo(4));
            Assert.That(slots[0].DisplayLabel, Is.EqualTo("Train Soldier"));
            Assert.That(slots[1].DisplayLabel, Is.EqualTo("Train Archer"));
            Assert.That(slots[2].DisplayLabel, Is.EqualTo("Train Mage"));
            Assert.That(slots[3].DisplayLabel, Is.EqualTo("Train Medic"));

            var actions = source as IEntityCommandPanelActionSource
                ?? throw new InvalidOperationException("UxPrototype command panel source should support activation.");
            Assert.That(actions.ActivateSlot(target, 0, 2), Is.True);

            object state = GetPrototypeState(engine);
            object queueSnapshot = ReadObjectProperty(BuildSnapshot(state, engine), "SelectedQueue");
            Assert.That(ReadProperty<string>(queueSnapshot, "Header"), Is.EqualTo("Blue Barracks"));
            IReadOnlyList<object> entries = ReadObjectList(queueSnapshot, "Entries");
            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(ReadProperty<string>(entries[0], "Label"), Is.EqualTo("Mage"));
        }

        [Test]
        public void UxPrototype_SelectionInput_ClickAndDrag_UseCoreSelectionInfrastructure()
        {
            var frameTimesMs = new List<double>();

            using var engine = CreateEngine();
            LoadMap(engine, "ux_prototype_battle", frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

            var backend = GetInputBackend(engine);

            ClickEntityByName(engine, backend, "Heavy Cavalry A");
            AssertPrimarySelection(engine, "Heavy Cavalry A");

            string[] formation =
            {
                "Soldier A",
                "Archer A",
                "Mage A",
                "Medic A",
                "Heavy Cavalry A",
                "Horse Archer A",
                "Ram A",
                "Catapult A"
            };

            DragSelectByEntityNames(engine, backend, 24f, formation);
            AssertSelectionContains(engine, formation);
            Assert.That(ReadSelectedEntityName(engine), Is.EqualTo("Soldier A"),
                "Box select should preserve a deterministic primary selection through the shared Core selection pipeline.");
        }

        private static object BuildSnapshot(object state, GameEngine engine)
        {
            MethodInfo buildSnapshot = state.GetType().GetMethod("BuildSnapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException("BuildSnapshot");
            object? snapshot = buildSnapshot.Invoke(state, new object?[] { engine, null });
            return snapshot ?? throw new InvalidOperationException("BuildSnapshot returned null.");
        }

        private static object GetPrototypeState(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue("UxPrototypeMod.State", out object? state) && state != null
                ? state
                : throw new InvalidOperationException("UxPrototype state not found.");
        }

        private static void InvokeState(object state, string methodName, params object[] args)
        {
            MethodInfo method = state.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(methodName);
            method.Invoke(state, args);
        }

        private static void AssertSelectionSnapshot(object state, GameEngine engine, TestInputBackend backend, string entityName, string expectedType, params string[] expectedSkillLabels)
        {
            ClickEntityByName(engine, backend, entityName);
            object snapshot = BuildSnapshot(state, engine);
            Assert.That(ReadProperty<string>(snapshot, "SelectedEntityLabel"), Is.EqualTo(entityName));
            Assert.That(ReadProperty<string>(snapshot, "SelectedEntityType"), Is.EqualTo(expectedType));
            IReadOnlyList<string> actualSkillLabels = ReadObjectList(snapshot, "SelectedSkills")
                .Select(skill => ReadProperty<string>(skill, "Label"))
                .ToArray();
            Assert.That(actualSkillLabels, Is.EqualTo(expectedSkillLabels));
        }

        private static void AssertEntityUsesBillboardVisual(GameEngine engine, string entityName, string expectedMeshAssetKey)
        {
            Entity entity = FindEntityByName(engine.World, entityName);
            Assert.That(engine.World.Has<VisualRuntimeState>(entity), Is.True, $"{entityName} should carry VisualRuntimeState.");

            var visual = engine.World.Get<VisualRuntimeState>(entity);
            Assert.That(visual.MeshAssetId, Is.GreaterThan(0), $"{entityName} should resolve a mesh asset.");
            Assert.That(visual.RenderPath, Is.EqualTo(VisualRenderPath.StaticMesh), $"{entityName} should render through the static mesh lane.");
            Assert.That(visual.AnimatorControllerId, Is.EqualTo(0), $"{entityName} billboard visuals should not bind an animator.");
            Assert.That(engine.World.Has<AnimatorPackedState>(entity), Is.False, $"{entityName} billboard visuals should not attach AnimatorPackedState.");

            var meshRegistry = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry) as MeshAssetRegistry
                ?? throw new InvalidOperationException("PresentationMeshAssetRegistry missing.");
            Assert.That(meshRegistry.GetName(visual.MeshAssetId), Is.EqualTo(expectedMeshAssetKey), $"{entityName} should point at the expected billboard mesh asset.");
        }

        private static void AssertGlobalTabEntries(object state, GameEngine engine, string tab, params string[] expectedActionIds)
        {
            InvokeState(state, "SetGlobalTab", tab);
            object snapshot = BuildSnapshot(state, engine);
            Assert.That(ReadProperty<string>(snapshot, "GlobalTab"), Is.EqualTo(tab));
            IReadOnlyList<string> actionIds = ReadObjectList(snapshot, "GlobalEntries")
                .Select(entry => ReadProperty<string>(entry, "ActionId"))
                .ToArray();
            Assert.That(actionIds, Is.EqualTo(expectedActionIds));
        }

        private static void AssertRosterTabRows(object state, GameEngine engine, string tab, params string[] expectedLabels)
        {
            InvokeState(state, "SetRosterTab", tab);
            object snapshot = BuildSnapshot(state, engine);
            Assert.That(ReadProperty<string>(snapshot, "RosterTab"), Is.EqualTo(tab));
            IReadOnlyList<string> labels = ReadObjectList(snapshot, "RosterRows")
                .Select(row => ReadProperty<string>(row, "Label"))
                .ToArray();
            Assert.That(labels, Is.EqualTo(expectedLabels));
        }

        private static void AssertFactionTabRows(object state, GameEngine engine, string tab, params string[] expectedLabels)
        {
            InvokeState(state, "SetFactionTab", tab);
            object snapshot = BuildSnapshot(state, engine);
            Assert.That(ReadProperty<string>(snapshot, "FactionTab"), Is.EqualTo(tab));
            IReadOnlyList<string> labels = ReadObjectList(snapshot, "FactionOperations")
                .Select(row => ReadProperty<string>(row, "Label"))
                .ToArray();
            Assert.That(labels, Is.EqualTo(expectedLabels));
        }

        private static void AssertSkillCooldownCycle(object state, GameEngine engine, TestInputBackend backend, List<double> frameTimesMs, string entityName, string actionId, string expectedLabel)
        {
            ClickEntityByName(engine, backend, entityName);
            object readySnapshot = BuildSnapshot(state, engine);
            object readySkill = ReadObjectList(readySnapshot, "SelectedSkills").Single(skill => ReadProperty<string>(skill, "Label") == expectedLabel);
            Assert.That(ReadProperty<bool>(readySkill, "Enabled"), Is.True);
            Assert.That(ReadProperty<bool>(readySkill, "Active"), Is.False);
            Assert.That(ReadProperty<string>(readySkill, "CountText"), Is.EqualTo("Ready"));

            InvokeState(state, "TriggerAction", engine, actionId);

            object activeSnapshot = BuildSnapshot(state, engine);
            object activeSkill = ReadObjectList(activeSnapshot, "SelectedSkills").Single(skill => ReadProperty<string>(skill, "Label") == expectedLabel);
            Assert.That(ReadProperty<bool>(activeSkill, "Enabled"), Is.False);
            Assert.That(ReadProperty<bool>(activeSkill, "Active"), Is.True);
            Assert.That(ReadProperty<string>(activeSkill, "CountText"), Does.EndWith("s"));

            Tick(engine, 360, frameTimesMs);
            ClickEntityByName(engine, backend, entityName);

            object cooledSnapshot = BuildSnapshot(state, engine);
            object cooledSkill = ReadObjectList(cooledSnapshot, "SelectedSkills").Single(skill => ReadProperty<string>(skill, "Label") == expectedLabel);
            Assert.That(ReadProperty<bool>(cooledSkill, "Enabled"), Is.True);
            Assert.That(ReadProperty<bool>(cooledSkill, "Active"), Is.False);
            Assert.That(ReadProperty<string>(cooledSkill, "CountText"), Is.EqualTo("Ready"));
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

            Assert.That(found, Is.Not.EqualTo(Entity.Null), $"Entity '{entityName}' should exist on the prototype map.");
            return found;
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(TestInputBackendKey, out object? backend) && backend is TestInputBackend typed
                ? typed
                : throw new InvalidOperationException("UX prototype test input backend missing.");
        }

        private static void ClickEntityByName(GameEngine engine, TestInputBackend backend, string entityName)
        {
            Entity entity = FindEntityByName(engine.World, entityName);
            Vector2 projectedScreenPoint = ProjectEntity(engine, entity);
            Vector2 screenPoint = FindHoverScreenPoint(engine, backend, entityName, projectedScreenPoint);
            ClickScreen(engine, backend, screenPoint);

            AssertPrimarySelection(engine, entityName,
                $"screen=({screenPoint.X:F1},{screenPoint.Y:F1}) projected=({projectedScreenPoint.X:F1},{projectedScreenPoint.Y:F1}) hovered={ReadHoveredEntityName(engine)}");
        }

        private static void DragSelectByEntityNames(GameEngine engine, TestInputBackend backend, float padding, params string[] entityNames)
        {
            Assert.That(entityNames.Length, Is.GreaterThanOrEqualTo(2), "Box selection needs at least two entities to define a rectangle.");

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            for (int i = 0; i < entityNames.Length; i++)
            {
                string entityName = entityNames[i];
                Vector2 point = FindHoverScreenPoint(engine, backend, entityName, ProjectEntity(engine, FindEntityByName(engine.World, entityName)));
                minX = MathF.Min(minX, point.X);
                minY = MathF.Min(minY, point.Y);
                maxX = MathF.Max(maxX, point.X);
                maxY = MathF.Max(maxY, point.Y);
            }

            Vector2 from = new(minX - padding, minY - padding);
            Vector2 to = new(maxX + padding, maxY + padding);
            DragMouse(engine, backend, from, to);
        }

        private static Vector2 FindHoverScreenPoint(GameEngine engine, TestInputBackend backend, string entityName, Vector2 projectedScreenPoint)
        {
            var hoveredSamples = new List<string>(HoverProbeOffsets.Length);
            for (int i = 0; i < HoverProbeOffsets.Length; i++)
            {
                Vector2 candidate = projectedScreenPoint + HoverProbeOffsets[i];
                backend.SetMousePosition(candidate);
                Tick(engine, 1);

                string hovered = ReadHoveredEntityName(engine);
                hoveredSamples.Add($"{candidate.X:F1},{candidate.Y:F1}->{hovered}");
                if (string.Equals(hovered, entityName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            Assert.Fail(
                $"Failed to hover '{entityName}' near projected point ({projectedScreenPoint.X:F1},{projectedScreenPoint.Y:F1}). Samples: {string.Join(" | ", hoveredSamples)}");
            return projectedScreenPoint;
        }

        private static void ClickScreen(GameEngine engine, TestInputBackend backend, Vector2 screenPoint)
        {
            backend.SetMousePosition(screenPoint);
            Tick(engine, 1);
            backend.SetButton("<Mouse>/LeftButton", true);
            Tick(engine, 2);
            backend.SetButton("<Mouse>/LeftButton", false);
            Tick(engine, 2);
        }

        private static void DragMouse(GameEngine engine, TestInputBackend backend, Vector2 from, Vector2 to)
        {
            backend.SetMousePosition(from);
            Tick(engine, 1);
            backend.SetButton("<Mouse>/LeftButton", true);
            Tick(engine, 2);
            backend.SetMousePosition(to);
            Tick(engine, 2);
            backend.SetButton("<Mouse>/LeftButton", false);
            Tick(engine, 2);
        }

        private static Vector2 ProjectEntity(GameEngine engine, Entity entity)
        {
            var projector = engine.GetService(CoreServiceKeys.ScreenProjector) as IScreenProjector
                ?? throw new InvalidOperationException("ScreenProjector missing.");

            if (engine.World.TryGet(entity, out VisualTransform transform))
            {
                Vector2 screen = projector.WorldToScreen(transform.Position);
                Assert.That(float.IsFinite(screen.X) && float.IsFinite(screen.Y), Is.True,
                    $"Projected screen position for entity #{entity.Id} must stay finite.");
                return screen;
            }

            ref var position = ref engine.World.Get<WorldPositionCm>(entity);
            Vector2 fallback = projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(position.Value, yMeters: 0f));
            Assert.That(float.IsFinite(fallback.X) && float.IsFinite(fallback.Y), Is.True,
                $"Fallback projected screen position for entity #{entity.Id} must stay finite.");
            return fallback;
        }

        private static void AssertPrimarySelection(GameEngine engine, string entityName, string? diagnostic = null)
        {
            string selectedEntityName = ReadSelectedEntityName(engine);
            Assert.That(selectedEntityName, Is.EqualTo(entityName), diagnostic);

            Entity[] selection = GetSelectionSnapshot(engine);
            Assert.That(selection.Length, Is.GreaterThanOrEqualTo(1));
            Entity expected = FindEntityByName(engine.World, entityName);
            Assert.That(Array.IndexOf(selection, expected), Is.GreaterThanOrEqualTo(0));
        }

        private static void AssertSelectionContains(GameEngine engine, params string[] entityNames)
        {
            Entity[] selection = GetSelectionSnapshot(engine);
            Assert.That(selection.Length, Is.GreaterThanOrEqualTo(entityNames.Length));

            for (int i = 0; i < entityNames.Length; i++)
            {
                Entity entity = FindEntityByName(engine.World, entityNames[i]);
                Assert.That(Array.IndexOf(selection, entity), Is.GreaterThanOrEqualTo(0), $"Selection should contain '{entityNames[i]}'.");
            }
        }

        private static string ReadSelectedEntityName(GameEngine engine)
        {
            return SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected) &&
                   engine.World.TryGet(selected, out Name name)
                ? name.Value
                : string.Empty;
        }

        private static string ReadHoveredEntityName(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CoreServiceKeys.HoveredEntity.Name, out object? hoveredObj) &&
                   hoveredObj is Entity hovered &&
                   hovered != Entity.Null &&
                   engine.World.TryGet(hovered, out Name name)
                ? name.Value
                : string.Empty;
        }

        private static Entity[] GetSelectionSnapshot(GameEngine engine)
        {
            return SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext);
        }

        private static Entity GetLocalPlayer(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) &&
                   localObj is Entity entity &&
                   engine.World.IsAlive(entity)
                ? entity
                : throw new InvalidOperationException("LocalPlayerEntity missing.");
        }

        private static T ReadProperty<T>(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(target.GetType().FullName, propertyName);
            object? value = property.GetValue(target);
            return value is T typed
                ? typed
                : value == null && default(T) == null
                    ? default!
                    : throw new InvalidCastException($"Property {propertyName} on {target.GetType().Name} was {value?.GetType().FullName ?? "null"}.");
        }

        private static object ReadObjectProperty(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(target.GetType().FullName, propertyName);
            return property.GetValue(target) ?? throw new InvalidOperationException($"{propertyName} on {target.GetType().Name} was null.");
        }

        private static IReadOnlyList<object> ReadObjectList(object target, string propertyName)
        {
            return ReadObjectProperty(target, propertyName) is IEnumerable sequence
                ? sequence.Cast<object>().ToArray()
                : throw new InvalidCastException($"{propertyName} on {target.GetType().Name} was not enumerable.");
        }

        private static int CountNamedLike(World world, string pattern)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity _, ref Name name) =>
            {
                if (name.Value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            });
            return count;
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);

            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, new SkiaImageSizeProvider());

            var view = new StubViewController(1920f, 1080f);
            engine.SetService(CoreServiceKeys.ViewController, view);
            var cameraAdapter = new StubCameraAdapter();
            var timingDiagnostics = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter, timingDiagnostics);
            var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, view);
            var screenRayProvider = new CoreScreenRayProvider(engine.GameSession.Camera, view);
            screenProjector.BindPresenter(cameraPresenter);
            screenRayProvider.BindPresenter(cameraPresenter);
            engine.SetService(CoreServiceKeys.ScreenProjector, screenProjector);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, screenRayProvider);

            var culling = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, view, timingDiagnostics);
            engine.RegisterPresentationSystem(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);
            engine.GlobalContext["Tests.UxPrototype.HeadlessCamera"] = new HeadlessCameraRuntime(
                cameraPresenter,
                engine.GetService(CoreServiceKeys.PresentationFrameSetup));

            engine.Start();
            return engine;
        }

        private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs, int frames = 12)
        {
            engine.LoadMap(mapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null, $"{mapId} should create a live map session.");
            Tick(engine, frames, frameTimesMs);
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new TestInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            backend.SetMousePosition(new Vector2(960f, 540f));
            engine.GlobalContext[TestInputBackendKey] = backend;
        }

        private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
        {
            for (int i = 0; i < frames; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
                UpdateHeadlessCamera(engine);
                frameTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency);
            }
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
                UpdateHeadlessCamera(engine);
            }
        }

        private static void UpdateHeadlessCamera(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue("Tests.UxPrototype.HeadlessCamera", out object? runtimeObj) ||
                runtimeObj is not HeadlessCameraRuntime runtime)
            {
                return;
            }

            float alpha = runtime.PresentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
            runtime.CameraPresenter.Update(engine.GameSession.Camera, alpha);
        }

        private static List<string> ExtractUiText(UIRoot root)
        {
            if (root.Scene?.Root == null)
            {
                return new List<string>();
            }

            var lines = new List<string>();
            CollectUiText(root.Scene.Root, lines);
            return lines;
        }

        private static void CollectUiText(UiNode node, List<string> lines)
        {
            if (!string.IsNullOrWhiteSpace(node.TextContent))
            {
                lines.Add(node.TextContent.Trim());
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                CollectUiText(node.Children[i], lines);
            }
        }

        private static string BuildTraceJsonl(IReadOnlyList<object> trace)
        {
            var options = new JsonSerializerOptions { WriteIndented = false };
            var lines = new List<string>(trace.Count);
            for (int i = 0; i < trace.Count; i++)
            {
                lines.Add(JsonSerializer.Serialize(trace[i], options));
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string BuildBattleReport(
            IReadOnlyList<string> timeline,
            IReadOnlyList<double> frameTimesMs,
            int farmsBefore,
            int farmsAfter,
            int workersBefore,
            int workersAfter)
        {
            double median = Median(frameTimesMs);
            double max = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
            var sb = new StringBuilder();
            sb.AppendLine("# UX Prototype Acceptance");
            sb.AppendLine();
            sb.AppendLine("## Scenario");
            sb.AppendLine("- Load the prototype battlefield mod with the Ludots UI/runtime stack.");
            sb.AppendLine("- Validate that the HUD mounts with objective, resource, roster, and global panel surfaces.");
            sb.AppendLine("- Queue one construction, one production task, and one navmesh bake through the prototype state service.");
            sb.AppendLine("- Advance the authoritative engine until new content is spawned and the editor task completes.");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            for (int i = 0; i < timeline.Count; i++)
            {
                sb.AppendLine($"- {timeline[i]}");
            }
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine($"- farms: {farmsBefore} -> {farmsAfter}");
            sb.AppendLine($"- workers: {workersBefore} -> {workersAfter}");
            sb.AppendLine($"- median tick: {median:F3}ms");
            sb.AppendLine($"- max tick: {max:F3}ms");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return """
flowchart LR
    A["Load ux_prototype_battle"] --> B["Mount prototype HUD"]
    B --> C["Queue construction + production + bake"]
    C --> D["Tick simulation"]
    D --> E["Spawn new farm + worker"]
    D --> F["Finish navmesh bake"]
    E --> G["Write acceptance artifacts"]
    F --> G
""";
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

        private static double Median(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            var ordered = values.OrderBy(v => v).ToArray();
            int middle = ordered.Length / 2;
            if ((ordered.Length & 1) == 0)
            {
                return (ordered[middle - 1] + ordered[middle]) * 0.5d;
            }

            return ordered[middle];
        }

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly Dictionary<string, bool> _buttons = new(StringComparer.OrdinalIgnoreCase);
            private Vector2 _mousePosition;
            private float _mouseWheel;

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out bool pressed) && pressed;
            public Vector2 GetMousePosition() => _mousePosition;
            public float GetMouseWheel() => _mouseWheel;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;

            public void SetButton(string devicePath, bool pressed)
            {
                _buttons[devicePath] = pressed;
            }

            public void SetMousePosition(Vector2 mousePosition)
            {
                _mousePosition = mousePosition;
            }

            public void SetMouseWheel(float mouseWheel)
            {
                _mouseWheel = mouseWheel;
            }
        }

        private sealed class StubViewController : IViewController
        {
            public StubViewController(float width, float height)
            {
                Resolution = new Vector2(width, height);
            }

            public Vector2 Resolution { get; }
            public float Fov => 60f;
            public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
        }

        private sealed class HeadlessCameraRuntime
        {
            public HeadlessCameraRuntime(CameraPresenter cameraPresenter, PresentationFrameSetupSystem? presentationFrameSetup)
            {
                CameraPresenter = cameraPresenter;
                PresentationFrameSetup = presentationFrameSetup;
            }

            public CameraPresenter CameraPresenter { get; }
            public PresentationFrameSetupSystem? PresentationFrameSetup { get; }
        }

        private sealed class StubCameraAdapter : ICameraAdapter
        {
            public CameraRenderState3D LastState { get; private set; }

            public void UpdateCamera(in CameraRenderState3D state)
            {
                LastState = state;
            }
        }
    }
}

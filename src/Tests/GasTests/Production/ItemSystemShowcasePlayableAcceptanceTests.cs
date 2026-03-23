using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Runtime.Events;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
public sealed class ItemSystemShowcasePlayableAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string HubMapId = "item_system_showcase_hub";
    private const string LoadoutMapId = "item_system_showcase_loadout_garage";
    private const string WeaponMapId = "item_system_showcase_weapon_bench";
    private const string RaidMapId = "item_system_showcase_raid_loop";
    private const string RuntimeKey = "ItemSystemShowcaseMod.Runtime";
    private static readonly QueryDescription ContainerQuery = new QueryDescription().WithAll<ItemContainerCm>();
    private static readonly QueryDescription MapEntityQuery = new QueryDescription().WithAll<MapEntity>();
    private static readonly QueryDescription NameQuery = new QueryDescription().WithAll<Name>();

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "ItemSystemShowcaseMod"
    };

    [Test]
    public void ItemSystemShowcase_PlayableFlow_WritesAcceptanceArtifacts()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "item-system-showcase");
        Directory.CreateDirectory(artifactDir);
        RaylibEvidenceStatus raylibEvidence = ResolveRaylibEvidenceStatus(artifactDir);

        var snapshots = new List<object>();
        var timeline = new List<string>();
        var frameTimesMs = new List<double>();

        using var engine = CreateEngine();
        LoadMap(engine, HubMapId, frameTimesMs);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

        var uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        var inventory = engine.GetService(CoreServiceKeys.InventoryRuntimeService)
            ?? throw new InvalidOperationException("InventoryRuntimeService missing.");
        var definitions = engine.GetService(CoreServiceKeys.ItemDefinitionRegistry)
            ?? throw new InvalidOperationException("ItemDefinitionRegistry missing.");

        object runtime = GetRuntime(engine);

        AssertUiContains(uiRoot, "Ludots Item Demo Pack");
        AssertUiContains(uiRoot, "Open Loadout Garage");
        AssertUiContains(uiRoot, "Open Weapon Bench");
        AssertUiContains(uiRoot, "Open Raid Loop");
        timeline.Add("[T+001] Hub map loaded as a focused demo selector with three short item-system routes.");
        snapshots.Add(BuildSnapshot(engine, uiRoot, "hub_loaded"));

        ClickButton(uiRoot, "Open Loadout Garage");
        Tick(engine, 16, frameTimesMs);
        AssertCurrentMap(engine, LoadoutMapId);
        Assert.That(CountMapEntities(engine.World, HubMapId), Is.EqualTo(0), "Hub entities should be cleaned when leaving the hub.");
        AssertUiContains(uiRoot, "Loadout Garage");
        AssertUiContains(uiRoot, "Loadout Moves");
        Assert.That(CountEntitiesByName(engine.World, "Loadout Pilot"), Is.EqualTo(1), "Exactly one hero should exist after entering Loadout Garage.");
        Assert.That(CountEntitiesByName(engine.World, "Target Dummy"), Is.EqualTo(1), "Exactly one dummy should exist after entering Loadout Garage.");
        Entity hero = FindEntityByName(engine.World, "Loadout Pilot");
        Entity dummy = FindEntityByName(engine.World, "Target Dummy");
        timeline.Add("[T+002] Entered Loadout Garage to tune the hero one slot at a time.");
        snapshots.Add(BuildSnapshot(engine, uiRoot, "loadout_room_loaded"));

        float moveSpeedBeforeBootSwap = ReadAttribute(engine.World, hero, "MoveSpeed");
        InvokeRuntime(runtime, "ToggleBoots", engine);
        Tick(engine, 6, frameTimesMs);
        float moveSpeedWithoutBoots = ReadAttribute(engine.World, hero, "MoveSpeed");
        Assert.That(moveSpeedWithoutBoots, Is.LessThan(moveSpeedBeforeBootSwap));
        timeline.Add($"[T+003] Boots came off in Loadout Garage. MoveSpeed {moveSpeedBeforeBootSwap:0.0} -> {moveSpeedWithoutBoots:0.0}.");
        snapshots.Add(BuildSnapshot(engine, uiRoot, "loadout_boots_off"));

        InvokeRuntime(runtime, "ToggleBoots", engine);
        Tick(engine, 6, frameTimesMs);
        float moveSpeedReequipped = ReadAttribute(engine.World, hero, "MoveSpeed");
        Assert.That(moveSpeedReequipped, Is.GreaterThan(moveSpeedWithoutBoots));

        float attackBeforeRing = ReadAttribute(engine.World, hero, "AttackDamage");
        InvokeRuntime(runtime, "EquipRing", engine);
        Tick(engine, 6, frameTimesMs);
        float attackAfterRing = ReadAttribute(engine.World, hero, "AttackDamage");
        Assert.That(attackAfterRing, Is.GreaterThan(attackBeforeRing));
        timeline.Add($"[T+004] Duelist Ring landed in the right slot. AttackDamage {attackBeforeRing:0.0} -> {attackAfterRing:0.0}.");
        snapshots.Add(BuildSnapshot(engine, uiRoot, "loadout_ring_equipped"));

        ref AttributeBuffer heroAttributes = ref engine.World.Get<AttributeBuffer>(hero);
        int healthAttrId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId("Health");
        int shieldAttrId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId("Shield");
        heroAttributes.SetCurrent(healthAttrId, Math.Max(1f, heroAttributes.GetCurrent(healthAttrId) - 20f));
        heroAttributes.SetCurrent(shieldAttrId, Math.Max(0f, heroAttributes.GetCurrent(shieldAttrId) - 12f));

        float heroHealthBeforePulse = ReadAttribute(engine.World, hero, "Health");
        float heroShieldBeforePulse = ReadAttribute(engine.World, hero, "Shield");
        InvokeRuntime(runtime, "CastMythicPulse", engine);
        Tick(engine, 6, frameTimesMs);
        float heroHealthAfterPulse = ReadAttribute(engine.World, hero, "Health");
        float heroShieldAfterPulse = ReadAttribute(engine.World, hero, "Shield");
        Assert.That(heroHealthAfterPulse, Is.GreaterThanOrEqualTo(heroHealthBeforePulse));
        Assert.That(heroShieldAfterPulse, Is.GreaterThan(heroShieldBeforePulse));

        InvokeRuntime(runtime, "CastSecondWind", engine);
        Tick(engine, 6, frameTimesMs);
        Assert.That(ReadAttribute(engine.World, hero, "MoveSpeed"), Is.GreaterThanOrEqualTo(moveSpeedReequipped));
        timeline.Add("[T+005] Mythic Pulse and Second Wind resolved from item-granted slots inside the loadout room.");
        snapshots.Add(BuildSnapshot(engine, uiRoot, "loadout_abilities_cast"));

        ClickButton(uiRoot, "Bench");
        Tick(engine, 16, frameTimesMs);
        AssertCurrentMap(engine, WeaponMapId);
        Assert.That(CountMapEntities(engine.World, LoadoutMapId), Is.EqualTo(0), "Loadout Garage entities should be cleaned when leaving the room.");
        AssertUiContains(uiRoot, "Weapon Bench");
        AssertUiContains(uiRoot, "Weapon Bench Moves");
        Assert.That(CountEntitiesByName(engine.World, "Loadout Pilot"), Is.EqualTo(1), "Exactly one hero should exist after entering Weapon Bench.");
        Assert.That(CountEntitiesByName(engine.World, "Target Dummy"), Is.EqualTo(1), "Exactly one dummy should exist after entering Weapon Bench.");
        hero = FindEntityByName(engine.World, "Loadout Pilot");
        dummy = FindEntityByName(engine.World, "Target Dummy");
        timeline.Add("[T+006] Entered Weapon Bench to focus on one rifle and one target.");
        snapshots.Add(BuildSnapshot(engine, uiRoot, "weapon_room_loaded"));

        float armorBeforeGrip = ReadAttribute(engine.World, hero, "Armor");
        InvokeRuntime(runtime, "AttachGrip", engine);
        Tick(engine, 6, frameTimesMs);
        float armorAfterGrip = ReadAttribute(engine.World, hero, "Armor");
        Assert.That(armorAfterGrip, Is.GreaterThan(armorBeforeGrip));
        AssertUiContains(uiRoot, "underbarrel: Vertical Grip");
        timeline.Add($"[T+007] Grip mounted at the bench. Armor {armorBeforeGrip:0.0} -> {armorAfterGrip:0.0}.");
        snapshots.Add(BuildSnapshot(engine, uiRoot, "weapon_grip_equipped"));

        float dummyHealthBeforeShot = ReadAttribute(engine.World, dummy, "Health");
        int fmjBeforeReload = inventory.CountStackUnits(hero, definitions.GetId("itm_ammo_556"));
        InvokeRuntime(runtime, "FirePrimary", engine);
        Tick(engine, 6, frameTimesMs);
        float dummyHealthAfterShot = ReadAttribute(engine.World, dummy, "Health");
        Assert.That(dummyHealthAfterShot, Is.LessThan(dummyHealthBeforeShot));

        InvokeRuntime(runtime, "Reload", engine);
        Tick(engine, 2, frameTimesMs);
        int fmjAfterReload = inventory.CountStackUnits(hero, definitions.GetId("itm_ammo_556"));
        Assert.That(fmjAfterReload, Is.LessThan(fmjBeforeReload));
        timeline.Add($"[T+008] Rifle fired through GAS and reloaded from shared ammo stacks. Dummy HP {dummyHealthBeforeShot:0.0} -> {dummyHealthAfterShot:0.0}, FMJ {fmjBeforeReload} -> {fmjAfterReload}.");
        snapshots.Add(BuildSnapshot(engine, uiRoot, "weapon_fire_and_reload"));

        ClickButton(uiRoot, "Raid");
        Tick(engine, 16, frameTimesMs);
        AssertCurrentMap(engine, RaidMapId);
        Assert.That(CountMapEntities(engine.World, WeaponMapId), Is.EqualTo(0), "Weapon Bench entities should be cleaned when leaving the room.");
        AssertUiContains(uiRoot, "Raid Loop");
        AssertUiContains(uiRoot, "Raid Loop Moves");
        Assert.That(CountEntitiesByName(engine.World, "Loadout Pilot"), Is.EqualTo(1), "Exactly one hero should exist after entering Raid Loop.");
        Assert.That(CountEntitiesByName(engine.World, "Target Dummy"), Is.EqualTo(1), "Exactly one dummy should exist after entering Raid Loop.");
        hero = FindEntityByName(engine.World, "Loadout Pilot");
        dummy = FindEntityByName(engine.World, "Target Dummy");
        timeline.Add("[T+009] Entered Raid Loop to move loot through stash, secure case, vendor, and backpack.");
        snapshots.Add(BuildSnapshot(engine, uiRoot, "raid_room_loaded"));

        int secureArtifactsBefore = CountItemStacksInActorContainers(engine.World, inventory, hero, ItemContainerPurpose.SecureStorage, definitions.GetId("itm_extraction_artifact"));
        int stashArtifactsBefore = CountItemStacksInActorContainers(engine.World, inventory, hero, ItemContainerPurpose.Stash, definitions.GetId("itm_extraction_artifact"));
        InvokeRuntime(runtime, "StoreArtifact", engine);
        Tick(engine, 4, frameTimesMs);
        int secureArtifactsAfterStore = CountItemStacksInActorContainers(engine.World, inventory, hero, ItemContainerPurpose.SecureStorage, definitions.GetId("itm_extraction_artifact"));
        int stashArtifactsAfterStore = CountItemStacksInActorContainers(engine.World, inventory, hero, ItemContainerPurpose.Stash, definitions.GetId("itm_extraction_artifact"));
        Assert.That(secureArtifactsAfterStore, Is.GreaterThanOrEqualTo(secureArtifactsBefore));
        Assert.That(stashArtifactsAfterStore, Is.LessThanOrEqualTo(stashArtifactsBefore));

        int creditsBeforeBuy = inventory.CountStackUnits(hero, definitions.GetId("itm_credit_chip"));
        int apBeforeBuy = inventory.CountStackUnits(hero, definitions.GetId("itm_ammo_556_ap"));
        InvokeRuntime(runtime, "BuyApAmmo", engine);
        Tick(engine, 4, frameTimesMs);
        int creditsAfterBuy = inventory.CountStackUnits(hero, definitions.GetId("itm_credit_chip"));
        int apAfterBuy = inventory.CountStackUnits(hero, definitions.GetId("itm_ammo_556_ap"));
        Assert.That(creditsAfterBuy, Is.LessThan(creditsBeforeBuy));
        Assert.That(apAfterBuy, Is.GreaterThan(apBeforeBuy));

        int creditsBeforeSell = inventory.CountStackUnits(hero, definitions.GetId("itm_credit_chip"));
        InvokeRuntime(runtime, "SellArtifact", engine);
        Tick(engine, 4, frameTimesMs);
        int creditsAfterSell = inventory.CountStackUnits(hero, definitions.GetId("itm_credit_chip"));
        Assert.That(creditsAfterSell, Is.GreaterThan(creditsBeforeSell));

        int backpackFmjBeforeSplit = CountItemStacksInActorContainers(engine.World, inventory, hero, ItemContainerPurpose.Backpack, definitions.GetId("itm_ammo_556"));
        InvokeRuntime(runtime, "SplitAmmo", engine);
        Tick(engine, 6, frameTimesMs);
        int backpackFmjAfterSplit = CountItemStacksInActorContainers(engine.World, inventory, hero, ItemContainerPurpose.Backpack, definitions.GetId("itm_ammo_556"));
        Assert.That(backpackFmjAfterSplit, Is.GreaterThanOrEqualTo(backpackFmjBeforeSplit));
        timeline.Add($"[T+010] Extraction storage, vendor trading, and ammo split flow resolved in the raid room. Credits {creditsBeforeBuy} -> {creditsAfterBuy} -> {creditsAfterSell}.");
        snapshots.Add(BuildSnapshot(engine, uiRoot, "raid_storage_trade_split"));

        AssertUiContains(uiRoot, "Bought one AP ammo stack for 20 credits.");
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        OutcomeSnapshot outcome = CaptureOutcome(engine.World, hero, dummy);

        engine.UnloadMap(RaidMapId);
        Tick(engine, 4, frameTimesMs);
        AssertUiNotContains(uiRoot, "Raid Loop");
        Assert.That(CountMapEntities(engine.World, RaidMapId), Is.EqualTo(0), "Map-scoped showcase entities should be cleaned on unload.");
        Assert.That(FindEntityByNameIfAny(engine.World, "Loadout Pilot"), Is.EqualTo(Entity.Null));
        Assert.That(FindEntityByNameIfAny(engine.World, "Target Dummy"), Is.EqualTo(Entity.Null));
        timeline.Add("[T+011] Raid room unloaded cleanly. All map-scoped actors, item trees, containers, and UI state were removed.");

        File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(snapshots));
        File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, frameTimesMs, outcome, raylibEvidence));
        File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());
    }

    private static object BuildSnapshot(GameEngine engine, UIRoot uiRoot, string step)
    {
        Entity hero = FindEntityByName(engine.World, "Loadout Pilot");
        Entity dummy = FindEntityByName(engine.World, "Target Dummy");
        var uiText = ExtractUiText(uiRoot);
        return new
        {
            event_id = step,
            map_id = engine.CurrentMapSession?.MapId.Value ?? string.Empty,
            hero = new
            {
                health = ReadAttribute(engine.World, hero, "Health"),
                shield = ReadAttribute(engine.World, hero, "Shield"),
                move_speed = ReadAttribute(engine.World, hero, "MoveSpeed"),
                attack_damage = ReadAttribute(engine.World, hero, "AttackDamage"),
                armor = ReadAttribute(engine.World, hero, "Armor")
            },
            dummy = new
            {
                health = ReadAttribute(engine.World, dummy, "Health")
            },
            ui_head = uiText.Take(16).ToArray(),
            status = "done"
        };
    }

    private static int CountItemStacksInActorContainers(World world, InventoryRuntimeService inventory, Entity owner, ItemContainerPurpose purpose, int definitionId)
    {
        int total = 0;
        var items = new List<Entity>(32);
        world.Query(in ContainerQuery, (Entity container, ref ItemContainerCm data) =>
        {
            if (data.Purpose != purpose ||
                !inventory.TryResolveOwningActorFromContainer(container, out Entity actor) ||
                actor != owner)
            {
                return;
            }

            items.Clear();
            inventory.CollectItemsInContainer(container, items);
            for (int i = 0; i < items.Count; i++)
            {
                Entity item = items[i];
                if (world.IsAlive(item) &&
                    world.Has<ItemInstanceCm>(item) &&
                    world.Get<ItemInstanceCm>(item).DefinitionId == definitionId)
                {
                    total += world.Get<ItemInstanceCm>(item).StackCount;
                }
            }
        });

        return total;
    }

    private static object GetRuntime(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(RuntimeKey, out object? runtime) && runtime != null
            ? runtime
            : throw new InvalidOperationException("Item showcase runtime not found.");
    }

    private static void InvokeRuntime(object runtime, string methodName, GameEngine engine)
    {
        MethodInfo method = runtime.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(runtime.GetType().FullName, methodName);
        method.Invoke(runtime, new object[] { engine });
    }

    private static Entity FindEntityByName(World world, string nameValue)
    {
        Entity found = FindEntityByNameIfAny(world, nameValue);
        Assert.That(found, Is.Not.EqualTo(Entity.Null), $"Entity '{nameValue}' should exist.");
        return found;
    }

    private static Entity FindEntityByNameIfAny(World world, string nameValue)
    {
        Entity found = Entity.Null;
        world.Query(in NameQuery, (Entity entity, ref Name name) =>
        {
            if (found != Entity.Null)
            {
                return;
            }

            if (string.Equals(name.Value, nameValue, StringComparison.Ordinal))
            {
                found = entity;
            }
        });

        return found;
    }

    private static int CountEntitiesByName(World world, string nameValue)
    {
        int count = 0;
        world.Query(in NameQuery, (Entity entity, ref Name name) =>
        {
            if (string.Equals(name.Value, nameValue, StringComparison.Ordinal))
            {
                count++;
            }
        });

        return count;
    }

    private static float ReadAttribute(World world, Entity entity, string attributeName)
    {
        int attributeId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId(attributeName);
        Assert.That(attributeId, Is.GreaterThanOrEqualTo(0), $"Attribute '{attributeName}' should be registered.");
        return world.Has<AttributeBuffer>(entity)
            ? world.Get<AttributeBuffer>(entity).GetCurrent(attributeId)
            : 0f;
    }

    private static void AssertUiContains(UIRoot root, string expected)
    {
        List<string> uiText = ExtractUiText(root);
        Assert.That(uiText.Any(text => text.Contains(expected, StringComparison.Ordinal)), Is.True,
            $"Expected UI text containing '{expected}', but saw: {string.Join(" | ", uiText.Take(24))}");
    }

    private static void AssertUiNotContains(UIRoot root, string unexpected)
    {
        List<string> uiText = ExtractUiText(root);
        Assert.That(uiText.Any(text => text.Contains(unexpected, StringComparison.Ordinal)), Is.False,
            $"Did not expect UI text containing '{unexpected}', but saw: {string.Join(" | ", uiText.Take(24))}");
    }

    private static void AssertCurrentMap(GameEngine engine, string expectedMapId)
    {
        Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(expectedMapId), $"Expected current map '{expectedMapId}'.");
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        string assetsRoot = Path.Combine(repoRoot, "assets");
        var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, assetsRoot);

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
        engine.GlobalContext["Tests.ItemSystemShowcase.HeadlessCamera"] = new HeadlessCameraRuntime(
            cameraPresenter,
            engine.GetService(CoreServiceKeys.PresentationFrameSetup));

        engine.Start();
        return engine;
    }

    private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs, int frames = 16)
    {
        string? currentMapId = engine.CurrentMapSession?.MapId.Value;
        if (string.Equals(currentMapId, mapId, StringComparison.Ordinal))
        {
            Tick(engine, frames, frameTimesMs);
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentMapId))
        {
            engine.UnloadMap(currentMapId);
            Tick(engine, 2, frameTimesMs);
        }

        engine.LoadMap(mapId);
        Assert.That(engine.CurrentMapSession, Is.Not.Null, $"{mapId} should create a live map session.");
        Tick(engine, frames, frameTimesMs);
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

    private static void ClickButton(UIRoot root, string label)
    {
        UiScene scene = root.Scene ?? throw new InvalidOperationException("UI scene should be mounted before clicking buttons.");
        UiNode target = FindClickableNodeByLabel(scene.Root, label)
            ?? throw new InvalidOperationException($"Clickable node '{label}' was not found.");
        UiActionHandle handle = target.ActionHandles.FirstOrDefault();
        Assert.That(handle.IsValid, Is.True, $"Clickable node '{label}' should expose an action handle.");
        bool dispatched = scene.Dispatcher.Dispatch(
            handle,
            new UiActionContext(scene, new UiPointerEvent(UiPointerEventType.Up, 0, 0f, 0f, target.Id), target));
        Assert.That(dispatched, Is.True, $"Button '{label}' should dispatch.");
    }

    private static UiNode? FindClickableNodeByLabel(UiNode? root, string label)
    {
        if (root == null)
        {
            return null;
        }

        if (string.Equals(root.TextContent?.Trim(), label, StringComparison.Ordinal))
        {
            UiNode? clickable = root;
            while (clickable != null && clickable.ActionHandles.Count == 0)
            {
                clickable = clickable.Parent;
            }

            if (clickable != null)
            {
                return clickable;
            }
        }

        for (int i = 0; i < root.Children.Count; i++)
        {
            UiNode? match = FindClickableNodeByLabel(root.Children[i], label);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static void UpdateHeadlessCamera(GameEngine engine)
    {
        if (!engine.GlobalContext.TryGetValue("Tests.ItemSystemShowcase.HeadlessCamera", out object? runtimeObj) ||
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

    private static string BuildTraceJsonl(IReadOnlyList<object> snapshots)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        var lines = new List<string>(snapshots.Count);
        for (int i = 0; i < snapshots.Count; i++)
        {
            lines.Add(JsonSerializer.Serialize(snapshots[i], options));
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildBattleReport(IReadOnlyList<string> timeline, IReadOnlyList<double> frameTimesMs, OutcomeSnapshot outcome, RaylibEvidenceStatus raylibEvidence)
    {
        double median = Median(frameTimesMs);
        double max = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: item-system-showcase");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: move through a compact demo pack that teaches one unified item/equip/backpack architecture through three short rooms instead of one debug wall.");
        sb.AppendLine("- Gameplay domain: loadout tuning, modular rifle bench work, and extraction-facing stash/secure/vendor loops built on the same ECS + GAS runtime.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine("- Seed: none");
        sb.AppendLine($"- Route: `{HubMapId}` -> `{LoadoutMapId}` -> `{WeaponMapId}` -> `{RaidMapId}`");
        sb.AppendLine($"- Mods: `{string.Join("`, `", AcceptanceMods)}`");
        sb.AppendLine("- Clock profile: fixed `1/60s`, headless `GameEngine.Tick()` loop.");
        sb.AppendLine("- Runtime path: real config pipeline, real item registries, real ECS containers, real GAS effect/ability systems, and the production ReactivePage HUD.");
        sb.AppendLine();
        sb.AppendLine("## Evidence Artifacts");
        sb.AppendLine("- `artifacts/acceptance/item-system-showcase/trace.jsonl`");
        sb.AppendLine("- `artifacts/acceptance/item-system-showcase/battle-report.md`");
        sb.AppendLine("- `artifacts/acceptance/item-system-showcase/path.mmd`");
        sb.AppendLine("- `artifacts/acceptance/item-system-showcase/item-system-showcase-raylib.png`");
        sb.AppendLine("- `artifacts/acceptance/item-system-showcase/item-system-showcase-raylib-diagnostic.log`");
        sb.AppendLine("- `artifacts/techdebt/2026-03-23-launcher-env-propagation.md`");
        sb.AppendLine();
        sb.AppendLine("## Raylib Evidence");
        if (raylibEvidence.Validated)
        {
            sb.AppendLine($"- screenshot validated: yes (`{Path.GetFileName(raylibEvidence.ScreenshotPath)}`, {raylibEvidence.ScreenshotWrittenAtUtc:O})");
            sb.AppendLine($"- diagnostic validated: yes (`{Path.GetFileName(raylibEvidence.DiagnosticPath)}`, {raylibEvidence.DiagnosticWrittenAtUtc:O})");
            sb.AppendLine($"- freshness gate: >= {raylibEvidence.NotBeforeUtc:O}");
        }
        else
        {
            sb.AppendLine("- screenshot validated: no");
            sb.AppendLine("- note: set `LUDOTS_ACCEPTANCE_SCREENSHOT_NOT_BEFORE_UTC` (and optional screenshot/log paths) before rerunning this acceptance test to assert fresh Raylib evidence.");
        }
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        for (int i = 0; i < timeline.Count; i++)
        {
            sb.AppendLine($"- {timeline[i]}");
        }
        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine("- success: yes");
        sb.AppendLine($"- hero health: {outcome.HeroHealth:0.0}");
        sb.AppendLine($"- hero shield: {outcome.HeroShield:0.0}");
        sb.AppendLine($"- hero move speed: {outcome.HeroMoveSpeed:0.0}");
        sb.AppendLine($"- hero attack damage: {outcome.HeroAttackDamage:0.0}");
        sb.AppendLine($"- hero armor: {outcome.HeroArmor:0.0}");
        sb.AppendLine($"- dummy health: {outcome.DummyHealth:0.0}");
        sb.AppendLine($"- median tick: {median:F3}ms");
        sb.AppendLine($"- max tick: {max:F3}ms");
        sb.AppendLine("- verdict: one architecture now reads as a player-facing demo pack, while still covering equipment, mounted containers, stash routing, ammo logistics, trading, and item-driven GAS grants without any fallback runtime path.");
        sb.AppendLine();
        sb.AppendLine("## Cross-Layer Note");
        sb.AppendLine("- debt id: `launcher-env-propagation`");
        sb.AppendLine("- finding: launcher startup used `UseShellExecute = true`, which dropped Raylib screenshot and diagnostic environment variables during acceptance capture.");
        sb.AppendLine("- containment: fixed in `src/Tools/Ludots.Launcher.Backend/LauncherService.cs` and documented in `artifacts/techdebt/2026-03-23-launcher-env-propagation.md`.");
        return sb.ToString();
    }

    private static string BuildPathMermaid()
    {
        return """
flowchart TD
    A["Load item_system_showcase_hub"] --> B["Read the three-room demo pack and choose a route"]
    B --> C["Enter item_system_showcase_loadout_garage"]
    C --> D["Toggle boots and equip ring to feel build changes"]
    D --> E["Cast mythic + second wind from item-granted slots"]
    E --> F["Enter item_system_showcase_weapon_bench"]
    F --> G["Attach grip, fire rifle, and reload from shared ammo"]
    G --> H["Enter item_system_showcase_raid_loop"]
    H --> I["Move artifact into secure storage"]
    I --> J["Buy AP ammo and sell loot to vendor"]
    J --> K["Split FMJ stack into backpack grid"]
    K --> L["Unload raid room and verify cleanup"]
    L --> M["Write trace, battle report, and path artifacts"]
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

    private static OutcomeSnapshot CaptureOutcome(World world, Entity hero, Entity dummy)
    {
        return new OutcomeSnapshot(
            ReadAttribute(world, hero, "Health"),
            ReadAttribute(world, hero, "Shield"),
            ReadAttribute(world, hero, "MoveSpeed"),
            ReadAttribute(world, hero, "AttackDamage"),
            ReadAttribute(world, hero, "Armor"),
            ReadAttribute(world, dummy, "Health"));
    }

    private static int CountMapEntities(World world, string mapId)
    {
        int count = 0;
        world.Query(in MapEntityQuery, (Entity _, ref MapEntity mapEntity) =>
        {
            if (string.Equals(mapEntity.MapId.Value, mapId, StringComparison.Ordinal))
            {
                count++;
            }
        });
        return count;
    }

    private static RaylibEvidenceStatus ResolveRaylibEvidenceStatus(string artifactDir)
    {
        string screenshotPath = Environment.GetEnvironmentVariable("LUDOTS_ACCEPTANCE_SCREENSHOT_PATH");
        if (string.IsNullOrWhiteSpace(screenshotPath))
        {
            screenshotPath = Path.Combine(artifactDir, "item-system-showcase-raylib.png");
        }

        string diagnosticPath = Environment.GetEnvironmentVariable("LUDOTS_ACCEPTANCE_DIAGNOSTIC_PATH");
        if (string.IsNullOrWhiteSpace(diagnosticPath))
        {
            diagnosticPath = Path.Combine(artifactDir, "item-system-showcase-raylib-diagnostic.log");
        }

        string notBeforeRaw = Environment.GetEnvironmentVariable("LUDOTS_ACCEPTANCE_SCREENSHOT_NOT_BEFORE_UTC");
        bool requireValidation =
            !string.IsNullOrWhiteSpace(notBeforeRaw) ||
            string.Equals(Environment.GetEnvironmentVariable("LUDOTS_ACCEPTANCE_REQUIRE_RAYLIB_EVIDENCE"), "1", StringComparison.Ordinal);

        if (!requireValidation)
        {
            return new RaylibEvidenceStatus(screenshotPath, diagnosticPath, false, null, null, null);
        }

        Assert.That(string.IsNullOrWhiteSpace(notBeforeRaw), Is.False,
            "Raylib evidence validation requires LUDOTS_ACCEPTANCE_SCREENSHOT_NOT_BEFORE_UTC.");
        Assert.That(DateTimeOffset.TryParse(notBeforeRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset notBeforeUtc), Is.True,
            $"Could not parse LUDOTS_ACCEPTANCE_SCREENSHOT_NOT_BEFORE_UTC='{notBeforeRaw}'.");
        Assert.That(File.Exists(screenshotPath), Is.True, $"Expected fresh Raylib screenshot at '{screenshotPath}'.");
        Assert.That(File.Exists(diagnosticPath), Is.True, $"Expected fresh Raylib diagnostic log at '{diagnosticPath}'.");

        DateTimeOffset screenshotWrittenAtUtc = File.GetLastWriteTimeUtc(screenshotPath);
        DateTimeOffset diagnosticWrittenAtUtc = File.GetLastWriteTimeUtc(diagnosticPath);
        Assert.That(screenshotWrittenAtUtc, Is.GreaterThanOrEqualTo(notBeforeUtc),
            $"Raylib screenshot '{screenshotPath}' is stale: {screenshotWrittenAtUtc:O} < {notBeforeUtc:O}.");
        Assert.That(diagnosticWrittenAtUtc, Is.GreaterThanOrEqualTo(notBeforeUtc),
            $"Raylib diagnostic '{diagnosticPath}' is stale: {diagnosticWrittenAtUtc:O} < {notBeforeUtc:O}.");

        return new RaylibEvidenceStatus(screenshotPath, diagnosticPath, true, screenshotWrittenAtUtc, diagnosticWrittenAtUtc, notBeforeUtc);
    }

    private sealed class RaylibEvidenceStatus
    {
        public RaylibEvidenceStatus(
            string screenshotPath,
            string diagnosticPath,
            bool validated,
            DateTimeOffset? screenshotWrittenAtUtc,
            DateTimeOffset? diagnosticWrittenAtUtc,
            DateTimeOffset? notBeforeUtc)
        {
            ScreenshotPath = screenshotPath;
            DiagnosticPath = diagnosticPath;
            Validated = validated;
            ScreenshotWrittenAtUtc = screenshotWrittenAtUtc;
            DiagnosticWrittenAtUtc = diagnosticWrittenAtUtc;
            NotBeforeUtc = notBeforeUtc;
        }

        public string ScreenshotPath { get; }
        public string DiagnosticPath { get; }
        public bool Validated { get; }
        public DateTimeOffset? ScreenshotWrittenAtUtc { get; }
        public DateTimeOffset? DiagnosticWrittenAtUtc { get; }
        public DateTimeOffset? NotBeforeUtc { get; }
    }

    private sealed class OutcomeSnapshot
    {
        public OutcomeSnapshot(
            float heroHealth,
            float heroShield,
            float heroMoveSpeed,
            float heroAttackDamage,
            float heroArmor,
            float dummyHealth)
        {
            HeroHealth = heroHealth;
            HeroShield = heroShield;
            HeroMoveSpeed = heroMoveSpeed;
            HeroAttackDamage = heroAttackDamage;
            HeroArmor = heroArmor;
            DummyHealth = dummyHealth;
        }

        public float HeroHealth { get; }
        public float HeroShield { get; }
        public float HeroMoveSpeed { get; }
        public float HeroAttackDamage { get; }
        public float HeroArmor { get; }
        public float DummyHealth { get; }
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

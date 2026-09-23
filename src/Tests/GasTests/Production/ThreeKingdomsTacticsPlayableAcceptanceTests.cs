using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Runtime.Events;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;
using ThreeKingdomsTacticsMod;
using ThreeKingdomsTacticsMod.Runtime;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
public sealed class ThreeKingdomsTacticsPlayableAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string ArtifactFolderName = "three-kingdoms-tactics";
    private const string MapId = ThreeKingdomsTacticsIds.MapId;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "ThreeKingdomsTacticsMod"
    };

    [Test]
    public async Task ThreeKingdomsTactics_PlayableFlow_WritesAcceptanceArtifacts()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", ArtifactFolderName);
        Directory.CreateDirectory(artifactDir);

        var frameTimesMs = new List<double>();
        var timeline = new List<string>();
        var trace = new List<object>();

        using var engine = CreateEngine();
        UIRoot uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");

        LoadMap(engine, MapId, frameTimesMs);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "Three Kingdoms tactics map should load without trigger errors.");
        Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(MapId));
        Assert.That(engine.GlobalContext.TryGetValue(ThreeKingdomsTacticsIds.RuntimeKey, out object? runtimeObj), Is.True);
        var runtime = runtimeObj as ThreeKingdomsTacticsRuntime
            ?? throw new InvalidOperationException("Three Kingdoms tactics runtime missing.");

        AssertStaticContentIsComplete();
        AssertGasContentLoaded(engine);

        ThreeKingdomsTacticsSnapshot loaded = runtime.Snapshot;
        Assert.That(loaded.AllGenerals, Is.EqualTo(100));
        Assert.That(loaded.UniqueSkills, Is.EqualTo(100));
        Assert.That(loaded.TroopTypes, Is.EqualTo(100));
        Assert.That(loaded.ArsenalItems, Is.EqualTo(100));
        Assert.That(loaded.GasAbilityDefinitions, Is.EqualTo(100));
        Assert.That(loaded.GraphPrograms, Is.EqualTo(3));
        Assert.That(loaded.PlayerUnitsAlive, Is.EqualTo(12));
        Assert.That(loaded.EnemyUnitsAlive, Is.EqualTo(12));
        Assert.That(loaded.Outcome, Is.EqualTo("InProgress"));
        Assert.That(loaded.DevelopmentLevel, Is.EqualTo(0));
        Assert.That(loaded.CampaignMomentum, Is.EqualTo(0));
        string[] mapRows = runtime.BuildMapRows();
        Assert.That(mapRows, Has.Length.EqualTo(ThreeKingdomsContent.MapHeight));
        Assert.That(mapRows.Any(row => row.Contains('P')), Is.True);
        Assert.That(mapRows.Any(row => row.Contains('E')), Is.True);

        AssertUiContains(uiRoot, "Three Kingdoms Grand Tactics");
        AssertUiContains(uiRoot, "Seamless Campaign Map");
        AssertUiContains(uiRoot, "Fielded Units");
        AssertUiContains(uiRoot, "Battle Report");
        AssertUiContains(uiRoot, loaded.SelectedSkill);
        Assert.That(ThreeKingdomsTacticsIds.DataPlaneTopic, Is.EqualTo("ludots.threeKingdoms.tactics.world"));

        var dataPlane = new ThreeKingdomsTacticsTopicProducer(engine, runtime);
        ThreeKingdomsTacticsSnapshot dataPlaneLoaded = ReadDataPlaneSnapshot(dataPlane, requestId: 101);
        Assert.That(dataPlaneLoaded.SelectedGeneral, Is.EqualTo(loaded.SelectedGeneral));
        Assert.That(dataPlaneLoaded.Units, Has.Count.EqualTo(loaded.Units.Count));

        WebUiOutboundPacket selectAck = await DispatchDataPlaneCommandAsync(dataPlane, "selectNext", new { }, clientSeq: 201);
        AssertCommandAck(selectAck, 201);
        Tick(engine, 2, frameTimesMs);
        ThreeKingdomsTacticsSnapshot dataPlaneSelected = ReadDataPlaneSnapshot(dataPlane, requestId: 102);
        Assert.That(dataPlaneSelected.SelectedIndex, Is.Not.EqualTo(dataPlaneLoaded.SelectedIndex));
        Assert.That(dataPlaneSelected.SelectedGeneral, Is.EqualTo(runtime.Snapshot.SelectedGeneral));

        timeline.Add("[T+001] Loaded the seamless Three Kingdoms tactics map with 100 unique officer skills, 100 troop charters, GAS definitions, graph programs, and the native command UI visible.");
        trace.Add(CaptureTrace(runtime, "loaded"));

        ThreeKingdomsUnitView beforeMove = Selected(runtime.Snapshot);
        WebUiOutboundPacket moveAck = await DispatchDataPlaneCommandAsync(dataPlane, "move", new { dx = 0, dy = -2 }, clientSeq: 202);
        AssertCommandAck(moveAck, 202);
        Tick(engine, 4, frameTimesMs);
        ThreeKingdomsUnitView afterMove = Selected(runtime.Snapshot);
        Assert.That(afterMove.X, Is.EqualTo(beforeMove.X));
        Assert.That(afterMove.Y, Is.EqualTo(beforeMove.Y - 2));
        Assert.That(runtime.Snapshot.LastAction, Does.Contain("marched"));
        timeline.Add($"[T+002] DataPlane move command marched {afterMove.Name} north from ({beforeMove.X},{beforeMove.Y}) to ({afterMove.X},{afterMove.Y}).");
        trace.Add(CaptureTrace(runtime, "move_north"));

        ClickButton(uiRoot, "Attack");
        Tick(engine, 4, frameTimesMs);
        Assert.That(runtime.Snapshot.LastAction, Does.Contain("range").Or.Contain("attack"));
        timeline.Add("[T+003] The attack command resolved against the nearest enemy and reported range or damage through the battle log.");
        trace.Add(CaptureTrace(runtime, "attack_nearest"));

        string selectedSkill = runtime.Snapshot.SelectedSkill;
        int enemyHealthBeforeSkill = runtime.Snapshot.Units.Where(unit => unit.TeamId == 2 && unit.Alive).Sum(unit => unit.Health);
        ClickButton(uiRoot, selectedSkill);
        Tick(engine, 8, frameTimesMs);
        int enemyHealthAfterSkill = runtime.Snapshot.Units.Where(unit => unit.TeamId == 2 && unit.Alive).Sum(unit => unit.Health);
        Assert.That(runtime.Snapshot.LastAction, Does.Contain(selectedSkill).Or.Contain("rallied").Or.Contain("restored").Or.Contain("opened"));
        Assert.That(enemyHealthAfterSkill, Is.LessThanOrEqualTo(enemyHealthBeforeSkill));
        Assert.That(runtime.Snapshot.LastGraphProgram, Is.EqualTo("tk.graph.skillScore"));
        Assert.That(runtime.Snapshot.LastGraphScore, Is.GreaterThan(0));
        Assert.That(runtime.Snapshot.LastAction, Does.Contain("tk.graph.skillScore"));
        timeline.Add($"[T+004] Cast {selectedSkill}; the selected officer skill executed through the GAS ability slot and graph score {runtime.Snapshot.LastGraphScore} updated battlefield state.");
        trace.Add(CaptureTrace(runtime, "cast_skill"));

        var inventory = engine.GetService(CoreServiceKeys.InventoryRuntimeService)
            ?? throw new InvalidOperationException("InventoryRuntimeService missing.");
        var itemDefinitions = engine.GetService(CoreServiceKeys.ItemDefinitionRegistry)
            ?? throw new InvalidOperationException("ItemDefinitionRegistry missing.");
        var localPlayer = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        ThreeKingdomsUnitState selectedStateBeforeTroop = runtime.Units[Selected(runtime.Snapshot).Index];
        ThreeKingdomsTroopTypeDefinition nextTroop = ThreeKingdomsContent.TroopTypes[selectedStateBeforeTroop.TroopType.Index % ThreeKingdomsContent.TroopTypes.Count];
        int nextTroopDefinitionId = itemDefinitions.GetId(nextTroop.ItemDefinitionId);
        Assert.That(nextTroopDefinitionId, Is.GreaterThan(0));
        int nextTroopStockBefore = inventory.CountStackUnits(localPlayer, nextTroopDefinitionId);
        string troopBefore = runtime.Snapshot.SelectedTroop;
        ClickButton(uiRoot, "Troop");
        Tick(engine, 4, frameTimesMs);
        Assert.That(runtime.Snapshot.SelectedTroop, Is.Not.EqualTo(troopBefore));
        Assert.That(runtime.Snapshot.LastAction, Does.Contain("changed troop charter"));
        Assert.That(runtime.Snapshot.LastItemTransaction, Does.Contain(nextTroop.ItemDefinitionId));
        Assert.That(inventory.CountStackUnits(localPlayer, nextTroopDefinitionId), Is.EqualTo(nextTroopStockBefore - 1));
        Assert.That(runtime.Snapshot.ArsenalItems, Is.EqualTo(loaded.ArsenalItems - 1));
        timeline.Add($"[T+005] Reformed the selected unit from {troopBefore} into {runtime.Snapshot.SelectedTroop}, consuming {nextTroop.ItemDefinitionId} from the 100-item troop arsenal.");
        trace.Add(CaptureTrace(runtime, "cycle_troop"));

        int turnBefore = runtime.Snapshot.Turn;
        int playerHealthBeforeEndTurn = runtime.Snapshot.Units.Where(unit => unit.TeamId == 1 && unit.Alive).Sum(unit => unit.Health);
        ClickButton(uiRoot, "End Turn");
        Tick(engine, 8, frameTimesMs);
        Assert.That(runtime.Snapshot.Turn, Is.GreaterThan(turnBefore));
        Assert.That(runtime.Snapshot.Phase, Is.EqualTo("Player"));
        Assert.That(runtime.Snapshot.Units.Where(unit => unit.TeamId == 1 && unit.Alive).Sum(unit => unit.Health), Is.LessThanOrEqualTo(playerHealthBeforeEndTurn));
        timeline.Add("[T+006] End Turn advanced the round clock, ran the enemy pulse, and returned to the player command phase.");
        trace.Add(CaptureTrace(runtime, "end_turn"));

        WebUiOutboundPacket developAck = await DispatchDataPlaneCommandAsync(dataPlane, "develop", new { }, clientSeq: 207);
        AssertCommandAck(developAck, 207);
        Tick(engine, 4, frameTimesMs);
        Assert.That(runtime.Snapshot.DevelopmentLevel, Is.GreaterThanOrEqualTo(1));
        Assert.That(runtime.Snapshot.CampaignMomentum, Is.GreaterThanOrEqualTo(12));
        Assert.That(runtime.Snapshot.LastAction, Does.Contain("developed"));
        timeline.Add($"[T+007] Developed the selected commandery; campaign momentum rose to {runtime.Snapshot.CampaignMomentum}.");
        trace.Add(CaptureTrace(runtime, "develop"));

        ThreeKingdomsUnitView beforeAdvance = Selected(runtime.Snapshot);
        WebUiOutboundPacket advanceAck = await DispatchDataPlaneCommandAsync(dataPlane, "advance", new { }, clientSeq: 208);
        AssertCommandAck(advanceAck, 208);
        Tick(engine, 4, frameTimesMs);
        ThreeKingdomsUnitView afterAdvance = Selected(runtime.Snapshot);
        Assert.That(afterAdvance.X != beforeAdvance.X || afterAdvance.Y != beforeAdvance.Y, Is.True);
        Assert.That(runtime.Snapshot.LastAction, Does.Contain("advanced"));
        timeline.Add($"[T+008] Advanced {afterAdvance.Name} from ({beforeAdvance.X},{beforeAdvance.Y}) to ({afterAdvance.X},{afterAdvance.Y}) through the seamless map.");
        trace.Add(CaptureTrace(runtime, "advance"));

        int battleGuard = 0;
        while (runtime.Snapshot.EnemyUnitsAlive > 0 && battleGuard < 16)
        {
            WebUiOutboundPacket battleAck = await DispatchDataPlaneCommandAsync(dataPlane, "battle", new { }, clientSeq: 300 + battleGuard);
            AssertCommandAck(battleAck, 300 + battleGuard);
            Tick(engine, 6, frameTimesMs);
            trace.Add(CaptureTrace(runtime, $"battle_{battleGuard + 1:00}"));
            battleGuard++;
        }

        Assert.That(runtime.Snapshot.EnemyUnitsAlive, Is.EqualTo(0));
        Assert.That(runtime.Snapshot.Outcome, Is.EqualTo("Victory"));
        Assert.That(runtime.Snapshot.LastAction, Does.Contain("Victory achieved"));
        timeline.Add($"[T+009] Resolved {battleGuard} DataPlane battle commands, eliminated every opposing formation, and reached {runtime.Snapshot.Outcome}.");

        File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(trace), Encoding.UTF8);
        File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, frameTimesMs), Encoding.UTF8);
        File.WriteAllText(Path.Combine(artifactDir, "map.txt"), string.Join(Environment.NewLine, runtime.BuildMapRows()), Encoding.UTF8);
    }

    private static void AssertStaticContentIsComplete()
    {
        Assert.That(ThreeKingdomsContent.Generals, Has.Count.EqualTo(100));
        Assert.That(ThreeKingdomsContent.Generals.Select(static general => general.Id).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(100));
        Assert.That(ThreeKingdomsContent.Generals.Select(static general => general.SkillId).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(100));
        Assert.That(ThreeKingdomsContent.Generals.Select(static general => general.SkillName).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(100));
        Assert.That(ThreeKingdomsContent.Generals.Select(static general => general.SkillDetail).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(100));
        Assert.That(
            ThreeKingdomsContent.Generals
                .Select(static general => $"{general.SkillPattern}:{general.SkillPower}:{general.Leadership}:{general.WarPower}:{general.Strategy}")
                .Distinct(StringComparer.Ordinal)
                .Count(),
            Is.EqualTo(100),
            "Each officer skill should have a unique gameplay tuning signature, not only a unique label.");
        Assert.That(ThreeKingdomsContent.TroopTypes, Has.Count.EqualTo(100));
        Assert.That(ThreeKingdomsContent.TroopTypes.Select(static troop => troop.Id).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(100));
        Assert.That(ThreeKingdomsContent.TroopTypes.Select(static troop => troop.Name).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(100));
        Assert.That(ThreeKingdomsContent.TroopTypes.Select(static troop => troop.ItemDefinitionId).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(100));
        Assert.That(
            ThreeKingdomsContent.TroopTypes
                .Select(static troop => $"{troop.Role}:{troop.TerrainAffinity}:{troop.Move}:{troop.Range}:{troop.Attack}:{troop.Defense}:{troop.SupplyCost}")
                .Distinct(StringComparer.Ordinal)
                .Count(),
            Is.EqualTo(100),
            "Each troop type should have a unique tactical stat signature.");
    }

    private static void AssertGasContentLoaded(GameEngine engine)
    {
        AbilityDefinitionRegistry abilities = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry)
            ?? throw new InvalidOperationException("AbilityDefinitionRegistry missing.");
        EffectTemplateRegistry effects = engine.GetService(CoreServiceKeys.EffectTemplateRegistry)
            ?? throw new InvalidOperationException("EffectTemplateRegistry missing.");

        int abilityCount = 0;
        int effectCount = 0;
        foreach (ThreeKingdomsGeneralDefinition general in ThreeKingdomsContent.Generals)
        {
            int abilityId = AbilityIdRegistry.GetId(general.SkillId);
            if (abilityId > 0 && abilities.TryGet(abilityId, out _))
            {
                abilityCount++;
            }

            string effectKey = general.SkillId.Replace("Ability.", "Effect.", StringComparison.Ordinal);
            int effectId = EffectTemplateIdRegistry.GetId(effectKey);
            if (effectId > 0 && effects.TryGet(effectId, out _))
            {
                effectCount++;
            }
        }

        Assert.That(abilityCount, Is.EqualTo(100), "All generated officer skills should be loaded into AbilityDefinitionRegistry.");
        Assert.That(effectCount, Is.EqualTo(100), "All generated officer effects should be loaded into EffectTemplateRegistry.");
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        string assetsRoot = Path.Combine(repoRoot, "assets");
        var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
        InstallDummyInput(engine);

        AcceptanceUiHostInstaller.Install(engine);

        engine.Start();
        return engine;
    }

    private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs)
    {
        engine.LoadMap(mapId);
        Assert.That(engine.CurrentMapSession, Is.Not.Null, $"{mapId} should create a live map session.");
        Tick(engine, 12, frameTimesMs);
    }

    private static ThreeKingdomsTacticsSnapshot ReadDataPlaneSnapshot(ThreeKingdomsTacticsTopicProducer producer, long requestId)
    {
        var context = new WebUiTopicContext("acceptance-session", ThreeKingdomsTacticsIds.DataPlaneTopic, requestId, default);
        Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
        Assert.That(packet.Topic, Is.EqualTo(ThreeKingdomsTacticsIds.DataPlaneTopic));
        Assert.That(packet.Kind, Is.EqualTo(WebUiPacketKind.Snapshot));
        ThreeKingdomsTacticsSnapshot? snapshot = JsonSerializer.Deserialize<ThreeKingdomsTacticsSnapshot>(packet.Payload.Span, JsonOptions);
        return snapshot ?? throw new InvalidOperationException("DataPlane snapshot payload did not deserialize.");
    }

    private static async Task<WebUiOutboundPacket> DispatchDataPlaneCommandAsync(
        ThreeKingdomsTacticsTopicProducer producer,
        string commandName,
        object payload,
        long clientSeq)
    {
        var router = new WebUiCommandRouter(
            new ThreeKingdomsGenerationResolver(),
            new ThreeKingdomsPermissionValidator());
        router.Register(commandName, new ThreeKingdomsTacticsCommandHandler(producer));

        var request = new WebUiCommandRequest(
            commandName,
            clientSeq,
            Array.Empty<WebUiEntityRef>(),
            JsonSerializer.SerializeToElement(payload, JsonOptions));
        var packet = new WebUiInboundPacket(
            "acceptance-session",
            ThreeKingdomsTacticsIds.DataPlaneTopic,
            WebUiPacketKind.Command,
            WebUiDeliverySemantics.ReliableOrdered,
            JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions),
            "application/json",
            RequestId: clientSeq,
            ClientSeq: clientSeq);

        return await router.HandleAsync(packet, TestContext.CurrentContext.CancellationToken);
    }

    private static void AssertCommandAck(WebUiOutboundPacket packet, long clientSeq)
    {
        Assert.That(packet.Kind, Is.EqualTo(WebUiPacketKind.CommandAck));
        Assert.That(packet.Delivery, Is.EqualTo(WebUiDeliverySemantics.ReliableOrdered));
        Assert.That(packet.ClientSeq, Is.EqualTo(clientSeq));
    }

    private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
    {
        for (int i = 0; i < frames; i++)
        {
            long started = Stopwatch.GetTimestamp();
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
            frameTimesMs.Add((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency);
        }
    }

    private static void ClickButton(UIRoot root, string label)
    {
        UiScene scene = root.Scene ?? throw new InvalidOperationException("UI scene should be mounted before clicking buttons.");
        UiNode target = FindClickableNodeByLabel(scene.Root, label)
            ?? throw new InvalidOperationException($"Clickable node '{label}' was not found. UI text: {string.Join(" | ", ExtractUiText(root).Take(32))}");
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

        if (root.ActionHandles.Count > 0 && SubtreeContainsExactText(root, label))
        {
            return root;
        }

        for (int i = 0; i < root.Children.Count; i++)
        {
            UiNode? found = FindClickableNodeByLabel(root.Children[i], label);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool SubtreeContainsExactText(UiNode node, string label)
    {
        if (string.Equals(node.TextContent?.Trim(), label, StringComparison.Ordinal))
        {
            return true;
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            if (SubtreeContainsExactText(node.Children[i], label))
            {
                return true;
            }
        }

        return false;
    }

    private static void AssertUiContains(UIRoot root, string expected)
    {
        List<string> uiText = ExtractUiText(root);
        Assert.That(uiText.Any(text => text.Contains(expected, StringComparison.Ordinal)), Is.True,
            $"Expected UI text containing '{expected}', but saw: {string.Join(" | ", uiText.Take(32))}");
    }

    private static List<string> ExtractUiText(UIRoot root)
    {
        var output = new List<string>();
        CollectUiText(root.Scene?.Root, output);
        return output;
    }

    private static void CollectUiText(UiNode? node, List<string> output)
    {
        if (node == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(node.TextContent))
        {
            output.Add(node.TextContent);
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            CollectUiText(node.Children[i], output);
        }
    }

    private static ThreeKingdomsUnitView Selected(ThreeKingdomsTacticsSnapshot snapshot)
    {
        return snapshot.Units.Single(unit => unit.Selected);
    }

    private static object CaptureTrace(ThreeKingdomsTacticsRuntime runtime, string step)
    {
        ThreeKingdomsTacticsSnapshot snapshot = runtime.Snapshot;
        return new
        {
            step,
            snapshot.Turn,
            snapshot.Round,
            snapshot.Phase,
            snapshot.SelectedGeneral,
            snapshot.SelectedSkill,
            snapshot.SelectedTroop,
            snapshot.PlayerUnitsAlive,
            snapshot.EnemyUnitsAlive,
            snapshot.ArsenalItems,
            snapshot.GasAbilityDefinitions,
            snapshot.GraphPrograms,
            snapshot.LastGraphProgram,
            snapshot.LastGraphScore,
            snapshot.LastItemTransaction,
            snapshot.LastAction,
            selected = Selected(snapshot),
            log = snapshot.LogLines.Take(6).ToArray()
        };
    }

    private static string BuildTraceJsonl(IEnumerable<object> entries)
    {
        return string.Join(Environment.NewLine, entries.Select(entry => JsonSerializer.Serialize(entry))) + Environment.NewLine;
    }

    private static string BuildBattleReport(IReadOnlyList<string> timeline, IReadOnlyList<double> frameTimesMs)
    {
        double average = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Average();
        double max = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
        var builder = new StringBuilder();
        builder.AppendLine("# Three Kingdoms Tactics Acceptance");
        builder.AppendLine();
        builder.AppendLine($"Frames: {frameTimesMs.Count}");
        builder.AppendLine($"Average frame ms: {average:0.000}");
        builder.AppendLine($"Max frame ms: {max:0.000}");
        builder.AppendLine();
        for (int i = 0; i < timeline.Count; i++)
        {
            builder.AppendLine(timeline[i]);
        }

        return builder.ToString();
    }

    private static void InstallDummyInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) ||
                File.Exists(Path.Combine(dir, ".git")) ||
                File.Exists(Path.Combine(dir, "gitbook", "README.md")))
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
}

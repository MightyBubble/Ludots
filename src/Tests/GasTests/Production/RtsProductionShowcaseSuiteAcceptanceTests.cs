using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Progression.Components;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;
using RtsProductionCapabilityMod.Runtime;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
public sealed class RtsProductionShowcaseSuiteAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;

    private static readonly string[] BaseMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "ParticipantViewCapabilityMod",
        "RtsProductionCapabilityMod",
        "RtsHudWebMod",
    };

    private static IEnumerable<ShowcaseCase> ShowcaseCases
    {
        get
        {
            yield return new ShowcaseCase(
                "RedAlertLikeShowcaseMod",
                "redalert_like_showcase",
                "RedAlertLike",
                new[] { "allied", "soviet" },
                new[]
                {
                    new ProductionExpectation("allied.powerplant", "allied", "Allied Power Plant", "Building", ProductionParadigm.DirectBuild),
                    new ProductionExpectation("allied.deploy_mcv", "allied", "Forward Construction Yard", "Building", ProductionParadigm.DeployBuild),
                    new ProductionExpectation("allied.ranger", "allied", "Ranger Squad", "Unit", ProductionParadigm.Train),
                },
                "redalert.allied.radar",
                "allied.medium_tank",
                "allied_soviet_ceasefire",
                "ore_for_credits");

            yield return new ShowcaseCase(
                "StarCraftLikeShowcaseMod",
                "starcraft_like_showcase",
                "StarCraftLike",
                new[] { "terran", "protoss", "zerg" },
                new[]
                {
                    new ProductionExpectation("terran.supply_depot", "terran", "Supply Depot", "Building", ProductionParadigm.WorkerBuild),
                    new ProductionExpectation("protoss.gateway_unit", "protoss", "Gateway Zealot Pair", "Unit", ProductionParadigm.Train),
                    new ProductionExpectation("zerg.spawning_pool", "zerg", "Spawning Pool", "Building", ProductionParadigm.MorphBuild),
                },
                "starcraft.protoss.warpgate",
                "protoss.warp_zealot",
                "terran_protoss_truce",
                "minerals_for_gas");

            yield return new ShowcaseCase(
                "EmpireLikeShowcaseMod",
                "empire_like_showcase",
                "EmpireLike",
                new[] { "romans", "han" },
                new[]
                {
                    new ProductionExpectation("romans.farm", "romans", "Roman Farm", "Building", ProductionParadigm.WorkerBuild),
                    new ProductionExpectation("han.mill", "han", "Han Mill", "Building", ProductionParadigm.WorkerBuild),
                },
                "empire.romans.age2",
                "romans.legionary",
                "roman_han_alliance",
                "tribute_gold_for_food");

            yield return new ShowcaseCase(
                "FourXLikeShowcaseMod",
                "fourx_like_showcase",
                "FourXLike",
                new[] { "aurora", "vanta", "helio" },
                new[]
                {
                    new ProductionExpectation("aurora.city_district", "aurora", "Aurora Industrial District", "Building", ProductionParadigm.CityQueue),
                    new ProductionExpectation("helio.research_forum", "helio", "Helio Research Forum", "Building", ProductionParadigm.CityQueue),
                },
                "fourx.aurora.trade_routes",
                "aurora.trade_fleet",
                "aurora_vanta_trade_pact",
                "alloys_for_crystal");
        }
    }

    [TestCaseSource(nameof(ShowcaseCases))]
    public void ProductionShowcase_LoadsWebHudAndDrivesProductionTechDiplomacyTrade(ShowcaseCase showcase)
    {
        string repoRoot = FindRepoRoot();
        var frameTimesMs = new List<double>(512);
        using GameEngine engine = CreateEngine(repoRoot, showcase.RootModId);
        engine.Start();
        engine.LoadMap(showcase.MapId);
        Tick(engine, 8, frameTimesMs);

        RtsProductionRuntime runtime = ResolveRuntime(engine);
        UIRoot uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");

        RtsProductionSnapshot initial = runtime.BuildSnapshot(engine);
        Assert.Multiple(() =>
        {
            Assert.That(initial.ScenarioReady, Is.True);
            Assert.That(initial.Title, Is.EqualTo(showcase.ExpectedTitle));
            Assert.That(initial.Factions.Select(faction => faction.Id), Is.SupersetOf(showcase.FactionIds));
            Assert.That(initial.Acceptance, Is.Not.Empty);
            Assert.That(AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot), Has.Some.Contains(showcase.ExpectedTitle));
        });

        foreach (string factionId in showcase.FactionIds)
        {
            runtime.SelectFaction(engine, factionId);
            Tick(engine, 2, frameTimesMs);

            RtsProductionSnapshot switched = runtime.BuildSnapshot(engine);
            RtsFactionSnapshot faction = RequireFaction(switched, factionId);
            var playerLookup = engine.GetService(CoreServiceKeys.PlayerEntityLookup)
                ?? throw new InvalidOperationException("PlayerEntityLookup missing.");
            Assert.Multiple(() =>
            {
                Assert.That(switched.CurrentFactionId, Is.EqualTo(factionId));
                Assert.That(engine.GetService(CoreServiceKeys.LocalPlayerId), Is.EqualTo(faction.PlayerId));
                Assert.That(engine.GetService(CoreServiceKeys.SelectionViewViewerEntity), Is.EqualTo(playerLookup.Get(faction.PlayerId)));
                Assert.That(engine.GetService(CoreServiceKeys.SelectionViewKey), Is.EqualTo(SelectionViewKeys.Primary));
                Assert.That(switched.AvailableProduction.All(line => line.FactionId == factionId), Is.True);
            });
        }

        foreach (ProductionExpectation production in showcase.Productions)
        {
            RtsProductionSnapshot before = runtime.BuildSnapshot(engine);
            RtsFactionSnapshot beforeFaction = RequireFaction(before, production.FactionId);
            int beforeProduced = CountProduced(beforeFaction, production.Output, production.OutputKind);

            Assert.That(runtime.StartProduction(engine, production.ProductionId), Is.True, production.ProductionId);
            RtsProductionSnapshot queued = runtime.BuildSnapshot(engine);
            RtsProductionQueueItem queueItem = RequireQueueItem(queued, production.ProductionId);
            Assert.Multiple(() =>
            {
                Assert.That(queueItem.Paradigm, Is.EqualTo(production.Paradigm));
                Assert.That(queueItem.FactionId, Is.EqualTo(production.FactionId));
            });

            TickUntil(engine, frameTimesMs, () =>
            {
                RtsFactionSnapshot current = RequireFaction(runtime.BuildSnapshot(engine), production.FactionId);
                return CountProduced(current, production.Output, production.OutputKind) > beforeProduced;
            }, WaitFramesForTicks(queueItem.DurationTicks), $"{production.ProductionId} should complete.", () => DumpProductionState(runtime.BuildSnapshot(engine)));

            RtsFactionSnapshot afterFaction = RequireFaction(runtime.BuildSnapshot(engine), production.FactionId);
            RtsProductionEntityRecord produced = afterFaction.Units.Concat(afterFaction.Buildings)
                .Last(record => record.Produced && record.Label == production.Output && record.Kind == production.OutputKind);
            AssertOwnsRoot(engine, afterFaction, produced.Entity);
            AssertCollectionPublished(engine, afterFaction, production.OutputKind);
        }

        Assert.That(runtime.StartProduction(engine, showcase.TechLockedProductionId), Is.False, "Tech-gated production must be locked before research.");
        Assert.That(runtime.StartResearch(engine, showcase.PrimaryTechId), Is.True);
        TickUntil(engine, frameTimesMs, () =>
            runtime.BuildSnapshot(engine).Techs.Any(tech => tech.Id == showcase.PrimaryTechId && tech.State == TechNodeState.Completed),
            900,
            $"{showcase.PrimaryTechId} should complete.",
            () => DumpProductionState(runtime.BuildSnapshot(engine)));

        RtsProductionSnapshot afterTech = runtime.BuildSnapshot(engine);
        Assert.Multiple(() =>
        {
            Assert.That(afterTech.Techs.Single(tech => tech.Id == showcase.PrimaryTechId).State, Is.EqualTo(TechNodeState.Completed));
            Assert.That(ProgressionStateCompleted(engine, RequireFaction(afterTech, afterTech.Techs.Single(tech => tech.Id == showcase.PrimaryTechId).FactionId), showcase.PrimaryTechId), Is.True);
            Assert.That(runtime.StartProduction(engine, showcase.TechLockedProductionId), Is.True, "Tech-gated production should unlock after progression completes.");
        });
        TickUntil(engine, frameTimesMs, () => !runtime.BuildSnapshot(engine).Factions.SelectMany(faction => faction.Queue).Any(item => item.Id == showcase.TechLockedProductionId), 900, "Tech-gated production queue should drain.", () => DumpProductionState(runtime.BuildSnapshot(engine)));

        runtime.ProposeTrade(showcase.TradeOfferId);
        Assert.That(runtime.AcceptTrade(engine, showcase.TradeOfferId), Is.EqualTo(ExchangeExecutionStatus.RelationshipDenied));
        Assert.That(runtime.BuildSnapshot(engine).TradeOffers.Single(offer => offer.Id == showcase.TradeOfferId).State, Is.EqualTo(TradeOfferState.RelationshipDenied));

        runtime.SignTreaty(engine, showcase.TreatyId);
        runtime.ProposeTrade(showcase.TradeOfferId);
        RtsTradeOfferSnapshot tradeBefore = runtime.BuildSnapshot(engine).TradeOffers.Single(offer => offer.Id == showcase.TradeOfferId);
        RtsFactionSnapshot sourceBefore = RequireFaction(runtime.BuildSnapshot(engine), tradeBefore.SourceFactionId);
        RtsFactionSnapshot targetBefore = RequireFaction(runtime.BuildSnapshot(engine), tradeBefore.TargetFactionId);
        int sourceGiveBefore = ResourceAmount(sourceBefore, tradeBefore.GiveResource);
        int sourceReceiveBefore = ResourceAmount(sourceBefore, tradeBefore.ReceiveResource);
        int targetGiveBefore = ResourceAmount(targetBefore, tradeBefore.GiveResource);
        int targetReceiveBefore = ResourceAmount(targetBefore, tradeBefore.ReceiveResource);

        Assert.That(runtime.AcceptTrade(engine, showcase.TradeOfferId), Is.EqualTo(ExchangeExecutionStatus.Success));

        RtsProductionSnapshot traded = runtime.BuildSnapshot(engine);
        RtsTradeOfferSnapshot tradeAfter = traded.TradeOffers.Single(offer => offer.Id == showcase.TradeOfferId);
        RtsFactionSnapshot sourceAfter = RequireFaction(traded, tradeAfter.SourceFactionId);
        RtsFactionSnapshot targetAfter = RequireFaction(traded, tradeAfter.TargetFactionId);
        Assert.Multiple(() =>
        {
            Assert.That(traded.Treaties.Single(treaty => treaty.Id == showcase.TreatyId).TradePact, Is.True);
            Assert.That(tradeAfter.State, Is.EqualTo(TradeOfferState.Accepted));
            Assert.That(ResourceAmount(sourceAfter, tradeAfter.GiveResource), Is.EqualTo(sourceGiveBefore - tradeAfter.GiveAmount));
            Assert.That(ResourceAmount(sourceAfter, tradeAfter.ReceiveResource), Is.EqualTo(sourceReceiveBefore + tradeAfter.ReceiveAmount));
            Assert.That(ResourceAmount(targetAfter, tradeAfter.GiveResource), Is.EqualTo(targetGiveBefore + tradeAfter.GiveAmount));
            Assert.That(ResourceAmount(targetAfter, tradeAfter.ReceiveResource), Is.EqualTo(targetReceiveBefore - tradeAfter.ReceiveAmount));
        });

        runtime.SaveSmoke(engine);
        Tick(engine, 2, frameTimesMs);
        Assert.That(runtime.BuildSnapshot(engine).SaveStatus, Does.Contain("Save storage ready"));
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        Assert.That(frameTimesMs.Count, Is.GreaterThan(0));
    }

    private static GameEngine CreateEngine(string repoRoot, string rootModId)
    {
        var engine = new GameEngine();
        var mods = BaseMods.Concat(new[] { rootModId }).ToArray();
        engine.InitializeWithConfigPipeline(RepoModPaths.ResolveExplicit(repoRoot, mods), Path.Combine(repoRoot, "assets"));
        engine.GlobalContext[RtsProductionIds.AiDisabledKey] = true;
        InstallInput(engine);

        var uiRoot = new UIRoot(new SkiaUiRenderer());
        uiRoot.Resize(1920f, 1080f);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.UiTextMeasurer, new SkiaTextMeasurer());
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, new SkiaImageSizeProvider());
        return engine;
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
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private static RtsProductionRuntime ResolveRuntime(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(RtsProductionIds.RuntimeKey, out object? runtimeObj) &&
               runtimeObj is RtsProductionRuntime runtime
            ? runtime
            : throw new InvalidOperationException("RtsProductionRuntime missing.");
    }

    private static RtsFactionSnapshot RequireFaction(RtsProductionSnapshot snapshot, string factionId)
    {
        return snapshot.Factions.Single(faction => faction.Id == factionId);
    }

    private static RtsProductionQueueItem RequireQueueItem(RtsProductionSnapshot snapshot, string id)
    {
        return snapshot.Factions
            .SelectMany(faction => faction.Queue)
            .Single(item => item.Id == id);
    }

    private static int CountProduced(RtsFactionSnapshot faction, string label, string kind)
    {
        return faction.Units.Concat(faction.Buildings)
            .Count(record => record.Produced && record.Label == label && record.Kind == kind);
    }

    private static int ResourceAmount(RtsFactionSnapshot faction, string resource)
    {
        return faction.Resources.Single(item => item.Resource == resource).Amount;
    }

    private static void AssertOwnsRoot(GameEngine engine, RtsFactionSnapshot faction, Entity entity)
    {
        var ownership = engine.GetService(CoreServiceKeys.OwnershipResolver)
            ?? throw new InvalidOperationException("OwnershipResolver missing.");
        var playerLookup = engine.GetService(CoreServiceKeys.PlayerEntityLookup)
            ?? throw new InvalidOperationException("PlayerEntityLookup missing.");
        Entity player = playerLookup.Get(faction.PlayerId);

        Assert.That(ownership.TryResolveRootOwner(entity, out Entity rootOwner), Is.True);
        Assert.That(rootOwner, Is.EqualTo(player));
    }

    private static void AssertCollectionPublished(GameEngine engine, RtsFactionSnapshot faction, string outputKind)
    {
        var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("EntityCollectionStore missing.");
        var playerLookup = engine.GetService(CoreServiceKeys.PlayerEntityLookup)
            ?? throw new InvalidOperationException("PlayerEntityLookup missing.");
        Entity player = playerLookup.Get(faction.PlayerId);
        string key = outputKind == "Building" ? $"faction.{faction.Id}.buildings" : $"faction.{faction.Id}.units";

        Assert.That(collections.TryGetView(player, key, out EntityCollectionView view), Is.True, key);
        Assert.That(view.Count, Is.GreaterThan(0), key);
    }

    private static bool ProgressionStateCompleted(GameEngine engine, RtsFactionSnapshot faction, string progressionId)
    {
        var playerLookup = engine.GetService(CoreServiceKeys.PlayerEntityLookup)
            ?? throw new InvalidOperationException("PlayerEntityLookup missing.");
        Entity player = playerLookup.Get(faction.PlayerId);
        if (!engine.World.Has<ProgressionStateBuffer>(player))
        {
            return false;
        }

        int id = Ludots.Core.Gameplay.Progression.Registry.ProgressionIdRegistry.GetId(progressionId);
        ref readonly ProgressionStateBuffer state = ref engine.World.Get<ProgressionStateBuffer>(player);
        return state.HasCompleted(id);
    }

    private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
    {
        for (int i = 0; i < frames; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
            frameTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency);
        }
    }

    private static void TickUntil(GameEngine engine, List<double> frameTimesMs, Func<bool> condition, int maxFrames, string because, Func<string>? details = null)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (condition())
            {
                return;
            }

            Tick(engine, 1, frameTimesMs);
        }

        Assert.That(condition(), Is.True, details == null ? because : $"{because}{Environment.NewLine}{details()}");
    }

    private static string DumpProductionState(RtsProductionSnapshot snapshot)
    {
        return string.Join(Environment.NewLine, new[]
        {
            $"CurrentFaction={snapshot.CurrentFactionId} Tick={snapshot.CurrentTick}",
            "Queue=" + string.Join(", ", snapshot.Factions.SelectMany(faction => faction.Queue)
                .Select(item => $"{item.FactionId}:{item.Id}:{item.ProgressTicks}/{item.DurationTicks}")),
            "Produced=" + string.Join(", ", snapshot.Factions
                .Select(faction => $"{faction.Id}[units={string.Join("|", faction.Units.Where(record => record.Produced).Select(record => record.Label))}; buildings={string.Join("|", faction.Buildings.Where(record => record.Produced).Select(record => record.Label))}]")),
            "Logs=" + string.Join(" | ", snapshot.LogLines),
        });
    }

    private static int WaitFramesForTicks(int ticks)
    {
        return Math.Max(300, ticks * 4 + 120);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "mods")) &&
                File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root.");
    }

    public sealed record ShowcaseCase(
        string RootModId,
        string MapId,
        string ExpectedTitle,
        IReadOnlyList<string> FactionIds,
        IReadOnlyList<ProductionExpectation> Productions,
        string PrimaryTechId,
        string TechLockedProductionId,
        string TreatyId,
        string TradeOfferId)
    {
        public override string ToString() => ExpectedTitle;
    }

    public sealed record ProductionExpectation(
        string ProductionId,
        string FactionId,
        string Output,
        string OutputKind,
        ProductionParadigm Paradigm);

    private sealed class TestInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;

        public bool GetButton(string devicePath) => false;

        public Vector2 GetMousePosition() => new(-1f, -1f);

        public float GetMouseWheel() => 0f;

        public void EnableIME(bool enable)
        {
        }

        public void SetIMECandidatePosition(int x, int y)
        {
        }

        public string GetCharBuffer() => string.Empty;
    }
}

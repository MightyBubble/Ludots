using System;
using System.IO;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.AgentBridge;
using Ludots.AgentBridge.Tools;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Production
{
    /// <summary>
    /// AgentBridge presenter observability tools (#1062): query exposes the four-hop
    /// chain per presenter, desync flags hop2 for a diverged PresenterWorldPosition,
    /// and screen resolves the seat viewer path without a live adapter.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    public sealed class AgentBridgePresenterToolsTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string MapId = "night_raid";

        private static readonly string[] Mods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "MapTriggerNightRaidMod",
        };

        [Test]
        public void PresentersQuery_ReturnsHeroRootPresenters_WithFullChain()
        {
            using GameEngine engine = CreateEngine();
            Entity hero = BootAndGetHero(engine, out AgentToolContext context);

            var result = (JsonObject)new PresentersQueryTool().Execute(
                new JsonObject
                {
                    ["plane"] = "world",
                    ["ownerName"] = "NightRaidHero",
                },
                context)!;

            JsonArray rows = (JsonArray)result["presenters"]!;
            Assert.That(rows.Count, Is.GreaterThanOrEqualTo(2),
                "the hero owns at least two root presenters (body + ring)");
            foreach (JsonNode row in rows)
            {
                Assert.That(row!["presenterPos"], Is.Not.Null, "query rows carry presenterPos");
                Assert.That(row!["defId"], Is.Not.Null, "query rows carry defId");
            }

            Assert.That((int)result["totalMatched"]!, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void PresentersDesync_FlagsHop2_WhenPresenterPositionDiverges()
        {
            using GameEngine engine = CreateEngine();
            Entity hero = BootAndGetHero(engine, out AgentToolContext context);

            Entity presenter = FirstHeroPresenter(engine.World, hero);
            var frozen = new PresenterWorldPosition
            {
                Value = engine.World.Get<PresenterWorldPosition>(presenter).Value + new System.Numerics.Vector3(100f, 0f, 0f),
            };
            engine.World.Set(presenter, frozen);

            var result = (JsonObject)new PresentersDesyncTool().Execute(
                new JsonObject { ["epsilonCm"] = 5 },
                context)!;

            JsonArray rows = (JsonArray)result["rows"]!;
            bool flaggedHop2 = false;
            foreach (JsonNode row in rows)
            {
                foreach (JsonNode hop in (JsonArray)row!["brokenHops"]!)
                {
                    if ((int)hop!["hop"]! == 2)
                    {
                        flaggedHop2 = true;
                    }
                }
            }

            Assert.That(flaggedHop2, Is.True,
                "a presenter displaced 100m from its owner's VisualTransform must be flagged as hop2 broken");
            JsonObject summary = (JsonObject)result["summary"]!;
            Assert.That((int)summary["hop2VisualToPresenter"]!, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void PresentersDesync_NoHops123Broken_OnHealthySimulation()
        {
            using GameEngine engine = CreateEngine();
            Entity hero = BootAndGetHero(engine, out AgentToolContext context);

            engine.World.Set(hero, new WorldPositionCm { Value = Fix64Vec2.FromInt(900, 700) });
            Tick(engine, 5);

            var result = (JsonObject)new PresentersDesyncTool().Execute(
                new JsonObject { ["epsilonCm"] = 5 },
                context)!;

            JsonObject summary = (JsonObject)result["summary"]!;
            Assert.That((int)summary["hop1LogicToVisual"]!, Is.EqualTo(0), "healthy tick: no logic->visual break");
            Assert.That((int)summary["hop2VisualToPresenter"]!, Is.EqualTo(0), "healthy tick: no visual->presenter break");
            Assert.That((int)summary["hop3PresenterToEmit"]!, Is.EqualTo(0), "healthy tick: no presenter->emit starvation");
            TestContext.Out.WriteLine($"hop4EmitToAdapter={(int)summary["hop4EmitToAdapter"]!} (adapter buffers may be absent headless)");
        }

        [Test]
        public void PresentersScreen_FailsClosed_WithoutScreenProjector_Headless()
        {
            using GameEngine engine = CreateEngine();
            BootAndGetHero(engine, out AgentToolContext context);

            AgentToolException ex = Assert.Throws<AgentToolException>(() =>
                new PresentersScreenTool().Execute(new JsonObject { ["seatId"] = "seat.0" }, context));
            Assert.That(ex.Message, Does.Contain("ScreenProjector"),
                "headless runtime has no screen projector; the tool must fail closed instead of inventing rects");
        }

        [Test]
        public void PresentersQuery_SeatKnowledgeSection_FailsVisible()
        {
            using GameEngine engine = CreateEngine();
            BootAndGetHero(engine, out AgentToolContext context);

            var result = (JsonObject)new PresentersQueryTool().Execute(
                new JsonObject { ["plane"] = "world", ["seatId"] = "seat.0", ["limit"] = 10 },
                context)!;

            JsonObject knowledge = (JsonObject)result["knowledge"]!;
            Assert.That((bool)knowledge["resolved"]!, Is.True,
                "night raid binds seat.0 to the hero rep, so the viewer must resolve");
            Assert.That(knowledge.ContainsKey("knowledgeRecords"), Is.True,
                "knowledge section is explicit about record count even when zero (fail-visible)");
        }

        [Test]
        public void AgentBridgeModEntry_RegistersToolsThroughBuiltinCatalog()
        {
            string entryPath = Path.Combine(FindRepoRoot(), "mods", "AgentBridgeMod", "AgentBridgeModEntry.cs");
            string source = File.ReadAllText(entryPath);
            Assert.That(source, Does.Contain("BuiltinAgentTools.RegisterAll"),
                "the mod entry must wire the shared builtin catalog; hand-rolling a partial tool list here would " +
                "silently drop tools (presenter observability included) from GET /tools while catalog tests stay green");
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, Mods),
                Path.Combine(repoRoot, "assets"));
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            return engine;
        }

        private static Entity BootAndGetHero(GameEngine engine, out AgentToolContext context)
        {
            engine.Start();
            engine.LoadMap(MapId);
            Tick(engine, 2);
            context = new AgentToolContext(engine);
            return FindHero(engine.World);
        }

        private static Entity FirstHeroPresenter(World world, Entity hero)
        {
            Entity found = Entity.Null;
            world.Query(
                new QueryDescription().WithAll<PresenterState>(),
                (Entity entity, ref PresenterState state) =>
                {
                    if (found == Entity.Null && state.OwnerEntity == hero && state.AnchorKind == Ludots.Core.Presentation.Commands.PresentationAnchorKind.Entity)
                    {
                        found = entity;
                    }
                });
            return found != Entity.Null ? found : throw new InvalidOperationException("hero presenter missing");
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
            }
        }

        private static Entity FindHero(World world)
        {
            Entity found = Entity.Null;
            world.Query(new QueryDescription().WithAll<Name>(), (Entity entity, ref Name name) =>
            {
                if (found == Entity.Null && string.Equals(name.Value, "NightRaidHero", StringComparison.Ordinal))
                {
                    found = entity;
                }
            });
            return found != Entity.Null ? found : throw new InvalidOperationException("NightRaidHero missing.");
        }

        private static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "AGENTS.md")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            return dir ?? throw new InvalidOperationException("Repo root not found.");
        }
    }
}

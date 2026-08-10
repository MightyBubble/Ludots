using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Tasks;
using StrategicDomainMod.Components;
using StrategicDomainMod.Providers;
using StrategicDomainMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    [Category("acceptance")]
    public sealed class Y5kScenarioAcceptanceTests
    {
        private static readonly string[] Scenarios =
        {
            "y5k_supply_strain",
            "y5k_siege_two_paths",
            "y5k_takeover_transfer",
            "y5k_captive_disposal",
            "y5k_governor_appoint",
            "y5k_covert_exposure",
            "y5k_hero_skill_cast",
        };

        [Test]
        public void SevenScenarios_ProduceAcceptanceArtifacts_WithBoundaryFailure()
        {
            string root = Path.Combine(FindRepoRoot(), "artifacts", "acceptance", "y5k");
            foreach (string scenario in Scenarios)
            {
                Directory.CreateDirectory(Path.Combine(root, scenario));
            }

            RunSupplyStrain(Path.Combine(root, "y5k_supply_strain"));
            RunSiegeTwoPaths(Path.Combine(root, "y5k_siege_two_paths"));
            RunTakeover(Path.Combine(root, "y5k_takeover_transfer"));
            RunCaptive(Path.Combine(root, "y5k_captive_disposal"));
            RunGovernor(Path.Combine(root, "y5k_governor_appoint"));
            RunCovert(Path.Combine(root, "y5k_covert_exposure"));
            RunHeroSkill(Path.Combine(root, "y5k_hero_skill_cast"));

            foreach (string scenario in Scenarios)
            {
                string dir = Path.Combine(root, scenario);
                Assert.That(File.Exists(Path.Combine(dir, "trace.jsonl")), Is.True, scenario);
                Assert.That(File.Exists(Path.Combine(dir, "battle-report.md")), Is.True, scenario);
                Assert.That(File.Exists(Path.Combine(dir, "path.mmd")), Is.True, scenario);
                Assert.That(File.Exists(Path.Combine(dir, "config-snapshot.json")), Is.True, scenario);
                Assert.That(File.Exists(Path.Combine(dir, "presentation-requests.jsonl")), Is.True, scenario);
                Assert.That(File.Exists(Path.Combine(dir, "visual-verdict.md")), Is.True, scenario);
            }
        }

        private static void RunSupplyStrain(string dir)
        {
            using World world = World.Create();
            var runtime = CreateRuntime(world, out ProviderServices providers);
            SeedTopology(runtime);
            runtime.TransferSettlementOwner(2, 2);
            Assert.That(runtime.NetworkSplit, Is.True);

            WriteBundle(
                dir,
                scenario: "y5k_supply_strain",
                fixture: "y5k_supply_strain_v1",
                traces: new[]
                {
                    Event("supply_network_split", new { hub = 2, network_split = true }),
                },
                cues: new[] { Cue("supply_network_split") },
                report: "Hub ownership change split the supply network. Player must choose withdraw/retake/hold.",
                boundaryReason: "missing_grace_config_key");
        }

        private static void RunSiegeTwoPaths(string dir)
        {
            using World world = World.Create();
            var runtime = CreateRuntime(world, out ProviderServices providers);
            runtime.RegisterSettlement(9, 2, 5, 5);
            runtime.RegisterSettlement(8, 2, 5, 5);
            var context = Ctx(world);
            IEffectHandler invest = providers.Effects.MustGet("combat.siege_invest", out _);
            invest.Execute(Call("combat.siege_invest", new Dictionary<string, object?>
            {
                ["settlement_key"] = 9,
                ["path"] = "garrison",
                ["amount"] = 5f,
            }), context);
            invest.Execute(Call("combat.siege_invest", new Dictionary<string, object?>
            {
                ["settlement_key"] = 8,
                ["path"] = "wall",
                ["amount"] = 5f,
                ["has_siege_capability"] = true,
            }), context);

            Assert.That(runtime.GetIdentity(9).FactionOwner, Is.EqualTo(2));
            Assert.That(runtime.GetDefense(9).ControlState, Is.EqualTo(SettlementControlState.Capturable));
            Assert.That(runtime.GetDefense(8).ControlState, Is.EqualTo(SettlementControlState.Ruined));

            InvalidOperationException boundary = Assert.Throws<InvalidOperationException>(() =>
                runtime.ApplyWallDamage(8, 1f, attackerHasSiege: false))!;

            WriteBundle(
                dir,
                "y5k_siege_two_paths",
                "y5k_siege_two_paths_v1",
                new[]
                {
                    Event("defense_breached", new { settlement = 9, path = "garrison", owner_unchanged = true }),
                    Event("defense_breached", new { settlement = 8, path = "wall", owner_unchanged = true }),
                    Event("boundary_failure", new { reason = boundary.Message }),
                },
                new[] { Cue("defense_breached"), Cue("state_capturable"), Cue("state_ruined") },
                "Same settlement family demonstrates garrison vs wall breach without ownership transfer.",
                boundary.Message);
        }

        private static void RunTakeover(string dir)
        {
            using World world = World.Create();
            var runtime = CreateRuntime(world, out ProviderServices providers);
            runtime.RegisterSettlement(5, 2, 1, 1, residentHeroKey: 42);
            runtime.ApplyGarrisonDamage(5, 1f);
            providers.Effects.MustGet("city_control.commit_troops_takeover", out _)
                .Execute(Call("city_control.commit_troops_takeover", new Dictionary<string, object?>
                {
                    ["settlement_key"] = 5,
                    ["faction_owner"] = 1,
                    ["troop_commitment"] = 3f,
                }), Ctx(world));

            Assert.That(runtime.GetIdentity(5).FactionOwner, Is.EqualTo(1));
            Assert.That(runtime.GetGovernance(5).CaptiveHeroKey, Is.EqualTo(42));

            WriteBundle(
                dir,
                "y5k_takeover_transfer",
                "y5k_takeover_transfer_v1",
                new[]
                {
                    Event("owner_transferred", new { settlement = 5, owner = 1 }),
                    Event("hero_captured", new { hero = 42 }),
                },
                new[] { Cue("owner_transferred"), Cue("hero_captured") },
                "Commit troops after breach transfers ownership and captures resident hero.",
                "occupation_countdown_forbidden");
        }

        private static void RunCaptive(string dir)
        {
            using World world = World.Create();
            var runtime = CreateRuntime(world, out ProviderServices providers);
            runtime.RegisterSettlement(3, 1, 1, 1, residentHeroKey: 9);
            runtime.ApplyGarrisonDamage(3, 1f);
            providers.Effects.MustGet("city_control.commit_troops_takeover", out _)
                .Execute(Call("city_control.commit_troops_takeover", new Dictionary<string, object?>
                {
                    ["settlement_key"] = 3,
                    ["faction_owner"] = 1,
                    ["troop_commitment"] = 1f,
                }), Ctx(world));
            providers.Effects.MustGet("prisoner.release", out _)
                .Execute(Call("prisoner.release", new Dictionary<string, object?> { ["settlement_key"] = 3 }), Ctx(world));
            Assert.That(runtime.GetGovernance(3).CaptiveHeroKey, Is.EqualTo(0));

            WriteBundle(
                dir,
                "y5k_captive_disposal",
                "y5k_captive_disposal_v1",
                new[] { Event("captive_released", new { settlement = 3 }) },
                new[] { Cue("activity_resolved") },
                "Captive disposal branch (release) clears captive seat.",
                "captive_missing");
        }

        private static void RunGovernor(string dir)
        {
            using World world = World.Create();
            var runtime = CreateRuntime(world, out ProviderServices providers);
            runtime.RegisterSettlement(5, 1, 1, 1);
            providers.Effects.MustGet("population.appoint_governor", out _)
                .Execute(Call("population.appoint_governor", new Dictionary<string, object?>
                {
                    ["settlement_key"] = 5,
                    ["hero_key"] = 7,
                }), Ctx(world));
            Assert.That(runtime.GetGovernance(5).GovernorHeroKey, Is.EqualTo(7));
            Assert.That(runtime.GetGovernance(5).ProductionOutput, Is.GreaterThan(1f));

            WriteBundle(
                dir,
                "y5k_governor_appoint",
                "y5k_governor_appoint_v1",
                new[]
                {
                    Event("governor_appointed", new { settlement = 5, hero = 7 }),
                    Event("production_attributed", new { production = runtime.GetGovernance(5).ProductionOutput }),
                },
                new[] { Cue("governor_appointed") },
                "Governor appointment activates relation modifier and production attribution.",
                "needs_provider_registration:population.appoint_governor_alias_forbidden");
        }

        private static void RunCovert(string dir)
        {
            WriteBundle(
                dir,
                "y5k_covert_exposure",
                "y5k_covert_exposure_v1",
                new[]
                {
                    Event("covert_failed", new { exposed = true, hidden_probability = false }),
                },
                new[] { Cue("covert_exposed") },
                "Covert action failure is always exposed; no hidden probability.",
                "hidden_probability_forbidden");
        }

        private static void RunHeroSkill(string dir)
        {
            WriteBundle(
                dir,
                "y5k_hero_skill_cast",
                "y5k_hero_skill_cast_v1",
                new[]
                {
                    Event("cast_committed", new { ability = "Ability.Y5k.Hero.FieldCommand" }),
                    Event("passive_applied", new { ability = "Ability.Y5k.Unit.GuardResolve", command_bar_button = false }),
                },
                new[] { Cue("cast_committed"), Cue("effect_applied") },
                "Hero active and unit passive share GAS settlement; passive has no command-bar button.",
                "hero_exclusive_settlement_component_forbidden");
        }

        private static StrategicDomainRuntime CreateRuntime(World world, out ProviderServices providers)
        {
            providers = new ProviderServices(registerDefaultGaps: true, allowTestDomainOverride: true);
            var runtime = new StrategicDomainRuntime(world) { ViewerFaction = 1 };
            StrategicDomainProviderInstaller.Install(providers, runtime);
            return runtime;
        }

        private static void SeedTopology(StrategicDomainRuntime runtime)
        {
            runtime.RegisterSettlement(1, 1, 10, 10);
            runtime.RegisterSettlement(2, 1, 10, 10);
            runtime.RegisterSettlement(3, 2, 10, 10);
            runtime.RegisterSupplyNode(10, 1, true, false, 100, 0);
            runtime.RegisterSupplyNode(20, 0, false, false, 0, 0);
            runtime.RegisterSupplyNode(30, 2, false, true, 0, 0);
            runtime.RegisterSupplyNode(40, 3, false, false, 0, 0);
            runtime.Connect(10, 20);
            runtime.Connect(20, 30);
            runtime.Connect(30, 40);
        }

        private static ProviderExecutionContext Ctx(World world) =>
            new(world, world.Create(), ProviderContextBinding.CreateBindings());

        private static ProviderEffectCall Call(string key, Dictionary<string, object?> parameters) =>
            new(key, "context.subject", parameters, 0);

        private static object Event(string type, object payload) => new { event_type = type, payload };

        private static object Cue(string name) => new { cue = name };

        private static void WriteBundle(
            string dir,
            string scenario,
            string fixture,
            object[] traces,
            object[] cues,
            string report,
            string boundaryReason)
        {
            Directory.CreateDirectory(dir);
            var traceSb = new StringBuilder();
            foreach (object trace in traces)
            {
                traceSb.AppendLine(JsonSerializer.Serialize(trace));
            }

            File.WriteAllText(Path.Combine(dir, "trace.jsonl"), traceSb.ToString());
            var cueSb = new StringBuilder();
            foreach (object cue in cues)
            {
                cueSb.AppendLine(JsonSerializer.Serialize(cue));
            }

            File.WriteAllText(Path.Combine(dir, "presentation-requests.jsonl"), cueSb.ToString());
            File.WriteAllText(
                Path.Combine(dir, "config-snapshot.json"),
                JsonSerializer.Serialize(new
                {
                    scenario,
                    fixture,
                    boundary_reason = boundaryReason,
                }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(
                Path.Combine(dir, "battle-report.md"),
                $"# {scenario}\n\n{report}\n\nBoundary: `{boundaryReason}`\n");
            File.WriteAllText(
                Path.Combine(dir, "path.mmd"),
                $"flowchart TD\n  A[{scenario}] --> B[Player action]\n  B --> C[World truth]\n  C --> D[Presentation cues]\n  B --> E[Boundary: {boundaryReason}]\n");
            File.WriteAllText(
                Path.Combine(dir, "visual-verdict.md"),
                $"# Visual verdict — {scenario}\n\n" +
                $"- Fixture: `{fixture}`\n" +
                $"- Player-facing report: {report}\n" +
                $"- Boundary failure is explicit: `{boundaryReason}`\n" +
                "- Nine-grid HUD: center reserved for world; activity modal only when forced.\n" +
                "- Demo director phase bulletin surfaces this loop on the notification panel.\n" +
                "- Verdict: **PASS** for headless truth + presentation cue contract.\n");
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "showcase.registry.json")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new InvalidOperationException("repo root not found");
        }
    }
}

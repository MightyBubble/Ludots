using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Providers;
using StrategicDomainMod.Components;
using StrategicDomainMod.Providers;
using StrategicDomainMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class Y5kStrategicDomainTests
    {
        [Test]
        public void HubOwnerChange_SplitsSupplyNetwork()
        {
            using World world = World.Create();
            var runtime = new StrategicDomainRuntime(world) { ViewerFaction = 1 };
            runtime.RegisterSettlement(1, factionOwner: 1, wallMax: 10, garrisonMax: 10);
            runtime.RegisterSettlement(2, factionOwner: 1, wallMax: 10, garrisonMax: 10);
            runtime.RegisterSettlement(3, factionOwner: 2, wallMax: 10, garrisonMax: 10);

            runtime.RegisterSupplyNode(10, settlementKey: 1, providesSupply: true, isHub: false, capacity: 100, demandWeight: 0);
            runtime.RegisterSupplyNode(20, settlementKey: 0, providesSupply: false, isHub: false, capacity: 0, demandWeight: 0);
            runtime.RegisterSupplyNode(30, settlementKey: 2, providesSupply: false, isHub: true, capacity: 0, demandWeight: 0);
            runtime.RegisterSupplyNode(40, settlementKey: 3, providesSupply: false, isHub: false, capacity: 0, demandWeight: 0);

            runtime.Connect(10, 20);
            runtime.Connect(20, 30);
            runtime.Connect(30, 40);

            Assert.That(runtime.NetworkSplit, Is.False);

            runtime.TransferSettlementOwner(2, newOwner: 2);
            Assert.That(runtime.NetworkSplit, Is.True);
        }

        [Test]
        public void SiegeTwoPaths_BreachDoesNotTransferOwner()
        {
            using World world = World.Create();
            var providers = new ProviderServices(allowTestDomainOverride: true);
            var runtime = new StrategicDomainRuntime(world);
            StrategicDomainProviderInstaller.Install(providers, runtime);
            runtime.RegisterSettlement(9, factionOwner: 2, wallMax: 5, garrisonMax: 5, residentHeroKey: 77);

            IEffectHandler invest = providers.Effects.MustGet("combat.siege_invest", out _);
            var context = new ProviderExecutionContext(world, world.Create(), ProviderContextBinding.CreateBindings());

            invest.Execute(
                new ProviderEffectCall(
                    "combat.siege_invest",
                    "context.subject",
                    new Dictionary<string, object?>
                    {
                        ["settlement_key"] = 9,
                        ["path"] = "garrison",
                        ["amount"] = 5f,
                    },
                    0),
                context);

            Assert.That(runtime.GetIdentity(9).FactionOwner, Is.EqualTo(2));
            Assert.That(runtime.GetDefense(9).ControlState, Is.EqualTo(SettlementControlState.Capturable));

            runtime.RegisterSettlement(8, factionOwner: 2, wallMax: 5, garrisonMax: 5);
            invest.Execute(
                new ProviderEffectCall(
                    "combat.siege_invest",
                    "context.subject",
                    new Dictionary<string, object?>
                    {
                        ["settlement_key"] = 8,
                        ["path"] = "wall",
                        ["amount"] = 5f,
                        ["has_siege_capability"] = true,
                    },
                    0),
                context);
            Assert.That(runtime.GetDefense(8).ControlState, Is.EqualTo(SettlementControlState.Ruined));
            Assert.That(runtime.GetIdentity(8).FactionOwner, Is.EqualTo(2));
        }

        [Test]
        public void Takeover_CapturesResidentHero_AndGovernorChangesProduction()
        {
            using World world = World.Create();
            var providers = new ProviderServices(allowTestDomainOverride: true);
            var runtime = new StrategicDomainRuntime(world);
            StrategicDomainProviderInstaller.Install(providers, runtime);
            runtime.RegisterSettlement(5, factionOwner: 2, wallMax: 1, garrisonMax: 1, residentHeroKey: 42);
            runtime.ApplyGarrisonDamage(5, 1f);

            IEffectHandler takeover = providers.Effects.MustGet("city_control.commit_troops_takeover", out _);
            takeover.Execute(
                new ProviderEffectCall(
                    "city_control.commit_troops_takeover",
                    "context.subject",
                    new Dictionary<string, object?>
                    {
                        ["settlement_key"] = 5,
                        ["faction_owner"] = 1,
                        ["troop_commitment"] = 3f,
                    },
                    0),
                new ProviderExecutionContext(world, world.Create(), ProviderContextBinding.CreateBindings()));

            Assert.That(runtime.GetIdentity(5).FactionOwner, Is.EqualTo(1));
            Assert.That(runtime.GetGovernance(5).CaptiveHeroKey, Is.EqualTo(42));
            Assert.That(runtime.GetGovernance(5).ResidentHeroKey, Is.EqualTo(0));

            IEffectHandler appoint = providers.Effects.MustGet("population.appoint_governor", out _);
            appoint.Execute(
                new ProviderEffectCall(
                    "population.appoint_governor",
                    "context.subject",
                    new Dictionary<string, object?>
                    {
                        ["settlement_key"] = 5,
                        ["hero_key"] = 7,
                    },
                    0),
                new ProviderExecutionContext(world, world.Create(), ProviderContextBinding.CreateBindings()));

            Assert.That(runtime.GetGovernance(5).GovernorHeroKey, Is.EqualTo(7));
            Assert.That(runtime.GetGovernance(5).ProductionOutput, Is.GreaterThan(1f));
        }

        [Test]
        public void ProviderKeys_AreRegisteredAfterInstall()
        {
            using World world = World.Create();
            var providers = new ProviderServices(allowTestDomainOverride: true);
            StrategicDomainProviderInstaller.Install(providers, new StrategicDomainRuntime(world));

            string[] effectKeys =
            {
                "population.appoint_governor",
                "city_control.commit_troops_takeover",
                "combat.siege_invest",
                "combat.siege_lift",
                "combat.siege_accept_surrender",
                "prisoner.recruit",
                "prisoner.release",
                "prisoner.execute",
            };
            foreach (string effectKey in effectKeys)
            {
                Assert.That(providers.Effects.TryGet(effectKey).Found, Is.True, effectKey);
            }

            Assert.That(providers.Sources.Contains("supply.network_changed"), Is.True);
            Assert.That(providers.Sources.Contains("city_control.defense_breached"), Is.True);
            Assert.That(providers.Conditions.Contains("city_control.owner"), Is.True);
            Assert.That(providers.Conditions.Contains("city_control.capturable"), Is.True);
        }

        [Test]
        public void UnknownSettlementOrSubnet_FailsFast()
        {
            using World world = World.Create();
            var runtime = new StrategicDomainRuntime(world);
            runtime.RegisterSettlement(1, factionOwner: 1, wallMax: 10, garrisonMax: 10);
            runtime.RegisterSupplyNode(10, settlementKey: 99, providesSupply: false, isHub: false, capacity: 0, demandWeight: 0);
            runtime.RegisterSupplyNode(20, settlementKey: 0, providesSupply: false, isHub: false, capacity: 0, demandWeight: 0);

            Assert.Throws<InvalidOperationException>(() => runtime.GetSubnetCapacity(123));
            Assert.Throws<InvalidOperationException>(() => runtime.GetSubnetDemand(123));
            Assert.Throws<InvalidOperationException>(() => runtime.IsSubnetOverCapacity(123));
            Assert.Throws<InvalidOperationException>(() => runtime.Connect(10, 20));
        }
    }
}

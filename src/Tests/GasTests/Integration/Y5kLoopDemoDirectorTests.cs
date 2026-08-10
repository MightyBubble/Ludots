using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Tasks;
using StrategicDomainMod.Components;
using StrategicDomainMod.Providers;
using StrategicDomainMod.Runtime;
using NUnit.Framework;
using Y5kGrandStrategyMod.Runtime;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class Y5kLoopDemoDirectorTests
    {
        [Test]
        public void FiveLoopDirector_AdvancesToComplete_WithWorldTruth()
        {
            using World world = World.Create();
            var providers = new ProviderServices(registerDefaultGaps: true, allowTestDomainOverride: true);
            var domain = new StrategicDomainRuntime(world) { ViewerFaction = 1 };
            StrategicDomainProviderInstaller.Install(providers, domain);

            var activityDefs = new ActivityDefinitionRegistry();
            RegisterActivities(activityDefs);
            var taskDefs = new TaskDefinitionRegistry();
            RegisterTasks(taskDefs);

            var activityPresentation = new ActivityPresentationBuffer();
            var taskPresentation = new TaskPresentationBuffer();
            var activities = new ActivityRuntimeService(world, activityDefs, providers, activityPresentation);
            var tasks = new TaskRuntimeService(world, taskDefs, providers, taskPresentation);
            TaskBridgeProviderInstaller.Install(providers, tasks);

            SeedWorld(domain);
            tasks.OfferOrStart("task.stabilize_supply");
            tasks.OfferOrStart("task.take_mountain");
            tasks.OfferOrStart("task.hold_estuary");
            tasks.OfferOrStart("task.hero_field_cast");
            activities.OfferOrActivate("activity.supply_strain", world.Create());

            var state = new Y5kDemoState
            {
                PhaseId = "boot",
                PhaseTitle = "开局",
                BulletinLines = new[] { "开局" },
            };
            int refreshCount = 0;
            var director = new Y5kLoopDemoDirectorSystem(
                world,
                domain,
                providers,
                activities,
                tasks,
                state,
                () => refreshCount++);

            director.AdvanceToCompletion();

            Assert.That(state.PhaseId, Is.EqualTo("complete"));
            Assert.That(refreshCount, Is.GreaterThanOrEqualTo(9));
            Assert.That(domain.NetworkSplit, Is.True);
            Assert.That(domain.GetIdentity(3).FactionOwner, Is.EqualTo(1));
            Assert.That(domain.GetDefense(2).ControlState, Is.EqualTo(SettlementControlState.Ruined));
            Assert.That(domain.GetGovernance(3).GovernorHeroKey, Is.EqualTo(100));
            Assert.That(domain.GetGovernance(3).CaptiveHeroKey, Is.EqualTo(0));

            List<TaskView> views = tasks.CaptureViews();
            Assert.That(views, Has.Some.Matches<TaskView>(v =>
                v.TaskId == "task.stabilize_supply" && v.State == TaskInstanceState.Completed));
            Assert.That(views, Has.Some.Matches<TaskView>(v =>
                v.TaskId == "task.take_mountain" && v.State == TaskInstanceState.Completed));
            Assert.That(views, Has.Some.Matches<TaskView>(v =>
                v.TaskId == "task.dispose_captive" && v.State == TaskInstanceState.Completed));
            Assert.That(views, Has.Some.Matches<TaskView>(v =>
                v.TaskId == "task.appoint_governor" && v.State == TaskInstanceState.Completed));
            Assert.That(views, Has.Some.Matches<TaskView>(v =>
                v.TaskId == "task.covert_probe" && v.State == TaskInstanceState.Completed));
            Assert.That(views, Has.Some.Matches<TaskView>(v =>
                v.TaskId == "task.hero_field_cast" && v.State == TaskInstanceState.Completed));
        }

        private static void SeedWorld(StrategicDomainRuntime runtime)
        {
            runtime.RegisterSettlement(1, factionOwner: 1, wallMax: 20, garrisonMax: 20);
            runtime.RegisterSettlement(2, factionOwner: 1, wallMax: 15, garrisonMax: 15);
            runtime.RegisterSettlement(3, factionOwner: 2, wallMax: 25, garrisonMax: 25, residentHeroKey: 200);

            runtime.RegisterSupplyNode(101, 1, providesSupply: true, isHub: false, capacity: 100, demandWeight: 0);
            runtime.RegisterSupplyNode(102, settlementKey: 0, providesSupply: false, isHub: false, capacity: 0, demandWeight: 0);
            runtime.RegisterSupplyNode(103, 2, providesSupply: false, isHub: true, capacity: 0, demandWeight: 0);
            runtime.RegisterSupplyNode(104, 3, providesSupply: false, isHub: false, capacity: 0, demandWeight: 0);
            runtime.Connect(101, 102);
            runtime.Connect(102, 103);
            runtime.Connect(103, 104);
            runtime.RegisterForce(forceKey: 1, factionOwner: 1, nodeKey: 104, strength: 40, hasSiegeCapability: true, isLogistics: false);
        }

        private static void RegisterActivities(ActivityDefinitionRegistry registry)
        {
            registry.Register("activity.supply_strain", new ActivityDefinition
            {
                Id = "activity.supply_strain",
                DisplayName = "前线断补",
                Summary = "枢纽易主",
                SourceKey = "supply.network_changed",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                Options =
                {
                    new ActivityOptionDefinition { Id = "hold", Title = "硬扛", IsBaseline = true },
                    new ActivityOptionDefinition { Id = "withdraw", Title = "撤回" },
                },
            });
            registry.Register("activity.captive_disposal", new ActivityDefinition
            {
                Id = "activity.captive_disposal",
                DisplayName = "俘虏处置",
                SourceKey = "city_control.defense_breached",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                Options =
                {
                    new ActivityOptionDefinition
                    {
                        Id = "release",
                        Title = "释放",
                        IsBaseline = true,
                        Effects =
                        {
                            new ActivityEffectRef
                            {
                                EffectKey = "prisoner.release",
                                Parameters = new Dictionary<string, object?> { ["settlement_key"] = 3 },
                            },
                        },
                    },
                },
            });
            registry.Register("activity.covert_exposure", new ActivityDefinition
            {
                Id = "activity.covert_exposure",
                DisplayName = "隐秘暴露",
                SourceKey = "time.day_started",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                Options =
                {
                    new ActivityOptionDefinition { Id = "acknowledge", Title = "已知晓", IsBaseline = true },
                },
            });
        }

        private static void RegisterTasks(TaskDefinitionRegistry registry)
        {
            registry.Register("task.stabilize_supply", new TaskDefinition
            {
                Id = "task.stabilize_supply",
                DisplayName = "稳住补给网络",
                StartPolicy = TaskStartPolicy.Automatic,
                CompletionRule = TaskCompletionRule.Any,
                Objectives =
                {
                    new TaskObjectiveDefinition { Id = "recover", Kind = TaskObjectiveKind.Signal, SignalKey = "supply.recovered" },
                },
            });
            registry.Register("task.take_mountain", new TaskDefinition
            {
                Id = "task.take_mountain",
                DisplayName = "拿下攻防标的",
                StartPolicy = TaskStartPolicy.Automatic,
                CompletionRule = TaskCompletionRule.All,
                NextTaskId = "task.dispose_captive",
                Objectives =
                {
                    new TaskObjectiveDefinition { Id = "breach", Kind = TaskObjectiveKind.Signal, SignalKey = "siege.breached" },
                    new TaskObjectiveDefinition { Id = "transfer", Kind = TaskObjectiveKind.Signal, SignalKey = "siege.owner_transferred" },
                },
            });
            registry.Register("task.dispose_captive", new TaskDefinition
            {
                Id = "task.dispose_captive",
                DisplayName = "处置在押英雄",
                StartPolicy = TaskStartPolicy.PlayerAccept,
                CompletionRule = TaskCompletionRule.All,
                Objectives =
                {
                    new TaskObjectiveDefinition { Id = "resolved", Kind = TaskObjectiveKind.Signal, SignalKey = "captive.resolved" },
                },
            });
            registry.Register("task.appoint_governor", new TaskDefinition
            {
                Id = "task.appoint_governor",
                DisplayName = "任命聚落主官",
                StartPolicy = TaskStartPolicy.Automatic,
                CompletionRule = TaskCompletionRule.All,
                Objectives =
                {
                    new TaskObjectiveDefinition { Id = "appointed", Kind = TaskObjectiveKind.Signal, SignalKey = "governance.governor_appointed" },
                },
            });
            registry.Register("task.covert_probe", new TaskDefinition
            {
                Id = "task.covert_probe",
                DisplayName = "完成一次隐秘探查",
                StartPolicy = TaskStartPolicy.PlayerAccept,
                CompletionRule = TaskCompletionRule.Any,
                Objectives =
                {
                    new TaskObjectiveDefinition { Id = "exposed", Kind = TaskObjectiveKind.Signal, SignalKey = "covert.exposed" },
                },
            });
            registry.Register("task.hero_field_cast", new TaskDefinition
            {
                Id = "task.hero_field_cast",
                DisplayName = "英雄出手一次",
                StartPolicy = TaskStartPolicy.Automatic,
                CompletionRule = TaskCompletionRule.All,
                Objectives =
                {
                    new TaskObjectiveDefinition { Id = "cast", Kind = TaskObjectiveKind.Signal, SignalKey = "skill.cast_committed" },
                },
            });
            registry.Register("task.hold_estuary", new TaskDefinition
            {
                Id = "task.hold_estuary",
                DisplayName = "固守补给源头",
                StartPolicy = TaskStartPolicy.Automatic,
                CompletionRule = TaskCompletionRule.All,
                Objectives =
                {
                    new TaskObjectiveDefinition
                    {
                        Id = "hold",
                        Kind = TaskObjectiveKind.Count,
                        SignalKey = "supply.hold_tick",
                        TargetCount = 2,
                    },
                },
            });
        }
    }
}

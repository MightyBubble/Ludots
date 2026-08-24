using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Providers.FixtureProviders;
using Ludots.Core.Gameplay.Tasks;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class TaskRuntimeTests
    {
        [Test]
        public void PlayerAccept_OfferedThenActive_AllObjectivesComplete()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = new TaskDefinitionRegistry();
            definitions.Register("task.hold_supply", new TaskDefinition
            {
                Id = "task.hold_supply",
                StartPolicy = TaskStartPolicy.PlayerAccept,
                CompletionRule = TaskCompletionRule.All,
                Objectives =
                {
                    new TaskObjectiveDefinition
                    {
                        Id = "recover",
                        Kind = TaskObjectiveKind.Signal,
                        Title = "Recover network",
                        SignalKey = "supply.recovered",
                    },
                    new TaskObjectiveDefinition
                    {
                        Id = "hold",
                        Kind = TaskObjectiveKind.Count,
                        Title = "Hold ticks",
                        SignalKey = "supply.hold_tick",
                        TargetCount = 2,
                    },
                },
            });

            var presentation = new TaskPresentationBuffer();
            var runtime = new TaskRuntimeService(world, definitions, services, presentation);
            TaskBridgeProviderInstaller.Install(services, runtime);

            Entity task = runtime.OfferOrStart("task.hold_supply");
            Assert.That(runtime.TryGetView(task, out TaskView offered), Is.True);
            Assert.That(offered.State, Is.EqualTo(TaskInstanceState.Offered));
            Assert.That(presentation.Cues, Has.Some.Matches<TaskPresentationCue>(c =>
                c.Kind == TaskPresentationCueKind.Offered));

            runtime.Accept(task);
            Assert.That(runtime.TryGetView(task, out TaskView active), Is.True);
            Assert.That(active.State, Is.EqualTo(TaskInstanceState.Active));

            runtime.EmitSignal("supply.recovered");
            runtime.EmitSignal("supply.hold_tick", 2);

            Assert.That(runtime.TryGetView(task, out TaskView completed), Is.True);
            Assert.That(completed.State, Is.EqualTo(TaskInstanceState.Completed));
            Assert.That(presentation.Cues, Has.Some.Matches<TaskPresentationCue>(c =>
                c.Kind == TaskPresentationCueKind.Completed));
        }

        [Test]
        public void AnyCompletion_AndDeterministicChain()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = new TaskDefinitionRegistry();
            definitions.Register("task.first", new TaskDefinition
            {
                Id = "task.first",
                StartPolicy = TaskStartPolicy.Automatic,
                CompletionRule = TaskCompletionRule.Any,
                Objectives =
                {
                    new TaskObjectiveDefinition
                    {
                        Id = "a",
                        Kind = TaskObjectiveKind.Signal,
                        SignalKey = "signal.a",
                    },
                    new TaskObjectiveDefinition
                    {
                        Id = "b",
                        Kind = TaskObjectiveKind.Signal,
                        SignalKey = "signal.b",
                    },
                },
                NextTaskId = "task.second",
            });
            definitions.Register("task.second", new TaskDefinition
            {
                Id = "task.second",
                StartPolicy = TaskStartPolicy.Automatic,
                CompletionRule = TaskCompletionRule.All,
                Objectives =
                {
                    new TaskObjectiveDefinition
                    {
                        Id = "c",
                        Kind = TaskObjectiveKind.Signal,
                        SignalKey = "signal.c",
                    },
                },
            });

            var runtime = new TaskRuntimeService(world, definitions, services, new TaskPresentationBuffer());
            TaskBridgeProviderInstaller.Install(services, runtime);

            Entity first = runtime.OfferOrStart("task.first");
            runtime.EmitSignal("signal.a");
            Assert.That(runtime.TryGetView(first, out TaskView firstView), Is.True);
            Assert.That(firstView.State, Is.EqualTo(TaskInstanceState.Completed));

            List<TaskView> views = runtime.CaptureViews();
            Assert.That(views, Has.Some.Matches<TaskView>(v =>
                v.TaskId == "task.second" && v.State == TaskInstanceState.Active));
        }

        [Test]
        public void Accept_FromNonOffered_Throws()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = new TaskDefinitionRegistry();
            definitions.Register("task.auto", new TaskDefinition
            {
                Id = "task.auto",
                StartPolicy = TaskStartPolicy.Automatic,
                Objectives =
                {
                    new TaskObjectiveDefinition
                    {
                        Id = "one",
                        Kind = TaskObjectiveKind.Signal,
                        SignalKey = "signal.one",
                    },
                },
            });

            var runtime = new TaskRuntimeService(world, definitions, services, new TaskPresentationBuffer());
            TaskBridgeProviderInstaller.Install(services, runtime);

            Entity task = runtime.OfferOrStart("task.auto");
            Assert.That(runtime.TryGetView(task, out TaskView view), Is.True);
            Assert.That(view.State, Is.EqualTo(TaskInstanceState.Active));
            Assert.Throws<InvalidOperationException>(() => runtime.Accept(task));
        }

        [Test]
        public void Abandon_FromTerminalState_Throws()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = new TaskDefinitionRegistry();
            definitions.Register("task.terminal", new TaskDefinition
            {
                Id = "task.terminal",
                StartPolicy = TaskStartPolicy.Automatic,
                CompletionRule = TaskCompletionRule.Any,
                Objectives =
                {
                    new TaskObjectiveDefinition
                    {
                        Id = "one",
                        Kind = TaskObjectiveKind.Signal,
                        SignalKey = "signal.one",
                    },
                },
            });

            var runtime = new TaskRuntimeService(world, definitions, services, new TaskPresentationBuffer());
            TaskBridgeProviderInstaller.Install(services, runtime);

            Entity task = runtime.OfferOrStart("task.terminal");
            runtime.EmitSignal("signal.one");
            Assert.That(runtime.TryGetView(task, out TaskView view), Is.True);
            Assert.That(view.State, Is.EqualTo(TaskInstanceState.Completed));
            Assert.Throws<InvalidOperationException>(() => runtime.Abandon(task, "late"));
        }

        [Test]
        public void Fail_FromTerminalState_Throws()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = new TaskDefinitionRegistry();
            definitions.Register("task.terminal", new TaskDefinition
            {
                Id = "task.terminal",
                StartPolicy = TaskStartPolicy.Automatic,
                CompletionRule = TaskCompletionRule.Any,
                Objectives =
                {
                    new TaskObjectiveDefinition
                    {
                        Id = "one",
                        Kind = TaskObjectiveKind.Signal,
                        SignalKey = "signal.one",
                    },
                },
            });

            var runtime = new TaskRuntimeService(world, definitions, services, new TaskPresentationBuffer());
            Entity task = runtime.OfferOrStart("task.terminal");
            runtime.EmitSignal("signal.one");

            Assert.Throws<InvalidOperationException>(() => runtime.Fail(task, "late"));
        }

        [Test]
        public void DuplicateActiveInstances_FailIndexRebuild()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = new TaskDefinitionRegistry();
            definitions.Register("task.duplicate", new TaskDefinition
            {
                Id = "task.duplicate",
                StartPolicy = TaskStartPolicy.Automatic,
                Objectives =
                {
                    new TaskObjectiveDefinition
                    {
                        Id = "one",
                        Kind = TaskObjectiveKind.Signal,
                        SignalKey = "signal.one",
                    },
                },
            });

            int definitionId = definitions.GetId("task.duplicate");
            world.Create(new TaskInstanceCm
            {
                DefinitionId = definitionId,
                InstanceId = 1,
                State = TaskInstanceState.Active,
                ScopeHost = Entity.Null,
                Revision = 1,
            });
            world.Create(new TaskInstanceCm
            {
                DefinitionId = definitionId,
                InstanceId = 2,
                State = TaskInstanceState.Active,
                ScopeHost = Entity.Null,
                Revision = 1,
            });

            Assert.Throws<InvalidOperationException>(() =>
                new TaskRuntimeService(world, definitions, services, new TaskPresentationBuffer()));
        }

        [Test]
        public void TaskCreateEffect_CreatesInstance_ThroughBridgeProvider()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = new TaskDefinitionRegistry();
            definitions.Register("task.from_effect", new TaskDefinition
            {
                Id = "task.from_effect",
                StartPolicy = TaskStartPolicy.Automatic,
                Objectives =
                {
                    new TaskObjectiveDefinition
                    {
                        Id = "one",
                        Kind = TaskObjectiveKind.Signal,
                        SignalKey = "signal.one",
                    },
                },
            });

            var runtime = new TaskRuntimeService(world, definitions, services, new TaskPresentationBuffer());
            TaskBridgeProviderInstaller.Install(services, runtime);

            IEffectHandler create = services.Effects.MustGet("task.create", out _);
            create.Execute(
                new ProviderEffectCall(
                    "task.create",
                    "context.subject",
                    new Dictionary<string, object?> { ["task_id"] = "task.from_effect" },
                    0),
                new ProviderExecutionContext(
                    world,
                    world.Create(),
                    ProviderContextBinding.CreateBindings()));

            List<TaskView> views = runtime.CaptureViews();
            Assert.That(views, Has.Some.Matches<TaskView>(v =>
                v.TaskId == "task.from_effect" && v.State == TaskInstanceState.Active));
        }

        [Test]
        public void TaskCreateEffect_WithoutTaskId_Throws()
        {
            using World world = World.Create();
            var services = CreateServices();
            var runtime = new TaskRuntimeService(
                world,
                new TaskDefinitionRegistry(),
                services,
                new TaskPresentationBuffer());
            TaskBridgeProviderInstaller.Install(services, runtime);

            IEffectHandler create = services.Effects.MustGet("task.create", out _);
            Assert.Throws<InvalidOperationException>(() =>
                create.Execute(
                    new ProviderEffectCall(
                        "task.create",
                        "context.subject",
                        new Dictionary<string, object?>(),
                        0),
                    new ProviderExecutionContext(
                        world,
                        world.Create(),
                        ProviderContextBinding.CreateBindings())));
        }

        [Test]
        public void EmptyObjectives_FailRegistration()
        {
            var definitions = new TaskDefinitionRegistry();
            Assert.Throws<InvalidOperationException>(() =>
                definitions.Register("task.empty", new TaskDefinition
                {
                    Id = "task.empty",
                    Objectives = { },
                }));
        }

        private static ProviderServices CreateServices()
        {
            var services = new ProviderServices(allowTestDomainOverride: true);
            FixtureProviderInstaller.InstallMinimal(services);
            return services;
        }
    }
}

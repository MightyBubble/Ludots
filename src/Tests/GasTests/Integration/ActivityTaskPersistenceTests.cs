using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Providers.FixtureProviders;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Persistence;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class ActivityTaskPersistenceTests
    {
        [Test]
        public void TaskParticipant_RoundTrip_RestoresSignalsAndIndex()
        {
            TaskDefinitionRegistry definitions = CreateTaskDefinitions();
            Entity sourceTask;
            JsonNode captured;
            using (World sourceWorld = World.Create())
            {
                var sourceRuntime = new TaskRuntimeService(
                    sourceWorld,
                    definitions,
                    CreateServices(),
                    new TaskPresentationBuffer());
                sourceTask = sourceRuntime.OfferOrStart("task.hold_line");
                sourceRuntime.EmitSignal("line.held", 3);
                captured = CoreSaveParticipants.CreateTaskParticipant(sourceRuntime).CaptureState();
            }

            using World targetWorld = World.Create();
            var targetRuntime = new TaskRuntimeService(
                targetWorld,
                definitions,
                CreateServices(),
                new TaskPresentationBuffer());
            int definitionId = definitions.GetId("task.hold_line");
            Entity restoredTask = targetWorld.Create(new TaskInstanceCm
            {
                DefinitionId = definitionId,
                InstanceId = 1,
                State = TaskInstanceState.Offered,
                ScopeHost = Entity.Null,
                ObjectiveMask = 0,
                Revision = 1,
            });

            CoreSaveParticipants.CreateTaskParticipant(targetRuntime).RestoreState(captured);

            Assert.That(targetRuntime.Signals.TryGetValue("line.held", out int count), Is.True);
            Assert.That(count, Is.EqualTo(3));
            Entity reoffered = targetRuntime.OfferOrStart("task.hold_line");
            Assert.That(reoffered, Is.EqualTo(restoredTask));
            Entity second = targetRuntime.OfferOrStart("task.escort");
            Assert.That(targetRuntime.TryGetView(second, out TaskView view), Is.True);
            Assert.That(view.InstanceId, Is.EqualTo(2));
        }

        [Test]
        public void TaskParticipant_RejectsInvalidNextInstanceId()
        {
            using World world = World.Create();
            var runtime = new TaskRuntimeService(
                world,
                CreateTaskDefinitions(),
                CreateServices(),
                new TaskPresentationBuffer());
            ISaveParticipant participant = CoreSaveParticipants.CreateTaskParticipant(runtime);

            var state = new JsonObject
            {
                ["signals"] = new JsonObject(),
                ["accumulators"] = new JsonObject(),
                ["nextInstanceId"] = 0,
            };
            Assert.Throws<SaveContextException>(() => participant.RestoreState(state));
        }

        [Test]
        public void ActivityParticipant_RoundTrip_ContinuesInstanceIds()
        {
            ActivityDefinitionRegistry definitions = CreateActivityDefinitions();
            JsonNode captured;
            using (World sourceWorld = World.Create())
            {
                var sourceRuntime = new ActivityRuntimeService(
                    sourceWorld,
                    definitions,
                    CreateServices(),
                    new ActivityPresentationBuffer());
                sourceRuntime.OfferOrActivate("activity.muster", sourceWorld.Create());
                captured = CoreSaveParticipants.CreateActivityParticipant(sourceRuntime).CaptureState();
            }

            using World targetWorld = World.Create();
            var targetRuntime = new ActivityRuntimeService(
                targetWorld,
                definitions,
                CreateServices(),
                new ActivityPresentationBuffer());
            CoreSaveParticipants.CreateActivityParticipant(targetRuntime).RestoreState(captured);

            Entity next = targetRuntime.OfferOrActivate("activity.muster", targetWorld.Create());
            Assert.That(targetWorld.Get<ActivityInstanceCm>(next).InstanceId, Is.EqualTo(2));
        }

        [Test]
        public void ActivityParticipant_RestoreRebuildsIndexFromWorld()
        {
            ActivityDefinitionRegistry definitions = CreateActivityDefinitions();
            using World world = World.Create();
            var runtime = new ActivityRuntimeService(
                world,
                definitions,
                CreateServices(),
                new ActivityPresentationBuffer());
            Entity scope = world.Create();
            Entity existing = world.Create(new ActivityInstanceCm
            {
                DefinitionId = definitions.GetId("activity.muster"),
                InstanceId = 7,
                State = ActivityInstanceState.Active,
                ScopeHost = scope,
                SelectedOptionIndex = -1,
                Revision = 1,
            });

            CoreSaveParticipants.CreateActivityParticipant(runtime)
                .RestoreState(new JsonObject { ["nextInstanceId"] = 8 });

            Entity reoffered = runtime.OfferOrActivate("activity.muster", scope);
            Assert.That(reoffered, Is.EqualTo(existing));
        }

        [Test]
        public void ScopeHostNormalizer_RewritesTaskAndActivityReferences()
        {
            using World sourceWorld = World.Create();
            Entity scope = sourceWorld.Create();
            Entity taskEntity = sourceWorld.Create(new TaskInstanceCm
            {
                DefinitionId = 1,
                InstanceId = 1,
                State = TaskInstanceState.Active,
                ScopeHost = scope,
            });
            Entity activityEntity = sourceWorld.Create(new ActivityInstanceCm
            {
                DefinitionId = 1,
                InstanceId = 1,
                State = ActivityInstanceState.Active,
                ScopeHost = scope,
                SelectedOptionIndex = -1,
            });

            SaveEntityWorldIdNormalizer.Normalize(sourceWorld);

            Assert.That(sourceWorld.Get<TaskInstanceCm>(taskEntity).ScopeHost.WorldId, Is.EqualTo(sourceWorld.Id));
            Assert.That(sourceWorld.Get<ActivityInstanceCm>(activityEntity).ScopeHost.WorldId, Is.EqualTo(sourceWorld.Id));
        }

        [Test]
        public void ScopeHostValidator_AcceptsLiveHost_RejectsDanglingHost()
        {
            using World world = World.Create();
            Entity scope = world.Create();
            world.Create(new TaskInstanceCm
            {
                DefinitionId = 1,
                InstanceId = 1,
                State = TaskInstanceState.Active,
                ScopeHost = scope,
            });
            world.Create(new ActivityInstanceCm
            {
                DefinitionId = 1,
                InstanceId = 1,
                State = ActivityInstanceState.Active,
                ScopeHost = scope,
                SelectedOptionIndex = -1,
            });

            Assert.DoesNotThrow(() =>
                SaveEntityReferenceValidator.Validate(world, SaveEntityInclusionPolicy.Default));

            using World danglingWorld = World.Create();
            Entity dead = danglingWorld.Create();
            danglingWorld.Destroy(dead);
            danglingWorld.Create(new TaskInstanceCm
            {
                DefinitionId = 1,
                InstanceId = 1,
                State = TaskInstanceState.Active,
                ScopeHost = dead,
            });

            Assert.Throws<SaveContextException>(() =>
                SaveEntityReferenceValidator.Validate(danglingWorld, SaveEntityInclusionPolicy.Default));
        }

        private static TaskDefinitionRegistry CreateTaskDefinitions()
        {
            var definitions = new TaskDefinitionRegistry();
            definitions.Register("task.hold_line", new TaskDefinition
            {
                Id = "task.hold_line",
                StartPolicy = TaskStartPolicy.PlayerAccept,
                Objectives =
                {
                    new TaskObjectiveDefinition
                    {
                        Id = "hold",
                        Kind = TaskObjectiveKind.Signal,
                        SignalKey = "line.held",
                    },
                },
            });
            definitions.Register("task.escort", new TaskDefinition
            {
                Id = "task.escort",
                StartPolicy = TaskStartPolicy.Automatic,
                Objectives =
                {
                    new TaskObjectiveDefinition
                    {
                        Id = "arrive",
                        Kind = TaskObjectiveKind.Signal,
                        SignalKey = "escort.arrived",
                    },
                },
            });
            return definitions;
        }

        private static ActivityDefinitionRegistry CreateActivityDefinitions()
        {
            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.muster", new ActivityDefinition
            {
                Id = "activity.muster",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                Options =
                {
                    new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                },
            });
            return definitions;
        }

        private static ProviderServices CreateServices()
        {
            var services = new ProviderServices(allowTestDomainOverride: true);
            FixtureProviderInstaller.InstallMinimal(services);
            return services;
        }
    }
}

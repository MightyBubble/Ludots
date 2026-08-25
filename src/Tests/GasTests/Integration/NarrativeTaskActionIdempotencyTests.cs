using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Providers.FixtureProviders;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Tests.TestCommon;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class NarrativeTaskActionIdempotencyTests
    {
        [Test]
        public void CompleteTask_OnAlreadyCompletedTask_IsNoOp()
        {
            using World world = World.Create();
            var runtime = CreateRuntime(world);
            int completedEvents = 0;
            runtime.TaskStateChanged += change =>
            {
                if (change.State == TaskInstanceState.Completed)
                {
                    completedEvents++;
                }
            };
            using GameEngine engine = CreateEngine();
            var director = new NarrativeDirector(engine, new NarrativeDefinitionRegistry(), runtime);

            Entity task = runtime.OfferOrStart("task.idempotency");
            director.CompleteTask("task.idempotency");
            Assert.That(runtime.TryGetView(task, out TaskView first), Is.True);
            Assert.That(first.State, Is.EqualTo(TaskInstanceState.Completed));

            int revisionAfterFirst = world.Get<TaskInstanceCm>(task).Revision;
            Assert.DoesNotThrow(() => director.CompleteTask("task.idempotency"));

            Assert.That(runtime.TryGetView(task, out TaskView second), Is.True);
            Assert.That(second.State, Is.EqualTo(TaskInstanceState.Completed));
            Assert.That(world.Get<TaskInstanceCm>(task).Revision, Is.EqualTo(revisionAfterFirst));
            Assert.That(completedEvents, Is.EqualTo(1));
        }

        [Test]
        public void FailTask_OnAlreadyFailedTask_IsNoOp()
        {
            using World world = World.Create();
            var runtime = CreateRuntime(world);
            int failedEvents = 0;
            runtime.TaskStateChanged += change =>
            {
                if (change.State == TaskInstanceState.Failed)
                {
                    failedEvents++;
                }
            };
            using GameEngine engine = CreateEngine();
            var director = new NarrativeDirector(engine, new NarrativeDefinitionRegistry(), runtime);

            Entity task = runtime.OfferOrStart("task.idempotency");
            director.FailTask("task.idempotency");
            Assert.That(runtime.TryGetView(task, out TaskView first), Is.True);
            Assert.That(first.State, Is.EqualTo(TaskInstanceState.Failed));

            int revisionAfterFirst = world.Get<TaskInstanceCm>(task).Revision;
            Assert.DoesNotThrow(() => director.FailTask("task.idempotency"));

            Assert.That(runtime.TryGetView(task, out TaskView second), Is.True);
            Assert.That(second.State, Is.EqualTo(TaskInstanceState.Failed));
            Assert.That(world.Get<TaskInstanceCm>(task).Revision, Is.EqualTo(revisionAfterFirst));
            Assert.That(failedEvents, Is.EqualTo(1));
        }

        [Test]
        public void CompleteTask_OnFailedTask_StillThrows()
        {
            using World world = World.Create();
            var runtime = CreateRuntime(world);
            using GameEngine engine = CreateEngine();
            var director = new NarrativeDirector(engine, new NarrativeDefinitionRegistry(), runtime);

            Entity task = runtime.OfferOrStart("task.idempotency");
            runtime.Fail(task, "killed");

            Assert.Throws<InvalidOperationException>(() => director.CompleteTask("task.idempotency"));
        }

        [Test]
        public void FailTask_OnCompletedTask_StillThrows()
        {
            using World world = World.Create();
            var runtime = CreateRuntime(world);
            using GameEngine engine = CreateEngine();
            var director = new NarrativeDirector(engine, new NarrativeDefinitionRegistry(), runtime);

            Entity task = runtime.OfferOrStart("task.idempotency");
            runtime.Complete("task.idempotency");

            Assert.Throws<InvalidOperationException>(() => director.FailTask("task.idempotency"));
        }

        [Test]
        public void CompleteTask_OnUnknownTask_StillThrows()
        {
            using World world = World.Create();
            var runtime = CreateRuntime(world);
            using GameEngine engine = CreateEngine();
            var director = new NarrativeDirector(engine, new NarrativeDefinitionRegistry(), runtime);

            Assert.Throws<InvalidOperationException>(() => director.CompleteTask("task.missing"));
        }

        private static TaskRuntimeService CreateRuntime(World world)
        {
            var services = new ProviderServices(allowTestDomainOverride: true);
            FixtureProviderInstaller.InstallMinimal(services);
            var definitions = new TaskDefinitionRegistry();
            definitions.Register("task.idempotency", new TaskDefinition
            {
                Id = "task.idempotency",
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
            return new TaskRuntimeService(world, definitions, services, new TaskPresentationBuffer());
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
                Path.Combine(repoRoot, "assets"));
            return engine;
        }

        private static string FindRepoRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, "src", "Core", "Ludots.Core.csproj")))
                {
                    return current;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate repo root.");
        }
    }
}

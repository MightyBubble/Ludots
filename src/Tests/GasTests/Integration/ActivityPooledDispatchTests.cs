using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Engine.Randomization;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Providers.FixtureProviders;
using Ludots.Core.Gameplay.Rng;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class ActivityPooledDispatchTests
    {
        [Test]
        public void PooledDraw_UnknownPool_RejectsBeforeEntityCreation()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = CreatePooledDefinitions(poolKey: "pool.missing");
            var presentation = new ActivityPresentationBuffer();
            var runtime = new ActivityRuntimeService(
                world,
                definitions,
                services,
                presentation,
                rngPickService: CreatePoolRng());

            Entity scope = world.Create();
            ActivityAdmissionResult result = runtime.OfferOrActivateChecked("activity.gate", scope);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Instance, Is.EqualTo(Entity.Null));
            Assert.That(result.RejectionCode, Is.EqualTo("admission.pool_unavailable:pool.missing"));
            Assert.That(runtime.CaptureViews(), Is.Empty);
            Assert.That(presentation.Cues, Has.Some.Matches<ActivityPresentationCue>(c =>
                c.Kind == ActivityPresentationCueKind.AdmissionRejected &&
                c.ActivityId == "activity.gate" &&
                c.ScopeKey == scope.Id &&
                c.Reason == "admission.pool_unavailable:pool.missing"));
        }

        [Test]
        public void PooledDraw_EmptyPool_RejectsBeforeEntityCreation()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = CreatePooledDefinitions(poolKey: "pool.empty");
            var presentation = new ActivityPresentationBuffer();
            var runtime = new ActivityRuntimeService(
                world,
                definitions,
                services,
                presentation,
                rngPickService: CreateEmptyPoolRng());

            Entity scope = world.Create();
            ActivityAdmissionResult result = runtime.OfferOrActivateChecked("activity.gate", scope);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Instance, Is.EqualTo(Entity.Null));
            Assert.That(result.RejectionCode, Is.EqualTo("admission.pool_unavailable:pool.empty"));
            Assert.That(runtime.CaptureViews(), Is.Empty);
            Assert.That(presentation.Cues, Has.Some.Matches<ActivityPresentationCue>(c =>
                c.Kind == ActivityPresentationCueKind.AdmissionRejected &&
                c.Reason == "admission.pool_unavailable:pool.empty"));
        }

        [Test]
        public void PooledDraw_SameStreamStateYieldsSameCandidate()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = CreatePooledDefinitions();
            (RngStreamService streams, RngPickService rng) = CreateMultiEntryPool();

            // Expectation from a twin service with the same named stream seed: one Pick
            // consumes exactly one stream value, so identical seeds yield identical draws.
            (RngStreamService expectedStreams, RngPickService expectedRng) = CreateMultiEntryPool();
            int expectedIndex = expectedRng.Pick("pool.caravan");
            string expectedId = expectedRng.GetDistribution("pool.caravan").GetEntry(expectedIndex).Id;

            var runtime = new ActivityRuntimeService(
                world,
                definitions,
                services,
                new ActivityPresentationBuffer(),
                rngPickService: rng);
            RngStream stream = streams.GetStream("pool.stream");
            RngStreamSnapshot initial = stream.CaptureSnapshot();

            Entity first = runtime.OfferOrActivate("activity.gate", world.Create());
            int firstDefinitionId = world.Get<ActivityInstanceCm>(first).DefinitionId;

            stream.RestoreSnapshot(initial);
            Entity second = runtime.OfferOrActivate("activity.gate", world.Create());
            int secondDefinitionId = world.Get<ActivityInstanceCm>(second).DefinitionId;

            Assert.That(secondDefinitionId, Is.EqualTo(firstDefinitionId));
            Assert.That(firstDefinitionId, Is.EqualTo(definitions.GetId(expectedId)));
        }

        [Test]
        public void PooledDraw_SelectedForcedCandidate_PresentsWithCandidateOptions()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = CreatePooledDefinitions();
            var presentation = new ActivityPresentationBuffer();
            var runtime = new ActivityRuntimeService(
                world,
                definitions,
                services,
                presentation,
                rngPickService: CreatePoolRng());

            Entity activity = runtime.OfferOrActivate("activity.gate", world.Create());

            Assert.That(activity, Is.Not.EqualTo(Entity.Null));
            Assert.That(runtime.TryGetState(activity, out ActivityInstanceState state, out string id), Is.True);
            Assert.That(state, Is.EqualTo(ActivityInstanceState.Active));
            Assert.That(id, Is.EqualTo("activity.caravan"));
            Assert.That(presentation.Cues, Has.Some.Matches<ActivityPresentationCue>(c =>
                c.Kind == ActivityPresentationCueKind.Presented && c.ActivityId == "activity.caravan"));

            var options = new List<ActivityOptionView>();
            Assert.That(runtime.TryGetActiveOptions(activity, null, options), Is.True);
            Assert.That(options, Has.Count.EqualTo(2));
        }

        [Test]
        public void PooledDraw_SelectedAutomaticCandidate_SettlesSilently()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = CreatePooledDefinitions();
            var presentation = new ActivityPresentationBuffer();
            var runtime = new ActivityRuntimeService(
                world,
                definitions,
                services,
                presentation,
                rngPickService: CreatePoolRng(entryId: "activity.supply_notice"));

            Entity activity = runtime.OfferOrActivate("activity.gate", world.Create());

            Assert.That(activity, Is.Not.EqualTo(Entity.Null));
            Assert.That(runtime.TryGetState(activity, out ActivityInstanceState state, out string id), Is.True);
            Assert.That(state, Is.EqualTo(ActivityInstanceState.Resolved));
            Assert.That(id, Is.EqualTo("activity.supply_notice"));
            Assert.That(presentation.Cues, Has.Some.Matches<ActivityPresentationCue>(c =>
                c.Kind == ActivityPresentationCueKind.AutomaticSettled &&
                c.ActivityId == "activity.supply_notice"));
        }

        [Test]
        public void PooledDispatch_WithoutRngPickService_FailsFast()
        {
            using World world = World.Create();
            var runtime = new ActivityRuntimeService(
                world,
                CreatePooledDefinitions(),
                CreateServices(),
                new ActivityPresentationBuffer());

            Assert.Throws<InvalidOperationException>(() =>
                runtime.OfferOrActivate("activity.gate", world.Create()));
        }

        [Test]
        public void PooledPolicy_WithoutPoolKey_FailsRegistration()
        {
            var definitions = new ActivityDefinitionRegistry();
            Assert.Throws<InvalidOperationException>(() =>
                definitions.Register("activity.bad", new ActivityDefinition
                {
                    Id = "activity.bad",
                    SourceKey = "fixture.signal_ping",
                    DispatchPolicy = ActivityDispatchPolicy.Pooled,
                }));
        }

        [Test]
        public void PoolKeyWithoutPooledPolicy_FailsRegistration()
        {
            var definitions = new ActivityDefinitionRegistry();
            Assert.Throws<InvalidOperationException>(() =>
                definitions.Register("activity.bad", new ActivityDefinition
                {
                    Id = "activity.bad",
                    SourceKey = "fixture.signal_ping",
                    DispatchPolicy = ActivityDispatchPolicy.Forced,
                    PoolKey = "pool.caravan",
                    Options =
                    {
                        new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                    },
                }));
        }

        [Test]
        public void PooledPolicy_WithOptions_FailsRegistration()
        {
            var definitions = new ActivityDefinitionRegistry();
            Assert.Throws<InvalidOperationException>(() =>
                definitions.Register("activity.bad", new ActivityDefinition
                {
                    Id = "activity.bad",
                    SourceKey = "fixture.signal_ping",
                    DispatchPolicy = ActivityDispatchPolicy.Pooled,
                    PoolKey = "pool.caravan",
                    Options =
                    {
                        new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                    },
                }));
        }

        private static ActivityDefinitionRegistry CreatePooledDefinitions(string poolKey = "pool.caravan")
        {
            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.gate", new ActivityDefinition
            {
                Id = "activity.gate",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Pooled,
                PoolKey = poolKey,
            });
            definitions.Register("activity.caravan", new ActivityDefinition
            {
                Id = "activity.caravan",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                Options =
                {
                    new ActivityOptionDefinition { Id = "admit", IsBaseline = true },
                    new ActivityOptionDefinition { Id = "decline" },
                },
            });
            definitions.Register("activity.patrol", new ActivityDefinition
            {
                Id = "activity.patrol",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                Options =
                {
                    new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                },
            });
            definitions.Register("activity.supply_notice", new ActivityDefinition
            {
                Id = "activity.supply_notice",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Automatic,
                AutomaticEffects =
                {
                    new ActivityEffectRef { EffectKey = "fixture.noop" },
                },
            });
            return definitions;
        }

        private static RngPickService CreatePoolRng(string entryId = "activity.caravan")
        {
            var streams = new RngStreamService();
            streams.DeclareStream("pool.stream", 815u);
            return new RngPickService(streams, new[]
            {
                new DistributionTable("pool.caravan", "pool.stream", new[]
                {
                    new DistributionEntryConfig(entryId, 10, Enabled: true, Locked: false, Modulation: null),
                }),
            });
        }

        private static RngPickService CreateEmptyPoolRng()
        {
            var streams = new RngStreamService();
            streams.DeclareStream("pool.stream", 815u);
            return new RngPickService(streams, new[]
            {
                new DistributionTable("pool.empty", "pool.stream", new[]
                {
                    new DistributionEntryConfig("activity.caravan", 7, Enabled: false, Locked: false, Modulation: null),
                }),
            });
        }

        private static (RngStreamService Streams, RngPickService Rng) CreateMultiEntryPool()
        {
            var streams = new RngStreamService();
            streams.DeclareStream("pool.stream", 815u);
            var tables = new[]
            {
                new DistributionTable("pool.caravan", "pool.stream", new[]
                {
                    new DistributionEntryConfig("activity.caravan", 7, Enabled: true, Locked: false, Modulation: null),
                    new DistributionEntryConfig("activity.patrol", 3, Enabled: true, Locked: false, Modulation: null),
                }),
            };
            return (streams, new RngPickService(streams, tables));
        }

        private static ProviderServices CreateServices()
        {
            var services = new ProviderServices(allowTestDomainOverride: true);
            FixtureProviderInstaller.InstallMinimal(services);
            return services;
        }
    }
}

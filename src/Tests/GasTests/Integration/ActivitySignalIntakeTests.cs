using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Providers.FixtureProviders;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class ActivitySignalIntakeTests
    {
        [Test]
        public void MatchingSignal_AdmitsForcedActivity()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.border_incident", new ActivityDefinition
            {
                Id = "activity.border_incident",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                Options =
                {
                    new ActivityOptionDefinition { Id = "hold", IsBaseline = true },
                },
            });

            var presentation = new ActivityPresentationBuffer();
            var runtime = new ActivityRuntimeService(world, definitions, services, presentation);
            Entity scope = world.Create();

            ActivitySignalIntakeResult result = runtime.IntakeSignal(new ActivitySignal(
                "fixture.signal_ping",
                "fact-1",
                occurredAt: 1000,
                scope,
                Array.Empty<Entity>(),
                new Dictionary<string, object?>()));

            Assert.That(result.IsIdempotentDrop, Is.False);
            Assert.That(result.MatchedAnyDefinition, Is.True);
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Matches[0].Accepted, Is.True);
            Assert.That(result.Matches[0].Instance, Is.Not.EqualTo(Entity.Null));
            Assert.That(runtime.TryGetState(result.Matches[0].Instance, out ActivityInstanceState state, out string id), Is.True);
            Assert.That(state, Is.EqualTo(ActivityInstanceState.Active));
            Assert.That(id, Is.EqualTo("activity.border_incident"));
            Assert.That(presentation.Cues, Has.Some.Matches<ActivityPresentationCue>(c =>
                c.Kind == ActivityPresentationCueKind.Presented));
        }

        [Test]
        public void NonMatchingSignal_CreatesNoEntity_AndGivesReason()
        {
            using World world = World.Create();
            var services = CreateServices();
            services.Conditions.Register(
                "fixture.always_false",
                new FixtureConditionProvider(false),
                ProviderParameterSchema.Empty);

            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.gated", new ActivityDefinition
            {
                Id = "activity.gated",
                SourceKey = "fixture.signal_ping",
                SourceSubscription = new ActivitySourceSubscription
                {
                    SourceKey = "fixture.signal_ping",
                    MatchCondition = new ActivityConditionRef { ConditionKey = "fixture.always_false" },
                },
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                Options =
                {
                    new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                },
            });

            var runtime = new ActivityRuntimeService(
                world,
                definitions,
                services,
                new ActivityPresentationBuffer());

            ActivitySignalIntakeResult result = runtime.IntakeSignal(CreateSignal("fact-1", world.Create()));

            Assert.That(result.MatchedAnyDefinition, Is.True);
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Matches[0].Accepted, Is.False);
            Assert.That(result.Matches[0].Instance, Is.EqualTo(Entity.Null));
            Assert.That(
                result.Matches[0].RejectionCode,
                Is.EqualTo($"{ActivitySignalFailures.MatchConditionFailed}:fixture.always_false"));
            Assert.That(runtime.CaptureViews(), Is.Empty);
        }

        [Test]
        public void SignalWithoutSubscribers_CreatesNoEntityAndNoCue()
        {
            using World world = World.Create();
            var services = CreateServices();
            var runtime = new ActivityRuntimeService(
                world,
                new ActivityDefinitionRegistry(),
                services,
                new ActivityPresentationBuffer());
            var presentation = runtime.Presentation;

            ActivitySignalIntakeResult result = runtime.IntakeSignal(CreateSignal("fact-1", world.Create()));

            Assert.That(result.IsIdempotentDrop, Is.False);
            Assert.That(result.MatchedAnyDefinition, Is.False);
            Assert.That(result.Matches, Is.Empty);
            Assert.That(runtime.CaptureViews(), Is.Empty);
            Assert.That(presentation.Cues, Is.Empty);
        }

        [Test]
        public void ScopeObjectParameterMatching_DrivesAdmission()
        {
            using World world = World.Create();
            var services = CreateServices();
            Entity expectedScope = world.Create();
            Entity target = world.Create();
            services.Conditions.Register(
                "fixture.signal_scope_matches",
                new SignalFieldCondition((context) =>
                    context.Subject.Id == expectedScope.Id &&
                    context.TryResolveReference("signal.object_refs", out object? refs) &&
                    refs is IReadOnlyList<Entity> objectRefs &&
                    objectRefs.Count == 1 &&
                    objectRefs[0].Id == target.Id &&
                    context.TryResolveReference("signal.zone_id", out object? zone) &&
                    zone is string zoneId &&
                    string.Equals(zoneId, "north", StringComparison.Ordinal)),
                ProviderParameterSchema.Empty);
            services.Conditions.Register(
                "fixture.always_false",
                new FixtureConditionProvider(false),
                ProviderParameterSchema.Empty);

            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.scoped", new ActivityDefinition
            {
                Id = "activity.scoped",
                SourceKey = "fixture.signal_ping",
                SourceSubscription = new ActivitySourceSubscription
                {
                    SourceKey = "fixture.signal_ping",
                    MatchCondition = new ActivityConditionRef { ConditionKey = "fixture.signal_scope_matches" },
                },
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                Options =
                {
                    new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                },
            });
            definitions.Register("activity.other", new ActivityDefinition
            {
                Id = "activity.other",
                SourceKey = "fixture.signal_ping",
                SourceSubscription = new ActivitySourceSubscription
                {
                    SourceKey = "fixture.signal_ping",
                    MatchCondition = new ActivityConditionRef { ConditionKey = "fixture.always_false" },
                },
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                Options =
                {
                    new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                },
            });

            var runtime = new ActivityRuntimeService(
                world,
                definitions,
                services,
                new ActivityPresentationBuffer());

            ActivitySignalIntakeResult matching = runtime.IntakeSignal(new ActivitySignal(
                "fixture.signal_ping",
                "fact-scope-1",
                occurredAt: 1000,
                expectedScope,
                new[] { target },
                new Dictionary<string, object?> { ["zone_id"] = "north" }));

            Assert.That(matching.Matches, Has.Count.EqualTo(2));
            Assert.That(matching.Matches, Has.Some.Matches<ActivitySignalMatchResult>(m =>
                m.ActivityId == "activity.scoped" && m.Accepted && m.Instance != Entity.Null));
            Assert.That(matching.Matches, Has.Some.Matches<ActivitySignalMatchResult>(m =>
                m.ActivityId == "activity.other" && !m.Accepted));

            Entity otherScope = world.Create();
            ActivitySignalIntakeResult nonMatching = runtime.IntakeSignal(new ActivitySignal(
                "fixture.signal_ping",
                "fact-scope-2",
                occurredAt: 1000,
                otherScope,
                new[] { target },
                new Dictionary<string, object?> { ["zone_id"] = "north" }));

            Assert.That(nonMatching.Matches, Has.Count.EqualTo(2));
            Assert.That(nonMatching.Matches, Has.None.Matches<ActivitySignalMatchResult>(m => m.Accepted));
            Assert.That(nonMatching.Matches, Has.Some.Matches<ActivitySignalMatchResult>(m =>
                m.ActivityId == "activity.scoped" &&
                m.RejectionCode == $"{ActivitySignalFailures.MatchConditionFailed}:fixture.signal_scope_matches"));
        }

        [Test]
        public void UnknownSourceKey_FailsFast_WithKeyName()
        {
            using World world = World.Create();
            var runtime = new ActivityRuntimeService(
                world,
                new ActivityDefinitionRegistry(),
                CreateServices(),
                new ActivityPresentationBuffer());

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                runtime.IntakeSignal(new ActivitySignal(
                    "fixture.not_registered",
                    "fact-1",
                    occurredAt: 1000,
                    world.Create(),
                    Array.Empty<Entity>(),
                    new Dictionary<string, object?>())));

            Assert.That(ex.Message, Does.Contain(ActivitySignalFailures.UnknownSourceKey));
            Assert.That(ex.Message, Does.Contain("fixture.not_registered"));
        }

        [Test]
        public void MalformedSignal_FailsFast_ListingMissingFields()
        {
            using World world = World.Create();
            var runtime = new ActivityRuntimeService(
                world,
                new ActivityDefinitionRegistry(),
                CreateServices(),
                new ActivityPresentationBuffer());

            InvalidOperationException missingSource = Assert.Throws<InvalidOperationException>(() =>
                runtime.IntakeSignal(new ActivitySignal(
                    string.Empty,
                    "fact-1",
                    occurredAt: 1000,
                    world.Create(),
                    Array.Empty<Entity>(),
                    new Dictionary<string, object?>())));

            Assert.That(missingSource.Message, Does.Contain(ActivitySignalFailures.Malformed));
            Assert.That(missingSource.Message, Does.Contain("source_key"));

            InvalidOperationException missingId = Assert.Throws<InvalidOperationException>(() =>
                runtime.IntakeSignal(new ActivitySignal(
                    "fixture.signal_ping",
                    string.Empty,
                    occurredAt: 1000,
                    world.Create(),
                    Array.Empty<Entity>(),
                    new Dictionary<string, object?>())));

            Assert.That(missingId.Message, Does.Contain("signal_id"));

            InvalidOperationException missingRefs = Assert.Throws<InvalidOperationException>(() =>
                runtime.IntakeSignal(new ActivitySignal(
                    "fixture.signal_ping",
                    "fact-2",
                    occurredAt: 1000,
                    world.Create(),
                    null,
                    new Dictionary<string, object?>())));

            Assert.That(missingRefs.Message, Does.Contain("object_refs"));

            InvalidOperationException missingParams = Assert.Throws<InvalidOperationException>(() =>
                runtime.IntakeSignal(new ActivitySignal(
                    "fixture.signal_ping",
                    "fact-3",
                    occurredAt: 1000,
                    world.Create(),
                    Array.Empty<Entity>(),
                    null)));

            Assert.That(missingParams.Message, Does.Contain("parameters"));
        }

        [Test]
        public void MalformedSubscription_FailsRegistration()
        {
            var definitions = new ActivityDefinitionRegistry();

            Assert.Throws<InvalidOperationException>(() =>
                definitions.Register("activity.bad", new ActivityDefinition
                {
                    Id = "activity.bad",
                    SourceKey = "fixture.signal_ping",
                    SourceSubscription = new ActivitySourceSubscription(),
                    DispatchPolicy = ActivityDispatchPolicy.Forced,
                    Options =
                    {
                        new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                    },
                }));

            Assert.Throws<InvalidOperationException>(() =>
                definitions.Register("activity.conflict", new ActivityDefinition
                {
                    Id = "activity.conflict",
                    SourceKey = "fixture.signal_ping",
                    SourceSubscription = new ActivitySourceSubscription
                    {
                        SourceKey = "supply.capacity_state_changed",
                    },
                    DispatchPolicy = ActivityDispatchPolicy.Forced,
                    Options =
                    {
                        new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                    },
                }));

            Assert.Throws<InvalidOperationException>(() =>
                definitions.Register("activity.blank_condition", new ActivityDefinition
                {
                    Id = "activity.blank_condition",
                    SourceKey = "fixture.signal_ping",
                    SourceSubscription = new ActivitySourceSubscription
                    {
                        SourceKey = "fixture.signal_ping",
                        MatchCondition = new ActivityConditionRef(),
                    },
                    DispatchPolicy = ActivityDispatchPolicy.Forced,
                    Options =
                    {
                        new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                    },
                }));
        }

        [Test]
        public void RepeatedSignalId_SecondSubmissionIsIdempotentDrop()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.once", new ActivityDefinition
            {
                Id = "activity.once",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                Options =
                {
                    new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                },
            });

            var runtime = new ActivityRuntimeService(
                world,
                definitions,
                services,
                new ActivityPresentationBuffer());
            ActivitySignal signal = CreateSignal("fact-same", world.Create());

            ActivitySignalIntakeResult first = runtime.IntakeSignal(signal);
            ActivitySignalIntakeResult second = runtime.IntakeSignal(signal);

            Assert.That(first.IsIdempotentDrop, Is.False);
            Assert.That(first.MatchedAnyDefinition, Is.True);
            Assert.That(second.IsIdempotentDrop, Is.True);
            Assert.That(second.Matches, Is.Empty);
            Assert.That(runtime.CaptureViews(), Has.Count.EqualTo(1));
        }

        [Test]
        public void MatchConditionWriteAttempt_FailsFast()
        {
            using World world = World.Create();
            var services = CreateServices();
            services.Conditions.Register(
                "fixture.writing",
                new WritingConditionProbe(),
                ProviderParameterSchema.Empty);

            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.guarded", new ActivityDefinition
            {
                Id = "activity.guarded",
                SourceKey = "fixture.signal_ping",
                SourceSubscription = new ActivitySourceSubscription
                {
                    SourceKey = "fixture.signal_ping",
                    MatchCondition = new ActivityConditionRef { ConditionKey = "fixture.writing" },
                },
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                Options =
                {
                    new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                },
            });

            var runtime = new ActivityRuntimeService(
                world,
                definitions,
                services,
                new ActivityPresentationBuffer());

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                runtime.IntakeSignal(CreateSignal("fact-1", world.Create())));

            Assert.That(ex.Message, Does.Contain(ProviderFailureCodes.ConditionWriteDetected));
            Assert.That(runtime.CaptureViews(), Is.Empty);
        }

        private static ActivitySignal CreateSignal(string signalId, Entity scope) =>
            new(
                "fixture.signal_ping",
                signalId,
                occurredAt: 1000,
                scope,
                Array.Empty<Entity>(),
                new Dictionary<string, object?>());

        private static ProviderServices CreateServices()
        {
            var services = new ProviderServices(allowTestDomainOverride: true);
            FixtureProviderInstaller.InstallMinimal(services);
            return services;
        }

        private sealed class SignalFieldCondition : IConditionProvider
        {
            private readonly Func<ProviderExecutionContext, bool> _predicate;

            public SignalFieldCondition(Func<ProviderExecutionContext, bool> predicate)
            {
                _predicate = predicate;
            }

            public bool Evaluate(ProviderExecutionContext context, IReadOnlyDictionary<string, object?> parameters)
            {
                return _predicate(context);
            }
        }
    }
}

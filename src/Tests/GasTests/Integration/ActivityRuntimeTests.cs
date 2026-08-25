using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Providers.FixtureProviders;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class ActivityRuntimeTests
    {
        [Test]
        public void ForcedActivity_PresentsAndResolvesBaselineOption()
        {
            using World world = World.Create();
            var services = CreateServices();
            var effect = new FixtureEffectHandler();
            services.Effects.Register(
                "fixture.settle_choice",
                effect,
                new ProviderParameterSchema(new[]
                {
                    new ProviderParameterField("note", ProviderParameterKind.String, required: false),
                }));

            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.border_incident", new ActivityDefinition
            {
                Id = "activity.border_incident",
                DisplayName = "Border Incident",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                Options =
                {
                    new ActivityOptionDefinition
                    {
                        Id = "hold",
                        Title = "Hold",
                        IsBaseline = true,
                        Effects =
                        {
                            new ActivityEffectRef
                            {
                                EffectKey = "fixture.settle_choice",
                                Parameters = new Dictionary<string, object?> { ["note"] = "hold" },
                            },
                        },
                    },
                    new ActivityOptionDefinition
                    {
                        Id = "withdraw",
                        Title = "Withdraw",
                        Effects =
                        {
                            new ActivityEffectRef { EffectKey = "fixture.settle_choice" },
                        },
                    },
                },
            });

            var presentation = new ActivityPresentationBuffer();
            var runtime = new ActivityRuntimeService(world, definitions, services, presentation);
            Entity scope = world.Create();

            Entity activity = runtime.OfferOrActivate("activity.border_incident", scope);
            Assert.That(activity, Is.Not.EqualTo(Entity.Null));
            Assert.That(runtime.TryGetState(activity, out ActivityInstanceState state, out string id), Is.True);
            Assert.That(state, Is.EqualTo(ActivityInstanceState.Active));
            Assert.That(id, Is.EqualTo("activity.border_incident"));
            Assert.That(presentation.Cues, Has.Some.Matches<ActivityPresentationCue>(c =>
                c.Kind == ActivityPresentationCueKind.Presented));

            var options = new List<ActivityOptionView>();
            Assert.That(runtime.TryGetActiveOptions(activity, null, options), Is.True);
            Assert.That(options.Count, Is.EqualTo(2));

            runtime.ResolveOption(activity, "hold");
            Assert.That(runtime.TryGetState(activity, out state, out _), Is.True);
            Assert.That(state, Is.EqualTo(ActivityInstanceState.Resolved));
            Assert.That(effect.Executed, Has.Count.EqualTo(1));
            Assert.That(presentation.Cues, Has.Some.Matches<ActivityPresentationCue>(c =>
                c.Kind == ActivityPresentationCueKind.Resolved && c.OptionId == "hold"));
        }

        [Test]
        public void AutomaticActivity_SettlesWithoutOptions()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.notice", new ActivityDefinition
            {
                Id = "activity.notice",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Automatic,
                AutomaticEffects =
                {
                    new ActivityEffectRef { EffectKey = "fixture.noop" },
                },
            });

            var presentation = new ActivityPresentationBuffer();
            var runtime = new ActivityRuntimeService(world, definitions, services, presentation);
            Entity activity = runtime.OfferOrActivate("activity.notice", world.Create());
            Assert.That(runtime.TryGetState(activity, out ActivityInstanceState state, out _), Is.True);
            Assert.That(state, Is.EqualTo(ActivityInstanceState.Resolved));
            Assert.That(presentation.Cues, Has.Some.Matches<ActivityPresentationCue>(c =>
                c.Kind == ActivityPresentationCueKind.AutomaticSettled));
        }

        [Test]
        public void HiddenOption_DoesNotAppear_BlockedOptionKeepsReason()
        {
            using World world = World.Create();
            var services = CreateServices();
            services.Conditions.Register(
                "fixture.always_false",
                new FixtureConditionProvider(false),
                ProviderParameterSchema.Empty);

            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.branch", new ActivityDefinition
            {
                Id = "activity.branch",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                Options =
                {
                    new ActivityOptionDefinition
                    {
                        Id = "baseline",
                        IsBaseline = true,
                    },
                    new ActivityOptionDefinition
                    {
                        Id = "hidden",
                        ShowCondition = new ActivityConditionRef { ConditionKey = "fixture.always_false" },
                    },
                    new ActivityOptionDefinition
                    {
                        Id = "blocked",
                        ExecuteCondition = new ActivityConditionRef { ConditionKey = "fixture.always_false" },
                    },
                },
            });

            var runtime = new ActivityRuntimeService(
                world,
                definitions,
                services,
                new ActivityPresentationBuffer());
            Entity activity = runtime.OfferOrActivate("activity.branch", world.Create());
            var options = new List<ActivityOptionView>();
            runtime.TryGetActiveOptions(activity, null, options);

            Assert.That(options, Has.None.Matches<ActivityOptionView>(o => o.OptionId == "hidden"));
            Assert.That(options, Has.Some.Matches<ActivityOptionView>(o =>
                o.OptionId == "blocked" && !o.Executable && o.BlockReason.Contains("fixture.always_false")));
        }

        [Test]
        public void ResolvedActivity_CannotResolveTwice()
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
            Entity activity = runtime.OfferOrActivate("activity.once", world.Create());
            runtime.ResolveOption(activity, "ok");

            Assert.Throws<InvalidOperationException>(() => runtime.ResolveOption(activity, "ok"));
        }

        [Test]
        public void UnknownActivityDefinition_Throws()
        {
            using World world = World.Create();
            var services = CreateServices();
            var runtime = new ActivityRuntimeService(
                world,
                new ActivityDefinitionRegistry(),
                services,
                new ActivityPresentationBuffer());

            Assert.Throws<InvalidOperationException>(() =>
                runtime.OfferOrActivate("activity.missing", world.Create()));
        }

        [Test]
        public void DefinitionWithoutBaseline_FailsRegistration()
        {
            var definitions = new ActivityDefinitionRegistry();
            Assert.Throws<InvalidOperationException>(() =>
                definitions.Register("activity.bad", new ActivityDefinition
                {
                    Id = "activity.bad",
                    SourceKey = "fixture.signal_ping",
                    DispatchPolicy = ActivityDispatchPolicy.Forced,
                    Options =
                    {
                        new ActivityOptionDefinition { Id = "a" },
                    },
                }));
        }

        [Test]
        public void DefinitionWithUnknownRepeatPolicy_FailsRegistration()
        {
            var definitions = new ActivityDefinitionRegistry();
            Assert.Throws<InvalidOperationException>(() =>
                definitions.Register("activity.bad", new ActivityDefinition
                {
                    Id = "activity.bad",
                    SourceKey = "fixture.signal_ping",
                    DispatchPolicy = ActivityDispatchPolicy.Forced,
                    RepeatPolicy = (ActivityRepeatPolicy)127,
                    Options =
                    {
                        new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                    },
                }));
        }

        [Test]
        public void RepeatableActivity_CreatesTwoInstances()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.repeat", new ActivityDefinition
            {
                Id = "activity.repeat",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                RepeatPolicy = ActivityRepeatPolicy.Repeatable,
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
            Entity scope = world.Create();

            Entity first = runtime.OfferOrActivate("activity.repeat", scope);
            Entity second = runtime.OfferOrActivate("activity.repeat", scope);

            Assert.That(first, Is.Not.EqualTo(Entity.Null));
            Assert.That(second, Is.Not.EqualTo(Entity.Null));
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(world.Get<ActivityInstanceCm>(first).InstanceId,
                Is.Not.EqualTo(world.Get<ActivityInstanceCm>(second).InstanceId));
        }

        [Test]
        public void PendingDedupeActivity_OffersSingleInstanceUntilResolved()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.dedupe", new ActivityDefinition
            {
                Id = "activity.dedupe",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                RepeatPolicy = ActivityRepeatPolicy.PendingDedupe,
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
            Entity scope = world.Create();

            Entity first = runtime.OfferOrActivate("activity.dedupe", scope);
            Entity second = runtime.OfferOrActivate("activity.dedupe", scope);
            Assert.That(second, Is.EqualTo(first));

            runtime.ResolveOption(first, "ok");
            Entity third = runtime.OfferOrActivate("activity.dedupe", scope);
            Assert.That(third, Is.Not.EqualTo(Entity.Null));
            Assert.That(third, Is.Not.EqualTo(first));
        }

        [Test]
        public void UniqueActivity_RejectsOfferAfterResolution()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.unique", new ActivityDefinition
            {
                Id = "activity.unique",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                RepeatPolicy = ActivityRepeatPolicy.Unique,
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
            Entity scope = world.Create();

            Entity first = runtime.OfferOrActivate("activity.unique", scope);
            Assert.That(first, Is.Not.EqualTo(Entity.Null));
            Entity whilePending = runtime.OfferOrActivate("activity.unique", scope);
            Assert.That(whilePending, Is.EqualTo(first));

            runtime.ResolveOption(first, "ok");
            Entity rejected = runtime.OfferOrActivate("activity.unique", scope);
            Assert.That(rejected, Is.EqualTo(Entity.Null));
            Assert.That(runtime.CaptureViews(), Has.Count.EqualTo(1));
        }

        [Test]
        public void AdmissionRejectionCue_CarriesDefinitionScopeAndReason()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.unique", new ActivityDefinition
            {
                Id = "activity.unique",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                RepeatPolicy = ActivityRepeatPolicy.Unique,
                Options =
                {
                    new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                },
            });

            var presentation = new ActivityPresentationBuffer();
            var runtime = new ActivityRuntimeService(world, definitions, services, presentation);
            Entity scope = world.Create();

            Entity first = runtime.OfferOrActivate("activity.unique", scope);
            runtime.ResolveOption(first, "ok");
            ActivityAdmissionResult result = runtime.OfferOrActivateChecked("activity.unique", scope);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Instance, Is.EqualTo(Entity.Null));
            Assert.That(result.RejectionCode, Is.EqualTo(ActivityAdmissionRejections.UniqueAlreadyResolved));
            Assert.That(presentation.Cues, Has.Some.Matches<ActivityPresentationCue>(c =>
                c.Kind == ActivityPresentationCueKind.AdmissionRejected &&
                c.ActivityId == "activity.unique" &&
                c.ScopeKey == scope.Id &&
                c.Reason == ActivityAdmissionRejections.UniqueAlreadyResolved));
        }

        [Test]
        public void TriggerConditionFailure_EmitsAdmissionRejectionCue()
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
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                TriggerCondition = new ActivityConditionRef { ConditionKey = "fixture.always_false" },
                Options =
                {
                    new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                },
            });

            var presentation = new ActivityPresentationBuffer();
            var runtime = new ActivityRuntimeService(world, definitions, services, presentation);
            Entity scope = world.Create();

            Entity offer = runtime.OfferOrActivate("activity.gated", scope);
            Assert.That(offer, Is.EqualTo(Entity.Null));
            Assert.That(presentation.Cues, Has.Some.Matches<ActivityPresentationCue>(c =>
                c.Kind == ActivityPresentationCueKind.AdmissionRejected &&
                c.ActivityId == "activity.gated" &&
                c.ScopeKey == scope.Id &&
                c.Reason == ActivityAdmissionRejections.TriggerConditionFailed));
        }

        [Test]
        public void CooldownActivity_RetryWithinWindow_RejectsWithoutEntity()
        {
            using World world = World.Create();
            var clock = new DiscreteClock();
            var services = CreateServices();
            var definitions = CreateCooldownDefinitions();

            var presentation = new ActivityPresentationBuffer();
            var runtime = new ActivityRuntimeService(world, definitions, services, presentation, clock);
            Entity scope = world.Create();

            Entity first = runtime.OfferOrActivate("activity.supply", scope);
            Assert.That(first, Is.Not.EqualTo(Entity.Null));
            Assert.That(world.Get<ActivityInstanceCm>(first).DispatchTick, Is.EqualTo(0));

            clock.Advance(ClockDomainId.Step, ticks: 2);
            ActivityAdmissionResult within = runtime.OfferOrActivateChecked("activity.supply", scope);
            Assert.That(within.Accepted, Is.False);
            Assert.That(within.Instance, Is.EqualTo(Entity.Null));
            Assert.That(within.RejectionCode, Is.EqualTo(ActivityAdmissionRejections.CooldownActive));
            Assert.That(runtime.CaptureViews(), Has.Count.EqualTo(1));
            Assert.That(presentation.Cues, Has.Some.Matches<ActivityPresentationCue>(c =>
                c.Kind == ActivityPresentationCueKind.AdmissionRejected &&
                c.ActivityId == "activity.supply" &&
                c.ScopeKey == scope.Id &&
                c.Reason == ActivityAdmissionRejections.CooldownActive));
        }

        [Test]
        public void CooldownActivity_RetryAfterWindowElapses_AdmitsNewInstance()
        {
            using World world = World.Create();
            var clock = new DiscreteClock();
            var services = CreateServices();
            var definitions = CreateCooldownDefinitions();

            var runtime = new ActivityRuntimeService(
                world,
                definitions,
                services,
                new ActivityPresentationBuffer(),
                clock);
            Entity scope = world.Create();

            Entity first = runtime.OfferOrActivate("activity.supply", scope);
            clock.Advance(ClockDomainId.Step, ticks: 3);

            Entity second = runtime.OfferOrActivate("activity.supply", scope);
            Assert.That(second, Is.Not.EqualTo(Entity.Null));
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(world.Get<ActivityInstanceCm>(second).DispatchTick, Is.EqualTo(3));
        }

        [Test]
        public void CooldownActivity_WithoutClock_FailsFast()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = CreateCooldownDefinitions();
            var runtime = new ActivityRuntimeService(
                world,
                definitions,
                services,
                new ActivityPresentationBuffer());

            Assert.Throws<InvalidOperationException>(() =>
                runtime.OfferOrActivate("activity.supply", world.Create()));
        }

        [Test]
        public void CooldownPolicy_WithoutCooldownConfig_FailsRegistration()
        {
            var definitions = new ActivityDefinitionRegistry();
            Assert.Throws<InvalidOperationException>(() =>
                definitions.Register("activity.bad", new ActivityDefinition
                {
                    Id = "activity.bad",
                    SourceKey = "fixture.signal_ping",
                    RepeatPolicy = ActivityRepeatPolicy.Cooldown,
                    Options =
                    {
                        new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                    },
                }));
        }

        [Test]
        public void CooldownPolicy_WithNonPositiveDuration_FailsRegistration()
        {
            var definitions = new ActivityDefinitionRegistry();
            Assert.Throws<InvalidOperationException>(() =>
                definitions.Register("activity.bad", new ActivityDefinition
                {
                    Id = "activity.bad",
                    SourceKey = "fixture.signal_ping",
                    RepeatPolicy = ActivityRepeatPolicy.Cooldown,
                    RepeatCooldown = new ActivityRepeatCooldown { DurationTicks = 0 },
                    Options =
                    {
                        new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                    },
                }));
        }

        [Test]
        public void RepeatCooldownWithoutCooldownPolicy_FailsRegistration()
        {
            var definitions = new ActivityDefinitionRegistry();
            Assert.Throws<InvalidOperationException>(() =>
                definitions.Register("activity.bad", new ActivityDefinition
                {
                    Id = "activity.bad",
                    SourceKey = "fixture.signal_ping",
                    RepeatPolicy = ActivityRepeatPolicy.Repeatable,
                    RepeatCooldown = new ActivityRepeatCooldown { DurationTicks = 3 },
                    Options =
                    {
                        new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                    },
                }));
        }

        [Test]
        public void MutexActivity_OccupiedGroup_RejectsWithGroupInReason()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = CreateMutexDefinitions();

            var presentation = new ActivityPresentationBuffer();
            var runtime = new ActivityRuntimeService(world, definitions, services, presentation);
            Entity scope = world.Create();

            Entity first = runtime.OfferOrActivate("activity.crisis_a", scope);
            Assert.That(first, Is.Not.EqualTo(Entity.Null));

            ActivityAdmissionResult blocked = runtime.OfferOrActivateChecked("activity.crisis_b", scope);
            Assert.That(blocked.Accepted, Is.False);
            Assert.That(blocked.Instance, Is.EqualTo(Entity.Null));
            Assert.That(blocked.RejectionCode, Does.StartWith(ActivityAdmissionRejections.MutexOccupied));
            Assert.That(blocked.RejectionCode, Does.Contain("crisis"));
            Assert.That(runtime.CaptureViews(), Has.Count.EqualTo(1));
            Assert.That(presentation.Cues, Has.Some.Matches<ActivityPresentationCue>(c =>
                c.Kind == ActivityPresentationCueKind.AdmissionRejected &&
                c.ActivityId == "activity.crisis_b" &&
                c.ScopeKey == scope.Id &&
                c.Reason == $"{ActivityAdmissionRejections.MutexOccupied}:crisis"));
        }

        [Test]
        public void MutexActivity_GroupReleases_AfterOccupantResolves()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = CreateMutexDefinitions();

            var runtime = new ActivityRuntimeService(
                world,
                definitions,
                services,
                new ActivityPresentationBuffer());
            Entity scope = world.Create();

            Entity first = runtime.OfferOrActivate("activity.crisis_a", scope);
            runtime.ResolveOption(first, "ok");

            Entity second = runtime.OfferOrActivate("activity.crisis_b", scope);
            Assert.That(second, Is.Not.EqualTo(Entity.Null));
            Assert.That(second, Is.Not.EqualTo(first));
        }

        [Test]
        public void MutexActivity_OtherScope_NotBlockedByOccupiedGroup()
        {
            using World world = World.Create();
            var services = CreateServices();
            var definitions = CreateMutexDefinitions();

            var runtime = new ActivityRuntimeService(
                world,
                definitions,
                services,
                new ActivityPresentationBuffer());
            Entity scopeA = world.Create();
            Entity scopeB = world.Create();

            runtime.OfferOrActivate("activity.crisis_a", scopeA);
            Entity other = runtime.OfferOrActivate("activity.crisis_b", scopeB);
            Assert.That(other, Is.Not.EqualTo(Entity.Null));
        }

        [Test]
        public void MutexPolicy_WithoutGroup_FailsRegistration()
        {
            var definitions = new ActivityDefinitionRegistry();
            Assert.Throws<InvalidOperationException>(() =>
                definitions.Register("activity.bad", new ActivityDefinition
                {
                    Id = "activity.bad",
                    SourceKey = "fixture.signal_ping",
                    RepeatPolicy = ActivityRepeatPolicy.Mutex,
                    Options =
                    {
                        new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                    },
                }));
        }

        [Test]
        public void MutexGroupWithoutMutexPolicy_FailsRegistration()
        {
            var definitions = new ActivityDefinitionRegistry();
            Assert.Throws<InvalidOperationException>(() =>
                definitions.Register("activity.bad", new ActivityDefinition
                {
                    Id = "activity.bad",
                    SourceKey = "fixture.signal_ping",
                    RepeatPolicy = ActivityRepeatPolicy.Unique,
                    MutexGroup = "crisis",
                    Options =
                    {
                        new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                    },
                }));
        }

        private static ActivityDefinitionRegistry CreateCooldownDefinitions()
        {
            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.supply", new ActivityDefinition
            {
                Id = "activity.supply",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                RepeatPolicy = ActivityRepeatPolicy.Cooldown,
                RepeatCooldown = new ActivityRepeatCooldown { DurationTicks = 3 },
                Options =
                {
                    new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                },
            });
            return definitions;
        }

        private static ActivityDefinitionRegistry CreateMutexDefinitions()
        {
            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.crisis_a", new ActivityDefinition
            {
                Id = "activity.crisis_a",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                RepeatPolicy = ActivityRepeatPolicy.Mutex,
                MutexGroup = "crisis",
                Options =
                {
                    new ActivityOptionDefinition { Id = "ok", IsBaseline = true },
                },
            });
            definitions.Register("activity.crisis_b", new ActivityDefinition
            {
                Id = "activity.crisis_b",
                SourceKey = "fixture.signal_ping",
                DispatchPolicy = ActivityDispatchPolicy.Forced,
                RepeatPolicy = ActivityRepeatPolicy.Mutex,
                MutexGroup = "crisis",
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

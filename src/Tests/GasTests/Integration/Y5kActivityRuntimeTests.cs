using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Providers.FixtureProviders;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class Y5kActivityRuntimeTests
    {
        [Test]
        public void ForcedActivity_PresentsAndResolvesBaselineOption()
        {
            using World world = World.Create();
            var services = new ProviderServices(registerDefaultGaps: false, allowTestDomainOverride: true);
            FixtureProviderInstaller.InstallMinimal(services);
            var effect = new FixtureEffectHandler();
            services.Effects.Register(
                "fixture.settle_choice",
                effect,
                new ProviderParameterSchema(new[]
                {
                    new ProviderParameterField("note", ProviderParameterKind.String, required: false),
                }));

            var definitions = new ActivityDefinitionRegistry();
            definitions.Register("activity.supply_strain", new ActivityDefinition
            {
                Id = "activity.supply_strain",
                DisplayName = "Supply Strain",
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

            Entity activity = runtime.OfferOrActivate("activity.supply_strain", scope);
            Assert.That(activity, Is.Not.EqualTo(Entity.Null));
            Assert.That(runtime.TryGetState(activity, out ActivityInstanceState state, out string id), Is.True);
            Assert.That(state, Is.EqualTo(ActivityInstanceState.Active));
            Assert.That(id, Is.EqualTo("activity.supply_strain"));
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
            var services = new ProviderServices(registerDefaultGaps: false, allowTestDomainOverride: true);
            FixtureProviderInstaller.InstallMinimal(services);
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
            var services = new ProviderServices(registerDefaultGaps: false, allowTestDomainOverride: true);
            FixtureProviderInstaller.InstallMinimal(services);
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
        public void DefinitionWithoutBaseline_FailsRegistration()
        {
            var definitions = new ActivityDefinitionRegistry();
            Assert.Throws<System.InvalidOperationException>(() =>
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
    }
}

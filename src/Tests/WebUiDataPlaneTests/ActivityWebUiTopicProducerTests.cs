using System.Text.Json;
using Arch.Core;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Providers.FixtureProviders;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class ActivityWebUiTopicProducerTests
{
	private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

	[Test]
	public void ForcedActivitySnapshot_CarriesOptionsBaselineAndBlockedReason()
	{
		using World world = World.Create();
		ActivityRuntimeService runtime = CreateRuntime(world, out _);
		var producer = new ActivityWebUiTopicProducer(
			"panel-kit.sample.activity",
			runtime,
			ActivityPanelProfile.CreateGeneric());

		runtime.OfferOrActivate("forced.blocked", world.Create());
		ActivityWebSnapshot snapshot = producer.BuildSnapshot();

		Assert.That(snapshot.Activities, Has.Length.EqualTo(1));
		ActivityWebRow row = snapshot.Activities[0];
		Assert.That(row.ActivityId, Is.EqualTo("forced.blocked"));
		Assert.That(row.State, Is.EqualTo("Active"));
		Assert.That(row.DispatchPolicy, Is.EqualTo("Forced"));
		Assert.That(row.Options, Has.Length.EqualTo(2));
		Assert.That(row.Options[0].OptionId, Is.EqualTo("hold"));
		Assert.That(row.Options[0].IsBaseline, Is.True);
		Assert.That(row.Options[0].Executable, Is.True);
		Assert.That(row.Options[1].Executable, Is.False);
		Assert.That(row.Options[1].BlockReason, Does.Contain("fixture.always_false"));
	}

	[Test]
	public void ResolvedActivity_MovesToHistoryWithSelectedOptionId()
	{
		using World world = World.Create();
		ActivityRuntimeService runtime = CreateRuntime(world, out _);
		var producer = new ActivityWebUiTopicProducer(
			"panel-kit.sample.activity",
			runtime,
			ActivityPanelProfile.CreateGeneric(historyLimit: 8));

		Entity activity = runtime.OfferOrActivate("forced.blocked", world.Create());
		runtime.ResolveOption(activity, "hold");
		ActivityWebSnapshot snapshot = producer.BuildSnapshot();

		Assert.That(snapshot.Activities, Has.Length.EqualTo(0), "Resolved instances must leave the choice list.");
		Assert.That(snapshot.History, Has.Length.EqualTo(1));
		Assert.That(snapshot.History[0].SelectedOptionId, Is.EqualTo("hold"));
		Assert.That(snapshot.History[0].Automatic, Is.False);
	}

	[Test]
	public void AutomaticActivity_SettlesIntoHistoryWithAutomaticFlag()
	{
		using World world = World.Create();
		ActivityRuntimeService runtime = CreateRuntime(world, out _);
		var producer = new ActivityWebUiTopicProducer(
			"panel-kit.sample.activity",
			runtime,
			ActivityPanelProfile.CreateGeneric());

		runtime.OfferOrActivate("auto.report", world.Create());
		ActivityWebSnapshot snapshot = producer.BuildSnapshot();

		Assert.That(snapshot.Activities, Has.Length.EqualTo(0));
		Assert.That(snapshot.History, Has.Length.EqualTo(1));
		Assert.That(snapshot.History[0].Automatic, Is.True);
		Assert.That(snapshot.History[0].SelectedOptionId, Is.EqualTo(string.Empty));
	}

	[Test]
	public void CueWindow_RidesTheSnapshot_AndSerializesAsJson()
	{
		using World world = World.Create();
		ActivityRuntimeService runtime = CreateRuntime(world, out _);
		var producer = new ActivityWebUiTopicProducer(
			"panel-kit.sample.activity",
			runtime,
			ActivityPanelProfile.CreateGeneric());

		Entity cooldownScope = world.Create();
		Entity first = runtime.OfferOrActivate("cooldown.rejected", cooldownScope);
		runtime.ResolveOption(first, "hold");
		runtime.OfferOrActivate("cooldown.rejected", cooldownScope);
		ActivityWebSnapshot snapshot = producer.BuildSnapshot();

		Assert.That(snapshot.Cues, Is.Not.Empty);
		Assert.That(snapshot.Cues[^1].Kind, Is.EqualTo("AdmissionRejected"));
		Assert.That(snapshot.Cues[^1].Reason, Does.Contain("admission.cooldown_active"));

		string json = JsonSerializer.Serialize(snapshot, WebJson);
		Assert.That(json, Does.Contain("\"activities\""));
		Assert.That(json, Does.Contain("\"history\""));
		Assert.That(json, Does.Contain("\"cues\""));
	}

	[Test]
	public void ScopeFilter_KeepsOnlyOwnerScopedInstances()
	{
		using World world = World.Create();
		ActivityRuntimeService runtime = CreateRuntime(world, out _);
		Entity owner = world.Create();
		runtime.OfferOrActivate("forced.blocked", owner);
		runtime.OfferOrActivate("forced.blocked", world.Create());

		var scoped = new ActivityWebUiTopicProducer(
			"panel-kit.sample.activity",
			runtime,
			ActivityPanelProfile.CreateGeneric(),
			ownerScope: owner,
			filterByOwnerScope: true);
		ActivityWebSnapshot snapshot = scoped.BuildSnapshot();

		Assert.That(snapshot.Activities, Has.Length.EqualTo(1));
		Assert.That(snapshot.OwnerEntityId, Is.EqualTo(owner.Id));
	}

	[Test]
	public void UnknownAllowListActivity_FailsSnapshotBuild()
	{
		using World world = World.Create();
		ActivityRuntimeService runtime = CreateRuntime(world, out _);
		var producer = new ActivityWebUiTopicProducer(
			"panel-kit.sample.activity",
			runtime,
			new ActivityPanelProfile(
				"profile.activity.generic",
				[ActivityInstanceState.Active],
				ActivityPanelSortKey.ActivityIdAscending,
				allowedActivityIds: ["no.such.activity"]));

		Assert.That(() => producer.BuildSnapshot(), Throws.InvalidOperationException.With.Message.Contain("no.such.activity"));
	}

	private static ActivityRuntimeService CreateRuntime(World world, out ActivityDefinitionRegistry definitions)
	{
		ActivityRuntimeService? created = null;
		ActivityRuntimeService Create() => created!;

		definitions = new ActivityDefinitionRegistry();
		definitions.Register("forced.blocked", new ActivityDefinition
		{
			SourceKey = "fixture.signal_ping",
			DispatchPolicy = ActivityDispatchPolicy.Forced,
			Options =
			{
				new ActivityOptionDefinition { Id = "hold", Title = "按兵不动", IsBaseline = true },
				new ActivityOptionDefinition
				{
					Id = "push",
					Title = "推进",
							ExecuteCondition = new ActivityConditionRef
					{
						ConditionKey = "fixture.always_false",
					},
				},
			},
		});
		definitions.Register("auto.report", new ActivityDefinition
		{
			SourceKey = "fixture.signal_ping",
			DispatchPolicy = ActivityDispatchPolicy.Automatic,
			AutomaticEffects = { new ActivityEffectRef { EffectKey = "fixture.noop" } },
		});
		definitions.Register("cooldown.rejected", new ActivityDefinition
		{
			SourceKey = "fixture.signal_ping",
			DispatchPolicy = ActivityDispatchPolicy.Forced,
			RepeatPolicy = ActivityRepeatPolicy.Cooldown,
			RepeatCooldown = new ActivityRepeatCooldown { DurationTicks = 100 },
			Options = { new ActivityOptionDefinition { Id = "hold", IsBaseline = true } },
		});

		var providers = new ProviderServices();
		FixtureProviderInstaller.InstallMinimal(providers);
		providers.Conditions.Register(
			"fixture.always_false",
			new Ludots.Core.Gameplay.Providers.FixtureProviders.FixtureConditionProvider(false),
			ProviderParameterSchema.Empty);
		var clock = new FixedTickClock();
		created = new ActivityRuntimeService(
			world,
			definitions,
			providers,
			new ActivityPresentationBuffer(),
			clock);
		return created;
	}

	private sealed class FixedTickClock : Ludots.Core.Engine.IClock
	{
		private int _now;
		public int Now(Ludots.Core.Engine.ClockDomainId domain) => _now;
		public void Advance(Ludots.Core.Engine.ClockDomainId domain, int ticks = 1) => _now += ticks;
	}
}

using System.Text.Json;
using Arch.Core;
using Ludots.Core.Gameplay.Quests;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class QuestObjectiveWebUiTopicProducerTests
{
	private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

	[Test]
	public void StageChange_IncrementsSnapshotRevision_AndUpdatesObjectiveText()
	{
		using World world = World.Create();
		QuestDefinitionRegistry definitions = CreateDefinitions();
		var runtime = new QuestRuntimeService(world, definitions);
		var producer = new QuestObjectiveWebUiTopicProducer(
			"panel-kit.sample.objective",
			runtime,
			QuestObjectivePanelProfile.CreateGeneric());

		runtime.StartQuest("alpha");
		QuestObjectiveWebSnapshot before = producer.BuildSnapshot();
		Assert.That(before.Quests, Has.Length.EqualTo(1));
		Assert.That(before.Quests[0].StageId, Is.EqualTo("start"));
		Assert.That(before.Quests[0].ObjectiveText, Is.EqualTo("Begin the alpha trial."));
		uint revisionBefore = before.Revision;

		runtime.AdvanceQuestStage("alpha", "mid");
		QuestObjectiveWebSnapshot after = producer.BuildSnapshot();

		Assert.That(after.Quests, Has.Length.EqualTo(1));
		Assert.That(after.Quests[0].StageId, Is.EqualTo("mid"));
		Assert.That(after.Quests[0].ObjectiveText, Is.EqualTo("Reach the mid checkpoint."));
		Assert.That(after.Quests[0].QuestRevision, Is.GreaterThan(before.Quests[0].QuestRevision));
		Assert.That(after.Revision, Is.Not.EqualTo(revisionBefore));
	}

	[Test]
	public void MultipleActiveQuests_AreFilteredAndSortedByProfile()
	{
		using World world = World.Create();
		QuestDefinitionRegistry definitions = CreateDefinitions();
		var runtime = new QuestRuntimeService(world, definitions);
		runtime.StartQuest("alpha");
		runtime.StartQuest("bravo");
		runtime.StartQuest("charlie");

		var filtered = new QuestObjectiveWebUiTopicProducer(
			"topic.objective.filtered",
			runtime,
			new QuestObjectivePanelProfile(
				"profile.objective.generic",
				[QuestState.Active],
				QuestObjectiveSortKey.AllowListOrder,
				allowedQuestIds: ["charlie", "alpha"]));

		QuestObjectiveWebSnapshot snapshot = filtered.BuildSnapshot();
		Assert.That(snapshot.Quests.Select(q => q.QuestId), Is.EqualTo(new[] { "charlie", "alpha" }));

		var byName = new QuestObjectiveWebUiTopicProducer(
			"topic.objective.by-name",
			runtime,
			QuestObjectivePanelProfile.CreateGeneric(QuestObjectiveSortKey.DisplayNameAscending));
		QuestObjectiveWebSnapshot named = byName.BuildSnapshot();
		Assert.That(named.Quests.Select(q => q.QuestId), Is.EqualTo(new[] { "alpha", "bravo", "charlie" }));
		Assert.That(named.Quests.Select(q => q.DisplayName), Is.EqualTo(new[] { "Alpha Quest", "Bravo Quest", "Charlie Quest" }));
	}

	[Test]
	public void RequiredTags_FilterActiveQuests()
	{
		using World world = World.Create();
		QuestDefinitionRegistry definitions = CreateDefinitions();
		var runtime = new QuestRuntimeService(world, definitions);
		runtime.StartQuest("alpha");
		runtime.StartQuest("bravo");

		var producer = new QuestObjectiveWebUiTopicProducer(
			"topic.objective.tags",
			runtime,
			QuestObjectivePanelProfile.CreateGeneric(requiredTags: ["quest.side"]));

		QuestObjectiveWebSnapshot snapshot = producer.BuildSnapshot();
		Assert.That(snapshot.Quests.Select(q => q.QuestId), Is.EqualTo(new[] { "bravo" }));
	}

	[Test]
	public void MissingQuestDefinition_FailsFastWithConcreteId()
	{
		using World world = World.Create();
		var runtime = new QuestRuntimeService(world, CreateDefinitions());
		var producer = new QuestObjectiveWebUiTopicProducer(
			"topic.objective",
			runtime,
			QuestObjectivePanelProfile.CreateGeneric(allowedQuestIds: ["missing-quest"]));

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => producer.BuildSnapshot())!;
		Assert.That(ex.Message, Does.Contain("missing-quest"));
		Assert.That(ex.Message, Does.Contain("not registered"));
	}

	[Test]
	public void MissingStage_FailsFastWithConcreteIds()
	{
		using World world = World.Create();
		QuestDefinitionRegistry definitions = CreateDefinitions();
		var runtime = new QuestRuntimeService(world, definitions);
		Entity questEntity = runtime.StartQuest("alpha");
		ref QuestInstanceCm quest = ref world.Get<QuestInstanceCm>(questEntity);
		quest.StageIndex = -1;

		var producer = new QuestObjectiveWebUiTopicProducer(
			"topic.objective",
			runtime,
			QuestObjectivePanelProfile.CreateGeneric());

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => producer.BuildSnapshot())!;
		Assert.That(ex.Message, Does.Contain("alpha"));
		Assert.That(ex.Message, Does.Contain("stage"));
	}

	[Test]
	public void MissingObjectiveText_FailsFastWithConcreteIds()
	{
		using World world = World.Create();
		var definitions = new QuestDefinitionRegistry();
		definitions.Register("blank", new QuestDefinition
		{
			DisplayName = "Blank",
			Summary = "No objective copy.",
			Stages =
			{
				new QuestStageDefinition { Id = "only", Title = "Only" }
			}
		});
		var runtime = new QuestRuntimeService(world, definitions);
		runtime.StartQuest("blank");
		var producer = new QuestObjectiveWebUiTopicProducer(
			"topic.objective",
			runtime,
			QuestObjectivePanelProfile.CreateGeneric());

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => producer.BuildSnapshot())!;
		Assert.That(ex.Message, Does.Contain("blank"));
		Assert.That(ex.Message, Does.Contain("only"));
		Assert.That(ex.Message, Does.Contain("objective text"));
	}

	[Test]
	public void MissingObjectiveTextToken_FailsFastWithConcreteTokenId()
	{
		using World world = World.Create();
		var definitions = new QuestDefinitionRegistry();
		definitions.Register("tokened", new QuestDefinition
		{
			DisplayName = "Tokened",
			Summary = "Uses a text token.",
			Stages =
			{
				new QuestStageDefinition
				{
					Id = "start",
					Title = "Start",
					ObjectiveTextToken = "quest.tokened.start"
				}
			}
		});
		var runtime = new QuestRuntimeService(world, definitions);
		runtime.StartQuest("tokened");

		var withoutHook = new QuestObjectiveWebUiTopicProducer(
			"topic.objective",
			runtime,
			QuestObjectivePanelProfile.CreateGeneric(),
			new QuestObjectiveTextValidator());
		InvalidOperationException noHook = Assert.Throws<InvalidOperationException>(() => withoutHook.BuildSnapshot())!;
		Assert.That(noHook.Message, Does.Contain("quest.tokened.start"));
		Assert.That(noHook.Message, Does.Contain("WPK-5"));

		var withMissingToken = new QuestObjectiveWebUiTopicProducer(
			"topic.objective",
			runtime,
			QuestObjectivePanelProfile.CreateGeneric(),
			new QuestObjectiveTextValidator(token => false));
		InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() => withMissingToken.BuildSnapshot())!;
		Assert.That(missing.Message, Does.Contain("quest.tokened.start"));
		Assert.That(missing.Message, Does.Contain("not registered"));
	}

	[Test]
	public void RegisteredObjectiveTextToken_ProjectsTokenReference()
	{
		using World world = World.Create();
		var definitions = new QuestDefinitionRegistry();
		definitions.Register("tokened", new QuestDefinition
		{
			DisplayName = "Tokened",
			Summary = "Uses a text token.",
			Stages =
			{
				new QuestStageDefinition
				{
					Id = "start",
					Title = "Start",
					ObjectiveTextToken = "quest.tokened.start"
				}
			}
		});
		var runtime = new QuestRuntimeService(world, definitions);
		runtime.StartQuest("tokened");
		var producer = new QuestObjectiveWebUiTopicProducer(
			"topic.objective",
			runtime,
			QuestObjectivePanelProfile.CreateGeneric(),
			new QuestObjectiveTextValidator(token => token == "quest.tokened.start"));

		QuestObjectiveWebSnapshot snapshot = producer.BuildSnapshot();
		Assert.That(snapshot.Quests, Has.Length.EqualTo(1));
		Assert.That(snapshot.Quests[0].ObjectiveTextToken, Is.EqualTo("quest.tokened.start"));
	}

	[Test]
	public void TryCreateSnapshot_EmitsJsonPacket_WithoutNarrativeDirectorDependency()
	{
		using World world = World.Create();
		var runtime = new QuestRuntimeService(world, CreateDefinitions());
		runtime.StartQuest("alpha");
		var producer = new QuestObjectiveWebUiTopicProducer(
			"panel-kit.sample.objective",
			runtime,
			QuestObjectivePanelProfile.CreateGeneric());
		var context = new WebUiTopicContext("session-a", producer.Topic, 9, JsonSerializer.SerializeToElement(new { }));

		Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
		Assert.That(packet.ContentType, Is.EqualTo(QuestObjectiveWebUiTopicProducer.JsonContentType));
		Assert.That(packet.Delivery, Is.EqualTo(WebUiDeliverySemantics.LatestWins));

		using JsonDocument document = JsonDocument.Parse(packet.Payload);
		Assert.That(document.RootElement.GetProperty("profileId").GetString(), Is.EqualTo("profile.objective.generic"));
		Assert.That(document.RootElement.GetProperty("quests")[0].GetProperty("questId").GetString(), Is.EqualTo("alpha"));

		AssertObjectiveSourceHasNoNarrativeDirectorReference();
	}

	[Test]
	public void OwnerScopeFilter_KeepsOnlyMatchingScopeHost()
	{
		using World world = World.Create();
		Entity host = world.Create();
		var runtime = new QuestRuntimeService(world, CreateDefinitions());
		runtime.StartQuest("alpha", host);
		runtime.StartQuest("bravo");

		var producer = new QuestObjectiveWebUiTopicProducer(
			"topic.objective.scoped",
			runtime,
			QuestObjectivePanelProfile.CreateGeneric(),
			ownerScope: host,
			filterByOwnerScope: true);

		QuestObjectiveWebSnapshot snapshot = producer.BuildSnapshot();
		Assert.That(snapshot.Quests.Select(q => q.QuestId), Is.EqualTo(new[] { "alpha" }));
		Assert.That(snapshot.OwnerEntityId, Is.EqualTo(host.Id));
	}

	private static void AssertObjectiveSourceHasNoNarrativeDirectorReference()
	{
		string[] roots =
		[
			Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "Libraries", "Ludots.WebUI.DataPlane")),
			Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "Libraries", "Ludots.WebUI.PanelKit"))
		];

		string[] files =
		[
			Path.Combine(roots[0], "QuestObjectiveWebUiTopicProducer.cs"),
			Path.Combine(roots[0], "QuestObjectivePanelProfile.cs"),
			Path.Combine(roots[0], "QuestObjectiveTextValidator.cs"),
			Path.Combine(roots[1], "WebUiQuestObjectivePanelDescriptors.cs")
		];

		foreach (string file in files)
		{
			Assert.That(File.Exists(file), Is.True, $"Expected source file '{file}'.");
			string text = File.ReadAllText(file);
			Assert.That(text, Does.Not.Contain("using Ludots.Core.Gameplay.Narrative"),
				$"Objective source '{file}' must not import Narrative namespace.");
			Assert.That(text, Does.Not.Match(@"\bNarrativeDirector\b"),
				$"Objective source '{file}' must not reference NarrativeDirector as a type.");
		}
	}

	private static QuestDefinitionRegistry CreateDefinitions()
	{
		var definitions = new QuestDefinitionRegistry();
		definitions.Register("alpha", new QuestDefinition
		{
			DisplayName = "Alpha Quest",
			Summary = "First objective.",
			Tags = { "quest.main" },
			Stages =
			{
				new QuestStageDefinition
				{
					Id = "start",
					Title = "Start",
					ObjectiveText = "Begin the alpha trial.",
					ObjectiveHint = "Talk to the guide."
				},
				new QuestStageDefinition
				{
					Id = "mid",
					Title = "Mid",
					ObjectiveText = "Reach the mid checkpoint.",
					ObjectiveHint = "Follow the markers."
				},
				new QuestStageDefinition
				{
					Id = "end",
					Title = "End",
					ObjectiveText = "Finish the alpha trial."
				}
			}
		});
		definitions.Register("bravo", new QuestDefinition
		{
			DisplayName = "Bravo Quest",
			Summary = "Side objective.",
			Tags = { "quest.side" },
			Stages =
			{
				new QuestStageDefinition
				{
					Id = "start",
					Title = "Start",
					ObjectiveText = "Complete the bravo errand."
				}
			}
		});
		definitions.Register("charlie", new QuestDefinition
		{
			DisplayName = "Charlie Quest",
			Summary = "Another objective.",
			Tags = { "quest.main" },
			Stages =
			{
				new QuestStageDefinition
				{
					Id = "start",
					Title = "Start",
					ObjectiveText = "Complete the charlie errand."
				}
			}
		});
		return definitions;
	}
}

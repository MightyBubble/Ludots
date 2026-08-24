using System.Text.Json;
using Arch.Core;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Tasks;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class TaskObjectiveWebUiTopicProducerTests
{
	private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

	[Test]
	public void ObjectiveCompletion_ChangesCurrentObjective_AndIncrementsSnapshotRevision()
	{
		using World world = World.Create();
		TaskDefinitionRegistry definitions = CreateDefinitions();
		var runtime = CreateRuntime(world, definitions);
		var producer = new TaskObjectiveWebUiTopicProducer(
			"panel-kit.sample.objective",
			runtime,
			TaskObjectivePanelProfile.CreateGeneric());

		runtime.OfferOrStart("alpha");
		TaskObjectiveWebSnapshot before = producer.BuildSnapshot();
		Assert.That(before.Tasks, Has.Length.EqualTo(1));
		Assert.That(before.Tasks[0].ObjectiveId, Is.EqualTo("start"));
		Assert.That(before.Tasks[0].ObjectiveText, Is.EqualTo("Begin the alpha trial."));
		uint revisionBefore = before.Revision;

		runtime.EmitSignal("alpha.start.done");
		TaskObjectiveWebSnapshot after = producer.BuildSnapshot();

		Assert.That(after.Tasks, Has.Length.EqualTo(1));
		Assert.That(after.Tasks[0].ObjectiveId, Is.EqualTo("mid"));
		Assert.That(after.Tasks[0].ObjectiveText, Is.EqualTo("Reach the mid checkpoint."));
		Assert.That(after.Revision, Is.Not.EqualTo(revisionBefore));
	}

	[Test]
	public void MultipleActiveTasks_AreFilteredAndSortedByProfile()
	{
		using World world = World.Create();
		TaskDefinitionRegistry definitions = CreateDefinitions();
		var runtime = CreateRuntime(world, definitions);
		runtime.OfferOrStart("alpha");
		runtime.OfferOrStart("bravo");
		runtime.OfferOrStart("charlie");

		var filtered = new TaskObjectiveWebUiTopicProducer(
			"topic.objective.filtered",
			runtime,
			new TaskObjectivePanelProfile(
				"profile.objective.generic",
				[TaskInstanceState.Active],
				TaskObjectiveSortKey.AllowListOrder,
				allowedTaskIds: ["charlie", "alpha"]));

		TaskObjectiveWebSnapshot snapshot = filtered.BuildSnapshot();
		Assert.That(snapshot.Tasks.Select(t => t.TaskId), Is.EqualTo(new[] { "charlie", "alpha" }));

		var byName = new TaskObjectiveWebUiTopicProducer(
			"topic.objective.by-name",
			runtime,
			TaskObjectivePanelProfile.CreateGeneric(TaskObjectiveSortKey.DisplayNameAscending));
		TaskObjectiveWebSnapshot named = byName.BuildSnapshot();
		Assert.That(named.Tasks.Select(t => t.TaskId), Is.EqualTo(new[] { "alpha", "bravo", "charlie" }));
		Assert.That(named.Tasks.Select(t => t.DisplayName), Is.EqualTo(new[] { "Alpha Task", "Bravo Task", "Charlie Task" }));
	}

	[Test]
	public void RequiredTags_FilterActiveTasks()
	{
		using World world = World.Create();
		TaskDefinitionRegistry definitions = CreateDefinitions();
		var runtime = CreateRuntime(world, definitions);
		runtime.OfferOrStart("alpha");
		runtime.OfferOrStart("bravo");

		var producer = new TaskObjectiveWebUiTopicProducer(
			"topic.objective.tags",
			runtime,
			TaskObjectivePanelProfile.CreateGeneric(requiredTags: ["task.side"]));

		TaskObjectiveWebSnapshot snapshot = producer.BuildSnapshot();
		Assert.That(snapshot.Tasks.Select(t => t.TaskId), Is.EqualTo(new[] { "bravo" }));
	}

	[Test]
	public void MissingTaskDefinition_FailsFastWithConcreteId()
	{
		using World world = World.Create();
		var runtime = CreateRuntime(world, CreateDefinitions());
		var producer = new TaskObjectiveWebUiTopicProducer(
			"topic.objective",
			runtime,
			TaskObjectivePanelProfile.CreateGeneric(allowedTaskIds: ["missing-task"]));

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => producer.BuildSnapshot())!;
		Assert.That(ex.Message, Does.Contain("missing-task"));
		Assert.That(ex.Message, Does.Contain("not registered"));
	}

	[Test]
	public void EmptyObjectives_AreRejectedAtRegistration()
	{
		var definitions = new TaskDefinitionRegistry();
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			definitions.Register("hollow", new TaskDefinition
			{
				DisplayName = "Hollow",
				StartPolicy = TaskStartPolicy.Automatic,
			}))!;
		Assert.That(ex.Message, Does.Contain("hollow"));
		Assert.That(ex.Message, Does.Contain("objective"));
	}

	[Test]
	public void MissingObjectiveText_FailsFastWithConcreteIds()
	{
		using World world = World.Create();
		var definitions = new TaskDefinitionRegistry();
		definitions.Register("blank", new TaskDefinition
		{
			DisplayName = "Blank",
			Summary = "No objective copy.",
			StartPolicy = TaskStartPolicy.Automatic,
			Objectives =
			{
				new TaskObjectiveDefinition { Id = "only", Kind = TaskObjectiveKind.Signal, SignalKey = "blank.never" }
			}
		});
		var runtime = CreateRuntime(world, definitions);
		runtime.OfferOrStart("blank");
		var producer = new TaskObjectiveWebUiTopicProducer(
			"topic.objective",
			runtime,
			TaskObjectivePanelProfile.CreateGeneric());

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => producer.BuildSnapshot())!;
		Assert.That(ex.Message, Does.Contain("blank"));
		Assert.That(ex.Message, Does.Contain("only"));
		Assert.That(ex.Message, Does.Contain("objective text"));
	}

	[Test]
	public void MissingObjectiveTextToken_FailsFastWithConcreteTokenId()
	{
		using World world = World.Create();
		var definitions = new TaskDefinitionRegistry();
		definitions.Register("tokened", new TaskDefinition
		{
			DisplayName = "Tokened",
			Summary = "Uses a text token.",
			StartPolicy = TaskStartPolicy.Automatic,
			Objectives =
			{
				new TaskObjectiveDefinition
				{
					Id = "start",
					Kind = TaskObjectiveKind.Signal,
					SignalKey = "tokened.never",
					TextToken = "task.tokened.start"
				}
			}
		});
		var runtime = CreateRuntime(world, definitions);
		runtime.OfferOrStart("tokened");

		var withoutHook = new TaskObjectiveWebUiTopicProducer(
			"topic.objective",
			runtime,
			TaskObjectivePanelProfile.CreateGeneric(),
			new TaskObjectiveTextValidator());
		InvalidOperationException noHook = Assert.Throws<InvalidOperationException>(() => withoutHook.BuildSnapshot())!;
		Assert.That(noHook.Message, Does.Contain("task.tokened.start"));
		Assert.That(noHook.Message, Does.Contain("WPK-5"));

		var withMissingToken = new TaskObjectiveWebUiTopicProducer(
			"topic.objective",
			runtime,
			TaskObjectivePanelProfile.CreateGeneric(),
			new TaskObjectiveTextValidator(token => false));
		InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() => withMissingToken.BuildSnapshot())!;
		Assert.That(missing.Message, Does.Contain("task.tokened.start"));
		Assert.That(missing.Message, Does.Contain("not registered"));
	}

	[Test]
	public void RegisteredObjectiveTextToken_ProjectsTokenReference()
	{
		using World world = World.Create();
		var definitions = new TaskDefinitionRegistry();
		definitions.Register("tokened", new TaskDefinition
		{
			DisplayName = "Tokened",
			Summary = "Uses a text token.",
			StartPolicy = TaskStartPolicy.Automatic,
			Objectives =
			{
				new TaskObjectiveDefinition
				{
					Id = "start",
					Kind = TaskObjectiveKind.Signal,
					SignalKey = "tokened.never",
					TextToken = "task.tokened.start"
				}
			}
		});
		var runtime = CreateRuntime(world, definitions);
		runtime.OfferOrStart("tokened");
		var producer = new TaskObjectiveWebUiTopicProducer(
			"topic.objective",
			runtime,
			TaskObjectivePanelProfile.CreateGeneric(),
			new TaskObjectiveTextValidator(token => token == "task.tokened.start"));

		TaskObjectiveWebSnapshot snapshot = producer.BuildSnapshot();
		Assert.That(snapshot.Tasks, Has.Length.EqualTo(1));
		Assert.That(snapshot.Tasks[0].ObjectiveTextToken, Is.EqualTo("task.tokened.start"));
	}

	[Test]
	public void TryCreateSnapshot_EmitsJsonPacket_WithoutNarrativeDirectorDependency()
	{
		using World world = World.Create();
		var runtime = CreateRuntime(world, CreateDefinitions());
		runtime.OfferOrStart("alpha");
		var producer = new TaskObjectiveWebUiTopicProducer(
			"panel-kit.sample.objective",
			runtime,
			TaskObjectivePanelProfile.CreateGeneric());
		var context = new WebUiTopicContext("session-a", producer.Topic, 9, JsonSerializer.SerializeToElement(new { }));

		Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
		Assert.That(packet.ContentType, Is.EqualTo(TaskObjectiveWebUiTopicProducer.JsonContentType));
		Assert.That(packet.Delivery, Is.EqualTo(WebUiDeliverySemantics.LatestWins));

		using JsonDocument document = JsonDocument.Parse(packet.Payload);
		Assert.That(document.RootElement.GetProperty("profileId").GetString(), Is.EqualTo("profile.objective.generic"));
		Assert.That(document.RootElement.GetProperty("tasks")[0].GetProperty("taskId").GetString(), Is.EqualTo("alpha"));

		AssertObjectiveSourceHasNoNarrativeDirectorReference();
	}

	[Test]
	public void OwnerScopeFilter_KeepsOnlyMatchingScopeHost()
	{
		using World world = World.Create();
		Entity host = world.Create();
		var runtime = CreateRuntime(world, CreateDefinitions());
		runtime.OfferOrStart("alpha", host);
		runtime.OfferOrStart("bravo");

		var producer = new TaskObjectiveWebUiTopicProducer(
			"topic.objective.scoped",
			runtime,
			TaskObjectivePanelProfile.CreateGeneric(),
			ownerScope: host,
			filterByOwnerScope: true);

		TaskObjectiveWebSnapshot snapshot = producer.BuildSnapshot();
		Assert.That(snapshot.Tasks.Select(t => t.TaskId), Is.EqualTo(new[] { "alpha" }));
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
			Path.Combine(roots[0], "TaskObjectiveWebUiTopicProducer.cs"),
			Path.Combine(roots[0], "TaskObjectivePanelProfile.cs"),
			Path.Combine(roots[0], "TaskObjectiveTextValidator.cs"),
			Path.Combine(roots[1], "WebUiTaskObjectivePanelDescriptors.cs")
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

	private static TaskRuntimeService CreateRuntime(World world, TaskDefinitionRegistry definitions)
	{
		return new TaskRuntimeService(world, definitions, new ProviderServices(), new TaskPresentationBuffer());
	}

	private static TaskDefinitionRegistry CreateDefinitions()
	{
		var definitions = new TaskDefinitionRegistry();
		definitions.Register("alpha", new TaskDefinition
		{
			DisplayName = "Alpha Task",
			Summary = "First objective.",
			Tags = { "task.main" },
			StartPolicy = TaskStartPolicy.Automatic,
			Objectives =
			{
				new TaskObjectiveDefinition
				{
					Id = "start",
					Kind = TaskObjectiveKind.Signal,
					SignalKey = "alpha.start.done",
					Title = "Begin the alpha trial.",
					Hint = "Talk to the guide."
				},
				new TaskObjectiveDefinition
				{
					Id = "mid",
					Kind = TaskObjectiveKind.Signal,
					SignalKey = "alpha.mid.done",
					Title = "Reach the mid checkpoint.",
					Hint = "Follow the markers."
				}
			}
		});
		definitions.Register("bravo", new TaskDefinition
		{
			DisplayName = "Bravo Task",
			Summary = "Side objective.",
			Tags = { "task.side" },
			StartPolicy = TaskStartPolicy.Automatic,
			Objectives =
			{
				new TaskObjectiveDefinition
				{
					Id = "start",
					Kind = TaskObjectiveKind.Signal,
					SignalKey = "bravo.never",
					Title = "Complete the bravo errand."
				}
			}
		});
		definitions.Register("charlie", new TaskDefinition
		{
			DisplayName = "Charlie Task",
			Summary = "Another objective.",
			Tags = { "task.main" },
			StartPolicy = TaskStartPolicy.Automatic,
			Objectives =
			{
				new TaskObjectiveDefinition
				{
					Id = "start",
					Kind = TaskObjectiveKind.Signal,
					SignalKey = "charlie.never",
					Title = "Complete the charlie errand."
				}
			}
		});
		return definitions;
	}
}

using System.Text.Json;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Tasks;
using Ludots.WebUI.DataPlane;

namespace UiRegionsMod.Runtime;

public abstract class JsonSnapshotTopicProducer : IWebUiTopicProducer
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	protected JsonSnapshotTopicProducer(string topic)
	{
		Topic = string.IsNullOrWhiteSpace(topic)
			? throw new ArgumentException("Topic is required.", nameof(topic))
			: topic.Trim();
	}

	public string Topic { get; }

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		object snapshot = BuildSnapshot();
		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
		packet = new WebUiOutboundPacket(
			context.SessionId,
			Topic,
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			payload,
			"application/json",
			context.RequestId);
		return true;
	}

	protected abstract object BuildSnapshot();
}

public sealed class TaskObjectiveTopicProducer : JsonSnapshotTopicProducer
{
	private readonly TaskRuntimeService _tasks;

	public TaskObjectiveTopicProducer(string topic, TaskRuntimeService tasks)
		: base(topic)
	{
		_tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
	}

	protected override object BuildSnapshot()
	{
		List<TaskView> views = _tasks.CaptureViews();
		return new
		{
			panelType = "objective",
			tasks = views.Select(v => new
			{
				id = v.TaskId,
				title = v.DisplayName,
				state = v.State.ToString(),
				rule = v.CompletionRule.ToString(),
				objectives = v.Objectives.Select(o => new
				{
					id = o.ObjectiveId,
					title = o.Title,
					completed = o.Completed,
					current = o.Current,
					target = o.Target,
				}),
			}),
		};
	}
}

public sealed class ActivityModalTopicProducer : JsonSnapshotTopicProducer
{
	private readonly ActivityRuntimeService _activities;
	private readonly ActivityPresentationBuffer _presentation;

	public ActivityModalTopicProducer(
		string topic,
		ActivityRuntimeService activities,
		ActivityPresentationBuffer presentation)
		: base(topic)
	{
		_activities = activities ?? throw new ArgumentNullException(nameof(activities));
		_presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
	}

	protected override object BuildSnapshot()
	{
		var cues = _presentation.Cues
			.Select(c => new { kind = c.Kind.ToString(), activityId = c.ActivityId, optionId = c.OptionId, reason = c.Reason })
			.ToArray();
		return new
		{
			panelType = "activity-modal",
			active = cues.Any(c => c.kind == nameof(ActivityPresentationCueKind.Presented)),
			cues,
		};
	}
}

public sealed class StaticHudTopicProducer : JsonSnapshotTopicProducer
{
	private readonly string _panelType;
	private readonly Func<object> _factory;

	public StaticHudTopicProducer(string topic, string panelType, Func<object> factory)
		: base(topic)
	{
		_panelType = panelType;
		_factory = factory ?? throw new ArgumentNullException(nameof(factory));
	}

	protected override object BuildSnapshot()
	{
		return new
		{
			panelType = _panelType,
			payload = _factory(),
		};
	}
}

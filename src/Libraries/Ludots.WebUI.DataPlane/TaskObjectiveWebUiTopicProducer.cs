using System.Text.Json;
using Arch.Core;
using Ludots.Core.Gameplay.Tasks;

namespace Ludots.WebUI.DataPlane;

/// <summary>
/// DataPlane topic producer for the Objective / Task tracker panel.
/// SSOT is <see cref="TaskRuntimeService"/> / Task views / Task events only.
/// </summary>
public sealed class TaskObjectiveWebUiTopicProducer : IWebUiTopicProducer
{
	public const string JsonContentType = "application/json+ludots-task-objective";

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly TaskRuntimeService _tasks;
	private readonly TaskObjectivePanelProfile _profile;
	private readonly ITaskObjectiveTextValidator _textValidator;
	private readonly Entity _ownerScope;
	private readonly bool _filterByOwnerScope;

	public TaskObjectiveWebUiTopicProducer(
		string topic,
		TaskRuntimeService tasks,
		TaskObjectivePanelProfile profile,
		ITaskObjectiveTextValidator? textValidator = null,
		Entity ownerScope = default,
		bool filterByOwnerScope = false)
	{
		Topic = string.IsNullOrWhiteSpace(topic)
			? throw new ArgumentException("Topic is required.", nameof(topic))
			: topic.Trim();
		_tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
		_profile = profile ?? throw new ArgumentNullException(nameof(profile));
		_textValidator = textValidator ?? new TaskObjectiveTextValidator();
		_ownerScope = NormalizeScope(ownerScope);
		_filterByOwnerScope = filterByOwnerScope;
	}

	public string Topic { get; }

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		TaskObjectiveWebSnapshot snapshot = BuildSnapshot();
		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
		packet = new WebUiOutboundPacket(
			context.SessionId,
			Topic,
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			payload,
			JsonContentType,
			context.RequestId);
		return true;
	}

	public TaskObjectiveWebSnapshot BuildSnapshot()
	{
		ValidateAllowListDefinitions();

		IReadOnlyList<TaskView> views = _tasks.CaptureViews();
		var rows = new List<TaskObjectiveWebRow>(views.Count);

		for (int i = 0; i < views.Count; i++)
		{
			TaskView view = views[i];
			if (!_profile.IncludesState(view.State))
			{
				continue;
			}

			if (_filterByOwnerScope && !ScopeEquals(view.ScopeHost, _ownerScope))
			{
				continue;
			}

			if (_profile.AllowedTaskIds != null &&
			    !ContainsId(_profile.AllowedTaskIds, view.TaskId))
			{
				continue;
			}

			if (!_tasks.TryGetDefinition(view.TaskId, out TaskDefinition definition))
			{
				throw new InvalidOperationException(
					$"Task definition '{view.TaskId}' is not registered.");
			}

			if (_profile.RequiredTags != null && !HasAllTags(definition, _profile.RequiredTags))
			{
				continue;
			}

			TaskObjectiveProgressView objective = ResolveCurrentObjective(view);
			TaskObjectiveDefinition? objectiveDefinition = FindObjectiveDefinition(definition, objective.ObjectiveId);
			string objectiveText = objective.Title;
			string objectiveHint = objectiveDefinition?.Hint ?? string.Empty;
			string objectiveTextToken = objectiveDefinition?.TextToken ?? string.Empty;
			string objectiveHintToken = objectiveDefinition?.HintToken ?? string.Empty;

			_textValidator.Validate(
				view.TaskId,
				objective.ObjectiveId,
				objectiveText,
				objectiveTextToken);

			if (!string.IsNullOrWhiteSpace(objectiveHintToken))
			{
				_textValidator.Validate(
					view.TaskId,
					objective.ObjectiveId,
					objectiveHint,
					objectiveHintToken);
			}

			rows.Add(new TaskObjectiveWebRow(
				view.TaskId,
				view.DisplayName,
				view.Summary,
				view.State.ToString(),
				objective.ObjectiveId,
				objective.Title,
				objectiveText,
				objectiveHint,
				string.IsNullOrWhiteSpace(objectiveTextToken) ? null : objectiveTextToken.Trim(),
				string.IsNullOrWhiteSpace(objectiveHintToken) ? null : objectiveHintToken.Trim(),
				view.InstanceId,
				view.Entity.Id,
				view.Entity.WorldId,
				view.Entity.Version,
				view.ScopeHost.Id,
				view.ScopeHost.WorldId,
				view.ScopeHost.Version));
		}

		SortRows(rows);

		uint revision = ComputeRevision(rows);
		return new TaskObjectiveWebSnapshot(
			_profile.ProfileId,
			_ownerScope.Id,
			_ownerScope.WorldId,
			_ownerScope.Version,
			revision,
			rows.ToArray());
	}

	private void ValidateAllowListDefinitions()
	{
		if (_profile.AllowedTaskIds == null)
		{
			return;
		}

		for (int i = 0; i < _profile.AllowedTaskIds.Count; i++)
		{
			string taskId = _profile.AllowedTaskIds[i];
			if (!_tasks.TryGetDefinition(taskId, out _))
			{
				throw new InvalidOperationException(
					$"Task definition '{taskId}' is not registered.");
			}
		}
	}

	private void SortRows(List<TaskObjectiveWebRow> rows)
	{
		switch (_profile.SortKey)
		{
			case TaskObjectiveSortKey.TaskIdAscending:
				rows.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.TaskId, b.TaskId));
				break;
			case TaskObjectiveSortKey.DisplayNameAscending:
				rows.Sort(static (a, b) =>
				{
					int byName = StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName);
					return byName != 0
						? byName
						: StringComparer.OrdinalIgnoreCase.Compare(a.TaskId, b.TaskId);
				});
				break;
			case TaskObjectiveSortKey.AllowListOrder:
				if (_profile.AllowedTaskIds == null)
				{
					throw new InvalidOperationException(
						$"Objective profile '{_profile.ProfileId}' sort key '{_profile.SortKey}' requires allowedTaskIds.");
				}

				IReadOnlyList<string> order = _profile.AllowedTaskIds;
				rows.Sort((a, b) =>
				{
					int ai = IndexOfId(order, a.TaskId);
					int bi = IndexOfId(order, b.TaskId);
					int byOrder = ai.CompareTo(bi);
					return byOrder != 0
						? byOrder
						: StringComparer.OrdinalIgnoreCase.Compare(a.TaskId, b.TaskId);
				});
				break;
			default:
				throw new InvalidOperationException(
					$"Objective profile '{_profile.ProfileId}' has unsupported sort key '{_profile.SortKey}'.");
		}
	}

	private static uint ComputeRevision(IReadOnlyList<TaskObjectiveWebRow> rows)
	{
		uint revision = (uint)rows.Count;
		for (int i = 0; i < rows.Count; i++)
		{
			TaskObjectiveWebRow row = rows[i];
			revision = unchecked((revision * 31u) + (uint)row.TaskInstanceId);
			revision = unchecked((revision * 31u) + (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(row.TaskId));
			revision = unchecked((revision * 31u) + (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(row.ObjectiveId));
			revision = unchecked((revision * 31u) + (uint)StringComparer.Ordinal.GetHashCode(row.State));
		}

		return revision == 0 ? 1u : revision;
	}

	private static TaskObjectiveProgressView ResolveCurrentObjective(TaskView view)
	{
		for (int i = 0; i < view.Objectives.Count; i++)
		{
			if (!view.Objectives[i].Completed)
			{
				return view.Objectives[i];
			}
		}

		if (view.Objectives.Count == 0)
		{
			throw new InvalidOperationException(
				$"Task '{view.TaskId}' has no objectives to project.");
		}

		return view.Objectives[0];
	}

	private static TaskObjectiveDefinition? FindObjectiveDefinition(TaskDefinition definition, string objectiveId)
	{
		for (int i = 0; i < definition.Objectives.Count; i++)
		{
			if (string.Equals(definition.Objectives[i].Id, objectiveId, StringComparison.OrdinalIgnoreCase))
			{
				return definition.Objectives[i];
			}
		}

		return null;
	}

	private static bool HasAllTags(TaskDefinition definition, IReadOnlyList<string> requiredTags)
	{
		for (int i = 0; i < requiredTags.Count; i++)
		{
			string required = requiredTags[i];
			bool found = false;
			for (int t = 0; t < definition.Tags.Count; t++)
			{
				if (string.Equals(definition.Tags[t], required, StringComparison.OrdinalIgnoreCase))
				{
					found = true;
					break;
				}
			}

			if (!found)
			{
				return false;
			}
		}

		return true;
	}

	private static bool ContainsId(IReadOnlyList<string> ids, string value)
	{
		for (int i = 0; i < ids.Count; i++)
		{
			if (string.Equals(ids[i], value, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static int IndexOfId(IReadOnlyList<string> ids, string value)
	{
		for (int i = 0; i < ids.Count; i++)
		{
			if (string.Equals(ids[i], value, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}

		return int.MaxValue;
	}

	private static Entity NormalizeScope(Entity scope)
	{
		return scope.Equals(default(Entity)) || scope.Equals(Entity.Null)
			? Entity.Null
			: scope;
	}

	private static bool ScopeEquals(Entity left, Entity right)
	{
		return NormalizeScope(left).Equals(NormalizeScope(right));
	}
}

public sealed record TaskObjectiveWebSnapshot(
	string ProfileId,
	int OwnerEntityId,
	int OwnerWorldId,
	int OwnerVersion,
	uint Revision,
	TaskObjectiveWebRow[] Tasks);

public sealed record TaskObjectiveWebRow(
	string TaskId,
	string DisplayName,
	string Summary,
	string State,
	string ObjectiveId,
	string ObjectiveTitle,
	string ObjectiveText,
	string ObjectiveHint,
	string? ObjectiveTextToken,
	string? ObjectiveHintToken,
	int TaskInstanceId,
	int TaskEntityId,
	int TaskWorldId,
	int TaskVersion,
	int ScopeHostEntityId,
	int ScopeHostWorldId,
	int ScopeHostVersion);

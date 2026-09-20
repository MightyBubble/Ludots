using Ludots.Core.Gameplay.Tasks;

namespace Ludots.WebUI.DataPlane;

/// <summary>
/// Data-driven Objective panel profile: which task states appear, how they sort, and optional filters.
/// Profile ids are panel-kit vocabulary only — no game/task display names.
/// </summary>
public sealed class TaskObjectivePanelProfile
{
	public const string GenericProfileId = "profile.objective.generic";

	public TaskObjectivePanelProfile(
		string profileId,
		IReadOnlyList<TaskInstanceState> includedStates,
		TaskObjectiveSortKey sortKey,
		IReadOnlyList<string>? allowedTaskIds = null,
		IReadOnlyList<string>? requiredTags = null)
	{
		if (string.IsNullOrWhiteSpace(profileId))
		{
			throw new ArgumentException("Profile id is required.", nameof(profileId));
		}

		ArgumentNullException.ThrowIfNull(includedStates);
		if (includedStates.Count == 0)
		{
			throw new ArgumentException("Objective profile must include at least one task state.", nameof(includedStates));
		}

		ProfileId = profileId.Trim();
		IncludedStates = includedStates.ToArray();
		SortKey = sortKey;
		AllowedTaskIds = NormalizeOptionalIds(allowedTaskIds, nameof(allowedTaskIds));
		RequiredTags = NormalizeOptionalIds(requiredTags, nameof(requiredTags));
	}

	public string ProfileId { get; }
	public IReadOnlyList<TaskInstanceState> IncludedStates { get; }
	public TaskObjectiveSortKey SortKey { get; }
	public IReadOnlyList<string>? AllowedTaskIds { get; }
	public IReadOnlyList<string>? RequiredTags { get; }

	public static TaskObjectivePanelProfile CreateGeneric(
		TaskObjectiveSortKey sortKey = TaskObjectiveSortKey.TaskIdAscending,
		IReadOnlyList<string>? allowedTaskIds = null,
		IReadOnlyList<string>? requiredTags = null)
	{
		return new TaskObjectivePanelProfile(
			GenericProfileId,
			[TaskInstanceState.Active],
			sortKey,
			allowedTaskIds,
			requiredTags);
	}

	public bool IncludesState(TaskInstanceState state)
	{
		for (int i = 0; i < IncludedStates.Count; i++)
		{
			if (IncludedStates[i] == state)
			{
				return true;
			}
		}

		return false;
	}

	private static IReadOnlyList<string>? NormalizeOptionalIds(IReadOnlyList<string>? ids, string paramName)
	{
		if (ids == null)
		{
			return null;
		}

		if (ids.Count == 0)
		{
			throw new ArgumentException($"{paramName} must be null or contain at least one id.", paramName);
		}

		var normalized = new string[ids.Count];
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < ids.Count; i++)
		{
			string id = ids[i];
			if (string.IsNullOrWhiteSpace(id))
			{
				throw new ArgumentException($"{paramName}[{i}] is required.", paramName);
			}

			string trimmed = id.Trim();
			if (!seen.Add(trimmed))
			{
				throw new ArgumentException($"{paramName} contains duplicate id '{trimmed}'.", paramName);
			}

			normalized[i] = trimmed;
		}

		return normalized;
	}
}

public enum TaskObjectiveSortKey : byte
{
	TaskIdAscending = 1,
	DisplayNameAscending = 2,
	AllowListOrder = 3,
}

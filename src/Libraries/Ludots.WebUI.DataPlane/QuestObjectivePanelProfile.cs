using Ludots.Core.Gameplay.Quests;

namespace Ludots.WebUI.DataPlane;

/// <summary>
/// Data-driven Objective panel profile: which quest states appear, how they sort, and optional filters.
/// Profile ids are panel-kit vocabulary only — no game/quest display names.
/// </summary>
public sealed class QuestObjectivePanelProfile
{
	public const string GenericProfileId = "profile.objective.generic";

	public QuestObjectivePanelProfile(
		string profileId,
		IReadOnlyList<QuestState> includedStates,
		QuestObjectiveSortKey sortKey,
		IReadOnlyList<string>? allowedQuestIds = null,
		IReadOnlyList<string>? requiredTags = null)
	{
		if (string.IsNullOrWhiteSpace(profileId))
		{
			throw new ArgumentException("Profile id is required.", nameof(profileId));
		}

		ArgumentNullException.ThrowIfNull(includedStates);
		if (includedStates.Count == 0)
		{
			throw new ArgumentException("Objective profile must include at least one quest state.", nameof(includedStates));
		}

		ProfileId = profileId.Trim();
		IncludedStates = includedStates.ToArray();
		SortKey = sortKey;
		AllowedQuestIds = NormalizeOptionalIds(allowedQuestIds, nameof(allowedQuestIds));
		RequiredTags = NormalizeOptionalIds(requiredTags, nameof(requiredTags));
	}

	public string ProfileId { get; }
	public IReadOnlyList<QuestState> IncludedStates { get; }
	public QuestObjectiveSortKey SortKey { get; }
	public IReadOnlyList<string>? AllowedQuestIds { get; }
	public IReadOnlyList<string>? RequiredTags { get; }

	public static QuestObjectivePanelProfile CreateGeneric(
		QuestObjectiveSortKey sortKey = QuestObjectiveSortKey.QuestIdAscending,
		IReadOnlyList<string>? allowedQuestIds = null,
		IReadOnlyList<string>? requiredTags = null)
	{
		return new QuestObjectivePanelProfile(
			GenericProfileId,
			[QuestState.Active],
			sortKey,
			allowedQuestIds,
			requiredTags);
	}

	public bool IncludesState(QuestState state)
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

public enum QuestObjectiveSortKey : byte
{
	QuestIdAscending = 1,
	DisplayNameAscending = 2,
	AllowListOrder = 3,
}

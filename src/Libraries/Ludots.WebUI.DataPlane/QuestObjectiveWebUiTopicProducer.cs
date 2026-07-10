using System.Text.Json;
using Arch.Core;
using Ludots.Core.Gameplay.Quests;

namespace Ludots.WebUI.DataPlane;

/// <summary>
/// DataPlane topic producer for the Objective / Quest tracker panel.
/// SSOT is <see cref="QuestRuntimeService"/> / Quest views / Quest events only.
/// </summary>
public sealed class QuestObjectiveWebUiTopicProducer : IWebUiTopicProducer
{
	public const string JsonContentType = "application/json+ludots-quest-objective";

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly QuestRuntimeService _quests;
	private readonly QuestObjectivePanelProfile _profile;
	private readonly IQuestObjectiveTextValidator _textValidator;
	private readonly Entity _ownerScope;
	private readonly bool _filterByOwnerScope;

	public QuestObjectiveWebUiTopicProducer(
		string topic,
		QuestRuntimeService quests,
		QuestObjectivePanelProfile profile,
		IQuestObjectiveTextValidator? textValidator = null,
		Entity ownerScope = default,
		bool filterByOwnerScope = false)
	{
		Topic = string.IsNullOrWhiteSpace(topic)
			? throw new ArgumentException("Topic is required.", nameof(topic))
			: topic.Trim();
		_quests = quests ?? throw new ArgumentNullException(nameof(quests));
		_profile = profile ?? throw new ArgumentNullException(nameof(profile));
		_textValidator = textValidator ?? new QuestObjectiveTextValidator();
		_ownerScope = NormalizeScope(ownerScope);
		_filterByOwnerScope = filterByOwnerScope;
	}

	public string Topic { get; }

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		QuestObjectiveWebSnapshot snapshot = BuildSnapshot();
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

	public QuestObjectiveWebSnapshot BuildSnapshot()
	{
		ValidateAllowListDefinitions();

		IReadOnlyList<QuestView> views = _quests.GetQuestViews();
		var rows = new List<QuestObjectiveWebRow>(views.Count);

		for (int i = 0; i < views.Count; i++)
		{
			QuestView view = views[i];
			if (!_profile.IncludesState(view.State))
			{
				continue;
			}

			if (_filterByOwnerScope && !ScopeEquals(view.ScopeHost, _ownerScope))
			{
				continue;
			}

			if (_profile.AllowedQuestIds != null &&
			    !ContainsId(_profile.AllowedQuestIds, view.QuestId))
			{
				continue;
			}

			if (!_quests.TryGetDefinition(view.QuestId, out QuestDefinition definition))
			{
				throw new InvalidOperationException(
					$"Quest definition '{view.QuestId}' is not registered.");
			}

			if (_profile.RequiredTags != null && !HasAllTags(definition, _profile.RequiredTags))
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(view.StageId) ||
			    !_quests.TryGetStage(view.QuestId, view.StageId, out QuestStageDefinition stage))
			{
				throw new InvalidOperationException(
					$"Quest '{view.QuestId}' is missing stage '{view.StageId}'.");
			}

			_textValidator.Validate(
				view.QuestId,
				stage.Id,
				stage.ObjectiveText,
				stage.ObjectiveTextToken);

			if (!string.IsNullOrWhiteSpace(stage.ObjectiveHintToken))
			{
				_textValidator.Validate(
					view.QuestId,
					stage.Id,
					stage.ObjectiveHint,
					stage.ObjectiveHintToken);
			}

			rows.Add(new QuestObjectiveWebRow(
				view.QuestId,
				view.DisplayName,
				view.Summary,
				view.State.ToString(),
				stage.Id,
				stage.Title,
				stage.ObjectiveText,
				stage.ObjectiveHint,
				string.IsNullOrWhiteSpace(stage.ObjectiveTextToken) ? null : stage.ObjectiveTextToken.Trim(),
				string.IsNullOrWhiteSpace(stage.ObjectiveHintToken) ? null : stage.ObjectiveHintToken.Trim(),
				view.Revision,
				view.QuestEntity.Id,
				view.QuestEntity.WorldId,
				view.QuestEntity.Version,
				view.ScopeHost.Id,
				view.ScopeHost.WorldId,
				view.ScopeHost.Version));
		}

		SortRows(rows);

		uint revision = ComputeRevision(rows);
		return new QuestObjectiveWebSnapshot(
			_profile.ProfileId,
			_ownerScope.Id,
			_ownerScope.WorldId,
			_ownerScope.Version,
			revision,
			rows.ToArray());
	}

	private void ValidateAllowListDefinitions()
	{
		if (_profile.AllowedQuestIds == null)
		{
			return;
		}

		for (int i = 0; i < _profile.AllowedQuestIds.Count; i++)
		{
			string questId = _profile.AllowedQuestIds[i];
			if (!_quests.TryGetDefinition(questId, out _))
			{
				throw new InvalidOperationException(
					$"Quest definition '{questId}' is not registered.");
			}
		}
	}

	private void SortRows(List<QuestObjectiveWebRow> rows)
	{
		switch (_profile.SortKey)
		{
			case QuestObjectiveSortKey.QuestIdAscending:
				rows.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.QuestId, b.QuestId));
				break;
			case QuestObjectiveSortKey.DisplayNameAscending:
				rows.Sort(static (a, b) =>
				{
					int byName = StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName);
					return byName != 0
						? byName
						: StringComparer.OrdinalIgnoreCase.Compare(a.QuestId, b.QuestId);
				});
				break;
			case QuestObjectiveSortKey.AllowListOrder:
				if (_profile.AllowedQuestIds == null)
				{
					throw new InvalidOperationException(
						$"Objective profile '{_profile.ProfileId}' sort key '{_profile.SortKey}' requires allowedQuestIds.");
				}

				IReadOnlyList<string> order = _profile.AllowedQuestIds;
				rows.Sort((a, b) =>
				{
					int ai = IndexOfId(order, a.QuestId);
					int bi = IndexOfId(order, b.QuestId);
					int byOrder = ai.CompareTo(bi);
					return byOrder != 0
						? byOrder
						: StringComparer.OrdinalIgnoreCase.Compare(a.QuestId, b.QuestId);
				});
				break;
			default:
				throw new InvalidOperationException(
					$"Objective profile '{_profile.ProfileId}' has unsupported sort key '{_profile.SortKey}'.");
		}
	}

	private static uint ComputeRevision(IReadOnlyList<QuestObjectiveWebRow> rows)
	{
		uint revision = (uint)rows.Count;
		for (int i = 0; i < rows.Count; i++)
		{
			QuestObjectiveWebRow row = rows[i];
			revision = unchecked((revision * 31u) + (uint)row.QuestRevision);
			revision = unchecked((revision * 31u) + (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(row.QuestId));
			revision = unchecked((revision * 31u) + (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(row.StageId));
			revision = unchecked((revision * 31u) + (uint)StringComparer.Ordinal.GetHashCode(row.State));
		}

		return revision == 0 ? 1u : revision;
	}

	private static bool HasAllTags(QuestDefinition definition, IReadOnlyList<string> requiredTags)
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

public sealed record QuestObjectiveWebSnapshot(
	string ProfileId,
	int OwnerEntityId,
	int OwnerWorldId,
	int OwnerVersion,
	uint Revision,
	QuestObjectiveWebRow[] Quests);

public sealed record QuestObjectiveWebRow(
	string QuestId,
	string DisplayName,
	string Summary,
	string State,
	string StageId,
	string StageTitle,
	string ObjectiveText,
	string ObjectiveHint,
	string? ObjectiveTextToken,
	string? ObjectiveHintToken,
	int QuestRevision,
	int QuestEntityId,
	int QuestWorldId,
	int QuestVersion,
	int ScopeHostEntityId,
	int ScopeHostWorldId,
	int ScopeHostVersion);

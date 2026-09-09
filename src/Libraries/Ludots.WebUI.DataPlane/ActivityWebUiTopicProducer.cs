using System.Text.Json;
using Arch.Core;
using Ludots.Core.Gameplay.Activities;

namespace Ludots.WebUI.DataPlane;

/// <summary>
/// DataPlane topic producer for the Activity event panel. SSOT is
/// <see cref="ActivityRuntimeService"/> views, active options, and the same-step
/// presentation cue window only — the panel never stores its own progress.
/// </summary>
public sealed class ActivityWebUiTopicProducer : IWebUiTopicProducer
{
    public const string JsonContentType = "application/json+ludots-activity";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ActivityRuntimeService _activities;
    private readonly ActivityPanelProfile _profile;
    private readonly Entity _ownerScope;
    private readonly bool _filterByOwnerScope;
    private readonly List<ActivityOptionView> _optionScratch = new(8);

    public ActivityWebUiTopicProducer(
        string topic,
        ActivityRuntimeService activities,
        ActivityPanelProfile profile,
        Entity ownerScope = default,
        bool filterByOwnerScope = false)
    {
        Topic = string.IsNullOrWhiteSpace(topic)
            ? throw new ArgumentException("Topic is required.", nameof(topic))
            : topic.Trim();
        _activities = activities ?? throw new ArgumentNullException(nameof(activities));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _ownerScope = NormalizeScope(ownerScope);
        _filterByOwnerScope = filterByOwnerScope;
    }

    public string Topic { get; }

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        ActivityWebSnapshot snapshot = BuildSnapshot();
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

    public ActivityWebSnapshot BuildSnapshot()
    {
        ValidateAllowListDefinitions();

        List<ActivityView> views = _activities.CaptureViews();
        var rows = new List<ActivityWebRow>(views.Count);
        var history = new List<ActivityWebHistoryRow>(views.Count);
        var resolvedViews = new List<ActivityView>(views.Count);

        for (int i = 0; i < views.Count; i++)
        {
            ActivityView view = views[i];
            if (_filterByOwnerScope && !ScopeEquals(view.ScopeHost, _ownerScope))
            {
                continue;
            }

            if (view.State == ActivityInstanceState.Resolved)
            {
                resolvedViews.Add(view);
                continue;
            }

            if (!_profile.IncludesState(view.State))
            {
                continue;
            }

            if (_profile.AllowedActivityIds != null &&
                !ContainsId(_profile.AllowedActivityIds, view.ActivityId))
            {
                continue;
            }

            rows.Add(BuildChoiceRow(view));
        }

        SortRows(rows);
        TrimHistory(resolvedViews);
        for (int i = 0; i < resolvedViews.Count; i++)
        {
            history.Add(BuildHistoryRow(resolvedViews[i]));
        }

        IReadOnlyList<ActivityPresentationCue> cues = _activities.Presentation.Cues;
        var cueRows = new ActivityWebCue[cues.Count];
        for (int i = 0; i < cues.Count; i++)
        {
            ActivityPresentationCue cue = cues[i];
            cueRows[i] = new ActivityWebCue(
                cue.Kind.ToString(),
                cue.ActivityId,
                cue.InstanceId,
                cue.OptionId,
                cue.Reason,
                cue.ScopeKey);
        }

        uint revision = ComputeRevision(rows, history);
        return new ActivityWebSnapshot(
            _profile.ProfileId,
            _ownerScope.Id,
            revision,
            rows.ToArray(),
            history.ToArray(),
            cueRows);
    }

    private ActivityWebRow BuildChoiceRow(ActivityView view)
    {
        _optionScratch.Clear();
        if (!_activities.TryGetActiveOptions(view.Entity, null, _optionScratch))
        {
            throw new InvalidOperationException(
                $"Activity instance '{view.ActivityId}' ({view.InstanceId}) is no longer active while building the panel snapshot.");
        }

        var options = new ActivityWebOption[_optionScratch.Count];
        for (int i = 0; i < _optionScratch.Count; i++)
        {
            ActivityOptionView option = _optionScratch[i];
            options[i] = new ActivityWebOption(
                option.OptionId,
                option.Title,
                option.Body,
                option.IsBaseline,
                option.Executable,
                option.BlockReason);
        }

        return new ActivityWebRow(
            view.ActivityId,
            view.DisplayName,
            view.Summary,
            view.State.ToString(),
            view.DispatchPolicy.ToString(),
            view.InstanceId,
            view.Entity.Id,
            view.Entity.WorldId,
            view.Entity.Version,
            options);
    }

    private static ActivityWebHistoryRow BuildHistoryRow(ActivityView view)
    {
        return new ActivityWebHistoryRow(
            view.ActivityId,
            view.DisplayName,
            view.InstanceId,
            view.SelectedOptionId,
            view.DispatchPolicy == ActivityDispatchPolicy.Automatic,
            view.Entity.Id,
            view.Entity.WorldId,
            view.Entity.Version);
    }

    private void TrimHistory(List<ActivityView> resolved)
    {
        resolved.Sort(static (a, b) => a.InstanceId.CompareTo(b.InstanceId));
        while (resolved.Count > _profile.HistoryLimit)
        {
            resolved.RemoveAt(0);
        }
    }

    private void ValidateAllowListDefinitions()
    {
        if (_profile.AllowedActivityIds == null)
        {
            return;
        }

        for (int i = 0; i < _profile.AllowedActivityIds.Count; i++)
        {
            string activityId = _profile.AllowedActivityIds[i];
            if (!_activities.TryGetDefinition(activityId, out _))
            {
                throw new InvalidOperationException(
                    $"Activity definition '{activityId}' is not registered.");
            }
        }
    }

    private void SortRows(List<ActivityWebRow> rows)
    {
        switch (_profile.SortKey)
        {
            case ActivityPanelSortKey.ActivityIdAscending:
                rows.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.ActivityId, b.ActivityId));
                break;
            case ActivityPanelSortKey.DisplayNameAscending:
                rows.Sort(static (a, b) =>
                {
                    int byName = StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName);
                    return byName != 0
                        ? byName
                        : StringComparer.OrdinalIgnoreCase.Compare(a.ActivityId, b.ActivityId);
                });
                break;
            case ActivityPanelSortKey.AllowListOrder:
                if (_profile.AllowedActivityIds == null)
                {
                    throw new InvalidOperationException(
                        $"Activity profile '{_profile.ProfileId}' sort key '{_profile.SortKey}' requires allowedActivityIds.");
                }

                IReadOnlyList<string> order = _profile.AllowedActivityIds;
                rows.Sort((a, b) =>
                {
                    int ai = IndexOfId(order, a.ActivityId);
                    int bi = IndexOfId(order, b.ActivityId);
                    int byOrder = ai.CompareTo(bi);
                    return byOrder != 0
                        ? byOrder
                        : StringComparer.OrdinalIgnoreCase.Compare(a.ActivityId, b.ActivityId);
                });
                break;
            default:
                throw new InvalidOperationException(
                    $"Activity profile '{_profile.ProfileId}' has unsupported sort key '{_profile.SortKey}'.");
        }
    }

    private static uint ComputeRevision(IReadOnlyList<ActivityWebRow> rows, IReadOnlyList<ActivityWebHistoryRow> history)
    {
        uint revision = (uint)(rows.Count + history.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            ActivityWebRow row = rows[i];
            revision = unchecked((revision * 31u) + (uint)row.InstanceId);
            revision = unchecked((revision * 31u) + (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(row.ActivityId));
            revision = unchecked((revision * 31u) + (uint)StringComparer.Ordinal.GetHashCode(row.State));
            revision = unchecked((revision * 31u) + (uint)row.Options.Length);
        }

        for (int i = 0; i < history.Count; i++)
        {
            revision = unchecked((revision * 31u) + (uint)history[i].InstanceId);
        }

        return revision == 0 ? 1u : revision;
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

public sealed record ActivityWebSnapshot(
    string ProfileId,
    int OwnerEntityId,
    uint Revision,
    ActivityWebRow[] Activities,
    ActivityWebHistoryRow[] History,
    ActivityWebCue[] Cues);

public sealed record ActivityWebRow(
    string ActivityId,
    string DisplayName,
    string Summary,
    string State,
    string DispatchPolicy,
    int InstanceId,
    int EntityId,
    int WorldId,
    int Version,
    ActivityWebOption[] Options);

public sealed record ActivityWebOption(
    string OptionId,
    string Title,
    string Body,
    bool IsBaseline,
    bool Executable,
    string BlockReason);

public sealed record ActivityWebHistoryRow(
    string ActivityId,
    string DisplayName,
    int InstanceId,
    string SelectedOptionId,
    bool Automatic,
    int EntityId,
    int WorldId,
    int Version);

public sealed record ActivityWebCue(
    string Kind,
    string ActivityId,
    int InstanceId,
    string OptionId,
    string Reason,
    int ScopeKey);

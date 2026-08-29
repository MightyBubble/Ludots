using Ludots.Core.Gameplay.Activities;

namespace Ludots.WebUI.DataPlane;

/// <summary>
/// Data-driven Activity event panel profile: which instance states appear in the
/// choice list, how they sort, and optional id filters. Profile ids are panel-kit
/// vocabulary only — no game/activity display names.
/// </summary>
public sealed class ActivityPanelProfile
{
    public const string GenericProfileId = "profile.activity.generic";

    public ActivityPanelProfile(
        string profileId,
        IReadOnlyList<ActivityInstanceState> includedStates,
        ActivityPanelSortKey sortKey,
        IReadOnlyList<string>? allowedActivityIds = null,
        int historyLimit = 16)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile id is required.", nameof(profileId));
        }

        ArgumentNullException.ThrowIfNull(includedStates);
        if (includedStates.Count == 0)
        {
            throw new ArgumentException("Activity profile must include at least one instance state.", nameof(includedStates));
        }

        if (historyLimit < 1 || historyLimit > 256)
        {
            throw new ArgumentException("History limit must be between 1 and 256.", nameof(historyLimit));
        }

        ProfileId = profileId.Trim();
        IncludedStates = includedStates.ToArray();
        SortKey = sortKey;
        AllowedActivityIds = NormalizeOptionalIds(allowedActivityIds, nameof(allowedActivityIds));
        HistoryLimit = historyLimit;
    }

    public string ProfileId { get; }
    public IReadOnlyList<ActivityInstanceState> IncludedStates { get; }
    public ActivityPanelSortKey SortKey { get; }
    public IReadOnlyList<string>? AllowedActivityIds { get; }
    public int HistoryLimit { get; }

    public static ActivityPanelProfile CreateGeneric(
        ActivityPanelSortKey sortKey = ActivityPanelSortKey.ActivityIdAscending,
        IReadOnlyList<string>? allowedActivityIds = null,
        int historyLimit = 16)
    {
        return new ActivityPanelProfile(
            GenericProfileId,
            [ActivityInstanceState.Active],
            sortKey,
            allowedActivityIds,
            historyLimit);
    }

    public bool IncludesState(ActivityInstanceState state)
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

public enum ActivityPanelSortKey : byte
{
    ActivityIdAscending = 1,
    DisplayNameAscending = 2,
    AllowListOrder = 3,
}

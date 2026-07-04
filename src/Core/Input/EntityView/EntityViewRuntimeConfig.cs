using System;
using System.Collections.Generic;
using Ludots.Core.Input.Selection;

namespace Ludots.Core.Input.EntityView;

public sealed class EntityViewRuntimeConfig
{
    public string DefaultViewKey { get; set; } = SelectionViewKeys.Primary;

    public List<EntityViewProfileEntry> Profiles { get; set; } = new();

    public void Validate(string configPath = "game.json entityViews")
    {
        if (string.IsNullOrWhiteSpace(DefaultViewKey))
        {
            throw new InvalidOperationException($"{configPath} requires entityViews.defaultViewKey.");
        }

        if (Profiles.Count <= 0)
        {
            throw new InvalidOperationException($"{configPath} requires at least one entityViews.profiles entry.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Profiles.Count; i++)
        {
            EntityViewProfileEntry profile = Profiles[i]
                ?? throw new InvalidOperationException($"{configPath} entityViews.profiles[{i}] is null.");
            profile.Validate($"{configPath} entityViews.profiles[{i}]");
            if (!seen.Add(profile.ViewKey))
            {
                throw new InvalidOperationException(
                    $"{configPath} defines duplicate entityViews profile viewKey '{profile.ViewKey}'.");
            }
        }

        if (!TryGetProfile(DefaultViewKey, out _))
        {
            throw new InvalidOperationException(
                $"{configPath} entityViews.defaultViewKey '{DefaultViewKey}' does not match any profile.");
        }
    }

    public bool TryGetProfile(string viewKey, out EntityViewProfileEntry profile)
    {
        profile = null!;
        if (string.IsNullOrWhiteSpace(viewKey))
        {
            return false;
        }

        for (int i = 0; i < Profiles.Count; i++)
        {
            EntityViewProfileEntry candidate = Profiles[i];
            if (string.Equals(candidate.ViewKey, viewKey, StringComparison.Ordinal))
            {
                profile = candidate;
                return true;
            }
        }

        return false;
    }

    public EntityViewProfileEntry RequireProfile(string viewKey, string configPath = "game.json entityViews")
    {
        if (!TryGetProfile(viewKey, out EntityViewProfileEntry profile))
        {
            throw new InvalidOperationException(
                $"{configPath} does not define entityViews profile for viewKey '{viewKey}'.");
        }

        return profile;
    }
}

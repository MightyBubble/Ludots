using System;
using Ludots.Core.EntityCollections;

namespace Ludots.Core.Input.EntityView;

public sealed class EntityViewProfileEntry
{
    public string ViewKey { get; set; } = string.Empty;

    public string CommandSourceCollectionKey { get; set; } = EntityCollectionKeys.CommandSource;

    public string DisplayCollectionKey { get; set; } = EntityCollectionKeys.SelectionLivePrimary;

    public void Validate(string configPath)
    {
        if (string.IsNullOrWhiteSpace(ViewKey))
        {
            throw new InvalidOperationException($"{configPath} requires entityViews.profiles[].viewKey.");
        }

        if (string.IsNullOrWhiteSpace(CommandSourceCollectionKey))
        {
            throw new InvalidOperationException(
                $"{configPath} requires entityViews.profiles[].commandSourceCollectionKey for view '{ViewKey}'.");
        }

        if (string.IsNullOrWhiteSpace(DisplayCollectionKey))
        {
            throw new InvalidOperationException(
                $"{configPath} requires entityViews.profiles[].displayCollectionKey for view '{ViewKey}'.");
        }
    }
}

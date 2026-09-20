using System;

namespace Ludots.Core.Systems
{
    /// <summary>
    /// #1108 placed-variable kinds for authoring panels and mount validation.
    /// Anchors are concrete map InstanceIds whose id contains "anchor"; regions are
    /// map JSON Regions entries and never enter <see cref="MapLoadEntityIndex"/>.
    /// </summary>
    public static class PlacedInstanceKinds
    {
        public const string Entity = "entity";
        public const string Anchor = "anchor";
        public const string Region = "region";

        public static bool IsAnchorInstanceId(string? instanceId)
        {
            return !string.IsNullOrWhiteSpace(instanceId) &&
                   instanceId.Contains("anchor", StringComparison.OrdinalIgnoreCase);
        }

        public static string KindForEntityInstance(string instanceId)
        {
            return IsAnchorInstanceId(instanceId) ? Anchor : Entity;
        }
    }
}

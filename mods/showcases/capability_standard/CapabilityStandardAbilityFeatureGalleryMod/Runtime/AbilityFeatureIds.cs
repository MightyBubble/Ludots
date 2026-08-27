namespace CapabilityStandardAbilityFeatureGalleryMod.Runtime;

public static class AbilityFeatureIds
{
    public const string ShowcaseIdPrefix = "capability_standard_ability_feature_";
    public const string ModAssetsRelative = "mods/showcases/capability_standard/CapabilityStandardAbilityFeatureGalleryMod/assets";

    public static string ShowcaseId(string feature) => ShowcaseIdPrefix + RequireFeatureName(feature);

    public static string MapId(string feature) => ShowcaseId(feature);

    public static string RequireFeatureName(string feature)
    {
        if (string.IsNullOrWhiteSpace(feature))
        {
            throw new InvalidOperationException("Ability feature gallery requires a feature id.");
        }

        return feature;
    }

    public static bool TryParseFeatureFromMapId(string? mapId, out string feature)
    {
        feature = "";
        if (string.IsNullOrWhiteSpace(mapId) || !mapId.StartsWith(ShowcaseIdPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        feature = mapId[ShowcaseIdPrefix.Length..];
        return feature.Length > 0;
    }
}

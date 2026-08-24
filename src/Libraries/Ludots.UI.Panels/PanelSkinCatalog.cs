namespace Ludots.UI.Panels;

/// <summary>
/// One selectable panel skin: display label plus accent color for the built-in
/// auto-layout renderer. Selection is data-only (game.json "panelSkin"); nobody
/// writes C# to change skins.
/// </summary>
public readonly record struct PanelSkinDescriptor(string Name, string Label, byte AccentR, byte AccentG, byte AccentB);

/// <summary>
/// Built-in skin catalog. "default" renders with a neutral accent; markup/compose/reactive
/// are accent variants of the same auto-layout renderer — mirrors what the four-skin
/// showcase actually differed by before skins moved engine-side.
/// </summary>
public static class PanelSkinCatalog
{
    public const string DefaultSkinName = "default";

    private static readonly Dictionary<string, PanelSkinDescriptor> Skins = new(StringComparer.Ordinal)
    {
        [DefaultSkinName] = new PanelSkinDescriptor(DefaultSkinName, "Default", 120, 120, 140),
        ["markup"] = new PanelSkinDescriptor("markup", "Markup", 68, 136, 204),
        ["compose"] = new PanelSkinDescriptor("compose", "Compose", 76, 175, 80),
        ["reactive"] = new PanelSkinDescriptor("reactive", "Reactive", 156, 39, 176),
    };

    public static IReadOnlyCollection<string> KnownSkins => Skins.Keys;

    /// <summary>
    /// True for skins owned by the browser UI stack ("web"): the native presentation
    /// installer must step aside for them instead of failing on an unknown name.
    /// </summary>
    public static bool IsBrowserStackSkin(string? skinName)
    {
        return string.Equals(skinName?.Trim(), "web", StringComparison.Ordinal);
    }

    public static PanelSkinDescriptor Resolve(string? skinName)
    {
        if (string.IsNullOrWhiteSpace(skinName) || string.Equals(skinName.Trim(), DefaultSkinName, StringComparison.Ordinal))
        {
            return Skins[DefaultSkinName];
        }

        if (Skins.TryGetValue(skinName.Trim(), out PanelSkinDescriptor descriptor))
        {
            return descriptor;
        }

        throw new InvalidOperationException(
            $"Unknown panel skin '{skinName}'. Known skins: {string.Join(", ", Skins.Keys)}, plus 'web' when a browser runtime is provisioned.");
    }
}

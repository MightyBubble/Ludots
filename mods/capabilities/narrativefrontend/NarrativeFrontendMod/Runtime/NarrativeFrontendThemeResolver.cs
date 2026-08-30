using System;
using System.IO;
using Ludots.Core.Engine;

namespace NarrativeFrontendMod.Runtime;

public static class NarrativeFrontendThemeResolver
{
    public static string ResolveFrameImageSource(
        GameEngine engine,
        string themeAssetRoot,
        NarrativeFrontendSurfaceKind kind)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (string.IsNullOrWhiteSpace(themeAssetRoot))
        {
            throw new ArgumentException("Narrative theme asset root is required.", nameof(themeAssetRoot));
        }

        string themeId = engine.MergedConfig?.PanelTheme?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(themeId))
        {
            return string.Empty;
        }

        string fileName = kind switch
        {
            NarrativeFrontendSurfaceKind.ChoiceList => "choice_frame.png",
            NarrativeFrontendSurfaceKind.OverlayDialogue => "panel_frame.png",
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(fileName))
        {
            return string.Empty;
        }

        string vfsPath = $"{themeAssetRoot.TrimEnd('/')}/{themeId}/images/{fileName}";
        if (engine.VFS == null ||
            !engine.VFS.TryResolveFullPath(vfsPath, out string resolved) ||
            !File.Exists(resolved))
        {
            throw new InvalidOperationException(
                $"Narrative theme '{themeId}' requires frame asset '{vfsPath}'.");
        }

        return resolved;
    }
}

using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ludots.Core.Config;
using Ludots.Platform.Abstractions;
using Ludots.Core.Engine;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;

namespace Ludots.UI.Panels;

/// <summary>
/// A loaded visual theme pack (#1011 theme axis): one parsed stylesheet for the native
/// renderer plus a data-URI variant for browser pages, with theme fonts registered into
/// the Skia font registry. Themes are orthogonal to skins — any backend renders any theme.
/// Optional slice assets follow the narrative theme convention under images/:
/// panel_frame.png (nine-slice chrome), bar_track.png / bar_fill_*.png (three-slice bars).
/// </summary>
public sealed class PanelTheme
{
    public PanelTheme(
        string id,
        UiStyleSheet styleSheet,
        string webCss,
        string? panelFrameImagePath = null,
        string? barTrackImagePath = null,
        string? barFillHealthImagePath = null,
        string? barFillManaImagePath = null)
    {
        Id = id;
        StyleSheet = styleSheet;
        WebCss = webCss;
        PanelFrameImagePath = panelFrameImagePath;
        BarTrackImagePath = barTrackImagePath;
        BarFillHealthImagePath = barFillHealthImagePath;
        BarFillManaImagePath = barFillManaImagePath;
    }

    public string Id { get; }

    public UiStyleSheet StyleSheet { get; }

    /// <summary>CSS text with url() rewritten to data: URIs — injectable into browser pages.</summary>
    public string WebCss { get; }

    /// <summary>Absolute path to images/panel_frame.png when present (nine-slice chrome).</summary>
    public string? PanelFrameImagePath { get; }

    /// <summary>Absolute path to images/bar_track.png when present (three-slice track).</summary>
    public string? BarTrackImagePath { get; }

    public string? BarFillHealthImagePath { get; }

    public string? BarFillManaImagePath { get; }

    public bool HasNineSliceFrame => !string.IsNullOrWhiteSpace(PanelFrameImagePath);

    public bool HasThreeSliceBars => !string.IsNullOrWhiteSpace(BarTrackImagePath);

    public string? ResolveBarFillImagePath(string variableName)
    {
        return variableName switch
        {
            "health" => BarFillHealthImagePath ?? BarFillManaImagePath,
            "mana" => BarFillManaImagePath ?? BarFillHealthImagePath,
            _ => BarFillHealthImagePath ?? BarFillManaImagePath,
        };
    }
}

/// <summary>
/// Theme pack discovery and loading. Entries merge across mods via
/// PanelThemes/themes.json (ArrayById by id — same-id downstream entries replace the
/// whole pack, mirroring the graphs family contract). Each entry declares a mod-scoped
/// root pointing at theme.css, images/, and fonts/.
/// </summary>
public static class PanelThemeCatalog
{
    public const string ConfigPath = "PanelThemes/themes.json";

    private static readonly Regex UrlPattern = new(
        @"url\(\s*(?<q>[""']?)(?<url>[^)""']+)\k<q>\s*\)",
        RegexOptions.Compiled);

    public static PanelTheme? TryLoad(GameEngine engine)
    {
        string? themeId = engine.MergedConfig?.PanelTheme;
        if (string.IsNullOrWhiteSpace(themeId))
        {
            return null;
        }

        ThemeEntry entry = ResolveEntry(engine, themeId.Trim());
        string cssPath = ResolveRoot(engine, entry.Root);
        string rootPath = Path.GetDirectoryName(cssPath)
            ?? throw new InvalidOperationException(
                $"Panel theme '{entry.Id}' root '{cssPath}' has no directory.");

        string rawCss = File.ReadAllText(cssPath);
        foreach (var font in entry.Fonts)
        {
            string fontPath = ResolveInsideRoot(rootPath, font.Value, entry.Id, $"font '{font.Key}'");
            if (!File.Exists(fontPath))
            {
                throw new InvalidOperationException(
                    $"Panel theme '{entry.Id}' declares font '{font.Key}' but the file is missing: '{fontPath}'.");
            }

            UiFontRegistry.RegisterFile(font.Key, fontPath);
        }

        UiStyleSheet styleSheet = UiCssParser.ParseStyleSheet(RewriteUrls(rawCss, source =>
            ResolveInsideRoot(rootPath, source, entry.Id, "image")));
        string webCss = RewriteUrls(rawCss, source =>
        {
            string path = ResolveInsideRoot(rootPath, source, entry.Id, "image");
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Panel theme '{entry.Id}' references missing image '{source}' (resolved '{path}').");
            }

            byte[] bytes = File.ReadAllBytes(path);
            string mediaType = path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : "image/png";
            return $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";
        });

        return new PanelTheme(
            entry.Id,
            styleSheet,
            webCss,
            TryOptionalImage(rootPath, "images/panel_frame.png"),
            TryOptionalImage(rootPath, "images/bar_track.png"),
            TryOptionalImage(rootPath, "images/bar_fill_health.png"),
            TryOptionalImage(rootPath, "images/bar_fill_mana.png"));
    }

    private static string? TryOptionalImage(string rootPath, string relative)
    {
        string combined = Path.GetFullPath(Path.Combine(rootPath, relative.Replace('/', Path.DirectorySeparatorChar)));
        return File.Exists(combined) ? combined : null;
    }

    private static ThemeEntry ResolveEntry(GameEngine engine, string themeId)
    {
        var entry = ConfigPipeline.RequireEntry(
            engine.ConfigCatalog,
            ConfigPath,
            ConfigMergePolicy.ArrayById,
            "id");
        IReadOnlyList<MergedConfigEntry> merged = engine.ConfigPipeline.MergeArrayByIdFromCatalog(in entry, engine.ConfigConflictReport);
        for (int i = 0; i < merged.Count; i++)
        {
            if (merged[i].Node is not JsonObject node)
            {
                continue;
            }

            if (node["id"]?.GetValue<string>() is not { } candidateId ||
                !string.Equals(candidateId, themeId, StringComparison.Ordinal))
            {
                continue;
            }

            if (node["root"]?.GetValue<string>() is not { } root || string.IsNullOrWhiteSpace(root))
            {
                throw new InvalidOperationException(
                    $"Panel theme '{themeId}' entry is missing its mod-scoped 'root' path.");
            }

            var fonts = new Dictionary<string, string>(StringComparer.Ordinal);
            if (node["fonts"] is JsonObject fontsNode)
            {
                foreach (var fontProperty in fontsNode)
                {
                    if (fontProperty.Value?.GetValue<string>() is { } fontFile &&
                        !string.IsNullOrWhiteSpace(fontFile))
                    {
                        fonts[fontProperty.Key] = fontFile;
                    }
                }
            }

            return new ThemeEntry(themeId, root.Trim(), fonts);
        }

        throw new InvalidOperationException(
            $"Unknown panel theme '{themeId}'. Declare it in PanelThemes/themes.json (ArrayById, id field).");
    }

    private static string ResolveInsideRoot(string rootPath, string relative, string themeId, string what)
    {
        string combined = Path.GetFullPath(Path.Combine(rootPath, relative.Replace('/', Path.DirectorySeparatorChar)));
        string rootFull = Path.GetFullPath(rootPath);
        if (!combined.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Panel theme '{themeId}' {what} path '{relative}' escapes the theme root.");
        }

        return combined;
    }

    private static string ResolveRoot(GameEngine engine, string modScopedRoot)
    {
        if (engine.VFS != null &&
            engine.VFS.TryResolveFullPath(modScopedRoot, out string resolved) &&
            File.Exists(resolved))
        {
            return resolved;
        }

        throw new InvalidOperationException(
            $"Panel theme css '{modScopedRoot}' cannot be resolved through the mod VFS.");
    }

    private static string RewriteUrls(string css, Func<string, string> rewrite)
    {
        return UrlPattern.Replace(css, match =>
        {
            string source = match.Groups["url"].Value;
            if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                Path.IsPathRooted(source) ||
                source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            return $"url({match.Groups["q"].Value}{rewrite(source)}{match.Groups["q"].Value})";
        });
    }

    private sealed record ThemeEntry(string Id, string Root, Dictionary<string, string> Fonts);
}

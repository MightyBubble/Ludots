using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.Presentation
{
    /// <summary>
    /// Shared display resolver for semantic text maps and 2D image assets (#128).
    /// Text always exits through PresentationTextCatalog; images resolve via VFS.
    /// </summary>
    public sealed class PresentationDisplayResolver
    {
        private readonly PresentationTextCatalog _textCatalog;
        private readonly PresentationTextLocaleSelection _localeSelection;
        private readonly PresentationSemanticMapCatalog _semanticMaps;
        private readonly PresentationImageAssetCatalog _imageAssets;
        private readonly IVirtualFileSystem _vfs;
        private readonly Dictionary<string, string> _glyphCache = new(StringComparer.Ordinal);

        public PresentationDisplayResolver(
            PresentationTextCatalog textCatalog,
            PresentationTextLocaleSelection localeSelection,
            PresentationSemanticMapCatalog semanticMaps,
            PresentationImageAssetCatalog imageAssets,
            IVirtualFileSystem vfs)
        {
            _textCatalog = textCatalog ?? throw new ArgumentNullException(nameof(textCatalog));
            _localeSelection = localeSelection ?? throw new ArgumentNullException(nameof(localeSelection));
            _semanticMaps = semanticMaps ?? throw new ArgumentNullException(nameof(semanticMaps));
            _imageAssets = imageAssets ?? throw new ArgumentNullException(nameof(imageAssets));
            _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
        }

        public bool TryResolveMappedText(PresentationSemanticDomain domain, string key, out string text)
        {
            text = string.Empty;
            if (!_semanticMaps.TryGet(domain, key, out PresentationSemanticMapDefinition map))
            {
                return false;
            }

            return TryFormatToken(map.TextToken, out text);
        }

        public string ResolveMappedTextOrThrow(PresentationSemanticDomain domain, string key)
        {
            if (TryResolveMappedText(domain, key, out string text))
            {
                return text;
            }

            throw new InvalidOperationException(
                $"No presentation semantic map for domain '{domain}' key '{key}'. Author Presentation/semantic_maps.json.");
        }

        public bool TryFormatToken(string textToken, out string text)
        {
            text = string.Empty;
            if (string.IsNullOrWhiteSpace(textToken))
            {
                return false;
            }

            int tokenId = _textCatalog.GetTokenId(textToken);
            if (tokenId <= 0)
            {
                return false;
            }

            int localeId = _localeSelection.ActiveLocaleId > 0
                ? _localeSelection.ActiveLocaleId
                : _textCatalog.DefaultLocaleId;
            var packet = PresentationTextPacket.FromToken(tokenId);
            return PresentationTextFormatter.TryFormat(_textCatalog, localeId, in packet, out text)
                && !string.IsNullOrWhiteSpace(text);
        }

        public string FormatTokenOrThrow(string textToken)
        {
            if (TryFormatToken(textToken, out string text))
            {
                return text;
            }

            throw new InvalidOperationException(
                $"Presentation text token '{textToken}' is not resolvable in the active locale.");
        }

        public bool TryFormatTokenRuns(
            string textToken,
            IReadOnlyList<PresentationTextArg>? args,
            out IReadOnlyList<PresentationTextRun> runs)
        {
            runs = Array.Empty<PresentationTextRun>();
            if (string.IsNullOrWhiteSpace(textToken))
            {
                return false;
            }

            int tokenId = _textCatalog.GetTokenId(textToken);
            if (tokenId <= 0)
            {
                return false;
            }

            int localeId = _localeSelection.ActiveLocaleId > 0
                ? _localeSelection.ActiveLocaleId
                : _textCatalog.DefaultLocaleId;
            var packet = PresentationTextPacket.FromToken(tokenId);
            if (args != null)
            {
                for (int i = 0; i < args.Count; i++)
                {
                    packet.SetArg(i, args[i]);
                }
            }

            return PresentationTextFormatter.TryFormatRuns(_textCatalog, localeId, in packet, out runs)
                && runs.Count > 0;
        }

        public IReadOnlyList<PresentationTextRun> FormatTokenRunsOrThrow(
            string textToken,
            IReadOnlyList<PresentationTextArg>? args = null)
        {
            if (TryFormatTokenRuns(textToken, args, out IReadOnlyList<PresentationTextRun> runs))
            {
                return runs;
            }

            throw new InvalidOperationException(
                $"Presentation text token '{textToken}' has no locale template runs for the active locale.");
        }

        public bool TryResolveImageSource(string imageId, out string source)
        {
            source = string.Empty;
            if (string.IsNullOrWhiteSpace(imageId) || !_imageAssets.TryGet(imageId, out PresentationImageAssetDefinition asset))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(asset.Path))
            {
                if (!_vfs.TryResolveFullPath(asset.Path, out string fullPath) || string.IsNullOrWhiteSpace(fullPath))
                {
                    if (string.IsNullOrWhiteSpace(asset.GlyphFallback))
                    {
                        throw new InvalidOperationException(
                            $"Presentation image asset '{imageId}' path '{asset.Path}' could not be resolved via VFS.");
                    }
                }
                else if (File.Exists(fullPath))
                {
                    source = fullPath;
                    return true;
                }
                else if (string.IsNullOrWhiteSpace(asset.GlyphFallback))
                {
                    throw new InvalidOperationException(
                        $"Presentation image asset '{imageId}' path '{asset.Path}' resolved to missing file '{fullPath}'.");
                }
            }

            if (!string.IsNullOrWhiteSpace(asset.GlyphFallback))
            {
                source = BuildGlyphDataUri(asset.GlyphFallback);
                return true;
            }

            return false;
        }

        public string ResolveImageSourceOrThrow(string imageId)
        {
            if (TryResolveImageSource(imageId, out string source))
            {
                return source;
            }

            throw new InvalidOperationException(
                $"Presentation image asset '{imageId}' could not be resolved.");
        }

        // Placeholder glyph chrome for assets authored with glyphFallback only — infra placeholder,
        // not player-content skin; single definition here.
        private const string GlyphBackdropHex = "#121A24";
        private const string GlyphBorderHex = "#F6C56B";
        private const string GlyphTextHex = "#F8FAFC";

        private string BuildGlyphDataUri(string glyph)
        {
            string normalized = glyph.Trim();
            if (_glyphCache.TryGetValue(normalized, out string? cached))
            {
                return cached;
            }

            string svg =
                "<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96' viewBox='0 0 96 96'>" +
                $"<rect x='4' y='4' width='88' height='88' rx='22' fill='{GlyphBackdropHex}' stroke='{GlyphBorderHex}' stroke-width='3'/>" +
                $"<text x='48' y='56' text-anchor='middle' font-family='Segoe UI, sans-serif' font-size='28' font-weight='700' fill='{GlyphTextHex}'>{EscapeXml(normalized)}</text>" +
                "</svg>";
            string uri = "data:image/svg+xml;utf8," + Uri.EscapeDataString(svg);
            _glyphCache[normalized] = uri;
            return uri;
        }

        private static string EscapeXml(string value)
            => value.Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}

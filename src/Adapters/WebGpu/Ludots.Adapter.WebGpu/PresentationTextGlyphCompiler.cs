using System.Globalization;
using System.Numerics;
using Ludots.Client.WebGpu.Runtime;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Adapter.WebGpu;

public sealed class PresentationTextGlyphCompiler
{
    private readonly WebGpuFontAtlas _atlas;
    private readonly PresentationTextCatalog _catalog;
    private readonly PresentationTextLocaleSelection _localeSelection;
    private readonly WorldHudStringTable _worldHudStrings;
    private readonly Dictionary<TextPacketCacheKey, WebGpuGlyphRun> _packetCache = new(256);
    private readonly Dictionary<string, WebGpuGlyphRun> _literalCache = new(256, StringComparer.Ordinal);

    public PresentationTextGlyphCompiler(
        WebGpuFontAtlas atlas,
        PresentationTextCatalog catalog,
        PresentationTextLocaleSelection localeSelection,
        WorldHudStringTable worldHudStrings)
    {
        _atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _localeSelection = localeSelection ?? throw new ArgumentNullException(nameof(localeSelection));
        _worldHudStrings = worldHudStrings ?? throw new ArgumentNullException(nameof(worldHudStrings));
    }

    public WebGpuFontAtlas Atlas => _atlas;

    public int CachedRunCount => _packetCache.Count + _literalCache.Count;

    public int CompilationCount { get; private set; }

    public WebGpuGlyphRun Resolve(in PresentationTextPacket packet)
    {
        if (!packet.HasValue)
        {
            throw new InvalidOperationException("WebGPU text received an empty PresentationTextPacket.");
        }

        int localeId = _localeSelection.ActiveLocaleId;
        var key = new TextPacketCacheKey(localeId, in packet);
        if (_packetCache.TryGetValue(key, out WebGpuGlyphRun? cached))
        {
            return cached;
        }

        if (!_catalog.TryGetTokenDefinition(packet.TokenId, out PresentationTextTokenDefinition definition))
        {
            throw new InvalidOperationException(
                $"WebGPU text cannot resolve unknown presentation token id {packet.TokenId}.");
        }

        if (packet.ArgCount != definition.ArgCount)
        {
            throw new InvalidOperationException(
                $"WebGPU text token '{definition.Key}' expects {definition.ArgCount} arguments but packet supplied {packet.ArgCount}.");
        }

        if (!_catalog.TryGetTemplate(localeId, packet.TokenId, out PresentationTextTemplate template))
        {
            string localeKey = _catalog.GetLocaleKey(localeId);
            throw new InvalidOperationException(
                $"WebGPU text token '{definition.Key}' has no template for locale '{localeKey}'.");
        }

        WebGpuGlyphRun compiled = Compile(template, in packet);
        _packetCache.Add(key, compiled);
        CompilationCount++;
        return compiled;
    }

    public WebGpuGlyphRun Resolve(in ScreenHudTextItem item)
    {
        if (item.Text.HasValue)
        {
            return Resolve(in item.Text);
        }

        if (item.Id0 <= 0)
        {
            throw new InvalidOperationException(
                $"WebGPU HUD text stableId={item.StableId} has neither a presentation packet nor a WorldHudStringTable id.");
        }

        string text = _worldHudStrings.TryGet(item.Id0)
            ?? throw new InvalidOperationException(
                $"WebGPU HUD text stableId={item.StableId} references missing WorldHudStringTable id {item.Id0}.");
        return ResolveLiteral(text);
    }

    public WebGpuGlyphRun Resolve(in WorldHudItem item)
    {
        if (item.Text.HasValue)
        {
            return Resolve(in item.Text);
        }

        if (item.Id0 <= 0)
        {
            throw new InvalidOperationException(
                $"WebGPU world HUD text stableId={item.StableId} has neither a presentation packet nor a WorldHudStringTable id.");
        }

        string text = _worldHudStrings.TryGet(item.Id0)
            ?? throw new InvalidOperationException(
                $"WebGPU world HUD text stableId={item.StableId} references missing WorldHudStringTable id {item.Id0}.");
        return ResolveLiteral(text);
    }

    public WebGpuGlyphRun Resolve(in ScreenOverlayItem item, ScreenOverlayBuffer overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (item.Kind != ScreenOverlayItemKind.Text)
        {
            throw new ArgumentException("Only ScreenOverlayItemKind.Text can be compiled as glyphs.", nameof(item));
        }

        if (item.Text.HasValue)
        {
            return Resolve(in item.Text);
        }

        string text = overlay.GetString(item.StringId)
            ?? throw new InvalidOperationException(
                $"WebGPU overlay text stableId={item.StableId} references missing overlay string id {item.StringId}.");
        return ResolveLiteral(text);
    }

    public WebGpuGlyphRun ResolveLiteral(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_literalCache.TryGetValue(text, out WebGpuGlyphRun? cached))
        {
            return cached;
        }

        WebGpuGlyphRun compiled = CompileLiteral(text);
        _literalCache.Add(text, compiled);
        CompilationCount++;
        return compiled;
    }

    public int WriteInstances(
        WebGpuGlyphRun run,
        float x,
        float y,
        int fontSize,
        Vector4 color,
        PresentationClipShape clip,
        Span<WebGpuGlyphInstance> destination)
    {
        ArgumentNullException.ThrowIfNull(run);
        ReadOnlySpan<WebGpuGlyphPlacement> glyphs = run.Glyphs;
        if (destination.Length < glyphs.Length)
        {
            throw new ArgumentException(
                $"Glyph destination has {destination.Length} slots but run requires {glyphs.Length}.",
                nameof(destination));
        }

        float resolvedFontSize = fontSize <= 0 ? 16f : fontSize;
        float scale = resolvedFontSize / _atlas.EmSizePx;
        Vector4 clipRect = new(clip.X, clip.Y, clip.Width, clip.Height);
        for (int i = 0; i < glyphs.Length; i++)
        {
            ref readonly WebGpuGlyphPlacement glyph = ref glyphs[i];
            destination[i] = new WebGpuGlyphInstance
            {
                CenterPx = new Vector2(x, y) + (glyph.CenterPx * scale),
                HalfSizePx = glyph.HalfSizePx * scale,
                Color = color,
                UvRect = glyph.UvRect,
                ClipRectPx = clipRect,
                ClipShape = (float)clip.Kind,
            };
        }

        return glyphs.Length;
    }

    public int WriteLocalOffsetInstances(
        WebGpuGlyphRun run,
        int fontSize,
        Vector4 color,
        uint anchorIndex,
        Span<WebGpuWorldGlyphInstance> destination)
    {
        ArgumentNullException.ThrowIfNull(run);
        ReadOnlySpan<WebGpuGlyphPlacement> glyphs = run.Glyphs;
        if (destination.Length < glyphs.Length)
        {
            throw new ArgumentException(
                $"Glyph destination has {destination.Length} slots but run requires {glyphs.Length}.",
                nameof(destination));
        }

        float resolvedFontSize = fontSize <= 0 ? 16f : fontSize;
        float scale = resolvedFontSize / _atlas.EmSizePx;
        for (int i = 0; i < glyphs.Length; i++)
        {
            ref readonly WebGpuGlyphPlacement glyph = ref glyphs[i];
            destination[i] = new WebGpuWorldGlyphInstance
            {
                LocalOffsetPx = glyph.CenterPx * scale,
                HalfSizePx = glyph.HalfSizePx * scale,
                Color = color,
                UvRect = glyph.UvRect,
                AnchorIndex = anchorIndex,
            };
        }

        return glyphs.Length;
    }

    /// <summary>
    /// Translates retained glyph instance centers without regenerating UV/layout data.
    /// </summary>
    public void TranslateInstances(Span<WebGpuGlyphInstance> instances, Vector2 delta)
    {
        if (delta == Vector2.Zero || instances.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < instances.Length; i++)
        {
            instances[i].CenterPx += delta;
        }
    }

    private WebGpuGlyphRun Compile(PresentationTextTemplate template, in PresentationTextPacket packet)
    {
        var counter = new GlyphRunWriter(_atlas, null);
        AppendTemplate(ref counter, template, in packet);
        var placements = new WebGpuGlyphPlacement[counter.GlyphCount];
        var writer = new GlyphRunWriter(_atlas, placements);
        AppendTemplate(ref writer, template, in packet);
        writer.RequireComplete();
        return new WebGpuGlyphRun(placements, writer.WidthPx, writer.HeightPx);
    }

    private WebGpuGlyphRun CompileLiteral(string text)
    {
        var counter = new GlyphRunWriter(_atlas, null);
        counter.Append(text.AsSpan());
        var placements = new WebGpuGlyphPlacement[counter.GlyphCount];
        var writer = new GlyphRunWriter(_atlas, placements);
        writer.Append(text.AsSpan());
        writer.RequireComplete();
        return new WebGpuGlyphRun(placements, writer.WidthPx, writer.HeightPx);
    }

    private static void AppendTemplate(
        ref GlyphRunWriter writer,
        PresentationTextTemplate template,
        in PresentationTextPacket packet)
    {
        ReadOnlySpan<PresentationTextTemplatePart> parts = template.GetParts();
        for (int i = 0; i < parts.Length; i++)
        {
            PresentationTextTemplatePart part = parts[i];
            switch (part.Kind)
            {
                case PresentationTextTemplatePartKind.Literal:
                    writer.Append(part.Literal.AsSpan());
                    break;

                case PresentationTextTemplatePartKind.Argument:
                    if ((uint)part.ArgIndex >= packet.ArgCount)
                    {
                        throw new InvalidOperationException(
                            $"Presentation text template '{template.Source}' references absent argument {part.ArgIndex}.");
                    }

                    AppendArgument(ref writer, packet.GetArg(part.ArgIndex));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Presentation text template '{template.Source}' contains unsupported part kind {part.Kind}.");
            }
        }
    }

    private static void AppendArgument(ref GlyphRunWriter writer, PresentationTextArg argument)
    {
        Span<char> characters = stackalloc char[64];
        bool formatted;
        int written;
        switch (argument.Type)
        {
            case PresentationTextArgType.Int32:
                formatted = argument.AsInt32().TryFormat(
                    characters,
                    out written,
                    provider: CultureInfo.InvariantCulture);
                break;

            case PresentationTextArgType.Float32:
                ReadOnlySpan<char> format = argument.Format switch
                {
                    PresentationTextArgFormat.Integer => "0",
                    PresentationTextArgFormat.Fixed0 => "0",
                    PresentationTextArgFormat.Fixed1 => "0.0",
                    PresentationTextArgFormat.Fixed2 => "0.00",
                    PresentationTextArgFormat.Default => "0.###",
                    _ => throw new InvalidOperationException(
                        $"WebGPU text does not support numeric format {argument.Format}."),
                };
                formatted = argument.AsFloat32().TryFormat(
                    characters,
                    out written,
                    format,
                    CultureInfo.InvariantCulture);
                break;

            default:
                throw new InvalidOperationException(
                    $"WebGPU text does not support presentation argument type {argument.Type}.");
        }

        if (!formatted)
        {
            throw new InvalidOperationException("WebGPU text numeric formatting exceeded its fixed stack buffer.");
        }

        writer.Append(characters[..written]);
    }

    private readonly struct TextPacketCacheKey : IEquatable<TextPacketCacheKey>
    {
        private readonly int _localeId;
        private readonly int _tokenId;
        private readonly byte _argCount;
        private readonly PresentationTextArg _arg0;
        private readonly PresentationTextArg _arg1;
        private readonly PresentationTextArg _arg2;
        private readonly PresentationTextArg _arg3;

        public TextPacketCacheKey(int localeId, in PresentationTextPacket packet)
        {
            _localeId = localeId;
            _tokenId = packet.TokenId;
            _argCount = packet.ArgCount;
            _arg0 = packet.Arg0;
            _arg1 = packet.Arg1;
            _arg2 = packet.Arg2;
            _arg3 = packet.Arg3;
        }

        public bool Equals(TextPacketCacheKey other)
        {
            return _localeId == other._localeId &&
                   _tokenId == other._tokenId &&
                   _argCount == other._argCount &&
                   ArgEquals(in _arg0, in other._arg0) &&
                   ArgEquals(in _arg1, in other._arg1) &&
                   ArgEquals(in _arg2, in other._arg2) &&
                   ArgEquals(in _arg3, in other._arg3);
        }

        public override bool Equals(object? obj) => obj is TextPacketCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_localeId);
            hash.Add(_tokenId);
            hash.Add(_argCount);
            AddArg(ref hash, in _arg0);
            AddArg(ref hash, in _arg1);
            AddArg(ref hash, in _arg2);
            AddArg(ref hash, in _arg3);
            return hash.ToHashCode();
        }

        private static bool ArgEquals(in PresentationTextArg left, in PresentationTextArg right)
        {
            return left.Type == right.Type &&
                   left.Format == right.Format &&
                   left.Raw32 == right.Raw32;
        }

        private static void AddArg(ref HashCode hash, in PresentationTextArg argument)
        {
            hash.Add((byte)argument.Type);
            hash.Add((byte)argument.Format);
            hash.Add(argument.Raw32);
        }
    }

    private struct GlyphRunWriter
    {
        private readonly WebGpuFontAtlas _atlas;
        private readonly WebGpuGlyphPlacement[]? _placements;
        private float _cursorX;
        private float _lineY;
        private float _maxWidth;
        private int _glyphIndex;

        public GlyphRunWriter(WebGpuFontAtlas atlas, WebGpuGlyphPlacement[]? placements)
        {
            _atlas = atlas;
            _placements = placements;
            _cursorX = 0f;
            _lineY = 0f;
            _maxWidth = 0f;
            _glyphIndex = 0;
        }

        public int GlyphCount => _glyphIndex;

        public float WidthPx => MathF.Max(_maxWidth, _cursorX);

        public float HeightPx => _lineY + _atlas.LineHeightPx;

        public void Append(ReadOnlySpan<char> text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (character == '\r')
                {
                    continue;
                }

                if (character == '\n')
                {
                    _maxWidth = MathF.Max(_maxWidth, _cursorX);
                    _cursorX = 0f;
                    _lineY += _atlas.LineHeightPx;
                    continue;
                }

                if (!_atlas.TryGetGlyph(character, out WebGpuFontGlyph glyph))
                {
                    throw new InvalidOperationException(
                        $"WebGPU font atlas does not contain required character U+{(int)character:X4}.");
                }

                if (glyph.HasPixels)
                {
                    float halfWidth = glyph.Width * 0.5f;
                    float halfHeight = glyph.Height * 0.5f;
                    if (_placements != null)
                    {
                        _placements[_glyphIndex] = new WebGpuGlyphPlacement(
                            character,
                            new Vector2(
                                _cursorX + glyph.BearingX + halfWidth,
                                _lineY + _atlas.EmSizePx + glyph.BearingY + halfHeight),
                            new Vector2(halfWidth, halfHeight),
                            new Vector4(
                                glyph.AtlasX / (float)_atlas.Width,
                                glyph.AtlasY / (float)_atlas.Height,
                                (glyph.AtlasX + glyph.Width) / (float)_atlas.Width,
                                (glyph.AtlasY + glyph.Height) / (float)_atlas.Height));
                    }

                    _glyphIndex++;
                }

                _cursorX += glyph.AdvanceX;
            }
        }

        public void RequireComplete()
        {
            if (_placements == null || _glyphIndex != _placements.Length)
            {
                throw new InvalidOperationException("WebGPU glyph run compilation produced an inconsistent glyph count.");
            }
        }
    }
}

public sealed class WebGpuGlyphRun
{
    private readonly WebGpuGlyphPlacement[] _glyphs;

    internal WebGpuGlyphRun(WebGpuGlyphPlacement[] glyphs, float widthPx, float heightPx)
    {
        _glyphs = glyphs;
        WidthPx = widthPx;
        HeightPx = heightPx;
    }

    public ReadOnlySpan<WebGpuGlyphPlacement> Glyphs => _glyphs;

    public int GlyphCount => _glyphs.Length;

    public float WidthPx { get; }

    public float HeightPx { get; }
}

using System.Globalization;
using System.Numerics;

namespace Ludots.Client.WebGpu.Runtime;

internal sealed class WebGpuPerformanceOverlay
{
    private const float FontSize = 15f;
    private const float TextX = 18f;
    private const float TextY = 12f;

    private readonly WebGpuFontAtlas _atlas;
    private readonly WebGpuScreenInstance[] _screenInstances = new WebGpuScreenInstance[2];
    private readonly WebGpuGlyphInstance[] _textInstances = new WebGpuGlyphInstance[512];
    private int _textInstanceCount;

    public WebGpuPerformanceOverlay(WebGpuFontAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        _atlas = atlas;
        _screenInstances[0] = CreatePanel(new Vector4(0.30f, 0.88f, 0.58f, 0.90f), new Vector2(210f, 104f));
        _screenInstances[1] = CreatePanel(new Vector4(0.015f, 0.025f, 0.032f, 0.94f), new Vector2(208f, 102f));
        _textInstanceCount = WriteText("FPS SAMPLING..."u8, new Vector4(0.82f, 0.90f, 0.94f, 1f));
    }

    public ReadOnlySpan<WebGpuScreenInstance> ScreenInstances => _screenInstances;

    public ReadOnlySpan<WebGpuGlyphInstance> TextInstances =>
        _textInstances.AsSpan(0, _textInstanceCount);

    public void Update(in FrameTimingSnapshot timing, in WebGpuFrameDiagnostics diagnostics)
    {
        Span<char> text = stackalloc char[384];
        int length = 0;
        length = Append(text, length, "FPS ");
        length = AppendFixed(text, length, timing.FramesPerSecond);
        length = Append(text, length, "  FRAME ");
        length = AppendFixed(text, length, timing.AverageFrameMilliseconds);
        length = Append(text, length, " MS\nWORST ");
        length = AppendFixed(text, length, timing.WorstFrameMilliseconds);
        length = Append(text, length, " MS / 1 SEC\nALLOC ");
        length = AppendFixed(text, length, timing.AllocatedBytesPerSecond / 1024.0);
        length = Append(text, length, " KB/S\nTICK ");
        length = AppendFixed(text, length, diagnostics.TotalTickMilliseconds);
        length = Append(text, length, "  SIM ");
        length = AppendFixed(text, length, diagnostics.SimulationMilliseconds);
        length = Append(text, length, "  PRES ");
        length = AppendFixed(text, length, diagnostics.PresentationMilliseconds);
        length = Append(text, length, "\nEMIT ");
        length = AppendFixed(text, length, diagnostics.PerformerEmitMilliseconds);
        length = Append(text, length, "  SYNC ");
        length = AppendFixed(text, length, diagnostics.PerformerTransformSyncMilliseconds);
        length = Append(text, length, "  BEHAV ");
        length = AppendFixed(text, length, diagnostics.PerformerBehaviorMilliseconds);
        length = Append(text, length, "\nBUILD ");
        length = AppendFixed(text, length, diagnostics.FrameBuildMilliseconds);
        length = Append(text, length, "  DIRECT ");
        length = AppendInteger(text, length, diagnostics.SingleVisualFastEmitCount);
        WebGpuHudFrameDiagnostics hud = diagnostics.Hud;
        length = Append(text, length, "\nHUD ");
        length = AppendHudPath(text, length, hud.BuildPath);
        length = Append(text, length, " CPU ");
        length = AppendFixed(text, length, hud.HudCpuBuildMilliseconds);
        length = Append(text, length, " BAR#");
        length = AppendInteger(text, length, hud.BarInstanceCount);
        length = Append(text, length, "/");
        length = AppendInteger(text, length, hud.BarUploadBytes / 1024);
        length = Append(text, length, "KB ANC#");
        length = AppendInteger(text, length, hud.LabelAnchorCount);
        length = Append(text, length, "/");
        length = AppendInteger(text, length, hud.AnchorUploadBytes / 1024);
        length = Append(text, length, "KB GLY#");
        length = AppendInteger(text, length, hud.GlyphCount);
        length = Append(text, length, "/");
        length = AppendInteger(text, length, hud.GlyphUploadBytes / 1024);
        length = Append(text, length, "KB\nRESOLVE ");
        length = AppendInteger(text, length, hud.TextsResolved);
        length = Append(text, length, " PO ");
        length = AppendInteger(text, length, hud.PositionOnlyBarsUpdated);
        length = Append(text, length, "/");
        length = AppendInteger(text, length, hud.PositionOnlyTextsUpdated);

        Vector4 statusColor = timing.FramesPerSecond switch
        {
            >= 55.0 => new Vector4(0.50f, 1.00f, 0.68f, 1f),
            >= 30.0 => new Vector4(1.00f, 0.86f, 0.38f, 1f),
            _ => new Vector4(1.00f, 0.47f, 0.40f, 1f),
        };
        _screenInstances[0].Color = new Vector4(statusColor.X, statusColor.Y, statusColor.Z, 0.90f);
        _textInstanceCount = WriteText(text[..length], statusColor);
    }

    private static WebGpuScreenInstance CreatePanel(Vector4 color, Vector2 halfSize) => new()
    {
        CenterPx = new Vector2(216f, 112f),
        HalfSizePx = halfSize,
        Color = color,
    };

    private int WriteText(ReadOnlySpan<byte> ascii, Vector4 color)
    {
        Span<char> text = stackalloc char[ascii.Length];
        for (int i = 0; i < ascii.Length; i++)
        {
            text[i] = (char)ascii[i];
        }

        return WriteText(text, color);
    }

    private int WriteText(ReadOnlySpan<char> text, Vector4 color)
    {
        float scale = FontSize / _atlas.EmSizePx;
        float cursorX = 0f;
        float lineY = 0f;
        int glyphIndex = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (character == '\n')
            {
                cursorX = 0f;
                lineY += _atlas.LineHeightPx * scale;
                continue;
            }

            if (!_atlas.TryGetGlyph(character, out WebGpuFontGlyph glyph))
            {
                throw new InvalidOperationException(
                    $"WebGPU performance HUD font atlas does not contain U+{(int)character:X4}.");
            }

            if (glyph.HasPixels)
            {
                if ((uint)glyphIndex >= (uint)_textInstances.Length)
                {
                    throw new InvalidOperationException(
                        $"WebGPU performance HUD exceeded {_textInstances.Length} glyphs.");
                }

                float halfWidth = glyph.Width * scale * 0.5f;
                float halfHeight = glyph.Height * scale * 0.5f;
                _textInstances[glyphIndex++] = new WebGpuGlyphInstance
                {
                    CenterPx = new Vector2(
                        TextX + cursorX + ((glyph.BearingX + (glyph.Width * 0.5f)) * scale),
                        TextY + lineY + ((_atlas.EmSizePx + glyph.BearingY + (glyph.Height * 0.5f)) * scale)),
                    HalfSizePx = new Vector2(halfWidth, halfHeight),
                    Color = color,
                    UvRect = new Vector4(
                        glyph.AtlasX / (float)_atlas.Width,
                        glyph.AtlasY / (float)_atlas.Height,
                        (glyph.AtlasX + glyph.Width) / (float)_atlas.Width,
                        (glyph.AtlasY + glyph.Height) / (float)_atlas.Height),
                };
            }

            cursorX += glyph.AdvanceX * scale;
        }

        return glyphIndex;
    }

    private static int Append(Span<char> destination, int offset, ReadOnlySpan<char> value)
    {
        if (value.Length > destination.Length - offset)
        {
            throw new InvalidOperationException("WebGPU performance HUD text exceeded its fixed buffer.");
        }

        value.CopyTo(destination[offset..]);
        return offset + value.Length;
    }

    private static int AppendFixed(Span<char> destination, int offset, double value)
    {
        if (!value.TryFormat(
                destination[offset..],
                out int charsWritten,
                "0.0",
                CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException("WebGPU performance HUD could not format a frame timing value.");
        }

        return offset + charsWritten;
    }

    private static int AppendInteger(Span<char> destination, int offset, int value)
    {
        if (!value.TryFormat(
                destination[offset..],
                out int charsWritten,
                provider: CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException("WebGPU performance HUD could not format an integer value.");
        }

        return offset + charsWritten;
    }

    private static int AppendHudPath(Span<char> destination, int offset, byte buildPath)
    {
        ReadOnlySpan<char> label = buildPath switch
        {
            1 => "HOLD",
            2 => "DELTA",
            3 => "FULL",
            _ => "NONE",
        };
        return Append(destination, offset, label);
    }
}

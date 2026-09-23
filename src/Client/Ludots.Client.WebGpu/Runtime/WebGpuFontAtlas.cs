using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Ludots.Client.WebGpu.Runtime;

public sealed class WebGpuFontAtlas
{
    private static ReadOnlySpan<byte> FileMagic => "LWGFA001"u8;

    private readonly byte[] _pixels;
    private readonly WebGpuFontGlyph[] _glyphs;

    public WebGpuFontAtlas(
        int width,
        int height,
        float emSizePx,
        float lineHeightPx,
        byte[] pixels,
        WebGpuFontGlyph[] glyphs,
        string sourceSha256)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (!float.IsFinite(emSizePx) || emSizePx <= 0f) throw new ArgumentOutOfRangeException(nameof(emSizePx));
        if (!float.IsFinite(lineHeightPx) || lineHeightPx <= 0f) throw new ArgumentOutOfRangeException(nameof(lineHeightPx));
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSha256);
        if (pixels.Length != checked(width * height))
        {
            throw new InvalidDataException(
                $"Font atlas pixel payload has {pixels.Length} bytes; expected {width * height} for {width}x{height} R8 data.");
        }

        ValidateGlyphs(glyphs, width, height);
        Width = width;
        Height = height;
        EmSizePx = emSizePx;
        LineHeightPx = lineHeightPx;
        _pixels = pixels;
        _glyphs = glyphs;
        SourceSha256 = sourceSha256;
    }

    public int Width { get; }

    public int Height { get; }

    public float EmSizePx { get; }

    public float LineHeightPx { get; }

    public string SourceSha256 { get; }

    public int GlyphCount => _glyphs.Length;

    public ReadOnlyMemory<byte> Pixels => _pixels;

    public static WebGpuFontAtlas Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = File.OpenRead(path);
        return Load(stream, path);
    }

    public static WebGpuFontAtlas Load(Stream stream, string sourceName = "font atlas stream")
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        byte[] magic = reader.ReadBytes(FileMagic.Length);
        if (!magic.AsSpan().SequenceEqual(FileMagic))
        {
            throw new InvalidDataException($"'{sourceName}' is not a Ludots WebGPU font atlas.");
        }

        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        float emSizePx = reader.ReadSingle();
        float lineHeightPx = reader.ReadSingle();
        int glyphCount = reader.ReadInt32();
        if (glyphCount <= 0 || glyphCount > 1_000_000)
        {
            throw new InvalidDataException($"'{sourceName}' declares invalid glyph count {glyphCount}.");
        }

        byte[] sourceHash = reader.ReadBytes(32);
        if (sourceHash.Length != 32)
        {
            throw new EndOfStreamException($"'{sourceName}' ended inside its source font hash.");
        }

        var glyphs = new WebGpuFontGlyph[glyphCount];
        for (int i = 0; i < glyphs.Length; i++)
        {
            glyphs[i] = new WebGpuFontGlyph(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle());
        }

        int pixelLength = checked(width * height);
        byte[] pixels = reader.ReadBytes(pixelLength);
        if (pixels.Length != pixelLength)
        {
            throw new EndOfStreamException(
                $"'{sourceName}' ended inside its R8 pixel payload ({pixels.Length}/{pixelLength} bytes). ");
        }

        if (stream.CanSeek && stream.Position != stream.Length)
        {
            throw new InvalidDataException($"'{sourceName}' contains trailing data after the atlas payload.");
        }

        return new WebGpuFontAtlas(
            width,
            height,
            emSizePx,
            lineHeightPx,
            pixels,
            glyphs,
            Convert.ToHexString(sourceHash));
    }

    public bool TryGetGlyph(int codePoint, out WebGpuFontGlyph glyph)
    {
        int low = 0;
        int high = _glyphs.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int current = _glyphs[middle].CodePoint;
            if (current == codePoint)
            {
                glyph = _glyphs[middle];
                return true;
            }

            if (current < codePoint)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        glyph = default;
        return false;
    }

    private static void ValidateGlyphs(WebGpuFontGlyph[] glyphs, int width, int height)
    {
        int previousCodePoint = -1;
        for (int i = 0; i < glyphs.Length; i++)
        {
            ref readonly WebGpuFontGlyph glyph = ref glyphs[i];
            if (glyph.CodePoint <= previousCodePoint)
            {
                throw new InvalidDataException("Font atlas glyphs must be strictly ordered by Unicode code point.");
            }

            if (glyph.AtlasX < 0 || glyph.AtlasY < 0 || glyph.Width < 0 || glyph.Height < 0 ||
                glyph.AtlasX + glyph.Width > width || glyph.AtlasY + glyph.Height > height ||
                !float.IsFinite(glyph.BearingX) || !float.IsFinite(glyph.BearingY) ||
                !float.IsFinite(glyph.AdvanceX) || glyph.AdvanceX < 0f)
            {
                throw new InvalidDataException($"Font atlas glyph U+{glyph.CodePoint:X4} has invalid metrics.");
            }

            previousCodePoint = glyph.CodePoint;
        }
    }
}

public readonly struct WebGpuFontGlyph
{
    public WebGpuFontGlyph(
        int codePoint,
        int atlasX,
        int atlasY,
        int width,
        int height,
        float bearingX,
        float bearingY,
        float advanceX)
    {
        CodePoint = codePoint;
        AtlasX = atlasX;
        AtlasY = atlasY;
        Width = width;
        Height = height;
        BearingX = bearingX;
        BearingY = bearingY;
        AdvanceX = advanceX;
    }

    public int CodePoint { get; }

    public int AtlasX { get; }

    public int AtlasY { get; }

    public int Width { get; }

    public int Height { get; }

    public float BearingX { get; }

    public float BearingY { get; }

    public float AdvanceX { get; }

    public bool HasPixels => Width > 0 && Height > 0;
}

public readonly struct WebGpuGlyphPlacement
{
    public WebGpuGlyphPlacement(int codePoint, Vector2 centerPx, Vector2 halfSizePx, Vector4 uvRect)
    {
        CodePoint = codePoint;
        CenterPx = centerPx;
        HalfSizePx = halfSizePx;
        UvRect = uvRect;
    }

    public int CodePoint { get; }

    public Vector2 CenterPx { get; }

    public Vector2 HalfSizePx { get; }

    public Vector4 UvRect { get; }
}

[StructLayout(LayoutKind.Sequential)]
public struct WebGpuGlyphInstance
{
    public Vector2 CenterPx;
    public Vector2 HalfSizePx;
    public Vector4 Color;
    public Vector4 UvRect;
    public Vector4 ClipRectPx;
    public float ClipShape;
    public Vector3 Padding;
}

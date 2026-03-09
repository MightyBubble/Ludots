using System.Collections.Concurrent;
using System.Text;
using SkiaSharp;
using Svg.Skia;

namespace Ludots.UI.Runtime;

internal static class UiImageSourceCache
{
    private static readonly ConcurrentDictionary<string, Lazy<UiImageResource?>> Cache = new(StringComparer.Ordinal);

    public static bool TryGetImage(string? source, out SKImage? image)
    {
        image = null;
        if (!TryGetResource(source, out UiImageResource? resource) || resource?.RasterImage == null)
        {
            return false;
        }

        image = resource.RasterImage;
        return true;
    }

    public static bool TryGetSize(string? source, out float width, out float height)
    {
        width = 0f;
        height = 0f;
        if (!TryGetResource(source, out UiImageResource? resource))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(resource);
        width = resource.Width;
        height = resource.Height;
        return true;
    }

    internal static bool TryGetResource(string? source, out UiImageResource? resource)
    {
        resource = null;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        string cacheKey = NormalizeCacheKey(source);
        Lazy<UiImageResource?> lazy = Cache.GetOrAdd(
            cacheKey,
            static key => new Lazy<UiImageResource?>(() => LoadResource(key), LazyThreadSafetyMode.ExecutionAndPublication));

        resource = lazy.Value;
        if (resource != null)
        {
            return true;
        }

        Cache.TryRemove(cacheKey, out _);
        return false;
    }

    private static UiImageResource? LoadResource(string cacheKey)
    {
        string? mediaType = null;
        byte[]? bytes = TryReadDataUri(cacheKey, out mediaType) ?? TryReadFile(cacheKey);
        if (bytes == null || bytes.Length == 0)
        {
            return null;
        }

        if (IsSvg(cacheKey, mediaType, bytes))
        {
            return LoadSvgResource(bytes);
        }

        using SKData data = SKData.CreateCopy(bytes);
        SKImage? image = SKImage.FromEncodedData(data);
        if (image == null)
        {
            return null;
        }

        return UiImageResource.FromRaster(image);
    }

    private static string NormalizeCacheKey(string source)
    {
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return source.Trim();
        }

        try
        {
            return Path.GetFullPath(source.Trim());
        }
        catch
        {
            return source.Trim();
        }
    }

    private static bool IsSvg(string source, string? mediaType, byte[] bytes)
    {
        if (!string.IsNullOrWhiteSpace(mediaType)
            && mediaType.Contains("image/svg+xml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (source.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string text = Encoding.UTF8.GetString(bytes);
        return text.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[]? TryReadDataUri(string source, out string? mediaType)
    {
        mediaType = null;
        if (!source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int commaIndex = source.IndexOf(',');
        if (commaIndex < 0 || commaIndex == source.Length - 1)
        {
            return null;
        }

        string header = source[..commaIndex];
        string payload = source[(commaIndex + 1)..];
        int semicolonIndex = header.IndexOf(';');
        mediaType = semicolonIndex >= 0
            ? header[5..semicolonIndex]
            : header[5..];

        if (header.Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.FromBase64String(payload);
        }

        return Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
    }

    private static byte[]? TryReadFile(string path)
    {
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static UiImageResource? LoadSvgResource(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        SKSvg svg = new();
        SKPicture? picture = svg.Load(stream);
        if (picture == null)
        {
            return null;
        }

        SKRect bounds = picture.CullRect;
        if (bounds.Width <= 0.01f || bounds.Height <= 0.01f)
        {
            bounds = new SKRect(0f, 0f, 1f, 1f);
        }

        return UiImageResource.FromSvg(picture, bounds);
    }

    internal sealed class UiImageResource
    {
        private UiImageResource(SKImage? rasterImage, SKPicture? svgPicture, SKRect sourceBounds)
        {
            RasterImage = rasterImage;
            SvgPicture = svgPicture;
            SourceBounds = sourceBounds;
        }

        internal SKImage? RasterImage { get; }

        internal SKPicture? SvgPicture { get; }

        internal SKRect SourceBounds { get; }

        internal float Width => SourceBounds.Width;

        internal float Height => SourceBounds.Height;

        internal bool IsSvg => SvgPicture != null;

        internal static UiImageResource FromRaster(SKImage image)
        {
            ArgumentNullException.ThrowIfNull(image);
            return new UiImageResource(image, null, new SKRect(0f, 0f, image.Width, image.Height));
        }

        internal static UiImageResource FromSvg(SKPicture picture, SKRect sourceBounds)
        {
            ArgumentNullException.ThrowIfNull(picture);
            return new UiImageResource(null, picture, sourceBounds);
        }
    }
}

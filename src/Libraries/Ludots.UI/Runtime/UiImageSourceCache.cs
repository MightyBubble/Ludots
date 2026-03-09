using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using SkiaSharp;
using Svg.Skia;

namespace Ludots.UI.Runtime;

internal static class UiImageSourceCache
{
	internal sealed class UiImageResource
	{
		internal SKImage? RasterImage { get; }

		internal SKPicture? SvgPicture { get; }

		internal SKRect SourceBounds { get; }

		internal float Width => SourceBounds.Width;

		internal float Height => SourceBounds.Height;

		internal bool IsSvg => SvgPicture != null;

		private UiImageResource(SKImage? rasterImage, SKPicture? svgPicture, SKRect sourceBounds)
		{
			RasterImage = rasterImage;
			SvgPicture = svgPicture;
			SourceBounds = sourceBounds;
		}

		internal static UiImageResource FromRaster(SKImage image)
		{
			ArgumentNullException.ThrowIfNull(image, "image");
			return new UiImageResource(image, null, new SKRect(0f, 0f, image.Width, image.Height));
		}

		internal static UiImageResource FromSvg(SKPicture picture, SKRect sourceBounds)
		{
			ArgumentNullException.ThrowIfNull(picture, "picture");
			return new UiImageResource(null, picture, sourceBounds);
		}
	}

	private static readonly ConcurrentDictionary<string, Lazy<UiImageResource?>> Cache = new ConcurrentDictionary<string, Lazy<UiImageResource>>(StringComparer.Ordinal);

	public static bool TryGetImage(string? source, out SKImage? image)
	{
		image = null;
		if (!TryGetResource(source, out UiImageResource resource) || resource?.RasterImage == null)
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
		if (!TryGetResource(source, out UiImageResource resource))
		{
			return false;
		}
		ArgumentNullException.ThrowIfNull(resource, "resource");
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
		string key = NormalizeCacheKey(source);
		Lazy<UiImageResource> orAdd = Cache.GetOrAdd(key, (string cacheKey) => new Lazy<UiImageResource>(() => LoadResource(cacheKey), LazyThreadSafetyMode.ExecutionAndPublication));
		resource = orAdd.Value;
		if (resource != null)
		{
			return true;
		}
		Cache.TryRemove(key, out Lazy<UiImageResource> _);
		return false;
	}

	private static UiImageResource? LoadResource(string cacheKey)
	{
		string mediaType = null;
		byte[] array = TryReadDataUri(cacheKey, out mediaType) ?? TryReadFile(cacheKey);
		if (array == null || array.Length == 0)
		{
			return null;
		}
		if (IsSvg(cacheKey, mediaType, array))
		{
			return LoadSvgResource(array);
		}
		using SKData data = SKData.CreateCopy(array);
		SKImage sKImage = SKImage.FromEncodedData(data);
		if (sKImage == null)
		{
			return null;
		}
		return UiImageResource.FromRaster(sKImage);
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
		if (!string.IsNullOrWhiteSpace(mediaType) && mediaType.Contains("image/svg+xml", StringComparison.OrdinalIgnoreCase))
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
		int num = source.IndexOf(',');
		if (num < 0 || num == source.Length - 1)
		{
			return null;
		}
		string text = source.Substring(0, num);
		string text2 = source;
		int num2 = num + 1;
		string text3 = text2.Substring(num2, text2.Length - num2);
		int num3 = text.IndexOf(';');
		string text4;
		if (num3 < 0)
		{
			text2 = text;
			text4 = text2.Substring(5, text2.Length - 5);
		}
		else
		{
			text4 = text.Substring(5, num3 - 5);
		}
		mediaType = text4;
		if (text.Contains(";base64", StringComparison.OrdinalIgnoreCase))
		{
			return Convert.FromBase64String(text3);
		}
		return Encoding.UTF8.GetBytes(Uri.UnescapeDataString(text3));
	}

	private static byte[]? TryReadFile(string path)
	{
		return File.Exists(path) ? File.ReadAllBytes(path) : null;
	}

	private static UiImageResource? LoadSvgResource(byte[] bytes)
	{
		using MemoryStream stream = new MemoryStream(bytes, writable: false);
		SKSvg sKSvg = new SKSvg();
		SKPicture sKPicture = sKSvg.Load((Stream)stream);
		if (sKPicture == null)
		{
			return null;
		}
		SKRect sourceBounds = sKPicture.CullRect;
		if (sourceBounds.Width <= 0.01f || sourceBounds.Height <= 0.01f)
		{
			sourceBounds = new SKRect(0f, 0f, 1f, 1f);
		}
		return UiImageResource.FromSvg(sKPicture, sourceBounds);
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ludots.UI.Browser;

public sealed class BrowserAppResourceResolver : IBrowserResourceResolver
{
	private static readonly IReadOnlyDictionary<string, string> DefaultContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		[".html"] = "text/html; charset=utf-8",
		[".htm"] = "text/html; charset=utf-8",
		[".js"] = "application/javascript; charset=utf-8",
		[".mjs"] = "application/javascript; charset=utf-8",
		[".css"] = "text/css; charset=utf-8",
		[".json"] = "application/json; charset=utf-8",
		[".wasm"] = "application/wasm",
		[".png"] = "image/png",
		[".jpg"] = "image/jpeg",
		[".jpeg"] = "image/jpeg",
		[".gif"] = "image/gif",
		[".webp"] = "image/webp",
		[".svg"] = "image/svg+xml",
		[".ico"] = "image/x-icon",
		[".woff"] = "font/woff",
		[".woff2"] = "font/woff2",
		[".ttf"] = "font/ttf",
		[".otf"] = "font/otf"
	};

	private readonly string _rootDirectory;
	private readonly string _indexFileName;
	private readonly IReadOnlyDictionary<string, string> _contentTypes;

	public BrowserAppResourceResolver(
		string rootDirectory,
		string indexFileName = "index.html",
		IReadOnlyDictionary<string, string>? contentTypes = null)
	{
		if (string.IsNullOrWhiteSpace(rootDirectory))
		{
			throw new ArgumentException("Browser app root directory is required.", nameof(rootDirectory));
		}
		if (string.IsNullOrWhiteSpace(indexFileName))
		{
			throw new ArgumentException("Browser app index file name is required.", nameof(indexFileName));
		}

		_rootDirectory = EnsureTrailingSeparator(Path.GetFullPath(rootDirectory));
		_indexFileName = indexFileName;
		_contentTypes = contentTypes ?? DefaultContentTypes;
	}

	public ValueTask<BrowserResource?> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(uri);
		cancellationToken.ThrowIfCancellationRequested();

		string? path = TryResolvePath(uri);
		if (path == null || !File.Exists(path))
		{
			return ValueTask.FromResult<BrowserResource?>(null);
		}

		return ReadResourceAsync(path, cancellationToken);
	}

	private async ValueTask<BrowserResource?> ReadResourceAsync(string path, CancellationToken cancellationToken)
	{
		byte[] content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
		return new BrowserResource(ResolveContentType(path), content);
	}

	private string? TryResolvePath(Uri uri)
	{
		string relativePath = Uri.UnescapeDataString(uri.AbsolutePath).Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
		if (string.IsNullOrWhiteSpace(relativePath))
		{
			relativePath = _indexFileName;
		}

		string candidate = Path.GetFullPath(Path.Combine(_rootDirectory, relativePath));
		if (!candidate.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		if (Directory.Exists(candidate))
		{
			candidate = Path.Combine(candidate, _indexFileName);
		}

		return candidate;
	}

	private string ResolveContentType(string path)
	{
		string extension = Path.GetExtension(path);
		return _contentTypes.TryGetValue(extension, out string? contentType)
			? contentType
			: "application/octet-stream";
	}

	private static string EnsureTrailingSeparator(string path)
	{
		return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
			? path
			: path + Path.DirectorySeparatorChar;
	}
}

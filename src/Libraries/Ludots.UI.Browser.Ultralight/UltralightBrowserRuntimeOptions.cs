using System;
using System.IO;

namespace Ludots.UI.Browser.Ultralight;

public sealed class UltralightBrowserRuntimeOptions
{
	public UltralightBrowserRuntimeOptions(string runtimeRootPath, string? cacheRootPath = null)
	{
		if (string.IsNullOrWhiteSpace(runtimeRootPath))
		{
			throw new ArgumentException("Ultralight runtime root path is required.", nameof(runtimeRootPath));
		}

		RuntimeRootPath = Path.GetFullPath(runtimeRootPath);
		CacheRootPath = string.IsNullOrWhiteSpace(cacheRootPath)
			? Path.Combine(Path.GetTempPath(), "Ludots", "Ultralight")
			: Path.GetFullPath(cacheRootPath);
	}

	public string RuntimeRootPath { get; }

	public string CacheRootPath { get; }
}

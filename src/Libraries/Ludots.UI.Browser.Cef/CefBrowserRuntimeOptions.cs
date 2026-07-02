using System;
using System.IO;

namespace Ludots.UI.Browser.Cef;

public sealed class CefBrowserRuntimeOptions
{
	public CefBrowserRuntimeOptions(string runtimeRootPath, string? cacheRootPath = null)
	{
		if (string.IsNullOrWhiteSpace(runtimeRootPath))
		{
			throw new ArgumentException("CEF runtime root path is required.", nameof(runtimeRootPath));
		}

		RuntimeRootPath = Path.GetFullPath(runtimeRootPath);
		CacheRootPath = string.IsNullOrWhiteSpace(cacheRootPath)
			? Path.Combine(Path.GetTempPath(), "Ludots", "Cef")
			: Path.GetFullPath(cacheRootPath);
	}

	public string RuntimeRootPath { get; }

	public string CacheRootPath { get; }
}

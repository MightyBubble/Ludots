using System;
using System.IO;

namespace Ludots.UI.Browser.CefLinux;

public sealed class CefLinuxBrowserRuntimeOptions
{
	public CefLinuxBrowserRuntimeOptions(string runtimeRootPath, string? cacheRootPath = null)
	{
		if (string.IsNullOrWhiteSpace(runtimeRootPath))
		{
			throw new ArgumentException("CEF Linux runtime root path is required.", nameof(runtimeRootPath));
		}

		RuntimeRootPath = Path.GetFullPath(runtimeRootPath);
		CacheRootPath = string.IsNullOrWhiteSpace(cacheRootPath)
			? Path.Combine(Path.GetTempPath(), "Ludots", "CefLinux")
			: Path.GetFullPath(cacheRootPath);
	}

	public string RuntimeRootPath { get; }

	public string CacheRootPath { get; }
}

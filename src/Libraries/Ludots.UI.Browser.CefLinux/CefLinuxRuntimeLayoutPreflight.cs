using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Ludots.UI.Browser.CefLinux;

public static class CefLinuxRuntimeLayoutPreflight
{
	private static readonly string[] RequiredManagedFiles =
	{
		"Ludots.UI.Browser.CefLinux.dll",
		"Ludots.UI.Browser.CefLinux.deps.json",
		"Ludots.UI.Browser.CefLinux.Core.dll",
		"Ludots.UI.Browser.CefLinux.Subprocess",
		"Ludots.UI.Browser.CefLinux.Subprocess.dll",
		"Ludots.UI.Browser.CefLinux.Subprocess.runtimeconfig.json",
		"CefNet.dll",
		"Ludots.UI.Browser.dll"
	};

	private static readonly string[] RequiredNativeFiles =
	{
		"libcef.so",
		"libEGL.so",
		"libGLESv2.so",
		"icudtl.dat",
		"resources.pak",
		"chrome_100_percent.pak",
		"chrome_200_percent.pak",
		"v8_context_snapshot.bin"
	};

	private static readonly string[] RequiredDirectories =
	{
		"locales"
	};

	private static readonly string[] RequiredLocaleFiles =
	{
		Path.Combine("locales", "en-US.pak")
	};

	public static void EnsureHostPlatformSupported()
	{
		if (OperatingSystem.IsLinux())
		{
			return;
		}

		throw new PlatformNotSupportedException(
			"Ludots.UI.Browser.CefLinux ships CEF linux-x64 natives only (binding CefNet " +
			typeof(global::CefNet.CefApi).Assembly.GetName().Version?.ToString(3) + "). Current OS '" +
			RuntimeInformation.OSDescription +
			"' cannot load libcef.so. Disable browserRuntime on this host, or use provider 'cef' on Windows.");
	}

	public static void EnsureComplete(string runtimeRootPath)
	{
		if (string.IsNullOrWhiteSpace(runtimeRootPath))
		{
			throw new ArgumentException("CEF Linux runtime root path is required.", nameof(runtimeRootPath));
		}

		string fullRuntimeRootPath = Path.GetFullPath(runtimeRootPath);
		if (!Directory.Exists(fullRuntimeRootPath))
		{
			throw new DirectoryNotFoundException($"CEF Linux runtime root was not found: {fullRuntimeRootPath}");
		}

		List<string> missingPaths = RequiredManagedFiles
			.Concat(RequiredNativeFiles)
			.Select(fileName => Path.Combine(fullRuntimeRootPath, fileName))
			.Where(path => !File.Exists(path))
			.ToList();

		foreach (string directoryName in RequiredDirectories)
		{
			string directoryPath = Path.Combine(fullRuntimeRootPath, directoryName);
			if (!Directory.Exists(directoryPath))
			{
				missingPaths.Add(directoryPath);
			}
		}

		missingPaths.AddRange(RequiredLocaleFiles
			.Select(fileName => Path.Combine(fullRuntimeRootPath, fileName))
			.Where(path => !File.Exists(path)));

		if (missingPaths.Count == 0)
		{
			return;
		}

		throw new InvalidOperationException(
			"CEF Linux runtime root is incomplete. Missing required CEF runtime paths: " +
			string.Join(", ", missingPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) +
			$". runtimeRootPath='{fullRuntimeRootPath}'.");
	}
}

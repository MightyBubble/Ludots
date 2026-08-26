using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Ludots.UI.Browser.Cef;

internal static class CefRuntimeLayoutPreflight
{
	private static readonly string[] RequiredFiles =
	{
		"CefSharp.dll",
		"CefSharp.BrowserSubprocess.Core.dll",
		"CefSharp.BrowserSubprocess.dll",
		"CefSharp.BrowserSubprocess.exe",
		"CefSharp.BrowserSubprocess.runtimeconfig.json",
		"CefSharp.Core.dll",
		"CefSharp.Core.Runtime.dll",
		"CefSharp.OffScreen.dll",
		"chrome_100_percent.pak",
		"chrome_200_percent.pak",
		"chrome_elf.dll",
		"d3dcompiler_47.dll",
		"dxcompiler.dll",
		"dxil.dll",
		"Ijwhost.dll",
		"libcef.dll",
		"libEGL.dll",
		"libGLESv2.dll",
		"resources.pak",
		"icudtl.dat",
		"v8_context_snapshot.bin",
		"vk_swiftshader.dll",
		"vk_swiftshader_icd.json",
		"vulkan-1.dll"
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
		if (OperatingSystem.IsWindows())
		{
			return;
		}

		throw new PlatformNotSupportedException(
			"Ludots.UI.Browser.Cef ships CefSharp.OffScreen.NETCore win-x64 natives only " +
			$"(TFM net8.0-windows / RID win-x64). Current OS '{RuntimeInformation.OSDescription}' cannot load libcef.dll. " +
			"Disable browserRuntime on this host, or register a Linux-capable provider such as Ultralight.");
	}

	public static void EnsureComplete(string runtimeRootPath)
	{
		EnsureHostPlatformSupported();
		if (string.IsNullOrWhiteSpace(runtimeRootPath))
		{
			throw new ArgumentException("CEF runtime root path is required.", nameof(runtimeRootPath));
		}

		string fullRuntimeRootPath = Path.GetFullPath(runtimeRootPath);
		if (!Directory.Exists(fullRuntimeRootPath))
		{
			throw new DirectoryNotFoundException($"CEF runtime root was not found: {fullRuntimeRootPath}");
		}

		List<string> missingPaths = RequiredFiles
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
			"CEF runtime root is incomplete. Missing required CEF runtime paths: " +
			string.Join(", ", missingPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) +
			$". runtimeRootPath='{fullRuntimeRootPath}'.");
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Ludots.UI.Browser.Ultralight;

public static class UltralightRuntimeLayoutPreflight
{
	private static readonly string[] RequiredManagedFiles =
	{
		"UltralightNet.dll",
		"UltralightNet.Binaries.dll",
		"UltralightNet.AppCore.dll",
		"UltralightNet.AppCore.Binaries.dll",
		"Ludots.UI.Browser.Ultralight.dll"
	};

	public static void EnsureComplete(string runtimeRootPath)
	{
		if (string.IsNullOrWhiteSpace(runtimeRootPath))
		{
			throw new ArgumentException("Ultralight runtime root path is required.", nameof(runtimeRootPath));
		}

		string fullRuntimeRootPath = Path.GetFullPath(runtimeRootPath);
		if (!Directory.Exists(fullRuntimeRootPath))
		{
			throw new DirectoryNotFoundException($"Ultralight runtime root was not found: {fullRuntimeRootPath}");
		}

		List<string> missingPaths = RequiredManagedFiles
			.Select(fileName => Path.Combine(fullRuntimeRootPath, fileName))
			.Where(path => !File.Exists(path))
			.ToList();

		foreach (string nativePath in EnumerateRequiredNativeLibraryPaths(fullRuntimeRootPath))
		{
			if (!File.Exists(nativePath))
			{
				missingPaths.Add(nativePath);
			}
		}

		if (missingPaths.Count == 0)
		{
			return;
		}

		throw new InvalidOperationException(
			"Ultralight runtime root is incomplete. Missing required Ultralight runtime paths: " +
			string.Join(", ", missingPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) +
			$". runtimeRootPath='{fullRuntimeRootPath}'.");
	}

	public static IReadOnlyList<string> EnumerateRequiredNativeLibraryPaths(string runtimeRootPath)
	{
		string fullRuntimeRootPath = Path.GetFullPath(runtimeRootPath);
		if (OperatingSystem.IsWindows())
		{
			return new[]
			{
				Path.Combine(fullRuntimeRootPath, "Ultralight.dll"),
				Path.Combine(fullRuntimeRootPath, "UltralightCore.dll"),
				Path.Combine(fullRuntimeRootPath, "WebCore.dll"),
				Path.Combine(fullRuntimeRootPath, "AppCore.dll")
			};
		}

		if (OperatingSystem.IsMacOS())
		{
			return PreferFlattenedOrRid(
				fullRuntimeRootPath,
				"osx-x64",
				"libUltralight.dylib",
				"libUltralightCore.dylib",
				"libWebCore.dylib",
				"libAppCore.dylib");
		}

		if (OperatingSystem.IsLinux())
		{
			return PreferFlattenedOrRid(
				fullRuntimeRootPath,
				"linux-x64",
				"libUltralight.so",
				"libUltralightCore.so",
				"libWebCore.so",
				"libAppCore.so");
		}

		throw new PlatformNotSupportedException(
			$"Ultralight native libraries are not defined for OS '{RuntimeInformation.OSDescription}'.");
	}

	private static IReadOnlyList<string> PreferFlattenedOrRid(
		string runtimeRootPath,
		string rid,
		params string[] fileNames)
	{
		string[] flattened = fileNames
			.Select(fileName => Path.Combine(runtimeRootPath, fileName))
			.ToArray();
		if (flattened.All(File.Exists))
		{
			return flattened;
		}

		return fileNames
			.Select(fileName => Path.Combine(runtimeRootPath, "runtimes", rid, "native", fileName))
			.ToArray();
	}
}

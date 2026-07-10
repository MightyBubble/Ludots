using System;
using System.IO;
using System.Runtime.InteropServices;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.Cef;

internal static class CefV8BufferNativeBridge
{
	private const string NativeLibraryName = "Ludots.UI.Browser.Cef.Native";
	private static bool _loaded;

	public static void EnsureLoaded(string runtimeRootPath)
	{
		if (_loaded)
		{
			return;
		}

		string fullRuntimeRootPath = Path.GetFullPath(runtimeRootPath);
		string libraryPath = Path.Combine(fullRuntimeRootPath, $"{NativeLibraryName}.dll");
		if (!File.Exists(libraryPath))
		{
			throw new FileNotFoundException("Ludots CEF native V8 buffer bridge was not found.", libraryPath);
		}

		NativeLibrary.Load(libraryPath);
		_loaded = true;
	}

	public static void SyncNativeRegions(CefV8BufferRegistry registry, BrowserSharedBufferBridge sharedBuffers)
	{
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(sharedBuffers);
		BrowserSharedBufferNativeRegion[] regions = sharedBuffers.GetNativeRegionsSnapshot();
		registry.Write(regions);
	}
}

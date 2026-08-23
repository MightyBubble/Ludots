using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.ComponentModel;
using CefSharp;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.Cef;

internal static class CefProcessRuntime
{
	private static readonly object Sync = new();
	private static readonly CefBrowserSurfaceRegistry Registry = new();

	private static int _runtimeOwnerCount;
	private static string? _defaultAssemblyRootPath;
	private static bool _defaultAssemblyResolverRegistered;
	private static bool _hostExitShutdownRequested;
	private static string? _nativeRuntimeRootPath;
	private static IntPtr _dllDirectoryCookie;
	private static IntPtr _libcefHandle;

	public static CefBrowserSurfaceRegistry SurfaceRegistry => Registry;

	public static int RuntimeOwnerCount
	{
		get
		{
			lock (Sync)
			{
				return _runtimeOwnerCount;
			}
		}
	}

	public static void PrepareAssemblyResolution(string runtimeRootPath)
	{
		CefRuntimeLayoutPreflight.EnsureComplete(runtimeRootPath);
		EnsureDefaultAssemblyResolution(runtimeRootPath);
	}

	public static void AcquireRuntimeOwner(CefBrowserRuntimeOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		CefRuntimeLayoutPreflight.EnsureComplete(options.RuntimeRootPath);
		lock (Sync)
		{
			EnsureDefaultAssemblyResolution(options.RuntimeRootPath);

			if (_hostExitShutdownRequested)
			{
				throw new InvalidOperationException("CEF host exit shutdown has already been requested and cannot be re-initialized in this process.");
			}

			if (global::CefSharp.Cef.IsShutdown)
			{
				throw new InvalidOperationException("CEF has already been shut down and cannot be re-initialized in this process.");
			}

			if (global::CefSharp.Cef.IsInitialized != true)
			{
				PrepareNativeRuntime(options.RuntimeRootPath);
				global::CefSharp.OffScreen.CefSettings settings = BuildSettings(options);
				bool initialized = global::CefSharp.Cef.Initialize(settings, performDependencyCheck: false);
				if (!initialized)
				{
					throw new InvalidOperationException("CEF initialization returned false.");
				}
			}

			_runtimeOwnerCount++;
		}
	}

	public static void ReleaseRuntimeOwner()
	{
		lock (Sync)
		{
			if (_runtimeOwnerCount > 0)
			{
				_runtimeOwnerCount--;
			}
		}
	}

	public static void ShutdownForHostExit()
	{
		lock (Sync)
		{
			if (_hostExitShutdownRequested)
			{
				return;
			}

			_hostExitShutdownRequested = true;
			_runtimeOwnerCount = 0;
			if (global::CefSharp.Cef.IsInitialized == true && !global::CefSharp.Cef.IsShutdown)
			{
				global::CefSharp.Cef.Shutdown();
			}

			RemoveDefaultAssemblyResolution();
		}
	}

	private static void PrepareNativeRuntime(string runtimeRootPath)
	{
		string fullRuntimeRootPath = Path.GetFullPath(runtimeRootPath);
		if (_nativeRuntimeRootPath != null)
		{
			if (!string.Equals(_nativeRuntimeRootPath, fullRuntimeRootPath, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException(
					$"CEF native runtime was already prepared for '{_nativeRuntimeRootPath}', cannot switch to '{fullRuntimeRootPath}'.");
			}

			return;
		}

		CefRuntimeLayoutPreflight.EnsureComplete(fullRuntimeRootPath);
		string libcefPath = Path.Combine(fullRuntimeRootPath, "libcef.dll");
		EnsureNoConflictingLoadedLibcef(libcefPath);

		if (OperatingSystem.IsWindows())
		{
			_dllDirectoryCookie = WindowsNativeMethods.AddDllDirectory(fullRuntimeRootPath);
			if (_dllDirectoryCookie == IntPtr.Zero)
			{
				throw CreateWin32Exception($"Failed to add CEF runtime directory to DLL search path: {fullRuntimeRootPath}");
			}

			_libcefHandle = WindowsNativeMethods.LoadLibraryEx(
				libcefPath,
				IntPtr.Zero,
				WindowsNativeMethods.LoadLibrarySearchDefaultDirs | WindowsNativeMethods.LoadLibrarySearchDllLoadDir);
			if (_libcefHandle == IntPtr.Zero)
			{
				throw CreateWin32Exception($"Failed to load CEF native library from '{libcefPath}'.");
			}
		}
		else
		{
			_libcefHandle = NativeLibrary.Load(libcefPath);
		}

		_nativeRuntimeRootPath = fullRuntimeRootPath;
	}

	private static void EnsureNoConflictingLoadedLibcef(string expectedLibcefPath)
	{
		IntPtr loadedModule = OperatingSystem.IsWindows()
			? WindowsNativeMethods.GetModuleHandle("libcef.dll")
			: IntPtr.Zero;
		if (loadedModule == IntPtr.Zero)
		{
			return;
		}

		string loadedPath = ResolveLoadedModulePath(loadedModule);
		if (!string.Equals(
			Path.GetFullPath(loadedPath),
			Path.GetFullPath(expectedLibcefPath),
			StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				$"A different libcef.dll is already loaded in this process: '{loadedPath}'. Expected '{expectedLibcefPath}'.");
		}

		_libcefHandle = loadedModule;
	}

	private static string ResolveLoadedModulePath(IntPtr moduleHandle)
	{
		if (!OperatingSystem.IsWindows())
		{
			return string.Empty;
		}

		var buffer = new char[32768];
		int length = WindowsNativeMethods.GetModuleFileName(moduleHandle, buffer, buffer.Length);
		if (length == 0)
		{
			throw CreateWin32Exception("Failed to resolve loaded libcef.dll path.");
		}

		return new string(buffer, 0, length);
	}

	private static Exception CreateWin32Exception(string message)
	{
		return new Win32Exception(Marshal.GetLastWin32Error(), message);
	}

	private static void EnsureDefaultAssemblyResolution(string runtimeRootPath)
	{
		string fullRuntimeRootPath = Path.GetFullPath(runtimeRootPath);
		if (_defaultAssemblyResolverRegistered)
		{
			if (!string.Equals(_defaultAssemblyRootPath, fullRuntimeRootPath, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException(
					$"CEF assembly resolver was already registered for '{_defaultAssemblyRootPath}', cannot switch to '{fullRuntimeRootPath}'.");
			}

			return;
		}

		_defaultAssemblyRootPath = fullRuntimeRootPath;
		AssemblyLoadContext.Default.Resolving += ResolveCefSharpAssemblyFromRuntimeRoot;
		_defaultAssemblyResolverRegistered = true;

		LoadProcessAssemblyIfPresent("CefSharp.Core.Runtime");
		LoadProcessAssemblyIfPresent("CefSharp.Core");
		LoadProcessAssemblyIfPresent("CefSharp");
		LoadProcessAssemblyIfPresent("CefSharp.OffScreen");
	}

	private static void RemoveDefaultAssemblyResolution()
	{
		if (!_defaultAssemblyResolverRegistered)
		{
			return;
		}

		AssemblyLoadContext.Default.Resolving -= ResolveCefSharpAssemblyFromRuntimeRoot;
		_defaultAssemblyResolverRegistered = false;
		_defaultAssemblyRootPath = null;
	}

	private static Assembly? ResolveCefSharpAssemblyFromRuntimeRoot(AssemblyLoadContext context, AssemblyName assemblyName)
	{
		if (!IsCefSharpAssemblyName(assemblyName.Name) || string.IsNullOrWhiteSpace(_defaultAssemblyRootPath))
		{
			return null;
		}

		Assembly? loadedAssembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate =>
			AssemblyName.ReferenceMatchesDefinition(candidate.GetName(), assemblyName));
		if (loadedAssembly != null)
		{
			return loadedAssembly;
		}

		string assemblyPath = Path.Combine(_defaultAssemblyRootPath, $"{assemblyName.Name}.dll");
		return File.Exists(assemblyPath)
			? AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath)
			: null;
	}

	private static void LoadProcessAssemblyIfPresent(string assemblyName)
	{
		if (string.IsNullOrWhiteSpace(_defaultAssemblyRootPath))
		{
			return;
		}

		string assemblyPath = Path.Combine(_defaultAssemblyRootPath, $"{assemblyName}.dll");
		if (!File.Exists(assemblyPath))
		{
			return;
		}

		AssemblyName assemblyIdentity = AssemblyName.GetAssemblyName(assemblyPath);
		bool alreadyLoaded = AssemblyLoadContext.Default.Assemblies.Any(candidate =>
			AssemblyName.ReferenceMatchesDefinition(candidate.GetName(), assemblyIdentity));
		if (!alreadyLoaded)
		{
			AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
		}
	}

	private static bool IsCefSharpAssemblyName(string? assemblyName)
	{
		return !string.IsNullOrWhiteSpace(assemblyName) &&
			assemblyName.StartsWith("CefSharp", StringComparison.OrdinalIgnoreCase);
	}

	private static global::CefSharp.OffScreen.CefSettings BuildSettings(CefBrowserRuntimeOptions options)
	{
		string runtimeRoot = options.RuntimeRootPath;
		CefRuntimeLayoutPreflight.EnsureComplete(runtimeRoot);

		string subprocessPath = Path.Combine(runtimeRoot, "CefSharp.BrowserSubprocess.exe");
		string localesPath = Path.Combine(runtimeRoot, "locales");

		string cacheRoot = options.CacheRootPath;
		string cachePath = Path.Combine(cacheRoot, "Default");
		Directory.CreateDirectory(cacheRoot);
		Directory.CreateDirectory(cachePath);

		var settings = new global::CefSharp.OffScreen.CefSettings
		{
			WindowlessRenderingEnabled = true,
			BackgroundColor = global::CefSharp.Cef.ColorSetARGB(0, 0, 0, 0),
			BrowserSubprocessPath = subprocessPath,
			LocalesDirPath = localesPath,
			ResourcesDirPath = runtimeRoot,
			RootCachePath = cacheRoot,
			CachePath = cachePath,
			LogFile = Path.Combine(cacheRoot, "cef.log"),
			LogSeverity = global::CefSharp.LogSeverity.Verbose
		};

		// CEF 表面承载 UI 面板（启动器/面板/流程图）而非游戏视口：软件合成即可，
		// 并规避离屏合成下 GPU 驱动栈不稳定连坐宿主进程（实测 LiveKernelEvent + gpu adapter 探测失败）。
		settings.CefCommandLineArgs.Add("disable-gpu");
		settings.CefCommandLineArgs.Add("disable-gpu-compositing");

		// 嵌入式浏览器禁绝回连 Google 与组件自更新：新档案下的组件安装流程会触发
		// 浏览器重启请求，CefSharp 无宿主接线时退化为 exit() 直接带走宿主进程（实测 exit 127）。
		settings.CefCommandLineArgs.Add("disable-component-update");
		settings.CefCommandLineArgs.Add("disable-background-networking");

		settings.RegisterScheme(new CefCustomScheme
		{
			SchemeName = CefBrowserRuntime.LocalAppSchemeName,
			DomainName = CefBrowserRuntime.LocalAppHostName,
			IsStandard = true,
			IsSecure = true,
			IsCorsEnabled = true,
			IsFetchEnabled = true,
			SchemeHandlerFactory = new CefBrowserSchemeHandlerFactory(Registry)
		});

		return settings;
	}

	private static class WindowsNativeMethods
	{
		public const uint LoadLibrarySearchDllLoadDir = 0x00000100;
		public const uint LoadLibrarySearchDefaultDirs = 0x00001000;

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		public static extern IntPtr AddDllDirectory(string newDirectory);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		public static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		public static extern IntPtr GetModuleHandle(string lpModuleName);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		public static extern int GetModuleFileName(IntPtr hModule, [Out] char[] lpFilename, int nSize);
	}
}

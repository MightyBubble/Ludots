using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
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
		EnsureDefaultAssemblyResolution(runtimeRootPath);
	}

	public static void AcquireRuntimeOwner(CefBrowserRuntimeOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
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
				global::CefSharp.OffScreen.CefSettings settings = BuildSettings(options);
				bool initialized = global::CefSharp.Cef.Initialize(settings, performDependencyCheck: true);
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
		if (!Directory.Exists(runtimeRoot))
		{
			throw new DirectoryNotFoundException($"CEF runtime root was not found: {runtimeRoot}");
		}

		string subprocessPath = Path.Combine(runtimeRoot, "CefSharp.BrowserSubprocess.exe");
		string localesPath = Path.Combine(runtimeRoot, "locales");
		if (!File.Exists(subprocessPath))
		{
			throw new FileNotFoundException("CEF browser subprocess executable was not found.", subprocessPath);
		}
		if (!Directory.Exists(localesPath))
		{
			throw new DirectoryNotFoundException($"CEF locales directory was not found: {localesPath}");
		}

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
			LogFile = Path.Combine(cacheRoot, "cef.log")
		};

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
}

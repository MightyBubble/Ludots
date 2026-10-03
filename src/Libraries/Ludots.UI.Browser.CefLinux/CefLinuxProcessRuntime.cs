using System;
using System.IO;
using System.Runtime.InteropServices;
using global::CefNet;
using Ludots.UI.Browser;
using Ludots.UI.Browser.CefLinux.Core;

namespace Ludots.UI.Browser.CefLinux;

internal static class CefLinuxProcessRuntime
{
	private static readonly object Sync = new();
	private static readonly CefLinuxBrowserSurfaceRegistry Registry = new();

	private static int _runtimeOwnerCount;
	private static bool _hostExitShutdownRequested;
	private static string? _nativeRuntimeRootPath;
	private static CefLinuxMessagePump? _messagePump;

	public static CefLinuxBrowserSurfaceRegistry SurfaceRegistry => Registry;

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

	public static void AcquireRuntimeOwner(CefLinuxBrowserRuntimeOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		CefLinuxRuntimeLayoutPreflight.EnsureComplete(options.RuntimeRootPath);
		lock (Sync)
		{
			if (_hostExitShutdownRequested)
			{
				throw new InvalidOperationException("CEF host exit shutdown has already been requested and cannot be re-initialized in this process.");
			}

			if (_runtimeOwnerCount > 0)
			{
				_runtimeOwnerCount++;
				return;
			}

			CefLinuxRuntimeLayoutPreflight.EnsureHostPlatformSupported();
			Initialize(options);
			_runtimeOwnerCount = 1;
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

			CefLinuxMessagePump? pump = _messagePump;
			if (pump == null)
			{
				return;
			}

			pump.RequestShutdown();
			pump.Join();
			_messagePump = null;
			_nativeRuntimeRootPath = null;
		}
	}

	private static void Initialize(CefLinuxBrowserRuntimeOptions options)
	{
		string fullRuntimeRootPath = Path.GetFullPath(options.RuntimeRootPath);
		if (_nativeRuntimeRootPath != null)
		{
			if (!string.Equals(_nativeRuntimeRootPath, fullRuntimeRootPath, StringComparison.Ordinal))
			{
				throw new InvalidOperationException(
					$"CEF native runtime was already prepared for '{_nativeRuntimeRootPath}', cannot switch to '{fullRuntimeRootPath}'.");
			}

			return;
		}

		CefLinuxRuntimeLayoutPreflight.EnsureComplete(fullRuntimeRootPath);

		// 先按绝对路径装载 libcef，让 CefNet 的 libcef P/Invoke 解析到包内这份原生库。
		NativeLibrary.Load(Path.Combine(fullRuntimeRootPath, "libcef.so"));
		_nativeRuntimeRootPath = fullRuntimeRootPath;

		CefLinuxMessagePump pump = null!;
		pump = new CefLinuxMessagePump(() => InitializeOnPumpThread(options, fullRuntimeRootPath, pump));
		pump.Start();
		pump.WaitInitializeCompleted();
		if (pump.InitializeFailure is { } failure)
		{
			_hostExitShutdownRequested = true;
			_nativeRuntimeRootPath = null;
			throw new InvalidOperationException(
				$"CEF Linux runtime failed to initialize for runtimeRootPath '{fullRuntimeRootPath}'.",
				failure);
		}

		_messagePump = pump;
	}

	private static void InitializeOnPumpThread(
		CefLinuxBrowserRuntimeOptions options,
		string runtimeRoot,
		CefLinuxMessagePump scheduler)
	{
		string subprocessPath = Path.Combine(runtimeRoot, "Ludots.UI.Browser.CefLinux.Subprocess");
		string localesPath = Path.Combine(runtimeRoot, "locales");

		string cacheRoot = options.CacheRootPath;
		string cachePath = Path.Combine(cacheRoot, "Default");
		Directory.CreateDirectory(cacheRoot);
		Directory.CreateDirectory(cachePath);

		var settings = new CefSettings
		{
			// Linux 游戏宿主不带 setuid chrome-sandbox；离屏渲染也不弹原生窗口。
			NoSandbox = true,
			WindowlessRenderingEnabled = true,
			// Linux 上 multi_threaded_message_loop 不可用，外部消息泵由本 provider 的泵线程驱动。
			ExternalMessagePump = true,
			BrowserSubprocessPath = subprocessPath,
			ResourcesDirPath = runtimeRoot,
			LocalesDirPath = localesPath,
			RootCachePath = cacheRoot,
			CachePath = cachePath,
			LogFile = Path.Combine(cacheRoot, "cef.log")
		};

		var app = new CefLinuxProcessApp(scheduler);
		CefApi.Initialize(CefMainArgs.CreateDefault(), settings, app, IntPtr.Zero);
		CefApi.RegisterSchemeHandlerFactory(
			BrowserLocalAppUri.Scheme,
			BrowserLocalAppUri.Host,
			new CefLinuxSchemeHandlerFactory(Registry));
	}
}

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UltralightNet;
using UltralightNet.AppCore;
using UltralightNet.Platform;

namespace Ludots.UI.Browser.Ultralight;

internal static class UltralightProcessRuntime
{
	private static readonly object Gate = new();
	private static readonly ConcurrentQueue<UltralightWorkItem> Work = new();
	private static readonly AutoResetEvent WorkSignal = new(false);

	private static Thread? _ultralightThread;
	private static int _ultralightThreadId;
	private static volatile bool _dispatcherRunning;
	private static volatile bool _dispatcherStopped;
	private static int _runtimeOwnerCount;
	private static bool _hostExitShutdownRequested;
	private static string? _runtimeRootPath;
	private static string? _resourceDirectoryPath;
	private static Renderer? _renderer;

	public static string ResourceDirectoryPath
	{
		get
		{
			string? path = _resourceDirectoryPath;
			return path
				?? throw new InvalidOperationException("Ultralight resource directory has not been acquired.");
		}
	}

	public static void AcquireRuntimeOwner(UltralightBrowserRuntimeOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		UltralightRuntimeLayoutPreflight.EnsureComplete(options.RuntimeRootPath);
		EnsureDispatcherStarted();
		Invoke(() =>
		{
			if (_hostExitShutdownRequested)
			{
				throw new InvalidOperationException(
					"Ultralight host exit shutdown has already been requested and cannot be re-initialized in this process.");
			}

			if (_renderer == null)
			{
				PrepareNativeSearchPath(options.RuntimeRootPath);
				_resourceDirectoryPath = EnsureResourceDirectory(options.RuntimeRootPath);
				Directory.CreateDirectory(options.CacheRootPath);
				EnsureIcuDataPresent(_resourceDirectoryPath);

				// Absolute ResourcePathPrefix makes AppCore FileExists(icudt67l.dat) return false and abort.
				// Volume-root FileSystem + relative ResourcePathPrefix keeps absolute file:// navigation working.
				(string fileSystemRoot, string resourcePathPrefix) = ResolvePlatformFileLayout(_resourceDirectoryPath);

				ULPlatform.EnableDefaultLogger = true;
				ULPlatform.SetDefaultFontLoader = true;
				ULPlatform.SetDefaultFileSystem = true;
				ULPlatform.ErrorMissingResources = true;
				ULPlatform.ErrorWrongThread = true;
				AppCoreMethods.SetPlatformFontLoader();
				AppCoreMethods.ulEnablePlatformFileSystem(fileSystemRoot);
				AppCoreMethods.ulEnableDefaultLogger(Path.Combine(_resourceDirectoryPath, "ultralight.log"));

				var config = new ULConfig
				{
					ResourcePathPrefix = resourcePathPrefix,
					CachePath = options.CacheRootPath
				};
				_renderer = ULPlatform.CreateRenderer(config, dispose: true);
				_runtimeRootPath = options.RuntimeRootPath;
			}
			else if (!string.Equals(_runtimeRootPath, options.RuntimeRootPath, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException(
					$"Ultralight native runtime was already prepared for '{_runtimeRootPath}', cannot switch to '{options.RuntimeRootPath}'.");
			}

			_runtimeOwnerCount++;
		});
	}

	public static void ReleaseRuntimeOwner()
	{
		Invoke(() =>
		{
			if (_runtimeOwnerCount > 0)
			{
				_runtimeOwnerCount--;
			}
		});
	}

	public static View CreateView(uint width, uint height, ULViewConfig viewConfig)
	{
		return Invoke(() =>
		{
			Renderer renderer = _renderer
				?? throw new InvalidOperationException("Ultralight process runtime has not been acquired.");
			return renderer.CreateView(width, height, viewConfig);
		});
	}

	public static void UpdateAndRender()
	{
		Invoke(() =>
		{
			_renderer?.Update();
			_renderer?.Render();
		});
	}

	public static void Run(Action action)
	{
		ArgumentNullException.ThrowIfNull(action);
		Invoke(action);
	}

	public static T Run<T>(Func<T> func)
	{
		ArgumentNullException.ThrowIfNull(func);
		return Invoke(func);
	}

	public static Task RunAsync(Action action, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(action);
		return InvokeAsync(action, cancellationToken);
	}

	public static Task<T> RunAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(func);
		return InvokeAsync(func, cancellationToken);
	}

	public static void ShutdownForHostExit()
	{
		if (_dispatcherStopped)
		{
			return;
		}

		EnsureDispatcherStarted();
		Invoke(() =>
		{
			if (_hostExitShutdownRequested)
			{
				return;
			}

			_hostExitShutdownRequested = true;
			_runtimeOwnerCount = 0;
			_renderer?.Dispose();
			_renderer = null;
			_runtimeRootPath = null;
			_resourceDirectoryPath = null;
		});

		lock (Gate)
		{
			_dispatcherStopped = true;
			_dispatcherRunning = false;
			WorkSignal.Set();
			Thread? thread = _ultralightThread;
			_ultralightThread = null;
			if (thread != null && thread.ManagedThreadId != Environment.CurrentManagedThreadId)
			{
				if (!thread.Join(TimeSpan.FromSeconds(5)))
				{
					throw new TimeoutException("Ultralight dispatcher thread did not exit within 5 seconds during host shutdown.");
				}
			}
		}
	}

	private static void EnsureDispatcherStarted()
	{
		lock (Gate)
		{
			if (_dispatcherStopped)
			{
				throw new ObjectDisposedException(
					nameof(UltralightProcessRuntime),
					"Ultralight dispatcher has already been shut down for host exit.");
			}

			if (_dispatcherRunning && _ultralightThread != null)
			{
				return;
			}

			_dispatcherRunning = true;
			_ultralightThread = new Thread(DispatcherLoop)
			{
				IsBackground = true,
				Name = "Ludots.Ultralight"
			};
			_ultralightThread.Start();
		}
	}

	private static void DispatcherLoop()
	{
		_ultralightThreadId = Environment.CurrentManagedThreadId;
		while (_dispatcherRunning)
		{
			while (Work.TryDequeue(out UltralightWorkItem? item))
			{
				item.Execute();
			}

			WorkSignal.WaitOne(16);
		}

		while (Work.TryDequeue(out UltralightWorkItem? item))
		{
			item.Execute();
		}
	}

	private static void ThrowIfDispatcherStopped()
	{
		if (_dispatcherStopped || _hostExitShutdownRequested)
		{
			throw new ObjectDisposedException(
				nameof(UltralightProcessRuntime),
				"Ultralight dispatcher has already been shut down for host exit.");
		}
	}

	private static void Invoke(Action action)
	{
		if (Environment.CurrentManagedThreadId == _ultralightThreadId)
		{
			action();
			return;
		}

		ThrowIfDispatcherStopped();
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Work.Enqueue(new UltralightWorkItem(() =>
		{
			try
			{
				action();
				completion.TrySetResult();
			}
			catch (Exception ex)
			{
				completion.TrySetException(ex);
			}
		}));
		WorkSignal.Set();
		completion.Task.GetAwaiter().GetResult();
	}

	private static T Invoke<T>(Func<T> func)
	{
		if (Environment.CurrentManagedThreadId == _ultralightThreadId)
		{
			return func();
		}

		ThrowIfDispatcherStopped();
		var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		Work.Enqueue(new UltralightWorkItem(() =>
		{
			try
			{
				completion.TrySetResult(func());
			}
			catch (Exception ex)
			{
				completion.TrySetException(ex);
			}
		}));
		WorkSignal.Set();
		return completion.Task.GetAwaiter().GetResult();
	}

	private static Task InvokeAsync(Action action, CancellationToken cancellationToken)
	{
		if (Environment.CurrentManagedThreadId == _ultralightThreadId)
		{
			action();
			return Task.CompletedTask;
		}

		ThrowIfDispatcherStopped();
		cancellationToken.ThrowIfCancellationRequested();
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Work.Enqueue(new UltralightWorkItem(() =>
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				action();
				completion.TrySetResult();
			}
			catch (Exception ex)
			{
				completion.TrySetException(ex);
			}
		}));
		WorkSignal.Set();
		return completion.Task.WaitAsync(cancellationToken);
	}

	private static Task<T> InvokeAsync<T>(Func<T> func, CancellationToken cancellationToken)
	{
		if (Environment.CurrentManagedThreadId == _ultralightThreadId)
		{
			return Task.FromResult(func());
		}

		ThrowIfDispatcherStopped();
		cancellationToken.ThrowIfCancellationRequested();
		var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		Work.Enqueue(new UltralightWorkItem(() =>
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				completion.TrySetResult(func());
			}
			catch (Exception ex)
			{
				completion.TrySetException(ex);
			}
		}));
		WorkSignal.Set();
		return completion.Task.WaitAsync(cancellationToken);
	}

	private static void PrepareNativeSearchPath(string runtimeRootPath)
	{
		string fullRoot = Path.GetFullPath(runtimeRootPath);
		string? nativeDir = null;
		foreach (string nativePath in UltralightRuntimeLayoutPreflight.EnumerateRequiredNativeLibraryPaths(fullRoot))
		{
			nativeDir = Path.GetDirectoryName(nativePath);
			break;
		}

		if (string.IsNullOrWhiteSpace(nativeDir) || !Directory.Exists(nativeDir))
		{
			return;
		}

		string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		if (!pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
			    .Any(entry => string.Equals(Path.GetFullPath(entry), Path.GetFullPath(nativeDir), StringComparison.OrdinalIgnoreCase)))
		{
			Environment.SetEnvironmentVariable("PATH", nativeDir + Path.PathSeparator + pathEnv);
		}

		if (!OperatingSystem.IsWindows())
		{
			string ldEnvName = OperatingSystem.IsMacOS() ? "DYLD_LIBRARY_PATH" : "LD_LIBRARY_PATH";
			string ldEnv = Environment.GetEnvironmentVariable(ldEnvName) ?? string.Empty;
			if (!ldEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
				    .Any(entry => string.Equals(Path.GetFullPath(entry), Path.GetFullPath(nativeDir), StringComparison.OrdinalIgnoreCase)))
			{
				Environment.SetEnvironmentVariable(ldEnvName, nativeDir + Path.PathSeparator + ldEnv);
			}
		}

		foreach (string nativePath in UltralightRuntimeLayoutPreflight.EnumerateRequiredNativeLibraryPaths(fullRoot))
		{
			NativeLibrary.Load(nativePath);
		}
	}

	private static string EnsureResourceDirectory(string runtimeRootPath)
	{
		string resourceDirectory = Path.Combine(Path.GetFullPath(runtimeRootPath), "ultralight-resources");
		Directory.CreateDirectory(resourceDirectory);
		ExtractResource("cacert.pem", Resources.Cacertpem, resourceDirectory);
		ExtractResource("icudt67l.dat", Resources.Icudt67ldat, resourceDirectory);
		return resourceDirectory;
	}

	private static void EnsureIcuDataPresent(string resourceDirectory)
	{
		string icuPath = Path.Combine(resourceDirectory, "icudt67l.dat");
		if (!File.Exists(icuPath) || new FileInfo(icuPath).Length <= 0)
		{
			throw new InvalidOperationException(
				$"Ultralight ICU data is missing or empty at '{icuPath}'. " +
				"The provider package must extract UltralightNet embedded resources before CreateRenderer.");
		}
	}

	private static (string FileSystemRoot, string ResourcePathPrefix) ResolvePlatformFileLayout(string resourceDirectory)
	{
		string fullResourceDirectory = Path.GetFullPath(resourceDirectory);
		if (OperatingSystem.IsWindows())
		{
			string? volumeRoot = Path.GetPathRoot(fullResourceDirectory);
			if (string.IsNullOrWhiteSpace(volumeRoot))
			{
				throw new InvalidOperationException(
					$"Ultralight could not resolve a Windows volume root for resource directory '{fullResourceDirectory}'.");
			}

			if (!fullResourceDirectory.StartsWith(volumeRoot, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException(
					$"Ultralight resource directory '{fullResourceDirectory}' is not under volume root '{volumeRoot}'.");
			}

			string relative = fullResourceDirectory
				.Substring(volumeRoot.Length)
				.Replace('\\', '/')
				.Trim('/');
			return (volumeRoot, relative + "/");
		}

		if (!fullResourceDirectory.StartsWith('/'))
		{
			throw new InvalidOperationException(
				$"Ultralight resource directory must be an absolute Unix path, got '{fullResourceDirectory}'.");
		}

		return ("/", fullResourceDirectory.TrimStart('/') + "/");
	}

	private static void ExtractResource(string fileName, Stream? stream, string resourceDirectory)
	{
		if (stream == null)
		{
			throw new InvalidOperationException($"Ultralight embedded resource '{fileName}' is missing from UltralightNet.");
		}

		string path = Path.Combine(resourceDirectory, fileName);
		using (stream)
		using (FileStream output = File.Create(path))
		{
			stream.CopyTo(output);
		}
	}

	private sealed class UltralightWorkItem
	{
		private readonly Action _action;

		public UltralightWorkItem(Action action)
		{
			_action = action;
		}

		public void Execute() => _action();
	}
}

internal static class UltralightLocalAppStager
{
	private static readonly Regex LocalAssetRegex = new(
		"""(?<attr>src|href)\s*=\s*["'](?<path>(?!https?:|data:|blob:|ludots-app:)[^"']+)["']""",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	public static async Task<string> StageAsync(
		string stagingRoot,
		Uri navigationUri,
		IBrowserResourceResolver resolver,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(stagingRoot);
		string entryPath = string.IsNullOrWhiteSpace(navigationUri.AbsolutePath) || navigationUri.AbsolutePath == "/"
			? "/index.html"
			: navigationUri.AbsolutePath;
		await StageUriAsync(stagingRoot, BrowserLocalAppUri.Create(entryPath), resolver, cancellationToken, recursiveHtml: true)
			.ConfigureAwait(false);
		string physicalEntry = Path.Combine(
			stagingRoot,
			entryPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(physicalEntry))
		{
			throw new FileNotFoundException(
				$"Ultralight failed to stage local app entry '{entryPath}' into '{stagingRoot}'.",
				physicalEntry);
		}

		// Ultralight file:// navigations do not expose query to page JS (location.search stays empty).
		// Persist the original query into the staged HTML so zero-code panels keep their topic.
		string fileUrl = new Uri(physicalEntry).AbsoluteUri;
		if (!string.IsNullOrEmpty(navigationUri.Query))
		{
			InjectNavigationQueryBootstrap(physicalEntry, navigationUri.Query);
			fileUrl += navigationUri.Query;
		}

		return fileUrl;
	}

	private static void InjectNavigationQueryBootstrap(string htmlPath, string query)
	{
		string html = File.ReadAllText(htmlPath);
		string bootstrap =
			"<script>window.__LUDOTS_NAV_QUERY__=" +
			System.Text.Json.JsonSerializer.Serialize(query) +
			";</script>";
		if (html.Contains("__LUDOTS_NAV_QUERY__", StringComparison.Ordinal))
		{
			return;
		}

		int head = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
		if (head >= 0)
		{
			int insertAt = html.IndexOf('>', head);
			if (insertAt >= 0)
			{
				File.WriteAllText(htmlPath, html.Insert(insertAt + 1, bootstrap));
				return;
			}
		}

		File.WriteAllText(htmlPath, bootstrap + html);
	}

	private static async Task StageUriAsync(
		string stagingRoot,
		Uri uri,
		IBrowserResourceResolver resolver,
		CancellationToken cancellationToken,
		bool recursiveHtml)
	{
		BrowserResource? resource = await resolver.ResolveAsync(uri, cancellationToken).ConfigureAwait(false);
		if (resource == null)
		{
			return;
		}

		string relative = uri.AbsolutePath.TrimStart('/');
		if (string.IsNullOrWhiteSpace(relative))
		{
			relative = "index.html";
		}

		string physical = Path.Combine(stagingRoot, relative.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
		await File.WriteAllBytesAsync(physical, resource.Content.ToArray(), cancellationToken).ConfigureAwait(false);

		if (!recursiveHtml ||
		    !resource.ContentType.Contains("html", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		string html = Encoding.UTF8.GetString(resource.Content.Span);
		foreach (Match match in LocalAssetRegex.Matches(html))
		{
			string assetPath = match.Groups["path"].Value.Trim();
			if (string.IsNullOrWhiteSpace(assetPath) || assetPath.StartsWith('#'))
			{
				continue;
			}

			string normalized = assetPath.StartsWith('/')
				? assetPath
				: "/" + assetPath.TrimStart('.', '/');
			await StageUriAsync(
					stagingRoot,
					BrowserLocalAppUri.Create(normalized),
					resolver,
					cancellationToken,
					recursiveHtml: false)
				.ConfigureAwait(false);
		}
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;

namespace Ludots.UI.Browser;

public sealed class BrowserRuntimeProviderLoadOptions
{
	public BrowserRuntimeProviderLoadOptions(
		IDictionary<string, object> services,
		string providerAssemblyPath,
		string providerHostTypeName)
	{
		Services = services ?? throw new ArgumentNullException(nameof(services));
		if (string.IsNullOrWhiteSpace(providerAssemblyPath))
		{
			throw new ArgumentException("Provider assembly path is required.", nameof(providerAssemblyPath));
		}
		if (string.IsNullOrWhiteSpace(providerHostTypeName))
		{
			throw new ArgumentException("Provider host type name is required.", nameof(providerHostTypeName));
		}

		ProviderAssemblyPath = providerAssemblyPath;
		ProviderHostTypeName = providerHostTypeName;
	}

	public IDictionary<string, object> Services { get; }

	public string ProviderAssemblyPath { get; }

	public string ProviderHostTypeName { get; }

	public string? RuntimeRootPath { get; init; }

	public string? BrowserCacheRootPath { get; init; }

	public string? ShadowCopyRootPath { get; init; }

	public string ProviderId { get; init; } = "browser-runtime-provider";

	public bool MapRuntimeRootToShadowCopy { get; init; } = true;

	public IReadOnlyCollection<string> DefaultLoadContextAssemblyNamePrefixes { get; init; } = Array.Empty<string>();

	public Action<string>? Log { get; init; }
}

public sealed class BrowserRuntimeProviderLoadHandle : IBrowserRuntimeHostLifecycle
{
	private readonly object _sync = new();
	private readonly IDictionary<string, object> _services;
	private readonly string _providerId;
	private readonly Action<string>? _log;

	private IBrowserRuntime? _runtime;
	private IBrowserRuntimeHostLifecycle? _providerLifecycle;
	private BrowserRuntimeProviderAssemblyLoadContext? _loadContext;
	private bool _shutdownRequested;

	internal BrowserRuntimeProviderLoadHandle(
		IDictionary<string, object> services,
		IBrowserRuntime runtime,
		IBrowserRuntimeHostLifecycle? providerLifecycle,
		BrowserRuntimeProviderAssemblyLoadContext loadContext,
		BrowserRuntimeProviderShadowCopy shadowCopy,
		string providerId,
		Action<string>? log)
	{
		_services = services;
		_runtime = runtime;
		_providerLifecycle = providerLifecycle;
		_loadContext = loadContext;
		SourceAssemblyPath = shadowCopy.SourceAssemblyPath;
		ShadowAssemblyPath = shadowCopy.ShadowAssemblyPath;
		ShadowCopyDirectory = shadowCopy.ShadowDirectoryPath;
		LoadContextWeakReference = new WeakReference(loadContext, trackResurrection: false);
		_providerId = providerId;
		_log = log;
	}

	public IBrowserRuntime Runtime
	{
		get
		{
			lock (_sync)
			{
				return _runtime
					?? throw new ObjectDisposedException(nameof(BrowserRuntimeProviderLoadHandle));
			}
		}
	}

	public string SourceAssemblyPath { get; }

	public string ShadowAssemblyPath { get; }

	public string ShadowCopyDirectory { get; }

	public WeakReference LoadContextWeakReference { get; }

	public bool? LastUnloadCollected { get; private set; }

	public void ShutdownProcessForHostExit()
	{
		WeakReference? weakReference = ReleaseProviderReferencesForUnload();
		if (weakReference == null)
		{
			return;
		}

		ForceFullCollection();
		LastUnloadCollected = !weakReference.IsAlive;
		WriteLog(
			$"Browser runtime provider '{_providerId}' collectible ALC collected={LastUnloadCollected.Value}; shadowCopy='{ShadowCopyDirectory}'.");
	}

	private WeakReference? ReleaseProviderReferencesForUnload()
	{
		IBrowserRuntime? runtime;
		IBrowserRuntimeHostLifecycle? lifecycle;
		BrowserRuntimeProviderAssemblyLoadContext? loadContext;
		lock (_sync)
		{
			if (_shutdownRequested)
			{
				return null;
			}

			_shutdownRequested = true;
			runtime = _runtime;
			lifecycle = _providerLifecycle;
			loadContext = _loadContext;
			_runtime = null;
			_providerLifecycle = null;
			_loadContext = null;
		}

		Exception? failure = null;
		try
		{
			runtime?.DisposeAsync().AsTask().GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			failure = ex;
		}

		try
		{
			lifecycle?.ShutdownProcessForHostExit();
		}
		catch (Exception ex)
		{
			failure = failure == null ? ex : new AggregateException(failure, ex);
		}

		RemoveServiceIfReferenceEquals(BrowserRuntimeServiceNames.BrowserRuntime, runtime);
		RemoveServiceIfReferenceEquals(BrowserRuntimeServiceNames.HostLifecycle, this);
		loadContext?.ReleaseDefaultLoadContextAssemblyResolver();
		loadContext?.Unload();

		if (failure != null)
		{
			ExceptionDispatchInfo.Capture(failure).Throw();
		}

		return LoadContextWeakReference;
	}

	private void RemoveServiceIfReferenceEquals(string key, object? expected)
	{
		if (_services.TryGetValue(key, out object? current) &&
			(expected == null || ReferenceEquals(current, expected)))
		{
			_services.Remove(key);
		}
	}

	private static void ForceFullCollection()
	{
		for (int i = 0; i < 3; i++)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}
	}

	private void WriteLog(string message)
	{
		if (_log != null)
		{
			_log(message);
			return;
		}

		Console.WriteLine(message);
	}
}

public static class BrowserRuntimeProviderLoader
{
	public static BrowserRuntimeProviderLoadHandle Install(BrowserRuntimeProviderLoadOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		KeyValuePair<string, object>[] serviceSnapshot = options.Services.ToArray();
		IBrowserRuntime? installedRuntime = null;
		IBrowserRuntimeHostLifecycle? providerLifecycle = null;
		BrowserRuntimeProviderShadowCopy shadowCopy = BrowserRuntimeProviderShadowCopy.Create(
			options.ProviderAssemblyPath,
			options.ShadowCopyRootPath,
			options.ProviderId);
		string effectiveRuntimeRootPath = shadowCopy.MapRequiredSourcePath(
			options.RuntimeRootPath,
			"browserRuntime.runtimeRootPath",
			options.MapRuntimeRootToShadowCopy);
		var loadContext = new BrowserRuntimeProviderAssemblyLoadContext(
			$"Ludots.BrowserRuntimeProvider.{options.ProviderId}.{Path.GetFileNameWithoutExtension(shadowCopy.ShadowAssemblyPath)}",
			shadowCopy.ShadowAssemblyPath,
			ResolveHostSharedAssemblies(),
			effectiveRuntimeRootPath,
			options.DefaultLoadContextAssemblyNamePrefixes);

		try
		{
			Assembly providerAssembly = loadContext.LoadFromAssemblyPath(shadowCopy.ShadowAssemblyPath);
			Type providerHostType = providerAssembly.GetType(options.ProviderHostTypeName, throwOnError: true)
				?? throw new InvalidOperationException(
					$"Browser runtime provider host type '{options.ProviderHostTypeName}' was not found.");

			MethodInfo installMethod = ResolveInstallMethod(providerHostType);
			object?[] arguments = { options.Services, effectiveRuntimeRootPath, options.BrowserCacheRootPath };

			object? installed = InvokeInstallMethod(installMethod, arguments);
			if (installed is not IBrowserRuntime runtime)
			{
				throw new InvalidOperationException("Browser runtime provider Install did not return an IBrowserRuntime.");
			}

			installedRuntime = runtime;
			providerLifecycle = ResolveProviderLifecycle(options.Services);
			EnsureBrowserRuntimeServiceMatches(options.Services, runtime);

			var handle = new BrowserRuntimeProviderLoadHandle(
				options.Services,
				runtime,
				providerLifecycle,
				loadContext,
				shadowCopy,
				options.ProviderId,
				options.Log);
			options.Services[BrowserRuntimeServiceNames.HostLifecycle] = handle;
			return handle;
		}
		catch (Exception ex)
		{
			Exception? cleanupFailure = CleanupFailedInstall(
				options.Services,
				serviceSnapshot,
				installedRuntime,
				providerLifecycle,
				loadContext);
			if (cleanupFailure != null)
			{
				throw new AggregateException(ex, cleanupFailure);
			}

			throw;
		}
	}

	private static IEnumerable<Assembly> ResolveHostSharedAssemblies()
	{
		yield return typeof(IBrowserRuntime).Assembly;
		yield return typeof(IBrowserRuntimeHostLifecycle).Assembly;
	}

	private static MethodInfo ResolveInstallMethod(Type providerHostType)
	{
		const string methodName = "Install";
		Type[] parameterTypes = { typeof(IDictionary<string, object>), typeof(string), typeof(string) };
		return providerHostType.GetMethod(
			methodName,
			BindingFlags.Public | BindingFlags.Static,
			binder: null,
			types: parameterTypes,
			modifiers: null)
			?? throw new InvalidOperationException(
				$"Browser runtime provider host type '{providerHostType.FullName}' does not expose the expected {methodName} method.");
	}

	private static object? InvokeInstallMethod(MethodInfo installMethod, object?[] arguments)
	{
		try
		{
			return installMethod.Invoke(null, arguments);
		}
		catch (TargetInvocationException ex) when (ex.InnerException != null)
		{
			ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
			throw;
		}
	}

	private static IBrowserRuntimeHostLifecycle? ResolveProviderLifecycle(IDictionary<string, object> services)
	{
		if (!services.TryGetValue(BrowserRuntimeServiceNames.HostLifecycle, out object? lifecycle))
		{
			return null;
		}

		return lifecycle as IBrowserRuntimeHostLifecycle
			?? throw new InvalidOperationException(
				$"Browser runtime service '{BrowserRuntimeServiceNames.HostLifecycle}' is already registered with incompatible type '{lifecycle.GetType().FullName}'.");
	}

	private static void EnsureBrowserRuntimeServiceMatches(
		IDictionary<string, object> services,
		IBrowserRuntime runtime)
	{
		if (services.TryGetValue(BrowserRuntimeServiceNames.BrowserRuntime, out object? current))
		{
			if (!ReferenceEquals(current, runtime))
			{
				throw new InvalidOperationException(
					$"Browser runtime service '{BrowserRuntimeServiceNames.BrowserRuntime}' does not match the provider return value.");
			}

			return;
		}

		services[BrowserRuntimeServiceNames.BrowserRuntime] = runtime;
	}

	private static Exception? CleanupFailedInstall(
		IDictionary<string, object> services,
		IReadOnlyCollection<KeyValuePair<string, object>> serviceSnapshot,
		IBrowserRuntime? installedRuntime,
		IBrowserRuntimeHostLifecycle? providerLifecycle,
		BrowserRuntimeProviderAssemblyLoadContext loadContext)
	{
		object? previousRuntime = FindSnapshotValue(serviceSnapshot, BrowserRuntimeServiceNames.BrowserRuntime);
		object? previousLifecycle = FindSnapshotValue(serviceSnapshot, BrowserRuntimeServiceNames.HostLifecycle);
		services.TryGetValue(BrowserRuntimeServiceNames.BrowserRuntime, out object? currentRuntime);
		services.TryGetValue(BrowserRuntimeServiceNames.HostLifecycle, out object? currentLifecycle);

		Exception? cleanupFailure = null;
		CaptureCleanupFailure(ref cleanupFailure, () =>
		{
			if (installedRuntime != null && !ReferenceEquals(installedRuntime, previousRuntime))
			{
				installedRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
			}
		});
		CaptureCleanupFailure(ref cleanupFailure, () =>
		{
			if (currentRuntime is IBrowserRuntime runtime &&
				!ReferenceEquals(runtime, installedRuntime) &&
				!ReferenceEquals(runtime, previousRuntime))
			{
				runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
			}
		});
		CaptureCleanupFailure(ref cleanupFailure, () =>
		{
			if (providerLifecycle != null && !ReferenceEquals(providerLifecycle, previousLifecycle))
			{
				providerLifecycle.ShutdownProcessForHostExit();
			}
		});
		CaptureCleanupFailure(ref cleanupFailure, () =>
		{
			if (currentLifecycle is IBrowserRuntimeHostLifecycle lifecycle &&
				!ReferenceEquals(lifecycle, providerLifecycle) &&
				!ReferenceEquals(lifecycle, previousLifecycle))
			{
				lifecycle.ShutdownProcessForHostExit();
			}
		});

		RestoreServices(services, serviceSnapshot);
		CaptureCleanupFailure(ref cleanupFailure, loadContext.ReleaseDefaultLoadContextAssemblyResolver);
		CaptureCleanupFailure(ref cleanupFailure, loadContext.Unload);
		return cleanupFailure;
	}

	private static void CaptureCleanupFailure(ref Exception? cleanupFailure, Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			cleanupFailure = cleanupFailure == null ? ex : new AggregateException(cleanupFailure, ex);
		}
	}

	private static object? FindSnapshotValue(
		IReadOnlyCollection<KeyValuePair<string, object>> serviceSnapshot,
		string key)
	{
		foreach (KeyValuePair<string, object> entry in serviceSnapshot)
		{
			if (string.Equals(entry.Key, key, StringComparison.Ordinal))
			{
				return entry.Value;
			}
		}

		return null;
	}

	private static void RestoreServices(
		IDictionary<string, object> services,
		IEnumerable<KeyValuePair<string, object>> serviceSnapshot)
	{
		services.Clear();
		foreach (KeyValuePair<string, object> entry in serviceSnapshot)
		{
			services[entry.Key] = entry.Value;
		}
	}
}

internal sealed class BrowserRuntimeProviderShadowCopy
{
	private BrowserRuntimeProviderShadowCopy(
		string sourceAssemblyPath,
		string sourceDirectoryPath,
		string shadowAssemblyPath,
		string shadowDirectoryPath)
	{
		SourceAssemblyPath = sourceAssemblyPath;
		SourceDirectoryPath = sourceDirectoryPath;
		ShadowAssemblyPath = shadowAssemblyPath;
		ShadowDirectoryPath = shadowDirectoryPath;
	}

	public string SourceAssemblyPath { get; }

	public string SourceDirectoryPath { get; }

	public string ShadowAssemblyPath { get; }

	public string ShadowDirectoryPath { get; }

	public static BrowserRuntimeProviderShadowCopy Create(
		string providerAssemblyPath,
		string? shadowCopyRootPath,
		string providerId)
	{
		string sourceAssemblyPath = Path.GetFullPath(providerAssemblyPath);
		if (!File.Exists(sourceAssemblyPath))
		{
			throw new FileNotFoundException("Browser runtime provider assembly was not found.", sourceAssemblyPath);
		}

		string sourceDirectoryPath = Path.GetDirectoryName(sourceAssemblyPath)
			?? throw new DirectoryNotFoundException(
				$"Browser runtime provider directory could not be resolved from '{sourceAssemblyPath}'.");
		string fingerprint = ComputeFingerprint(sourceAssemblyPath, sourceDirectoryPath);
		string shadowRootPath = string.IsNullOrWhiteSpace(shadowCopyRootPath)
			? ResolveDefaultShadowCopyRootPath()
			: Path.GetFullPath(shadowCopyRootPath);
		string shadowDirectoryPath = Path.Combine(
			shadowRootPath,
			SanitizePathSegment(providerId),
			$"{Path.GetFileNameWithoutExtension(sourceAssemblyPath)}-{fingerprint[..16]}");
		string shadowAssemblyPath = Path.Combine(shadowDirectoryPath, Path.GetFileName(sourceAssemblyPath));

		EnsureShadowCopy(sourceDirectoryPath, shadowDirectoryPath);
		if (!File.Exists(shadowAssemblyPath))
		{
			throw new FileNotFoundException("Browser runtime provider shadow copy assembly was not created.", shadowAssemblyPath);
		}

		return new BrowserRuntimeProviderShadowCopy(
			sourceAssemblyPath,
			sourceDirectoryPath,
			shadowAssemblyPath,
			shadowDirectoryPath);
	}

	public string MapRequiredSourcePath(string? path, string optionName, bool mapToShadowCopy)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new InvalidOperationException(
				$"{optionName} is required when loading a browser runtime provider. Host bootstrap must pass the provider package root explicitly.");
		}

		string fullPath = Path.GetFullPath(path);
		if (!mapToShadowCopy)
		{
			return fullPath;
		}

		if (!IsSameOrChildPath(SourceDirectoryPath, fullPath))
		{
			throw new InvalidOperationException(
				$"{optionName} must be inside the browser runtime provider package. " +
				$"runtimeRootPath='{fullPath}', providerPackageRoot='{SourceDirectoryPath}'.");
		}

		string relativePath = Path.GetRelativePath(SourceDirectoryPath, fullPath);
		return Path.GetFullPath(Path.Combine(ShadowDirectoryPath, relativePath));
	}

	private static void EnsureShadowCopy(string sourceDirectoryPath, string shadowDirectoryPath)
	{
		if (Directory.Exists(shadowDirectoryPath))
		{
			return;
		}

		string parentDirectory = Path.GetDirectoryName(shadowDirectoryPath)
			?? throw new DirectoryNotFoundException(
				$"Browser runtime provider shadow copy parent could not be resolved from '{shadowDirectoryPath}'.");
		Directory.CreateDirectory(parentDirectory);
		string tempDirectory = $"{shadowDirectoryPath}.tmp-{Guid.NewGuid():N}";
		CopyDirectory(sourceDirectoryPath, tempDirectory);
		try
		{
			if (!Directory.Exists(shadowDirectoryPath))
			{
				Directory.Move(tempDirectory, shadowDirectoryPath);
			}
		}
		finally
		{
			if (Directory.Exists(tempDirectory))
			{
				Directory.Delete(tempDirectory, recursive: true);
			}
		}
	}

	private static void CopyDirectory(string sourceDirectoryPath, string targetDirectoryPath)
	{
		foreach (string directory in Directory.EnumerateDirectories(sourceDirectoryPath, "*", SearchOption.AllDirectories))
		{
			string relativePath = Path.GetRelativePath(sourceDirectoryPath, directory);
			Directory.CreateDirectory(Path.Combine(targetDirectoryPath, relativePath));
		}

		Directory.CreateDirectory(targetDirectoryPath);
		foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectoryPath, "*", SearchOption.AllDirectories))
		{
			string relativePath = Path.GetRelativePath(sourceDirectoryPath, sourceFile);
			string targetFile = Path.Combine(targetDirectoryPath, relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
			CopyFile(sourceFile, targetFile);
		}
	}

	private static void CopyFile(string sourceFile, string targetFile)
	{
		using var source = new FileStream(
			sourceFile,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete);
		using var target = new FileStream(
			targetFile,
			FileMode.CreateNew,
			FileAccess.Write,
			FileShare.None);
		source.CopyTo(target);
		File.SetLastWriteTimeUtc(targetFile, File.GetLastWriteTimeUtc(sourceFile));
	}

	private static string ComputeFingerprint(string sourceAssemblyPath, string sourceDirectoryPath)
	{
		using SHA256 hash = SHA256.Create();
		AppendPathAndFile(hash, sourceAssemblyPath);
		string depsPath = Path.Combine(
			Path.GetDirectoryName(sourceAssemblyPath)!,
			$"{Path.GetFileNameWithoutExtension(sourceAssemblyPath)}.deps.json");
		if (File.Exists(depsPath))
		{
			AppendPathAndFile(hash, depsPath);
		}

		AppendDirectoryManifest(hash, sourceDirectoryPath);
		hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
		return Convert.ToHexString(hash.Hash!).ToLowerInvariant();
	}

	private static void AppendDirectoryManifest(HashAlgorithm hash, string sourceDirectoryPath)
	{
		foreach (string sourceFile in Directory
			.EnumerateFiles(sourceDirectoryPath, "*", SearchOption.AllDirectories)
			.OrderBy(file => Path.GetRelativePath(sourceDirectoryPath, file), StringComparer.OrdinalIgnoreCase))
		{
			var info = new FileInfo(sourceFile);
			string relativePath = Path.GetRelativePath(sourceDirectoryPath, sourceFile);
			AppendString(hash, relativePath);
			AppendString(hash, info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
			AppendString(hash, info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}
	}

	private static void AppendPathAndFile(HashAlgorithm hash, string filePath)
	{
		AppendString(hash, Path.GetFileName(filePath));

		var buffer = new byte[81920];
		using var stream = new FileStream(
			filePath,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete);
		int read;
		while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
		{
			hash.TransformBlock(buffer, 0, read, null, 0);
		}
	}

	private static void AppendString(HashAlgorithm hash, string value)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
		byte[] separator = { 0 };
		hash.TransformBlock(separator, 0, separator.Length, null, 0);
	}

	private static string ResolveDefaultShadowCopyRootPath()
	{
		string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string root = string.IsNullOrWhiteSpace(localAppData)
			? Path.GetTempPath()
			: localAppData;
		return Path.Combine(root, "Ludots", "BrowserRuntimeProviders");
	}

	private static string SanitizePathSegment(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "provider";
		}

		char[] invalidChars = Path.GetInvalidFileNameChars();
		var builder = new StringBuilder(value.Length);
		foreach (char c in value)
		{
			builder.Append(invalidChars.Contains(c) ? '_' : c);
		}

		return builder.ToString();
	}

	private static bool IsSameOrChildPath(string parentPath, string candidatePath)
	{
		string normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentPath));
		string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
		return string.Equals(normalizedParent, normalizedCandidate, StringComparison.OrdinalIgnoreCase) ||
			normalizedCandidate.StartsWith(
				normalizedParent + Path.DirectorySeparatorChar,
				StringComparison.OrdinalIgnoreCase);
	}
}

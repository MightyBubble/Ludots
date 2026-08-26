using System;
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
	private static readonly object Sync = new();

	private static int _runtimeOwnerCount;
	private static bool _hostExitShutdownRequested;
	private static string? _runtimeRootPath;
	private static string? _resourceDirectoryPath;
	private static Renderer? _renderer;

	public static Renderer Renderer
	{
		get
		{
			lock (Sync)
			{
				return _renderer
					?? throw new InvalidOperationException("Ultralight process runtime has not been acquired.");
			}
		}
	}

	public static string ResourceDirectoryPath
	{
		get
		{
			lock (Sync)
			{
				return _resourceDirectoryPath
					?? throw new InvalidOperationException("Ultralight resource directory has not been acquired.");
			}
		}
	}

	public static void AcquireRuntimeOwner(UltralightBrowserRuntimeOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		UltralightRuntimeLayoutPreflight.EnsureComplete(options.RuntimeRootPath);
		lock (Sync)
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

	public static void UpdateAndRender()
	{
		lock (Sync)
		{
			_renderer?.Update();
			_renderer?.Render();
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
			_renderer?.Dispose();
			_renderer = null;
			_runtimeRootPath = null;
			_resourceDirectoryPath = null;
		}
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

		return new Uri(physicalEntry).AbsoluteUri;
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

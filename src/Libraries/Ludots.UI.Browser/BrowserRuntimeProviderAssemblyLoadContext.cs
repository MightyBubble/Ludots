using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Ludots.UI.Browser;

internal sealed class BrowserRuntimeProviderAssemblyLoadContext : AssemblyLoadContext
{
	private readonly AssemblyDependencyResolver _dependencyResolver;
	private readonly Dictionary<string, Assembly> _sharedAssemblies;
	private readonly string _runtimeRootPath;
	private readonly string[] _defaultLoadContextAssemblyNamePrefixes;

	public BrowserRuntimeProviderAssemblyLoadContext(
		string name,
		string providerAssemblyPath,
		IEnumerable<Assembly> sharedAssemblies,
		string runtimeRootPath,
		IEnumerable<string>? defaultLoadContextAssemblyNamePrefixes = null)
		: base(name, isCollectible: true)
	{
		if (string.IsNullOrWhiteSpace(providerAssemblyPath))
		{
			throw new ArgumentException("Provider assembly path is required.", nameof(providerAssemblyPath));
		}
		if (string.IsNullOrWhiteSpace(runtimeRootPath))
		{
			throw new ArgumentException("Runtime root path is required.", nameof(runtimeRootPath));
		}

		string fullProviderAssemblyPath = Path.GetFullPath(providerAssemblyPath);
		if (!File.Exists(fullProviderAssemblyPath))
		{
			throw new FileNotFoundException("Browser runtime provider assembly was not found.", fullProviderAssemblyPath);
		}

		_dependencyResolver = new AssemblyDependencyResolver(fullProviderAssemblyPath);
		_runtimeRootPath = Path.GetFullPath(runtimeRootPath);
		_sharedAssemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
		foreach (Assembly assembly in sharedAssemblies)
		{
			string? assemblyName = assembly.GetName().Name;
			if (!string.IsNullOrWhiteSpace(assemblyName))
			{
				_sharedAssemblies[assemblyName] = assembly;
			}
		}

		_defaultLoadContextAssemblyNamePrefixes = (defaultLoadContextAssemblyNamePrefixes ?? Array.Empty<string>())
			.Where(prefix => !string.IsNullOrWhiteSpace(prefix))
			.Select(prefix => prefix.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (_defaultLoadContextAssemblyNamePrefixes.Length > 0)
		{
			AssemblyLoadContext.Default.Resolving += ResolveDefaultLoadContextAssembly;
			Unloading += _ => ReleaseDefaultLoadContextAssemblyResolver();
		}
	}

	protected override Assembly? Load(AssemblyName assemblyName)
	{
		if (!string.IsNullOrWhiteSpace(assemblyName.Name) &&
			_sharedAssemblies.TryGetValue(assemblyName.Name, out Assembly? sharedAssembly))
		{
			return sharedAssembly;
		}

		if (ShouldLoadFromDefaultContext(assemblyName.Name))
		{
			return LoadDefaultContextAssemblyFromRuntimeRoot(assemblyName);
		}

		string? assemblyPath = _dependencyResolver.ResolveAssemblyToPath(assemblyName);
		return string.IsNullOrWhiteSpace(assemblyPath)
			? null
			: LoadFromAssemblyPath(assemblyPath);
	}

	protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
	{
		string? unmanagedDllPath = _dependencyResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
		return string.IsNullOrWhiteSpace(unmanagedDllPath)
			? IntPtr.Zero
			: LoadUnmanagedDllFromPath(unmanagedDllPath);
	}

	private bool ShouldLoadFromDefaultContext(string? assemblyName)
	{
		return !string.IsNullOrWhiteSpace(assemblyName) &&
			_defaultLoadContextAssemblyNamePrefixes.Any(prefix =>
				assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
	}

	private Assembly? LoadDefaultContextAssemblyFromRuntimeRoot(AssemblyName assemblyName)
	{
		Assembly? loadedAssembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate =>
			AssemblyName.ReferenceMatchesDefinition(candidate.GetName(), assemblyName));
		if (loadedAssembly != null)
		{
			return loadedAssembly;
		}

		string? simpleName = assemblyName.Name;
		if (string.IsNullOrWhiteSpace(simpleName))
		{
			return null;
		}

		string assemblyPath = Path.Combine(_runtimeRootPath, $"{simpleName}.dll");
		return File.Exists(assemblyPath)
			? AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath)
			: null;
	}

	private Assembly? ResolveDefaultLoadContextAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
	{
		return ReferenceEquals(context, AssemblyLoadContext.Default) && ShouldLoadFromDefaultContext(assemblyName.Name)
			? LoadDefaultContextAssemblyFromRuntimeRoot(assemblyName)
			: null;
	}

	public void ReleaseDefaultLoadContextAssemblyResolver()
	{
		if (_defaultLoadContextAssemblyNamePrefixes.Length == 0)
		{
			return;
		}

		AssemblyLoadContext.Default.Resolving -= ResolveDefaultLoadContextAssembly;
	}
}

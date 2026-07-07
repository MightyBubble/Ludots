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
	private readonly string[] _processSharedAssemblyNamePrefixes;

	public BrowserRuntimeProviderAssemblyLoadContext(
		string name,
		string providerAssemblyPath,
		IEnumerable<Assembly> sharedAssemblies,
		IEnumerable<string>? processSharedAssemblyNamePrefixes = null,
		bool isCollectible = true)
		: base(name, isCollectible)
	{
		if (string.IsNullOrWhiteSpace(providerAssemblyPath))
		{
			throw new ArgumentException("Provider assembly path is required.", nameof(providerAssemblyPath));
		}

		string fullProviderAssemblyPath = Path.GetFullPath(providerAssemblyPath);
		if (!File.Exists(fullProviderAssemblyPath))
		{
			throw new FileNotFoundException("Browser runtime provider assembly was not found.", fullProviderAssemblyPath);
		}

		_dependencyResolver = new AssemblyDependencyResolver(fullProviderAssemblyPath);
		_sharedAssemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
		foreach (Assembly assembly in sharedAssemblies)
		{
			string? assemblyName = assembly.GetName().Name;
			if (!string.IsNullOrWhiteSpace(assemblyName))
			{
				_sharedAssemblies[assemblyName] = assembly;
			}
		}

		_processSharedAssemblyNamePrefixes = processSharedAssemblyNamePrefixes?
			.Where(prefix => !string.IsNullOrWhiteSpace(prefix))
			.Select(prefix => prefix.Trim())
			.ToArray()
			?? Array.Empty<string>();
	}

	protected override Assembly? Load(AssemblyName assemblyName)
	{
		if (!string.IsNullOrWhiteSpace(assemblyName.Name) &&
			_sharedAssemblies.TryGetValue(assemblyName.Name, out Assembly? sharedAssembly))
		{
			return sharedAssembly;
		}

		string? assemblyPath = _dependencyResolver.ResolveAssemblyToPath(assemblyName);
		if (ShouldShareWithProcess(assemblyName.Name))
		{
			return ResolveProcessSharedAssembly(assemblyName, assemblyPath);
		}

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

	private bool ShouldShareWithProcess(string? assemblyName)
	{
		if (string.IsNullOrWhiteSpace(assemblyName))
		{
			return false;
		}

		for (int i = 0; i < _processSharedAssemblyNamePrefixes.Length; i++)
		{
			if (assemblyName.StartsWith(_processSharedAssemblyNamePrefixes[i], StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static Assembly? ResolveProcessSharedAssembly(AssemblyName assemblyName, string? assemblyPath)
	{
		Assembly? loaded = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate =>
			AssemblyName.ReferenceMatchesDefinition(candidate.GetName(), assemblyName));
		if (loaded != null)
		{
			return loaded;
		}

		return string.IsNullOrWhiteSpace(assemblyPath)
			? null
			: AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
	}
}

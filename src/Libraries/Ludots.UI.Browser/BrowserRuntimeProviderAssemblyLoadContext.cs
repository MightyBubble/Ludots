using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Ludots.UI.Browser;

internal sealed class BrowserRuntimeProviderAssemblyLoadContext : AssemblyLoadContext
{
	private readonly AssemblyDependencyResolver _dependencyResolver;
	private readonly Dictionary<string, Assembly> _sharedAssemblies;

	public BrowserRuntimeProviderAssemblyLoadContext(
		string name,
		string providerAssemblyPath,
		IEnumerable<Assembly> sharedAssemblies)
		: base(name, isCollectible: true)
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
	}

	protected override Assembly? Load(AssemblyName assemblyName)
	{
		if (!string.IsNullOrWhiteSpace(assemblyName.Name) &&
			_sharedAssemblies.TryGetValue(assemblyName.Name, out Assembly? sharedAssembly))
		{
			return sharedAssembly;
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
}

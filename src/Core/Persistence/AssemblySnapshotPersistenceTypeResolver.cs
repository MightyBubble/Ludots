using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Ludots.Core.Persistence
{
    public sealed class AssemblySnapshotPersistenceTypeResolver : IPersistenceTypeResolver
    {
        private readonly Assembly[] _assemblies;

        public AssemblySnapshotPersistenceTypeResolver(IEnumerable<Assembly> assemblies)
        {
            if (assemblies == null) throw new ArgumentNullException(nameof(assemblies));

            _assemblies = assemblies
                .Where(assembly => assembly != null && !assembly.IsDynamic)
                .Distinct()
                .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
                .ThenBy(assembly => assembly.FullName, StringComparer.Ordinal)
                .ToArray();
        }

        public Type Resolve(string assemblyQualifiedTypeName)
        {
            if (string.IsNullOrWhiteSpace(assemblyQualifiedTypeName))
            {
                throw new InvalidOperationException("Persistence type name is required.");
            }

            if (!TryGetAssemblyName(assemblyQualifiedTypeName, out AssemblyName requestedAssemblyName))
            {
                throw new InvalidOperationException(
                    $"Persistence type '{assemblyQualifiedTypeName}' must be assembly-qualified.");
            }

            if (!TryResolveAssembly(requestedAssemblyName, out _))
            {
                throw new InvalidOperationException(
                    $"Persistence type '{assemblyQualifiedTypeName}' requires assembly '{requestedAssemblyName.FullName}', but that assembly is not in the loaded persistence assembly snapshot.");
            }

            Type? resolved = Type.GetType(
                assemblyQualifiedTypeName,
                ResolveAssembly,
                ResolveType,
                throwOnError: false,
                ignoreCase: false);

            if (resolved == null)
            {
                throw new InvalidOperationException(
                    $"Persistence type '{assemblyQualifiedTypeName}' was not found in the loaded persistence assembly snapshot.");
            }

            return resolved;
        }

        private Assembly? ResolveAssembly(AssemblyName assemblyName)
        {
            return TryResolveAssembly(assemblyName, out Assembly? assembly)
                ? assembly
                : null;
        }

        private static Type? ResolveType(Assembly? assembly, string typeName, bool ignoreCase)
        {
            return assembly?.GetType(typeName, throwOnError: false, ignoreCase: ignoreCase);
        }

        private bool TryResolveAssembly(AssemblyName requestedName, out Assembly? assembly)
        {
            for (int i = 0; i < _assemblies.Length; i++)
            {
                Assembly candidate = _assemblies[i];
                if (AssemblyName.ReferenceMatchesDefinition(requestedName, candidate.GetName()))
                {
                    assembly = candidate;
                    return true;
                }
            }

            assembly = null;
            return false;
        }

        private static bool TryGetAssemblyName(string assemblyQualifiedTypeName, out AssemblyName assemblyName)
        {
            assemblyName = null!;
            int separator = FindAssemblyNameSeparator(assemblyQualifiedTypeName);
            if (separator < 0 || separator == assemblyQualifiedTypeName.Length - 1)
            {
                return false;
            }

            string rawAssemblyName = assemblyQualifiedTypeName[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(rawAssemblyName))
            {
                return false;
            }

            try
            {
                assemblyName = new AssemblyName(rawAssemblyName);
                return !string.IsNullOrWhiteSpace(assemblyName.Name);
            }
            catch
            {
                return false;
            }
        }

        private static int FindAssemblyNameSeparator(string assemblyQualifiedTypeName)
        {
            int bracketDepth = 0;
            for (int i = 0; i < assemblyQualifiedTypeName.Length; i++)
            {
                char current = assemblyQualifiedTypeName[i];
                if (current == '[')
                {
                    bracketDepth++;
                }
                else if (current == ']')
                {
                    bracketDepth--;
                }
                else if (current == ',' && bracketDepth == 0)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}

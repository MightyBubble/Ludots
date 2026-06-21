using Arch.Persistence;
using MessagePack.Formatters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Persistence
{
    public static class LudotsCorePersistenceFormatters
    {
        public static ArchBinarySerializer CreateBinarySerializer()
        {
            return new ArchBinarySerializer(CreateFormatters());
        }

        public static IMessagePackFormatter[] CreateFormatters()
        {
            var formatters = new Dictionary<Type, IMessagePackFormatter>();
            AddComponentFormatter(formatters, new NameFormatter());
            AddComponentFormatter(formatters, new MapEntityFormatter());
            AddAutoDiscoveredUnmanagedFormatters(formatters);
            return formatters.Values.ToArray();
        }

        public static IReadOnlySet<Type> GetFormatterComponentTypes()
        {
            return CreateFormatters()
                .OfType<ILudotsPersistenceComponentFormatter>()
                .Select(formatter => formatter.ComponentType)
                .ToHashSet();
        }

        private static void AddComponentFormatter(
            Dictionary<Type, IMessagePackFormatter> formatters,
            IMessagePackFormatter formatter)
        {
            if (formatter is not ILudotsPersistenceComponentFormatter componentFormatter)
            {
                throw new ArgumentException(
                    $"Formatter '{formatter.GetType().FullName}' does not expose a Ludots component type.",
                    nameof(formatter));
            }

            formatters[componentFormatter.ComponentType] = formatter;
        }

        private static void AddAutoDiscoveredUnmanagedFormatters(Dictionary<Type, IMessagePackFormatter> formatters)
        {
            foreach (Assembly assembly in GetCandidateAssemblies())
            {
                foreach (Type type in GetLoadableTypes(assembly)
                    .Where(IsCandidateValueType)
                    .OrderBy(type => type.FullName, StringComparer.Ordinal))
                {
                    if (formatters.ContainsKey(type))
                    {
                        continue;
                    }

                    IMessagePackFormatter? formatter = TryCreateUnmanagedFormatter(type);
                    if (formatter != null)
                    {
                        AddComponentFormatter(formatters, formatter);
                    }
                }
            }
        }

        private static IEnumerable<Assembly> GetCandidateAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(IsCandidateAssembly)
                .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal);
        }

        private static bool IsCandidateAssembly(Assembly assembly)
        {
            if (assembly.IsDynamic)
            {
                return false;
            }

            string? name = assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return name.StartsWith("Ludots.", StringComparison.Ordinal) ||
                name.EndsWith("Mod", StringComparison.Ordinal);
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null)!;
            }
        }

        private static bool IsCandidateValueType(Type type)
        {
            return type.IsValueType &&
                !type.IsEnum &&
                !type.IsPrimitive &&
                !type.IsByRefLike &&
                !type.ContainsGenericParameters;
        }

        private static IMessagePackFormatter? TryCreateUnmanagedFormatter(Type type)
        {
            if (ContainsReferences(type))
            {
                return null;
            }

            try
            {
                Type formatterType = typeof(UnmanagedComponentFormatter<>).MakeGenericType(type);
                return (IMessagePackFormatter?)Activator.CreateInstance(formatterType);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static bool ContainsReferences(Type type)
        {
            MethodInfo method = typeof(LudotsCorePersistenceFormatters)
                .GetMethod(nameof(ContainsReferencesGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type);
            return (bool)method.Invoke(null, null)!;
        }

        private static bool ContainsReferencesGeneric<T>()
        {
            return RuntimeHelpers.IsReferenceOrContainsReferences<T>();
        }
    }
}

using Arch.Persistence;
using Arch.Relationships;
using Ludots.Core.Gameplay.Relationships;
using MessagePack.Formatters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Ludots.Core.Persistence
{
    public static class LudotsCorePersistenceFormatters
    {
        private static Lazy<FormatterCache> s_cache = CreateLazyCache();
        private static int s_cacheBuildCount;

        public static ArchBinarySerializer CreateBinarySerializer()
        {
            return s_cache.Value.Serializer;
        }

        public static IMessagePackFormatter[] CreateFormatters()
        {
            return s_cache.Value.Formatters.ToArray();
        }

        internal static (IMessagePackFormatter[] Formatters, IReadOnlySet<Type> ComponentTypes) CreateFormatterSet(
            IEnumerable<Assembly> candidateAssemblies)
        {
            if (candidateAssemblies == null) throw new ArgumentNullException(nameof(candidateAssemblies));

            return BuildFormatterSet(NormalizeCandidateAssemblies(candidateAssemblies));
        }

        internal static Assembly[] CreateCandidateAssemblySnapshot()
        {
            return GetCandidateAssemblies().ToArray();
        }

        public static IReadOnlySet<Type> GetFormatterComponentTypes()
        {
            return s_cache.Value.ComponentTypes;
        }

        internal static int FormatterCacheBuildCountForTests => s_cacheBuildCount;

        internal static void ResetCacheForTests()
        {
            s_cache = CreateLazyCache();
            Interlocked.Exchange(ref s_cacheBuildCount, 0);
        }

        private static Lazy<FormatterCache> CreateLazyCache()
        {
            return new Lazy<FormatterCache>(BuildCache, isThreadSafe: true);
        }

        private static FormatterCache BuildCache()
        {
            Interlocked.Increment(ref s_cacheBuildCount);
            (IMessagePackFormatter[] formatters, IReadOnlySet<Type> componentTypes) =
                BuildFormatterSet(GetCandidateAssemblies());
            return new FormatterCache(
                formatters,
                componentTypes);
        }

        private static (IMessagePackFormatter[] Formatters, IReadOnlySet<Type> ComponentTypes) BuildFormatterSet(
            IEnumerable<Assembly> candidateAssemblies)
        {
            IMessagePackFormatter[] formatters = BuildFormatters(candidateAssemblies);
            IReadOnlySet<Type> componentTypes = formatters
                .OfType<ILudotsPersistenceComponentFormatter>()
                .Select(formatter => formatter.ComponentType)
                .ToHashSet();
            return (formatters, componentTypes);
        }

        private static IMessagePackFormatter[] BuildFormatters(IEnumerable<Assembly> candidateAssemblies)
        {
            var formatters = new Dictionary<Type, IMessagePackFormatter>();
            AddFormatter(formatters, typeof(RelationshipEdge), new RelationshipEdgeFormatter());
            AddFormatter(formatters, typeof(RelationshipEdgeSet), new RelationshipEdgeSetFormatter());
            AddFormatter(formatters, typeof(InRelationship), new InRelationshipFormatter());
            AddComponentFormatter(formatters, new RelationshipComponentFormatter<RelationshipEdgeSet>());
            AddComponentFormatter(formatters, new RelationshipComponentFormatter<InRelationship>());
            AddComponentFormatter(formatters, new NameFormatter());
            AddComponentFormatter(formatters, new MapEntityFormatter());
            AddComponentFormatter(formatters, new PresentationFrameStateFormatter());
            AddComponentFormatter(formatters, new Physics2DRuntimeStateFormatter());
            AddAutoDiscoveredUnmanagedFormatters(formatters, candidateAssemblies);
            // Hand-written pruners (e.g. Physics2D perf, discovered from its own assembly) must
            // come after unmanaged discovery and overwrite the raw-bytes default: their whole
            // purpose is to keep volatile fields out of the persisted payload.
            AddDiscoveredComponentFormatters(formatters, candidateAssemblies);
            return formatters.Values.ToArray();
        }

        private static void AddFormatter(
            Dictionary<Type, IMessagePackFormatter> formatters,
            Type formatterType,
            IMessagePackFormatter formatter)
        {
            formatters[formatterType] = formatter;
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

        private static void AddAutoDiscoveredUnmanagedFormatters(
            Dictionary<Type, IMessagePackFormatter> formatters,
            IEnumerable<Assembly> candidateAssemblies)
        {
            foreach (Assembly assembly in candidateAssemblies)
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
            return NormalizeCandidateAssemblies(AppDomain.CurrentDomain.GetAssemblies()
                .Where(IsCandidateAssembly)
                .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal));
        }

        private static Assembly[] NormalizeCandidateAssemblies(IEnumerable<Assembly> assemblies)
        {
            return assemblies
                .Where(assembly => assembly != null && !assembly.IsDynamic)
                .Distinct()
                .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
                .ThenBy(assembly => assembly.FullName, StringComparer.Ordinal)
                .ToArray();
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

        private sealed record FormatterCache(
            IMessagePackFormatter[] Formatters,
            IReadOnlySet<Type> ComponentTypes)
        {
            private readonly ThreadLocal<ArchBinarySerializer> _serializer =
                new(() => new ArchBinarySerializer(Formatters), trackAllValues: false);

            public ArchBinarySerializer Serializer => _serializer.Value!;
        }

        private static void AddDiscoveredComponentFormatters(
            Dictionary<Type, IMessagePackFormatter> formatters,
            IEnumerable<Assembly> candidateAssemblies)
        {
            foreach (Assembly assembly in candidateAssemblies)
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = Array.FindAll(ex.Types, t => t != null); }

                foreach (Type type in types)
                {
                    if (!typeof(ILudotsPersistenceComponentFormatter).IsAssignableFrom(type) ||
                        type.IsAbstract || !type.IsClass || type.ContainsGenericParameters ||
                        type.GetConstructor(Type.EmptyTypes) == null)
                    {
                        continue;
                    }

                    var instance = (ILudotsPersistenceComponentFormatter)Activator.CreateInstance(type)!;
                    if (instance is not IMessagePackFormatter typed)
                    {
                        continue;
                    }

                    // Discovered hand-written formatters intentionally overwrite the auto-registered
                    // raw-bytes default — that overwrite is how volatile-field pruning takes effect.
                    formatters[instance.ComponentType] = typed;
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Arch.Core;
using Arch.Persistence;
using Arch.Relationships;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using MessagePack.Formatters;

namespace Ludots.Core.Persistence
{
    public static class LudotsPersistenceSerializerFactory
    {
        public static LudotsBinaryWorldSerializer Create(GameEngine engine)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));

            return Create(engine.ModLoader);
        }

        public static LudotsBinaryWorldSerializer Create(ModLoader? modLoader)
        {
            Assembly[] componentAssemblies = CreateComponentAssemblySnapshot(modLoader);
            Assembly[] resolverAssemblies = CreateResolverAssemblySnapshot(componentAssemblies);
            var typeResolver = new AssemblySnapshotPersistenceTypeResolver(resolverAssemblies);
            (IMessagePackFormatter[] formatters, IReadOnlySet<Type> componentTypes) =
                LudotsCorePersistenceFormatters.CreateFormatterSet(componentAssemblies);

            IMessagePackFormatter[] serializerFormatters = formatters
                .Concat(new IMessagePackFormatter[] { new PersistenceTypeFormatter(typeResolver) })
                .ToArray();

            return new LudotsBinaryWorldSerializer(serializerFormatters, componentTypes);
        }

        private static Assembly[] CreateComponentAssemblySnapshot(ModLoader? modLoader)
        {
            var assemblies = new List<Assembly>(LudotsCorePersistenceFormatters.CreateCandidateAssemblySnapshot());

            if (modLoader != null)
            {
                assemblies.AddRange(modLoader.LoadedAssemblies);
            }

            return NormalizeAssemblies(assemblies);
        }

        private static Assembly[] CreateResolverAssemblySnapshot(IEnumerable<Assembly> componentAssemblies)
        {
            var assemblies = new List<Assembly>
            {
                typeof(World).Assembly,
                typeof(ArchBinarySerializer).Assembly,
                typeof(Relationship<>).Assembly,
                typeof(GameEngine).Assembly
            };
            assemblies.AddRange(componentAssemblies);
            return NormalizeAssemblies(assemblies);
        }

        private static Assembly[] NormalizeAssemblies(IEnumerable<Assembly> assemblies)
        {
            return assemblies
                .Where(assembly => assembly != null && !assembly.IsDynamic)
                .Distinct()
                .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
                .ThenBy(assembly => assembly.FullName, StringComparer.Ordinal)
                .ToArray();
        }
    }
}

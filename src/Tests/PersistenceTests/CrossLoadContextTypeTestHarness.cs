using System.Reflection;
using System.Runtime.Loader;
using MessagePack;
using MessagePack.Formatters;

namespace Ludots.Tests.Persistence;

internal static class CrossLoadContextTypeTestHarness
{
    public static Type LoadDuplicateType(AssemblyLoadContext loadContext, Type canonicalType)
    {
        string assemblyPath = canonicalType.Assembly.Location;
        Assembly assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
        return assembly.GetType(canonicalType.FullName!, throwOnError: true)!;
    }

    public sealed class DuplicateAssemblyLoadContext : AssemblyLoadContext
    {
        public DuplicateAssemblyLoadContext()
            : base(isCollectible: true)
        {
        }
    }

    public sealed class SubstitutingTypeFormatter : IMessagePackFormatter<Type>
    {
        private readonly Type _canonicalType;
        private readonly Type _substituteType;

        public SubstitutingTypeFormatter(Type canonicalType, Type substituteType)
        {
            _canonicalType = canonicalType ?? throw new ArgumentNullException(nameof(canonicalType));
            _substituteType = substituteType ?? throw new ArgumentNullException(nameof(substituteType));
        }

        public int SubstitutionHitCount { get; private set; }

        public void Serialize(ref MessagePackWriter writer, Type value, MessagePackSerializerOptions options)
        {
            writer.Write(value.AssemblyQualifiedName);
        }

        public Type Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            string? typeName = reader.ReadString();
            if (string.Equals(typeName, _canonicalType.AssemblyQualifiedName, StringComparison.Ordinal))
            {
                SubstitutionHitCount++;
                return _substituteType;
            }

            return Type.GetType(typeName ?? string.Empty, throwOnError: true)!;
        }
    }
}

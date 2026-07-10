using System;
using MessagePack;
using MessagePack.Formatters;

namespace Ludots.Core.Persistence
{
    public sealed class PersistenceTypeFormatter : IMessagePackFormatter<Type>
    {
        private readonly IPersistenceTypeResolver _typeResolver;

        public PersistenceTypeFormatter(IPersistenceTypeResolver typeResolver)
        {
            _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        }

        public void Serialize(ref MessagePackWriter writer, Type value, MessagePackSerializerOptions options)
        {
            string? typeName = value?.AssemblyQualifiedName;
            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new InvalidOperationException("Persistence cannot serialize a Type without an assembly-qualified name.");
            }

            writer.Write(typeName);
        }

        public Type Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            string? typeName = reader.ReadString();
            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new InvalidOperationException("Persistence cannot deserialize an empty Type name.");
            }

            return _typeResolver.Resolve(typeName);
        }
    }
}

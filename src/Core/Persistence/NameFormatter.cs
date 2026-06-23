using MessagePack;
using MessagePack.Formatters;
using Ludots.Core.Components;

namespace Ludots.Core.Persistence
{
    public sealed class NameFormatter : IMessagePackFormatter<Name>, ILudotsPersistenceComponentFormatter
    {
        public Type ComponentType => typeof(Name);

        public void Serialize(ref MessagePackWriter writer, Name value, MessagePackSerializerOptions options)
        {
            writer.Write(value.Value);
        }

        public Name Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            return new Name { Value = reader.ReadString() ?? string.Empty };
        }
    }
}

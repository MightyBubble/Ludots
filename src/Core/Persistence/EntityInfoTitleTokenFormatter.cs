using Ludots.Core.Components;
using MessagePack;
using MessagePack.Formatters;

namespace Ludots.Core.Persistence
{
    public sealed class EntityInfoTitleTokenFormatter : IMessagePackFormatter<EntityInfoTitleToken>, ILudotsPersistenceComponentFormatter
    {
        public Type ComponentType => typeof(EntityInfoTitleToken);

        public void Serialize(ref MessagePackWriter writer, EntityInfoTitleToken value, MessagePackSerializerOptions options)
        {
            writer.Write(value.Value);
        }

        public EntityInfoTitleToken Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            return new EntityInfoTitleToken { Value = reader.ReadString() };
        }
    }
}

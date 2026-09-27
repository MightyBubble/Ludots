using Ludots.Core.Components;
using MessagePack;
using MessagePack.Formatters;

namespace Ludots.Core.Persistence
{
    public sealed class PlacedInstanceIdFormatter : IMessagePackFormatter<PlacedInstanceId>, ILudotsPersistenceComponentFormatter
    {
        public Type ComponentType => typeof(PlacedInstanceId);

        public void Serialize(ref MessagePackWriter writer, PlacedInstanceId value, MessagePackSerializerOptions options)
        {
            writer.Write(value.Value);
        }

        public PlacedInstanceId Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            return new PlacedInstanceId { Value = reader.ReadString() };
        }
    }
}

using Ludots.Core.Components;
using Ludots.Core.Map;
using MessagePack;
using MessagePack.Formatters;
using System;

namespace Ludots.Core.Persistence
{
    public sealed class MapEntityFormatter : IMessagePackFormatter<MapEntity>, ILudotsPersistenceComponentFormatter
    {
        public Type ComponentType => typeof(MapEntity);

        public void Serialize(ref MessagePackWriter writer, MapEntity value, MessagePackSerializerOptions options)
        {
            writer.Write(value.MapId.Value);
        }

        public MapEntity Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            return new MapEntity
            {
                MapId = new MapId(reader.ReadString() ?? string.Empty)
            };
        }
    }
}

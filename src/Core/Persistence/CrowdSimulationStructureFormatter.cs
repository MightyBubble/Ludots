using Ludots.Core.CrowdSimulation;
using MessagePack;
using MessagePack.Formatters;

namespace Ludots.Core.Persistence
{
    public sealed class CrowdSimulationStructureFormatter : IMessagePackFormatter<CrowdSimulationStructure>, ILudotsPersistenceComponentFormatter
    {
        public Type ComponentType => typeof(CrowdSimulationStructure);

        public void Serialize(ref MessagePackWriter writer, CrowdSimulationStructure value, MessagePackSerializerOptions options)
        {
            writer.Write(value.Template);
        }

        public CrowdSimulationStructure Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            return new CrowdSimulationStructure { Template = reader.ReadString()! };
        }
    }
}

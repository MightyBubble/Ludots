using MessagePack;
using MessagePack.Formatters;
using Ludots.Core.Components;

namespace Ludots.Core.Persistence
{
    public sealed class CameraProfileBindingFormatter : IMessagePackFormatter<CameraProfileBinding>, ILudotsPersistenceComponentFormatter
    {
        public Type ComponentType => typeof(CameraProfileBinding);

        public void Serialize(ref MessagePackWriter writer, CameraProfileBinding value, MessagePackSerializerOptions options)
        {
            writer.Write(value.ProfileId);
        }

        public CameraProfileBinding Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            return new CameraProfileBinding { ProfileId = reader.ReadString() ?? string.Empty };
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Persistence;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

namespace SaveShowcasesShared;

/// <summary>
/// Normalized world digest lens shared by save showcases: serialize → WorldId=0 →
/// sorted per-entity/component rows → SHA-256. Same contract as SaveContinuationTrace / Bridge save tools.
/// </summary>
public static class WorldDigestLens
{
    public static string FromEngine(GameEngine engine)
    {
        LudotsBinaryWorldSerializer serializer = LudotsPersistenceSerializerFactory.Create(engine);
        byte[] worldBytes = serializer.Serialize(engine.World);
        using World canonical = serializer.Deserialize(worldBytes);
        SaveEntityWorldIdNormalizer.Normalize(canonical, canonicalWorldId: 0);
        return HashWorld(canonical);
    }

    public static string FromSnapshot(GameEngine engine, WorldSaveSnapshot snapshot)
    {
        LudotsBinaryWorldSerializer serializer = LudotsPersistenceSerializerFactory.Create(engine);
        using World canonical = serializer.Deserialize(snapshot.WorldBytes);
        SaveEntityWorldIdNormalizer.Normalize(canonical, canonicalWorldId: 0);
        return HashWorld(canonical);
    }

    public static string Short(string digest) =>
        string.IsNullOrEmpty(digest) ? "-" : (digest.Length <= 12 ? digest : digest[..12]);

    private static string HashWorld(World world)
    {
        MessagePackSerializerOptions options = MessagePackSerializerOptions.Standard.WithResolver(
            CompositeResolver.Create(
                LudotsCorePersistenceFormatters.CreateFormatters(),
                new IFormatterResolver[]
                {
                    BuiltinResolver.Instance,
                    ContractlessStandardResolverAllowPrivate.Instance
                }));

        var rows = new List<string>();
        world.Query(in QueryDescription.Null, entity =>
        {
            Signature signature = world.GetSignature(entity);
            var componentRows = new List<string>(signature.Components.Length);
            foreach (ComponentType componentType in signature.Components)
            {
                Type type = componentType.Type;
                object? component = world.Get(entity, componentType);
                componentRows.Add(component == null
                    ? $"{type.FullName ?? type.Name}=<null>"
                    : $"{type.FullName ?? type.Name}={Convert.ToHexString(MessagePackSerializer.Serialize(type, component, options))}");
            }

            componentRows.Sort(StringComparer.Ordinal);
            rows.Add($"{entity.Id}:{entity.Version}|{string.Join("|", componentRows)}");
        });

        rows.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows))));
    }
}

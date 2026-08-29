using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Arch.Core;
using Ludots.Core.Engine;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

namespace Ludots.Core.Persistence
{
    /// <summary>
    /// Deterministic world state digest: row-sorted per-entity/per-component hashing through a
    /// canonicalized throwaway world. Raw blob bytes are not stable across serialize invocations
    /// (chunk/type ordering varies), so sorted rows are the only cross-invocation-comparable basis;
    /// entity WorldIds are normalized because Arch assigns them from a process-global counter.
    /// </summary>
    public static class SaveWorldStateDigest
    {
        public static string Compute(GameEngine engine)
        {
            LudotsBinaryWorldSerializer serializer = LudotsPersistenceSerializerFactory.Create(engine);
            byte[] worldBytes = serializer.Serialize(engine.World);
            using World canonical = serializer.Deserialize(worldBytes);
            SaveEntityWorldIdNormalizer.Normalize(canonical, 0);
            return ComputeRows(canonical);
        }

        public static string ComputeRows(World canonicalWorld)
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
            canonicalWorld.Query(in QueryDescription.Null, entity =>
            {
                Signature signature = canonicalWorld.GetSignature(entity);
                var componentRows = new List<string>(signature.Components.Length);
                foreach (ComponentType componentType in signature.Components)
                {
                    Type type = componentType.Type;
                    object? component = canonicalWorld.Get(entity, componentType);
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
}

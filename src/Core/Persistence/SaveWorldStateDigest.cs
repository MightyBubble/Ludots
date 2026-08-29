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

        public static System.Collections.Generic.IReadOnlyList<string> CaptureRows(GameEngine engine)
        {
            LudotsBinaryWorldSerializer serializer = LudotsPersistenceSerializerFactory.Create(engine);
            byte[] worldBytes = serializer.Serialize(engine.World);
            using World canonical = serializer.Deserialize(worldBytes);
            SaveEntityWorldIdNormalizer.Normalize(canonical, 0);
            var rows = new List<string>();
            CollectRows(canonical, rows);
            return rows;
        }

        /// <summary>Row-level diff: names the exact entity/component pairs that diverged.</summary>
        public static System.Collections.Generic.IReadOnlyList<string> DiffRows(
            System.Collections.Generic.IReadOnlyList<string> recorded, System.Collections.Generic.IReadOnlyList<string> playback)
        {
            var recordedSet = new System.Collections.Generic.HashSet<string>(recorded);
            var playbackSet = new System.Collections.Generic.HashSet<string>(playback);
            var diffs = new List<string>();
            foreach (string row in recorded)
            {
                if (!playbackSet.Contains(row)) diffs.Add("only-in-recorded: " + TruncateRow(row));
            }
            foreach (string row in playback)
            {
                if (!recordedSet.Contains(row)) diffs.Add("only-in-playback: " + TruncateRow(row));
            }
            return diffs;
        }

        private static string TruncateRow(string row)
        {
            return row[..Math.Min(row.Length, 400)];
        }

        private static void CollectRows(World world, List<string> rows)
        {
            MessagePackSerializerOptions options = MessagePackSerializerOptions.Standard.WithResolver(
                CompositeResolver.Create(
                    LudotsCorePersistenceFormatters.CreateFormatters(),
                    new IFormatterResolver[]
                    {
                        BuiltinResolver.Instance,
                        ContractlessStandardResolverAllowPrivate.Instance
                    }));
            world.Query(in QueryDescription.Null, entity =>
            {
                Signature signature = world.GetSignature(entity);
                var componentRows = new List<string>(signature.Components.Length);
                foreach (ComponentType componentType in signature.Components)
                {
                    Type type = componentType.Type;
                    // Excluded from the deterministic digest: perf bookkeeping markers (Perf* family)
                    // and presentation-mount bookkeeping (PresenterChildren/PresenterAnimatorSlot)
                    // flip per render frame or get rebuilt by presentation systems — none of it is
                    // simulation state, so none of it belongs in a replay-equality fingerprint.
                    if (type.Name.StartsWith("Perf", StringComparison.Ordinal) ||
                        type.Name.StartsWith("Presenter", StringComparison.Ordinal) ||
                        type.Name == "PresenterChildren" ||
                        type.Name == "PresenterAnimatorSlot")
                    {
                        continue;
                    }

                    object? component = world.Get(entity, componentType);
                    componentRows.Add(component == null
                        ? $"{type.FullName ?? type.Name}=<null>"
                        : $"{type.FullName ?? type.Name}={Convert.ToHexString(MessagePackSerializer.Serialize(type, component, options))}");
                }
                componentRows.Sort(StringComparer.Ordinal);
                rows.Add($"{entity.Id}:{entity.Version}|{string.Join("|", componentRows)}");
            });
            rows.Sort(StringComparer.Ordinal);
        }

        public static string ComputeRows(World canonicalWorld)
        {
            var rows = new List<string>();
            CollectRows(canonicalWorld, rows);
            rows.Sort(StringComparer.Ordinal);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows))));
        }
    }
}

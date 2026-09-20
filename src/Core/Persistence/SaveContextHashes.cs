using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Hosting;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;

namespace Ludots.Core.Persistence
{
    public static class SaveContextHashes
    {
        public static string ComputeModSetHash(GameEngine engine)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));

            var builder = new StringBuilder();
            builder.Append("ludots.modSet.v1\n");

            if (engine.TryGetService(CoreServiceKeys.ModLoadPlan, out ResolvedModLoadPlan plan))
            {
                builder.Append("planSchema=").Append(plan.SchemaVersion?.ToString() ?? string.Empty).Append('\n');
                builder.Append("planFingerprint=").Append(plan.PlanFingerprint ?? string.Empty).Append('\n');
                for (int i = 0; i < plan.OrderedMods.Count; i++)
                {
                    ResolvedModLoadEntry mod = plan.OrderedMods[i];
                    builder.Append(i).Append('|').Append(mod.Id ?? string.Empty).Append('|').Append(mod.RootPath ?? string.Empty).Append('\n');
                }
            }
            else if (engine.ModLoader?.LoadedModIds != null)
            {
                for (int i = 0; i < engine.ModLoader.LoadedModIds.Count; i++)
                {
                    builder.Append(i).Append('|').Append(engine.ModLoader.LoadedModIds[i] ?? string.Empty).Append('\n');
                }
            }

            return ComputeSha256Hex(builder.ToString());
        }

        public static string ComputeRegistryFingerprint(GameEngine engine)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));

            var builder = new StringBuilder();
            builder.Append("ludots.registryFingerprint.v1\n");

            AppendRegistry(builder, "attribute", AttributeRegistry.SnapshotMappings());
            AppendRegistry(builder, "ability", AbilityIdRegistry.SnapshotMappings());
            AppendRegistry(builder, "effectTemplate", EffectTemplateIdRegistry.SnapshotMappings());
            AppendRegistry(builder, "configKey", ConfigKeyRegistry.SnapshotMappings());
            AppendRegistry(builder, "unitType", UnitTypeRegistry.SnapshotMappings());
            AppendRegistry(builder, "tag", TagRegistry.SnapshotMappings());
            AppendRegistry(builder, "abilityFormSet", AbilityFormSetIdRegistry.SnapshotMappings());
            AppendRegistry(builder, "contextGroup", ContextGroupIdRegistry.SnapshotMappings());
            AppendRegistry(builder, "graph", GraphIdRegistry.SnapshotMappings());
            AppendRegistry(builder, "orderBlackboardKey", OrderBlackboardKeyRegistry.SnapshotMappings());
            AppendRegistry(builder, "presenterParamKey", PresenterParamKeyRegistry.SnapshotMappings());

            if (engine.TryGetService(CoreServiceKeys.EntityTemplateKeyRegistry, out EntityTemplateKeyRegistry templateKeys))
            {
                AppendRegistry(builder, "entityTemplate", templateKeys.SnapshotMappings());
            }
            else
            {
                AppendRegistry(builder, "entityTemplate", Array.Empty<RegistryMapping>());
            }

            if (engine.TryGetService(CoreServiceKeys.GraphOutputValueKeyRegistry, out StringIntRegistry graphOutputValueKeys))
            {
                AppendRegistry(builder, "graphOutputValueKey", graphOutputValueKeys.SnapshotMappings());
            }

            if (engine.TryGetService(CoreServiceKeys.EntityCollectionKeyRegistry, out StringIntRegistry entityCollectionKeys))
            {
                AppendRegistry(builder, "entityCollectionKey", entityCollectionKeys.SnapshotMappings());
            }

            return ComputeSha256Hex(builder.ToString());
        }

        public static string ComputeRegistryFingerprint(IReadOnlyDictionary<string, IReadOnlyList<RegistryMapping>> registries)
        {
            if (registries == null) throw new ArgumentNullException(nameof(registries));

            var builder = new StringBuilder();
            builder.Append("ludots.registryFingerprint.v1\n");

            string[] registryNames = new string[registries.Count];
            int index = 0;
            foreach (KeyValuePair<string, IReadOnlyList<RegistryMapping>> pair in registries)
            {
                registryNames[index++] = pair.Key;
            }

            Array.Sort(registryNames, StringComparer.Ordinal);
            for (int i = 0; i < registryNames.Length; i++)
            {
                string registryName = registryNames[i];
                AppendRegistry(builder, registryName, registries[registryName]);
            }

            return ComputeSha256Hex(builder.ToString());
        }

        private static void AppendRegistry(StringBuilder builder, string registryName, IReadOnlyList<RegistryMapping> mappings)
        {
            if (string.IsNullOrWhiteSpace(registryName))
            {
                throw new ArgumentException("Registry name must not be empty.", nameof(registryName));
            }

            if (mappings == null) throw new ArgumentNullException(nameof(mappings));

            var sorted = new RegistryMapping[mappings.Count];
            for (int i = 0; i < mappings.Count; i++)
            {
                sorted[i] = mappings[i];
            }

            RegistryMappingSnapshot.SortInPlace(sorted);

            builder.Append('[').Append(registryName).Append("]\n");
            for (int i = 0; i < sorted.Length; i++)
            {
                builder.Append(sorted[i].Name ?? string.Empty).Append('=').Append(sorted[i].Id).Append('\n');
            }
        }

        private static string ComputeSha256Hex(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}

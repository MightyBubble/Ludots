using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Modding;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;

namespace Ludots.Core.Networking.Session
{
    /// <summary>
    /// Builds path-independent content identity from existing engine pipelines (no parallel SSOT).
    /// </summary>
    public static class ContentIdentityManifestBuilder
    {
        private const string ModAssembliesEmptyCanonical = "ludots.content.modAssemblies.empty.v1";
        private const string ModAssetsEmptyCanonical = "ludots.content.modAssets.empty.v1";
        private const string MapsEmptyCanonical = "ludots.content.maps.empty.v1";

        private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
        {
            WriteIndented = false,
        };

        public static ContentIdentityManifest Build(GameEngine engine, NetworkRuntimeConfig networking)
        {
            if (engine == null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            if (networking == null)
            {
                throw new ArgumentNullException(nameof(networking));
            }

            if (engine.ConfigCatalog == null)
            {
                throw new InvalidOperationException("Content identity requires ConfigCatalog on the engine.");
            }

            if (engine.ConfigPipeline == null)
            {
                throw new InvalidOperationException("Content identity requires ConfigPipeline on the engine.");
            }

            if (engine.ModLoader == null)
            {
                throw new InvalidOperationException("Content identity requires ModLoader on the engine.");
            }

            if (!engine.TryGetService(CoreServiceKeys.ModLoadPlan, out ResolvedModLoadPlan plan) ||
                plan.OrderedMods == null)
            {
                throw new InvalidOperationException(
                    "Content identity requires ResolvedModLoadPlan via CoreServiceKeys.ModLoadPlan.");
            }

            networking.Validate();

            var items = new List<ContentIdentityItem>(64);
            AppendProtocolItems(items, networking);
            AppendBuildItem(items);
            AppendModAssemblyItems(items, plan, engine.ModLoader);
            AppendModAssetItems(items, plan);
            AppendConfigItems(items, engine);
            AppendMapItems(items, plan);
            AppendRegistryItem(items, engine);
            AppendReplicationSchemaItems(items, engine, networking);

            return ContentIdentityManifest.Create(items);
        }

        private static void AppendProtocolItems(List<ContentIdentityItem> items, NetworkRuntimeConfig networking)
        {
            items.Add(Item(
                ContentIdentityCategory.Protocol,
                "protocolVersion",
                Encoding.UTF8.GetBytes($"{networking.ProtocolMajor}.{networking.ProtocolMinor}")));
            items.Add(Item(
                ContentIdentityCategory.Protocol,
                "profileId",
                Encoding.UTF8.GetBytes(networking.ProfileId)));
            items.Add(Item(
                ContentIdentityCategory.Protocol,
                "referenceTransport",
                Encoding.UTF8.GetBytes(networking.ReferenceTransport)));
        }

        private static void AppendBuildItem(List<ContentIdentityItem> items)
        {
            Assembly core = typeof(GameEngine).Assembly;
            string version =
                core.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? core.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                ?? core.GetName().Version?.ToString()
                ?? throw new InvalidOperationException(
                    "Ludots.Core assembly does not expose an informational, file, or assembly version.");

            items.Add(Item(ContentIdentityCategory.Build, "Ludots.Core", Encoding.UTF8.GetBytes(version)));
        }

        private static void AppendModAssemblyItems(
            List<ContentIdentityItem> items,
            ResolvedModLoadPlan plan,
            ModLoader modLoader)
        {
            int assemblyCount = 0;
            for (int i = 0; i < plan.OrderedMods.Count; i++)
            {
                ResolvedModLoadEntry mod = plan.OrderedMods[i];
                if (string.IsNullOrWhiteSpace(mod.Id))
                {
                    throw new InvalidOperationException($"Resolved mod plan entry {i} has an empty id.");
                }

                if (!TryResolveMainAssemblyFilePath(mod, modLoader, out string assemblyPath))
                {
                    continue;
                }

                byte[] bytes = File.ReadAllBytes(assemblyPath);
                items.Add(new ContentIdentityItem(
                    ContentIdentityCategory.ModAssemblies,
                    mod.Id,
                    ContentFingerprintBuilder.FromCanonicalBytes(bytes)));
                assemblyCount++;
            }

            if (assemblyCount == 0)
            {
                items.Add(Item(
                    ContentIdentityCategory.ModAssemblies,
                    "none",
                    Encoding.UTF8.GetBytes(ModAssembliesEmptyCanonical)));
            }
        }

        private static void AppendModAssetItems(List<ContentIdentityItem> items, ResolvedModLoadPlan plan)
        {
            int assetCount = 0;
            for (int i = 0; i < plan.OrderedMods.Count; i++)
            {
                ResolvedModLoadEntry mod = plan.OrderedMods[i];
                if (string.IsNullOrWhiteSpace(mod.RootPath))
                {
                    throw new InvalidOperationException(
                        $"Resolved mod '{mod.Id}' is missing RootPath for content identity.");
                }

                if (!TryFindChildDirectory(mod.RootPath, "assets", out string assetsRoot))
                {
                    continue;
                }

                foreach (string filePath in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
                {
                    string relative = ToPosixRelative(assetsRoot, filePath);
                    string key = $"{mod.Id}/{relative}";
                    byte[] bytes = File.ReadAllBytes(filePath);
                    items.Add(new ContentIdentityItem(
                        ContentIdentityCategory.ModAssets,
                        key,
                        ContentFingerprintBuilder.FromCanonicalBytes(bytes)));
                    assetCount++;
                }
            }

            if (assetCount == 0)
            {
                items.Add(Item(
                    ContentIdentityCategory.ModAssets,
                    "none",
                    Encoding.UTF8.GetBytes(ModAssetsEmptyCanonical)));
            }
        }

        private static void AppendConfigItems(List<ContentIdentityItem> items, GameEngine engine)
        {
            ConfigCatalog catalog = engine.ConfigCatalog;
            ConfigPipeline pipeline = engine.ConfigPipeline;
            ConfigConflictReport? report = engine.ConfigConflictReport;

            int configCount = 0;
            foreach (ConfigCatalogEntry entry in catalog.Entries)
            {
                string relativePath = entry.RelativePath.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    throw new InvalidOperationException("Config catalog entry has an empty relative path.");
                }

                JsonNode? merged = report != null
                    ? pipeline.MergeFromCatalog(in entry, report)
                    : pipeline.MergeFromCatalog(in entry);
                if (merged == null)
                {
                    throw new InvalidOperationException(
                        $"Config catalog entry '{relativePath}' merged to null; cannot build content identity.");
                }

                string json = merged.ToJsonString(CanonicalJsonOptions);
                items.Add(Item(
                    ContentIdentityCategory.Config,
                    relativePath,
                    Encoding.UTF8.GetBytes(json)));
                configCount++;
            }

            if (configCount == 0)
            {
                throw new InvalidOperationException(
                    "Config catalog has no entries; content identity Config category cannot be empty.");
            }
        }

        private static void AppendMapItems(List<ContentIdentityItem> items, ResolvedModLoadPlan plan)
        {
            int mapFileCount = 0;
            for (int i = 0; i < plan.OrderedMods.Count; i++)
            {
                ResolvedModLoadEntry mod = plan.OrderedMods[i];
                if (!TryFindChildDirectory(mod.RootPath, "Maps", out string mapsRoot))
                {
                    continue;
                }

                foreach (string filePath in Directory.EnumerateFiles(mapsRoot, "*", SearchOption.AllDirectories))
                {
                    string relative = ToPosixRelative(mapsRoot, filePath);
                    string key = $"{mod.Id}/{relative}";
                    byte[] bytes = File.ReadAllBytes(filePath);
                    items.Add(new ContentIdentityItem(
                        ContentIdentityCategory.Maps,
                        key,
                        ContentFingerprintBuilder.FromCanonicalBytes(bytes)));
                    mapFileCount++;
                }
            }

            if (mapFileCount == 0)
            {
                items.Add(Item(
                    ContentIdentityCategory.Maps,
                    "none",
                    Encoding.UTF8.GetBytes(MapsEmptyCanonical)));
            }
        }

        private static void AppendRegistryItem(List<ContentIdentityItem> items, GameEngine engine)
        {
            string hex = SaveContextHashes.ComputeRegistryFingerprint(engine);
            if (!ContentFingerprint.TryParseHex(hex, out ContentFingerprint digest) || digest.IsEmpty)
            {
                throw new InvalidOperationException(
                    "SaveContextHashes.ComputeRegistryFingerprint did not return a usable hex digest.");
            }

            items.Add(new ContentIdentityItem(ContentIdentityCategory.Registries, "registries", digest));
        }

        private static void AppendReplicationSchemaItems(
            List<ContentIdentityItem> items,
            GameEngine engine,
            NetworkRuntimeConfig networking)
        {
            if (networking.CommandSchemas == null || networking.CommandSchemas.Count == 0)
            {
                throw new InvalidOperationException(
                    "Content identity ReplicationSchema requires NetworkRuntimeConfig.CommandSchemas.");
            }

            var ordered = new NetworkCommandSchemaConfig[networking.CommandSchemas.Count];
            for (int i = 0; i < networking.CommandSchemas.Count; i++)
            {
                ordered[i] = networking.CommandSchemas[i]
                    ?? throw new InvalidOperationException($"CommandSchemas[{i}] is null.");
            }

            Array.Sort(ordered, static (left, right) =>
                string.CompareOrdinal(left.OrderTypeKey, right.OrderTypeKey));

            for (int i = 0; i < ordered.Length; i++)
            {
                NetworkCommandSchemaConfig schema = ordered[i];
                if (string.IsNullOrWhiteSpace(schema.OrderTypeKey))
                {
                    throw new InvalidOperationException($"CommandSchemas[{i}].OrderTypeKey is required.");
                }

                string canonical =
                    "ludots.content.commandSchema.v1\n" +
                    "orderTypeKey=" + schema.OrderTypeKey + "\n" +
                    "targetKind=" + (byte)schema.TargetKind + "\n" +
                    "allowArg0=" + (schema.AllowArg0 ? "1" : "0") + "\n" +
                    "allowArg1=" + (schema.AllowArg1 ? "1" : "0") + "\n" +
                    "submitMode=" + (byte)schema.SubmitMode + "\n" +
                    "requiredTargetPositionAccess=" + (byte)schema.RequiredTargetPositionAccess + "\n";
                items.Add(Item(
                    ContentIdentityCategory.ReplicationSchema,
                    schema.OrderTypeKey,
                    Encoding.UTF8.GetBytes(canonical)));
            }

            if (engine.TryGetService(CoreServiceKeys.ReplicationSchemaProjectors, out ReplicationSchemaProjectorRegistry projectors) &&
                projectors.Count > 0)
            {
                Span<int> schemaIds = stackalloc int[projectors.Count];
                int written = projectors.CopyRegisteredSchemaIds(schemaIds);
                if (written != projectors.Count)
                {
                    throw new InvalidOperationException(
                        $"Replication schema projector copy wrote {written} ids; expected {projectors.Count}.");
                }

                int[] sortedIds = schemaIds.Slice(0, written).ToArray();
                Array.Sort(sortedIds);
                for (int i = 0; i < sortedIds.Length; i++)
                {
                    int schemaId = sortedIds[i];
                    string key = "projector:" + schemaId.ToString();
                    string canonical = "ludots.content.replicationProjector.v1\nschemaId=" + schemaId + "\n";
                    items.Add(Item(
                        ContentIdentityCategory.ReplicationSchema,
                        key,
                        Encoding.UTF8.GetBytes(canonical)));
                }
            }

            // CommandSchemas guarantee at least one item; fail hard if somehow still empty.
            bool hasReplication = false;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Category == ContentIdentityCategory.ReplicationSchema)
                {
                    hasReplication = true;
                    break;
                }
            }

            if (!hasReplication)
            {
                throw new InvalidOperationException(
                    "Content identity ReplicationSchema category has no items.");
            }
        }

        private static bool TryResolveMainAssemblyFilePath(
            ResolvedModLoadEntry mod,
            ModLoader modLoader,
            out string assemblyPath)
        {
            if (TryResolveMainAssemblyPathFromManifest(mod.RootPath, out assemblyPath))
            {
                return true;
            }

            foreach (Assembly assembly in modLoader.LoadedAssemblies)
            {
                if (!string.Equals(assembly.GetName().Name, mod.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(assembly.Location))
                {
                    throw new InvalidOperationException(
                        $"Mod '{mod.Id}' assembly is loaded but has an empty Location; cannot hash file bytes.");
                }

                assemblyPath = assembly.Location;
                return true;
            }

            assemblyPath = string.Empty;
            return false;
        }

        private static bool TryResolveMainAssemblyPathFromManifest(string rootPath, out string assemblyPath)
        {
            assemblyPath = string.Empty;
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                return false;
            }

            string manifestPath = Path.Combine(rootPath, "mod.json");
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            ModManifest manifest = ModManifestJson.ParseStrict(File.ReadAllText(manifestPath), manifestPath);
            if (string.IsNullOrWhiteSpace(manifest.Main))
            {
                return false;
            }

            if (Path.IsPathRooted(manifest.Main))
            {
                throw new InvalidOperationException(
                    $"Mod '{manifest.Name}' main assembly path must be relative; got '{manifest.Main}'.");
            }

            string candidate = Path.GetFullPath(Path.Combine(rootPath, manifest.Main));
            string rootFull = Path.GetFullPath(rootPath);
            if (!candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Mod '{manifest.Name}' main assembly escapes mod root.");
            }

            if (!File.Exists(candidate))
            {
                return false;
            }

            assemblyPath = candidate;
            return true;
        }

        private static bool TryFindChildDirectory(string rootPath, string directoryName, out string directoryPath)
        {
            directoryPath = string.Empty;
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                return false;
            }

            foreach (string candidate in Directory.EnumerateDirectories(rootPath))
            {
                if (string.Equals(Path.GetFileName(candidate), directoryName, StringComparison.OrdinalIgnoreCase))
                {
                    directoryPath = candidate;
                    return true;
                }
            }

            return false;
        }

        private static string ToPosixRelative(string rootPath, string filePath)
        {
            string relative = Path.GetRelativePath(rootPath, filePath);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                throw new InvalidOperationException(
                    $"Content identity refused a non-relative path under '{rootPath}'.");
            }

            return relative.Replace('\\', '/');
        }

        private static ContentIdentityItem Item(ContentIdentityCategory category, string key, byte[] canonicalBytes) =>
            new(category, key, ContentFingerprintBuilder.FromCanonicalBytes(canonicalBytes));
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Loads the mod mount family (GAS/map_trigger_mounts.json) into a
    /// <see cref="TriggerGraphMountTable"/>. The family is ArrayById-shaped but is
    /// validated per fragment: the same mount key from two fragments fails closed
    /// (no silent last-wins), every entry is strictly parsed, and each mount is
    /// attributed to the mod that authored it. Mount key = "{modId}.{id}".
    /// </summary>
    public sealed class MapTriggerMountsLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly TriggerGraphMountTable _table;

        public MapTriggerMountsLoader(ConfigPipeline pipeline, TriggerGraphMountTable table)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _table = table ?? throw new ArgumentNullException(nameof(table));
        }

        public void Load(
            ConfigCatalog catalog,
            ConfigConflictReport report = null,
            string relativePath = "GAS/map_trigger_mounts.json")
        {
            ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(
                catalog,
                relativePath,
                ConfigMergePolicy.ArrayById,
                "id");
            var fragments = _pipeline.CollectFragmentsWithSources(in entry);

            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int f = 0; f < fragments.Count; f++)
            {
                ConfigFragment fragment = fragments[f];
                report?.RecordFragment(relativePath, fragment.SourceUri);
                string ownerId = ParseOwnerId(fragment.SourceUri, relativePath);

                if (fragment.Node is not JsonArray array)
                {
                    throw new InvalidOperationException(
                        $"{relativePath} fragment '{fragment.SourceUri}' must be a JSON array of mount objects.");
                }

                var mounts = new List<TriggerGraphMount>(array.Count);
                for (int i = 0; i < array.Count; i++)
                {
                    if (array[i] is not JsonObject obj)
                    {
                        throw new InvalidOperationException(
                            $"{relativePath} fragment '{fragment.SourceUri}'[{i}] must be an object.");
                    }

                    string context = $"{relativePath} fragment '{fragment.SourceUri}'[{i}]";
                    TriggerGraphMount mount = TriggerGraphMount.ParseObject(obj, context);
                    ValidateFamilyMount(mount, ownerId, context);
                    string key = $"{ownerId}.{mount.Id}";
                    if (!seenKeys.Add(key))
                    {
                        throw new InvalidOperationException(
                            $"{context} declares mount key '{key}' which is already declared by another fragment; "
                            + "duplicate mount keys fail closed (declare 'replaces' only against an existing base mount).");
                    }

                    mounts.Add(mount);
                }

                _table.AddMounts(ownerId, mounts);
            }
        }

        private static void ValidateFamilyMount(TriggerGraphMount mount, string ownerId, string context)
        {
            if (mount.Id.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{context} requires field 'id'; the mount key is '{ownerId}.{mount.Id}'.");
            }

            if (mount.Domain == TriggerGraphMountDomain.Entity)
            {
                throw new InvalidOperationException(
                    $"{context} domain 'entity' is not valid in the mod mount family; entity-domain mounts are "
                    + "authored on entity templates or in map config TriggerGraphs.");
            }

            if (mount.ScopeInstanceId != null)
            {
                throw new InvalidOperationException(
                    $"{context} does not support 'scopeInstanceId'; mod-family mounts are scope-less (caster = event payload source).");
            }

            if (mount.Domain == TriggerGraphMountDomain.Map && mount.MapFilter.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{context} domain 'map' requires field 'map'; a map-domain mod mount must target one map.");
            }
        }

        private static string ParseOwnerId(string sourceUri, string relativePath)
        {
            int colon = sourceUri.IndexOf(':');
            if (colon <= 0)
            {
                throw new InvalidOperationException(
                    $"{relativePath} fragment '{sourceUri}' has an unrecognized source; cannot attribute the mount to a mod.");
            }

            return sourceUri.Substring(0, colon);
        }
    }
}

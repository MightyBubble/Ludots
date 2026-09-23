using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace Ludots.Core.Gameplay.MapTriggers
{
    public static class TriggerGraphMounting
    {
        public static List<Trigger> BuildTriggers(
            MapSession session,
            GraphProgramRegistry? programs,
            EntityTriggerGraphMounts? entityMounts)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            string mapId = session.MapId.Value;
            List<TriggerGraphMount> mounts = TriggerGraphMount.ParseList(session.MapConfig?.TriggerGraphs, mapId);
            var triggers = new List<Trigger>();
            if (mounts.Count == 0)
            {
                return triggers;
            }

            if (programs == null)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' declares {TriggerGraphMount.FieldName} but GraphProgramRegistry is not available.");
            }

            for (int m = 0; m < mounts.Count; m++)
            {
                if (mounts[m].Domain == TriggerGraphMountDomain.Entity)
                {
                    if (entityMounts == null)
                    {
                        throw new InvalidOperationException(
                            $"Map '{mapId}' {TriggerGraphMount.FieldName}[{m}] declares domain 'entity' but the entity mount pipeline is not available.");
                    }

                    Entity scope = ResolveRequiredScope(session, mounts[m], $"Map '{mapId}' {TriggerGraphMount.FieldName}[{m}]");
                    triggers.AddRange(entityMounts.MountEntityGraphs(
                        session,
                        scope,
                        mounts[m].Graph,
                        $"Map '{mapId}' {TriggerGraphMount.FieldName}[{m}]"));
                    continue;
                }

                AppendMapMountTriggers(triggers, session, programs, mounts[m], mapId);
            }

            return triggers;
        }

        /// <summary>
        /// Builds one entity-domain mount (scope = the entity itself): one dispatch
        /// trigger per entry plus think-wave resume companions. Caller owns
        /// registration and lifecycle dispatch (EntityTriggerGraphMounts).
        /// </summary>
        public static List<Trigger> BuildEntityMountTriggers(
            GraphProgramRegistry programs,
            Entity scope,
            string graph,
            string ownerLabel)
        {
            var triggers = new List<Trigger>();
            AppendEntityMountTriggers(triggers, programs, scope, graph, ownerLabel);
            return triggers;
        }

        private static void AppendMapMountTriggers(
            List<Trigger> triggers,
            MapSession session,
            GraphProgramRegistry programs,
            TriggerGraphMount mount,
            string mapId)
        {
            GraphProgramRegistration registration = RequireGraphRegistration(
                programs,
                mount.Graph,
                TriggerGraphMount.FieldName,
                $"Map '{mapId}'");

            Entity scope = Entity.Null;
            if (mount.ScopeInstanceId != null)
            {
                scope = ResolveRequiredScope(session, mount, $"Map '{mapId}' {TriggerGraphMount.FieldName}");
            }

            AppendEntryTriggers(
                triggers,
                registration,
                mount.Graph,
                scope,
                TriggerGraphMountDomain.Map,
                TriggerGraphMount.FieldName,
                $"Map '{mapId}'");
        }

        private static void AppendEntityMountTriggers(
            List<Trigger> triggers,
            GraphProgramRegistry programs,
            Entity scope,
            string graph,
            string ownerLabel)
        {
            GraphProgramRegistration registration = RequireGraphRegistration(
                programs,
                graph,
                TriggerGraphMount.FieldName,
                ownerLabel);
            AppendEntryTriggers(
                triggers,
                registration,
                graph,
                scope,
                TriggerGraphMountDomain.Entity,
                TriggerGraphMount.FieldName,
                ownerLabel);
        }

        private static GraphProgramRegistration RequireGraphRegistration(
            GraphProgramRegistry programs,
            string graph,
            string fieldName,
            string ownerLabel)
        {
            int graphId = GraphIdRegistry.GetId(graph);
            if (graphId == GraphIdRegistry.InvalidId ||
                !programs.TryGetRegistration(graphId, out GraphProgramRegistration registration))
            {
                throw new InvalidOperationException(
                    $"{ownerLabel} {fieldName} references graph '{graph}' which is not registered.");
            }

            if (registration.Kind != GraphKind.TriggerGraph)
            {
                throw new InvalidOperationException(
                    $"{ownerLabel} {fieldName} graph '{graph}' has kind '{registration.Kind}'; only TriggerGraph graphs can be mounted.");
            }

            return registration;
        }

        private static Entity ResolveRequiredScope(MapSession session, TriggerGraphMount mount, string context)
        {
            MapLoadEntityIndex index = session.EntityIndex
                ?? throw new InvalidOperationException($"Map '{session.MapId.Value}' has no entity index.");
            return index.GetRequired(session.MapId.Value, mount.ScopeInstanceId, TriggerGraphMount.FieldName);
        }

        private static void AppendEntryTriggers(
            List<Trigger> triggers,
            GraphProgramRegistration registration,
            string graph,
            Entity scope,
            TriggerGraphMountDomain domain,
            string fieldName,
            string ownerLabel)
        {
            IReadOnlyList<TriggerGraphEntry> entries = registration.TriggerGraphEntries;
            if (entries == null || entries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{ownerLabel} {fieldName} graph '{graph}' declares no event entries.");
            }

            GraphInstruction[] program = registration.Program;

            for (int e = 0; e < entries.Count; e++)
            {
                TriggerGraphEntry entry = entries[e];
                if ((uint)entry.StartPc >= (uint)program.Length)
                {
                    throw new InvalidOperationException(
                        $"{ownerLabel} {fieldName} graph '{graph}' entry '{entry.Label}' has start pc {entry.StartPc} outside the program (length {program.Length}).");
                }

                var refirePolicy = entry.Refire == TriggerGraphEntry.RefireRestart
                    ? TriggerGraphRefirePolicy.Restart
                    : TriggerGraphRefirePolicy.Ignore;
                var mountTrigger = new TriggerGraphMountTrigger(
                    GraphIdRegistry.GetId(graph),
                    graph,
                    entry,
                    scope,
                    refirePolicy,
                    domain);
                triggers.Add(mountTrigger);
                if (!mountTrigger.EntryIsResumeEvent)
                {
                    triggers.Add(new TriggerGraphResumeTrigger(mountTrigger));
                }
            }
        }
    }
}

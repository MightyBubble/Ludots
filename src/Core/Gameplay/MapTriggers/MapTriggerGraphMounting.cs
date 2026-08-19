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
    public static class MapTriggerGraphMounting
    {
        public static List<Trigger> BuildTriggers(MapSession session, GraphProgramRegistry? programs)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            string mapId = session.MapId.Value;
            List<MapTriggerGraphMount> mounts = MapTriggerGraphMount.ParseList(session.MapConfig?.MapTriggerGraphs, mapId);
            var triggers = new List<Trigger>();
            if (mounts.Count == 0)
            {
                return triggers;
            }

            if (programs == null)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' declares {MapTriggerGraphMount.FieldName} but GraphProgramRegistry is not available.");
            }

            for (int m = 0; m < mounts.Count; m++)
            {
                AppendMountTriggers(triggers, session, programs, mounts[m], mapId);
            }

            return triggers;
        }

        private static void AppendMountTriggers(
            List<Trigger> triggers,
            MapSession session,
            GraphProgramRegistry programs,
            MapTriggerGraphMount mount,
            string mapId)
        {
            int graphId = GraphIdRegistry.GetId(mount.Graph);
            if (graphId == GraphIdRegistry.InvalidId ||
                !programs.TryGetRegistration(graphId, out GraphProgramRegistration registration))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' {MapTriggerGraphMount.FieldName} references graph '{mount.Graph}' which is not registered.");
            }

            if (registration.Kind != GraphKind.MapTrigger)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' {MapTriggerGraphMount.FieldName} graph '{mount.Graph}' has kind '{registration.Kind}'; only MapTrigger graphs can be mounted.");
            }

            IReadOnlyList<MapTriggerGraphEntry> entries = registration.MapTriggerEntries;
            if (entries == null || entries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' {MapTriggerGraphMount.FieldName} graph '{mount.Graph}' declares no event entries.");
            }

            GraphInstruction[] program = registration.Program;

            Entity scope = Entity.Null;
            if (mount.ScopeInstanceId != null)
            {
                MapLoadEntityIndex index = session.EntityIndex
                    ?? throw new InvalidOperationException($"Map '{mapId}' has no entity index.");
                scope = index.GetRequired(mapId, mount.ScopeInstanceId, MapTriggerGraphMount.FieldName);
            }

            for (int e = 0; e < entries.Count; e++)
            {
                MapTriggerGraphEntry entry = entries[e];
                if ((uint)entry.StartPc >= (uint)program.Length)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' {MapTriggerGraphMount.FieldName} graph '{mount.Graph}' entry '{entry.Label}' has start pc {entry.StartPc} outside the program (length {program.Length}).");
                }

                var refirePolicy = entry.Refire == MapTriggerGraphEntry.RefireRestart
                    ? MapTriggerGraphRefirePolicy.Restart
                    : MapTriggerGraphRefirePolicy.Ignore;
                var mountTrigger = new MapTriggerGraphMountTrigger(graphId, mount.Graph, entry, scope, refirePolicy);
                triggers.Add(mountTrigger);
                if (!mountTrigger.EntryIsResumeEvent)
                {
                    triggers.Add(new MapTriggerGraphResumeTrigger(mountTrigger));
                }
            }
        }
    }
}

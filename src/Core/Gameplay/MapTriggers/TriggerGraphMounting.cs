using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Modding;
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
            EntityTriggerGraphMounts? entityMounts,
            CustomEventNameRegistry? customEvents = null,
            AbilityDefinitionRegistry? abilityDefinitions = null,
            Ludots.Core.Scripting.EventSchemaRegistry? eventSchemas = null)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            string mapId = session.MapId.Value;
            List<TriggerGraphMount> mounts = TriggerGraphMount.ParseList(session.MapConfig?.TriggerGraphs, mapId);
            var triggers = new List<Trigger>();
            if (programs == null && (mounts.Count > 0 || abilityDefinitions != null))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' declares {TriggerGraphMount.FieldName} but GraphProgramRegistry is not available.");
            }

            for (int m = 0; m < mounts.Count; m++)
            {
                TriggerGraphMount mount = mounts[m];
                string ownerLabel = $"Map '{mapId}' {TriggerGraphMount.FieldName}[{m}]";
                switch (mount.Domain)
                {
                    case TriggerGraphMountDomain.Entity:
                        if (entityMounts == null)
                        {
                            throw new InvalidOperationException(
                                $"{ownerLabel} declares domain 'entity' but the entity mount pipeline is not available.");
                        }

                        Entity scope = ResolveRequiredScope(session, mount, ownerLabel);
                        triggers.AddRange(entityMounts.MountEntityGraphs(session, scope, mount.Graph, ownerLabel));
                        break;

                    case TriggerGraphMountDomain.Ability:
                        throw new InvalidOperationException(
                            $"{ownerLabel} declares domain 'ability' (ability '{mount.Ability}') but no runtime mount pipeline exists yet; ability-domain TriggerGraph mounts are an authoring contract only and must not run as map-domain mounts.");

                    case TriggerGraphMountDomain.Map:
                        AppendMapMountTriggers(triggers, session, programs, mount, mapId, customEvents, eventSchemas);
                        break;
                }

            }

            if (abilityDefinitions != null && programs != null)
            {
                triggers.AddRange(BuildAbilityMountTriggers(
                    programs,
                    abilityDefinitions,
                    customEvents ?? throw new InvalidOperationException(
                        $"Map '{mapId}' requires CustomEventNameRegistry for ability TriggerGraph validation."),
                    $"Map '{mapId}'"));
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
            string ownerLabel,
            CustomEventNameRegistry customEvents,
            Ludots.Core.Systems.MapLoadEntityIndex? entityIndex = null,
            Ludots.Core.Scripting.EventSchemaRegistry? eventSchemas = null)
        {
            if (customEvents == null) throw new ArgumentNullException(nameof(customEvents));
            var triggers = new List<Trigger>();
            AppendEntityMountTriggers(triggers, programs, scope, graph, ownerLabel, customEvents, entityIndex, eventSchemas);
            return triggers;
        }

        private static void AppendMapMountTriggers(
            List<Trigger> triggers,
            MapSession session,
            GraphProgramRegistry programs,
            TriggerGraphMount mount,
            string mapId,
            CustomEventNameRegistry? customEvents,
            Ludots.Core.Scripting.EventSchemaRegistry? eventSchemas)
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
                mount.Route,
                0,
                TriggerGraphMount.FieldName,
                $"Map '{mapId}'",
                customEvents,
                entityIndex: session.EntityIndex,
                eventSchemas: eventSchemas);
        }

        private static void AppendEntityMountTriggers(
            List<Trigger> triggers,
            GraphProgramRegistry programs,
            Entity scope,
            string graph,
            string ownerLabel,
            CustomEventNameRegistry customEvents,
            Ludots.Core.Systems.MapLoadEntityIndex? entityIndex,
            Ludots.Core.Scripting.EventSchemaRegistry? eventSchemas)
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
                TriggerGraphMountRoute.Local,
                0,
                TriggerGraphMount.FieldName,
                ownerLabel,
                customEvents,
                entityIndex: entityIndex,
                eventSchemas: eventSchemas);
        }

        public static List<Trigger> BuildAbilityMountTriggers(
            GraphProgramRegistry programs,
            AbilityDefinitionRegistry abilityDefinitions,
            CustomEventNameRegistry customEvents,
            string ownerLabel)
        {
            if (programs == null) throw new ArgumentNullException(nameof(programs));
            if (abilityDefinitions == null) throw new ArgumentNullException(nameof(abilityDefinitions));
            if (customEvents == null) throw new ArgumentNullException(nameof(customEvents));

            var triggers = new List<Trigger>();
            IReadOnlyList<int> abilityIds = abilityDefinitions.RegisteredAbilityIds;
            for (int i = 0; i < abilityIds.Count; i++)
            {
                int abilityId = abilityIds[i];
                if (!abilityDefinitions.TryGet(abilityId, out AbilityDefinition definition) ||
                    definition.TriggerGraphs == null || definition.TriggerGraphs.Count == 0)
                {
                    continue;
                }

                for (int g = 0; g < definition.TriggerGraphs.Count; g++)
                {
                    string graph = definition.TriggerGraphs[g];
                    AppendEntryTriggers(
                        triggers,
                        RequireGraphRegistration(programs, graph, TriggerGraphMount.FieldName, $"{ownerLabel} ability '{abilityId}'"),
                        graph,
                        Entity.Null,
                        TriggerGraphMountDomain.Ability,
                        TriggerGraphMountRoute.Local,
                        abilityId,
                        TriggerGraphMount.FieldName,
                        $"{ownerLabel} ability '{abilityId}'",
                        customEvents);
                }
            }

            return triggers;
        }

        public static List<Trigger> BuildModMountTriggers(
            GraphProgramRegistry programs,
            ModManifest manifest,
            CustomEventNameRegistry customEvents)
        {
            if (programs == null) throw new ArgumentNullException(nameof(programs));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (customEvents == null) throw new ArgumentNullException(nameof(customEvents));

            var triggers = new List<Trigger>();
            if (manifest.TriggerGraphs == null || manifest.TriggerGraphs.Count == 0)
            {
                return triggers;
            }

            for (int g = 0; g < manifest.TriggerGraphs.Count; g++)
            {
                string graph = manifest.TriggerGraphs[g];
                GraphProgramRegistration registration = RequireGraphRegistration(
                    programs,
                    graph,
                    "triggerGraphs",
                    $"Mod '{manifest.Name}'");
                for (int e = 0; e < registration.TriggerGraphEntries.Count; e++)
                {
                    TriggerGraphEntry entry = registration.TriggerGraphEntries[e];
                    string eventName = entry.EventName;
                    if (GameEvents.IsMapScoped(eventName))
                    {
                        throw new InvalidOperationException(
                            $"Mod '{manifest.Name}' triggerGraphs graph '{graph}' entry '{entry.Label}' names map-scoped event '{eventName}'; Mod TriggerGraphs accept global engine events only, and map-scoped events fire only inside a map session.");
                    }

                    if (eventName.StartsWith(CustomEventNameRegistry.GasEventPrefix, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Mod '{manifest.Name}' triggerGraphs graph '{graph}' entry '{entry.Label}' names GAS bridge event '{eventName}'; Mod TriggerGraphs accept global engine events only, and {CustomEventNameRegistry.GasEventPrefix}* events fire only inside a map session.");
                    }

                    if (eventName == GameEvents.ModTriggerResume.Value)
                    {
                        throw new InvalidOperationException(
                            $"Mod '{manifest.Name}' triggerGraphs graph '{graph}' entry '{entry.Label}' names internal event '{eventName}'; Mod TriggerGraphs cannot bind the engine continuation pulse.");
                    }

                    if (customEvents.IsDeclaredCustom(eventName))
                    {
                        throw new InvalidOperationException(
                            $"Mod '{manifest.Name}' triggerGraphs graph '{graph}' entry '{entry.Label}' names declared custom event '{eventName}'; Mod TriggerGraphs accept global engine events only, and custom events fire only inside a map session.");
                    }
                }

                AppendEntryTriggers(
                    triggers,
                    registration,
                    graph,
                    Entity.Null,
                    TriggerGraphMountDomain.Mod,
                    TriggerGraphMountRoute.Local,
                    0,
                    "triggerGraphs",
                    $"Mod '{manifest.Name}'",
                    customEvents,
                    modIdFilter: manifest.Name);
            }

            return triggers;
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
            TriggerGraphMountRoute route,
            int abilityIdFilter,
            string fieldName,
            string ownerLabel,
            CustomEventNameRegistry? customEvents = null,
            string? modIdFilter = null,
            Ludots.Core.Systems.MapLoadEntityIndex? entityIndex = null,
            Ludots.Core.Scripting.EventSchemaRegistry? eventSchemas = null)
        {
            IReadOnlyList<TriggerGraphEntry> entries = registration.TriggerGraphEntries;
            if (entries == null || entries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{ownerLabel} {fieldName} graph '{graph}' declares no event entries.");
            }

            ValidatePlacedInstanceReads(registration.Program, graph, fieldName, ownerLabel, entityIndex);

            // Subscription-scope routing (#1123): the table an entry lands in is derived
            // from the event's schema scope, never authored per-entry, so a Map-scope
            // event cannot enter the global table or vice versa. Schema-less events
            // (engine legacy keys without a schema) stay in the map table. Entity-domain
            // global subscriptions fail closed here — phase one is map-owned globals only.
            for (int v = 0; v < entries.Count; v++)
            {
                if (domain == TriggerGraphMountDomain.Entity &&
                    eventSchemas != null &&
                    eventSchemas.TryGet(entries[v].EventName, out EventSchema entityEntrySchema) &&
                    entityEntrySchema.Scope == EventScope.Global)
                {
                    throw new InvalidOperationException(
                        $"{ownerLabel} {fieldName} graph '{graph}' entry '{entries[v].Label}' subscribes to global-scope event " +
                        $"'{entries[v].EventName}'; entity-domain global subscriptions are not supported in phase one (#1123).");
                }
            }

            if (customEvents != null)
            {
                for (int v = 0; v < entries.Count; v++)
                {
                    string eventName = entries[v].EventName;
                    if (!customEvents.IsKnownEntryEvent(eventName))
                    {
                        throw new InvalidOperationException(
                            $"{ownerLabel} {fieldName} graph '{graph}' entry '{entries[v].Label}' names unknown event '{eventName}'. " +
                            $"Known events — {customEvents.DescribeVocabulary()}. Declare custom events in {CustomEventNameRegistry.ConfigPath}.");
                    }
                }
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

                if (entry.IsHookFragment)
                {
                    // #1124: the body was woven into its target graph's anchor at compile
                    // time; the fragment itself never dispatches on its authored event.
                    continue;
                }

                if (entry.Filters.InstanceId != null &&
                    (entityIndex == null || !entityIndex.TryGet(entry.Filters.InstanceId, out _)))
                {
                    throw new InvalidOperationException(
                        $"{ownerLabel} {fieldName} graph '{graph}' entry '{entry.Label}' filters.instanceId references unknown placed instance " +
                        $"'{entry.Filters.InstanceId}' on this map; load fails closed instead of silently never matching.");
                }

                if (entry.Filters.Tag != null)
                {
                    // GraphRuntime's evaluator may not reference the GAS registry, so the
                    // tag name resolves to its id here at mount time; unknown names keep the
                    // filter permanently unmatched.
                    int tagId = Ludots.Core.Gameplay.GAS.Registry.TagRegistry.GetId(entry.Filters.Tag);
                    entry = new TriggerGraphEntry(
                        entry.Label,
                        entry.EventName,
                        entry.StartPc,
                        entry.Once,
                        new TriggerGraphEntryFilters(
                            entry.Filters.Region,
                            entry.Filters.Tag,
                            entry.Filters.Team,
                            entry.Filters.Threshold,
                            entry.Filters.Direction,
                            entry.Filters.Action,
                            entry.Filters.InstanceId,
                            tagId == Ludots.Core.Gameplay.GAS.Registry.TagRegistry.InvalidId ? null : tagId),
                        entry.Refire,
                        entry.Priority);
                }

                var refirePolicy = entry.Refire == TriggerGraphEntry.RefireRestart
                    ? TriggerGraphRefirePolicy.Restart
                    : TriggerGraphRefirePolicy.Ignore;
                EventScope subscriptionScope = EventScope.Map;
                if (eventSchemas != null && eventSchemas.TryGet(entry.EventName, out EventSchema entrySchema))
                {
                    subscriptionScope = entrySchema.Scope;
                }

                var mountTrigger = new TriggerGraphMountTrigger(
                    GraphIdRegistry.GetId(graph),
                    graph,
                    entry,
                    scope,
                    refirePolicy,
                    domain,
                    route,
                    abilityIdFilter,
                    modIdFilter,
                    subscriptionScope);
                triggers.Add(mountTrigger);
                if (!mountTrigger.EntryIsResumeEvent)
                {
                    triggers.Add(new TriggerGraphResumeTrigger(mountTrigger));
                }
            }
        }

        /// <summary>
        /// #1108 fail-closed contract: the compiler has no map context, so every
        /// LoadPlacedEntity instanceId must be proven against the mounting map's
        /// placed-instance catalog here. Programs are symbol-patched before mounting, so
        /// Imm is a ConfigKeyRegistry id; an unresolvable id is itself a load error.
        /// </summary>
        private static void ValidatePlacedInstanceReads(
            GraphInstruction[] program,
            string graph,
            string fieldName,
            string ownerLabel,
            Ludots.Core.Systems.MapLoadEntityIndex? entityIndex)
        {
            for (int i = 0; i < program.Length; i++)
            {
                if (program[i].Op != (ushort)GraphNodeOp.LoadPlacedEntity)
                {
                    continue;
                }

                string instanceId = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.GetName(program[i].Imm)
                    ?? throw new InvalidOperationException(
                        $"{ownerLabel} {fieldName} graph '{graph}' LoadPlacedEntity at pc {i} references an unregistered instance key id {program[i].Imm}.");

                if (entityIndex == null || !entityIndex.TryGet(instanceId, out _))
                {
                    throw new InvalidOperationException(
                        $"{ownerLabel} {fieldName} graph '{graph}' LoadPlacedEntity at pc {i} references unknown placed instance " +
                        $"'{instanceId}' on this map; load fails closed instead of authoring against a ghost (#1108).");
                }
            }
        }
    }
}

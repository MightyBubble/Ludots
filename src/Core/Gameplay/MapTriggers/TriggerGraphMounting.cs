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
    /// <summary>
    /// Entry-class filter for context trigger mounts (#1398 刀4): Interactive = input-action
    /// bound entries, Passive = map/event-bound entries. The mount trigger and its resume
    /// companion are always built together per entry, so a class filter never orphans one.
    /// </summary>
    public enum ContextMountEntryClass : byte
    {
        All = 0,
        Interactive = 1,
        Passive = 2,
    }

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
            Ludots.Core.Scripting.EventSchemaRegistry? eventSchemas = null,
            System.Collections.Generic.IReadOnlySet<string>? regionIds = null)
        {
            if (customEvents == null) throw new ArgumentNullException(nameof(customEvents));
            var triggers = new List<Trigger>();
            AppendEntityMountTriggers(triggers, programs, scope, graph, ownerLabel, customEvents, entityIndex, eventSchemas, regionIds);
            return triggers;
        }

        /// <summary>
        /// Install-time fail-fast for one profile-declared context trigger mount (#1398 S2b):
        /// the graph must be registered with TriggerGraph kind, declare at least one dispatch
        /// entry, and — when the mount narrows by event name — carry a dispatch entry on that
        /// exact event. Called from <c>InteractionContextProfileRegistry</c> install so unknown
        /// graph ids and event names fail at load, not at first activation.
        /// </summary>
        public static void ValidateContextTriggerMount(
            GraphProgramRegistry programs,
            Ludots.Core.Input.Interaction.InteractionContextTriggerMount mount,
            string ownerLabel)
        {
            if (programs == null) throw new ArgumentNullException(nameof(programs));
            if (mount == null) throw new ArgumentNullException(nameof(mount));

            GraphProgramRegistration registration = RequireGraphRegistration(
                programs, mount.Trigger, "triggers", ownerLabel);
            IReadOnlyList<TriggerGraphEntry> entries = registration.TriggerGraphEntries ?? Array.Empty<TriggerGraphEntry>();
            bool hasDispatchEntry = false;
            bool matchesEvent = false;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].IsHookFragment)
                {
                    continue;
                }

                hasDispatchEntry = true;
                if (!string.IsNullOrWhiteSpace(mount.Event) &&
                    string.Equals(entries[i].EventName, mount.Event, StringComparison.Ordinal))
                {
                    matchesEvent = true;
                }
            }

            if (!hasDispatchEntry)
            {
                throw new InvalidOperationException(
                    $"{ownerLabel} triggers graph '{mount.Trigger}' declares no dispatch entries; context mounts need at least one non-hook entry.");
            }

            if (!string.IsNullOrWhiteSpace(mount.Event) && !matchesEvent)
            {
                throw new InvalidOperationException(
                    $"{ownerLabel} triggers graph '{mount.Trigger}' has no dispatch entry on event '{mount.Event}'.");
            }
        }

        /// <summary>
        /// Builds one context-gated entity-domain mount (#1398 S2b): the mounted profile's
        /// trigger reference materialized on the context subject while the context is active.
        /// Same mount chain as entity mounts (placed-instance validation, event vocabulary,
        /// tag resolution, subscription-scope routing); the mount's optional event narrows the
        /// graph's dispatch entries and its optional filters block replaces the entries' own
        /// filters for this mount (reference-time override, not a merge). Caller owns
        /// registration and lifecycle (InteractionContextTriggerMountSystem).
        /// </summary>
        public static List<Trigger> BuildContextMountTriggers(
            GraphProgramRegistry programs,
            Entity scope,
            Ludots.Core.Input.Interaction.InteractionContextTriggerMount mount,
            string ownerLabel,
            CustomEventNameRegistry customEvents,
            Ludots.Core.Systems.MapLoadEntityIndex? entityIndex = null,
            Ludots.Core.Scripting.EventSchemaRegistry? eventSchemas = null,
            System.Collections.Generic.IReadOnlySet<string>? regionIds = null,
            ContextMountEntryClass entryClass = ContextMountEntryClass.All)
        {
            if (customEvents == null) throw new ArgumentNullException(nameof(customEvents));

            GraphProgramRegistration registration = RequireGraphRegistration(
                programs, mount.Trigger, "triggers", ownerLabel);
            var triggers = new List<Trigger>();
            AppendEntryTriggers(
                triggers,
                registration,
                mount.Trigger,
                scope,
                TriggerGraphMountDomain.Entity,
                TriggerGraphMountRoute.Local,
                0,
                "triggers",
                ownerLabel,
                customEvents,
                entityIndex: entityIndex,
                eventSchemas: eventSchemas,
                regionIds: regionIds,
                entryEventFilter: string.IsNullOrWhiteSpace(mount.Event) ? null : mount.Event,
                contextMount: mount,
                entryClass: entryClass);
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
                eventSchemas: eventSchemas,
                regionIds: CollectRegionIds(session));
        }

        private static void AppendEntityMountTriggers(
            List<Trigger> triggers,
            GraphProgramRegistry programs,
            Entity scope,
            string graph,
            string ownerLabel,
            CustomEventNameRegistry customEvents,
            Ludots.Core.Systems.MapLoadEntityIndex? entityIndex,
            Ludots.Core.Scripting.EventSchemaRegistry? eventSchemas,
            System.Collections.Generic.IReadOnlySet<string>? regionIds)
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
                eventSchemas: eventSchemas,
                regionIds: regionIds);
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
            Ludots.Core.Scripting.EventSchemaRegistry? eventSchemas = null,
            System.Collections.Generic.IReadOnlySet<string>? regionIds = null,
            string? entryEventFilter = null,
            Ludots.Core.Input.Interaction.InteractionContextTriggerMount? contextMount = null,
            ContextMountEntryClass entryClass = ContextMountEntryClass.All)
        {
            IReadOnlyList<TriggerGraphEntry> entries = registration.TriggerGraphEntries;
            if (entries == null || entries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{ownerLabel} {fieldName} graph '{graph}' declares no event entries.");
            }

            ValidatePlacedInstanceReads(
                registration.Program,
                graph,
                fieldName,
                ownerLabel,
                entityIndex,
                regionIds);

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
                    if (entries[v].IsActionBound)
                    {
                        continue;
                    }

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
            bool eventFilterMatched = false;

            for (int e = 0; e < entries.Count; e++)
            {
                TriggerGraphEntry entry = entries[e];

                // Entry-class filter (#1398 刀4): when the mount asks for only interactive or
                // only passive entries, skip the other class wholesale — the mount trigger and
                // its resume companion are built together below, so neither gets orphaned.
                if (entryClass == ContextMountEntryClass.Interactive && !entry.IsActionBound)
                {
                    continue;
                }

                if (entryClass == ContextMountEntryClass.Passive && entry.IsActionBound)
                {
                    continue;
                }

                if (entry.IsHookFragment)
                {
                    // #1124: the body was woven into its target graph's anchor at compile
                    // time; the fragment itself never dispatches on its authored event.
                    continue;
                }

                if (entryEventFilter != null &&
                    !string.Equals(entry.EventName, entryEventFilter, StringComparison.Ordinal))
                {
                    continue;
                }

                eventFilterMatched = true;

                if (contextMount?.Filters != null)
                {
                    entry = WithContextMountFilters(entry, contextMount.Filters, graph, ownerLabel);
                }

                if ((uint)entry.StartPc >= (uint)program.Length)
                {
                    throw new InvalidOperationException(
                        $"{ownerLabel} {fieldName} graph '{graph}' entry '{entry.Label}' has start pc {entry.StartPc} outside the program (length {program.Length}).");
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
                        entry.Priority,
                        entry.IsHookFragment,
                        entry.ActionId);
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

            if (entryEventFilter != null && !eventFilterMatched)
            {
                throw new InvalidOperationException(
                    $"{ownerLabel} {fieldName} graph '{graph}' has no dispatch entry on event '{entryEventFilter}'.");
            }
        }

        /// <summary>
        /// Context mounts may override the selected entries' authored filters with the mount's
        /// own filters block (#1398 S2b) — a reference-time replacement, not a merge. Parsing
        /// mirrors the entry filter compiler rules: trimmed non-empty strings, direction as
        /// cross_above/cross_below, threshold and direction declared together.
        /// </summary>
        private static TriggerGraphEntry WithContextMountFilters(
            TriggerGraphEntry entry,
            TriggerGraphEntryFiltersConfig filters,
            string graph,
            string ownerLabel)
        {
            string? region = RequireTrimmedFilterField(filters.Region, "region", graph, ownerLabel);
            string? tag = RequireTrimmedFilterField(filters.Tag, "tag", graph, ownerLabel);
            string? action = RequireTrimmedFilterField(filters.Action, "action", graph, ownerLabel);
            string? instanceId = RequireTrimmedFilterField(filters.InstanceId, "instanceId", graph, ownerLabel);
            string? varName = RequireTrimmedFilterField(filters.VarName, "varName", graph, ownerLabel);

            TriggerGraphEntryFilterDirection? direction = null;
            if (filters.Direction != null)
            {
                string directionText = filters.Direction.Trim();
                if (string.Equals(directionText, "cross_above", StringComparison.Ordinal))
                {
                    direction = TriggerGraphEntryFilterDirection.CrossAbove;
                }
                else if (string.Equals(directionText, "cross_below", StringComparison.Ordinal))
                {
                    direction = TriggerGraphEntryFilterDirection.CrossBelow;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"{ownerLabel} triggers graph '{graph}' filters field 'direction' must be 'cross_above' or 'cross_below' (got '{directionText}').");
                }
            }

            if (filters.Threshold.HasValue != direction.HasValue)
            {
                throw new InvalidOperationException(
                    $"{ownerLabel} triggers graph '{graph}' filters fields 'threshold' and 'direction' must be declared together.");
            }

            return new TriggerGraphEntry(
                entry.Label,
                entry.EventName,
                entry.StartPc,
                entry.Once,
                new TriggerGraphEntryFilters(
                    region,
                    tag,
                    filters.Team,
                    filters.Threshold,
                    direction,
                    action,
                    instanceId,
                    tagId: null,
                    varName: varName),
                entry.Refire,
                entry.Priority,
                entry.IsHookFragment,
                entry.ActionId);
        }

        private static string? RequireTrimmedFilterField(string? value, string field, string graph, string ownerLabel)
        {
            if (value == null)
            {
                return null;
            }

            string trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{ownerLabel} triggers graph '{graph}' filters field '{field}' requires a non-empty string.");
            }

            return trimmed;
        }

        /// <summary>
        /// #1108 fail-closed contract: the compiler has no map context, so every
        /// LoadPlaced* Imm must be proven against the mounting map's catalogs here.
        /// Programs are symbol-patched before mounting, so Imm is a ConfigKeyRegistry id;
        /// an unresolvable id is itself a load error. Regions never enter EntityIndex.
        /// </summary>
        private static void ValidatePlacedInstanceReads(
            GraphInstruction[] program,
            string graph,
            string fieldName,
            string ownerLabel,
            Ludots.Core.Systems.MapLoadEntityIndex? entityIndex,
            System.Collections.Generic.IReadOnlySet<string>? regionIds)
        {
            for (int i = 0; i < program.Length; i++)
            {
                GraphNodeOp op = (GraphNodeOp)program[i].Op;
                if (op != GraphNodeOp.LoadPlacedEntity &&
                    op != GraphNodeOp.LoadPlacedRegion &&
                    op != GraphNodeOp.LoadPlacedAnchor)
                {
                    continue;
                }

                string instanceId = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.GetName(program[i].Imm)
                    ?? throw new InvalidOperationException(
                        $"{ownerLabel} {fieldName} graph '{graph}' {op} at pc {i} references an unregistered instance key id {program[i].Imm}.");

                if (op == GraphNodeOp.LoadPlacedRegion)
                {
                    if (regionIds == null || !regionIds.Contains(instanceId))
                    {
                        throw new InvalidOperationException(
                            $"{ownerLabel} {fieldName} graph '{graph}' LoadPlacedRegion at pc {i} references unknown region " +
                            $"'{instanceId}' on this map; load fails closed instead of authoring against a ghost (#1108).");
                    }

                    continue;
                }

                if (entityIndex == null || !entityIndex.TryGet(instanceId, out _))
                {
                    throw new InvalidOperationException(
                        $"{ownerLabel} {fieldName} graph '{graph}' {op} at pc {i} references unknown placed instance " +
                        $"'{instanceId}' on this map; load fails closed instead of authoring against a ghost (#1108).");
                }

                if (op == GraphNodeOp.LoadPlacedAnchor &&
                    !Ludots.Core.Systems.PlacedInstanceKinds.IsAnchorInstanceId(instanceId))
                {
                    throw new InvalidOperationException(
                        $"{ownerLabel} {fieldName} graph '{graph}' LoadPlacedAnchor at pc {i} requires InstanceId containing 'anchor' " +
                        $"(got '{instanceId}'); load fails closed (#1108).");
                }
            }
        }

        internal static IReadOnlySet<string> CollectRegionIds(MapSession session)
        {
            List<MapRegionDefinition> regions = MapRegionDefinition.ParseList(
                session.MapConfig?.Regions, session.MapId.Value);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < regions.Count; i++)
            {
                ids.Add(regions[i].Id);
            }

            return ids;
        }
    }
}

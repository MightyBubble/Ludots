using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.Calendar
{
    public sealed class CalendarConfigLoader
    {
        public const string ClockPath = "Calendar/clock.json";
        public const string CalendarsPath = "Calendar/calendars.json";

        private readonly ConfigPipeline _pipeline;

        public CalendarConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public CalendarClockConfig? Load(
            CalendarDefinitionRegistry registry,
            ConfigCatalog? catalog = null,
            ConfigConflictReport? report = null)
        {
            ArgumentNullException.ThrowIfNull(registry);
            registry.Clear();

            bool hasClock = HasFragments(catalog, ClockPath);
            bool hasCalendars = catalog != null && catalog.TryGet(CalendarsPath, out _);
            if (hasCalendars)
            {
                IReadOnlyList<CalendarDefinition> calendars = LoadCalendars(catalog!, report);
                if (calendars.Count == 0)
                {
                    throw new InvalidOperationException($"{CalendarsPath} must declare at least one calendar.");
                }

                for (int i = 0; i < calendars.Count; i++)
                {
                    registry.Register(calendars[i]);
                }
            }

            if (!hasClock)
            {
                return null;
            }

            if (!hasCalendars)
            {
                throw new InvalidOperationException(
                    $"{ClockPath} requires {CalendarsPath} to declare at least one calendar.");
            }

            CalendarClockConfig clock = LoadClock(catalog!, report);
            if (!registry.TryGet(clock.ActiveCalendarId, out _))
            {
                throw new InvalidOperationException(
                    $"{ClockPath}.activeCalendarId '{clock.ActiveCalendarId}' is not registered in {CalendarsPath}.");
            }

            return clock;
        }

        public static CalendarClockConfig ParseClock(JsonObject root, string context = ClockPath)
        {
            string tickSource = RequireCanonicalString(root["tickSource"], $"{context}.tickSource");
            if (!string.Equals(tickSource, "Step", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{context}.tickSource '{tickSource}' is invalid. Supported: Step.");
            }

            int ticksPerDay = RequireInt(root["ticksPerDay"], $"{context}.ticksPerDay");
            if (ticksPerDay < 1)
            {
                throw new InvalidOperationException($"{context}.ticksPerDay must be >= 1.");
            }

            int startDayIndex = RequireInt(root["startDayIndex"], $"{context}.startDayIndex");
            if (startDayIndex < 0)
            {
                throw new InvalidOperationException($"{context}.startDayIndex must be >= 0.");
            }

            string activeCalendarId = RequireCanonicalString(root["activeCalendarId"], $"{context}.activeCalendarId");
            IReadOnlyList<CalendarDayPhaseDefinition> dayPhases = ParseDayPhases(
                root["dayPhases"],
                $"{context}.dayPhases");

            RejectUnknownObjectKeys(
                root,
                context,
                "tickSource",
                "ticksPerDay",
                "startDayIndex",
                "activeCalendarId",
                "dayPhases");

            return new CalendarClockConfig(
                tickSource,
                ticksPerDay,
                startDayIndex,
                activeCalendarId,
                dayPhases);
        }

        public static IReadOnlyList<CalendarDefinition> ParseCalendars(JsonArray array, string context = CalendarsPath)
        {
            var calendars = new List<CalendarDefinition>(array.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject node)
                {
                    throw new InvalidOperationException($"{context}[{i}] must be an object.");
                }

                CalendarDefinition calendar = ParseCalendar(node, $"{context}[{i}]");
                if (!seen.Add(calendar.Id))
                {
                    throw new InvalidOperationException($"{context} repeats calendar id '{calendar.Id}'.");
                }

                calendars.Add(calendar);
            }

            return calendars;
        }

        private bool HasFragments(ConfigCatalog? catalog, string relativePath)
        {
            if (catalog == null || !catalog.TryGet(relativePath, out ConfigCatalogEntry entry))
            {
                return false;
            }

            return _pipeline.CollectFragmentsWithSources(in entry).Count > 0;
        }

        private CalendarClockConfig LoadClock(ConfigCatalog catalog, ConfigConflictReport? report)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, ClockPath, ConfigMergePolicy.DeepObject);
            JsonObject? merged = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (merged == null)
            {
                throw new InvalidOperationException($"{ClockPath} must provide an explicit calendar clock object.");
            }

            return ParseClock(merged, ClockPath);
        }

        private IReadOnlyList<CalendarDefinition> LoadCalendars(ConfigCatalog catalog, ConfigConflictReport? report)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, CalendarsPath, ConfigMergePolicy.ArrayById, "id");
            IReadOnlyList<MergedConfigEntry> merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var array = new JsonArray();
            for (int i = 0; i < merged.Count; i++)
            {
                array.Add(merged[i].Node.DeepClone());
            }

            return ParseCalendars(array, CalendarsPath);
        }

        private static CalendarDefinition ParseCalendar(JsonObject node, string context)
        {
            string id = RequireCanonicalString(node["id"], $"{context}.id");
            int yearLengthDays = RequireInt(node["yearLengthDays"], $"{context}.yearLengthDays");
            if (yearLengthDays < 1)
            {
                throw new InvalidOperationException($"{context}.yearLengthDays must be >= 1.");
            }

            IReadOnlyList<CalendarEraDefinition> eras = ParseEras(node["eras"], $"{context}.eras");
            IReadOnlyList<CalendarCycleDefinition> cycles = ParseCycles(node["cycles"], $"{context}.cycles");
            RejectUnknownObjectKeys(node, context, "id", "yearLengthDays", "eras", "cycles");
            return new CalendarDefinition(id, yearLengthDays, eras, cycles);
        }

        private static IReadOnlyList<CalendarEraDefinition> ParseEras(JsonNode? node, string context)
        {
            JsonArray array = RequireArray(node, context);
            if (array.Count == 0)
            {
                throw new InvalidOperationException($"{context} must contain at least one era.");
            }

            var eras = new List<CalendarEraDefinition>(array.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int previousStart = -1;
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject eraNode)
                {
                    throw new InvalidOperationException($"{context}[{i}] must be an object.");
                }

                string id = RequireCanonicalString(eraNode["id"], $"{context}[{i}].id");
                string label = RequireCanonicalString(eraNode["label"], $"{context}[{i}].label");
                int startDayIndex = RequireInt(eraNode["startDayIndex"], $"{context}[{i}].startDayIndex");
                if (startDayIndex < 0)
                {
                    throw new InvalidOperationException($"{context}[{i}].startDayIndex must be >= 0.");
                }

                if (i == 0 && startDayIndex != 0)
                {
                    throw new InvalidOperationException($"{context}[0].startDayIndex must be 0.");
                }

                if (startDayIndex <= previousStart)
                {
                    throw new InvalidOperationException(
                        $"{context}[{i}].startDayIndex must increase from the previous era.");
                }

                if (!seen.Add(id))
                {
                    throw new InvalidOperationException($"{context} repeats era id '{id}'.");
                }

                RejectUnknownObjectKeys(eraNode, $"{context}[{i}]", "id", "label", "startDayIndex");
                eras.Add(new CalendarEraDefinition(id, label, startDayIndex));
                previousStart = startDayIndex;
            }

            return eras;
        }

        private static IReadOnlyList<CalendarCycleDefinition> ParseCycles(JsonNode? node, string context)
        {
            JsonArray array = RequireArray(node, context);
            if (array.Count == 0)
            {
                throw new InvalidOperationException($"{context} must contain at least one cycle.");
            }

            var cycles = new List<CalendarCycleDefinition>(array.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject cycleNode)
                {
                    throw new InvalidOperationException($"{context}[{i}] must be an object.");
                }

                string id = RequireCanonicalString(cycleNode["id"], $"{context}[{i}].id");
                int lengthDays = RequireInt(cycleNode["lengthDays"], $"{context}[{i}].lengthDays");
                if (lengthDays < 1)
                {
                    throw new InvalidOperationException($"{context}[{i}].lengthDays must be >= 1.");
                }

                IReadOnlyList<CalendarPhaseDefinition> phases = ParsePhases(
                    cycleNode["phases"],
                    $"{context}[{i}].phases",
                    lengthDays);
                if (!seen.Add(id))
                {
                    throw new InvalidOperationException($"{context} repeats cycle id '{id}'.");
                }

                RejectUnknownObjectKeys(cycleNode, $"{context}[{i}]", "id", "lengthDays", "phases");
                cycles.Add(new CalendarCycleDefinition(id, lengthDays, phases));
            }

            return cycles;
        }

        private static IReadOnlyList<CalendarPhaseDefinition> ParsePhases(
            JsonNode? node,
            string context,
            int cycleLengthDays)
        {
            JsonArray array = RequireArray(node, context);
            if (array.Count == 0)
            {
                throw new InvalidOperationException($"{context} must contain at least one phase.");
            }

            var phases = new List<CalendarPhaseDefinition>(array.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int sum = 0;
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject phaseNode)
                {
                    throw new InvalidOperationException($"{context}[{i}] must be an object.");
                }

                string id = RequireCanonicalString(phaseNode["id"], $"{context}[{i}].id");
                string label = RequireCanonicalString(phaseNode["label"], $"{context}[{i}].label");
                int lengthDays = RequireInt(phaseNode["lengthDays"], $"{context}[{i}].lengthDays");
                if (lengthDays < 1)
                {
                    throw new InvalidOperationException($"{context}[{i}].lengthDays must be >= 1.");
                }

                if (!seen.Add(id))
                {
                    throw new InvalidOperationException($"{context} repeats phase id '{id}'.");
                }

                RejectUnknownObjectKeys(phaseNode, $"{context}[{i}]", "id", "label", "lengthDays");
                phases.Add(new CalendarPhaseDefinition(id, label, lengthDays));
                sum = checked(sum + lengthDays);
            }

            if (sum != cycleLengthDays)
            {
                throw new InvalidOperationException(
                    $"{context} phase lengthDays must sum to {cycleLengthDays}, got {sum}.");
            }

            return phases;
        }

        private static IReadOnlyList<CalendarDayPhaseDefinition> ParseDayPhases(JsonNode? node, string context)
        {
            JsonArray array = RequireArray(node, context);
            if (array.Count == 0)
            {
                throw new InvalidOperationException($"{context} must contain at least one day phase.");
            }

            var phases = new List<CalendarDayPhaseDefinition>(array.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int previousStart = -1;
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject phaseNode)
                {
                    throw new InvalidOperationException($"{context}[{i}] must be an object.");
                }

                string id = RequireCanonicalString(phaseNode["id"], $"{context}[{i}].id");
                string label = RequireCanonicalString(phaseNode["label"], $"{context}[{i}].label");
                int startPermille = RequireInt(phaseNode["startPermille"], $"{context}[{i}].startPermille");
                if (i == 0 && startPermille != 0)
                {
                    throw new InvalidOperationException($"{context}[0].startPermille must be 0.");
                }

                if (startPermille < 0 || startPermille >= 1000)
                {
                    throw new InvalidOperationException($"{context}[{i}].startPermille must be in [0, 1000).");
                }

                if (startPermille <= previousStart)
                {
                    throw new InvalidOperationException(
                        $"{context}[{i}].startPermille must increase from the previous day phase.");
                }

                if (!seen.Add(id))
                {
                    throw new InvalidOperationException($"{context} repeats day phase id '{id}'.");
                }

                RejectUnknownObjectKeys(phaseNode, $"{context}[{i}]", "id", "label", "startPermille");
                phases.Add(new CalendarDayPhaseDefinition(id, label, startPermille));
                previousStart = startPermille;
            }

            return phases;
        }

        private static JsonArray RequireArray(JsonNode? node, string context)
        {
            if (node is JsonArray array)
            {
                return array;
            }

            throw new InvalidOperationException($"{context} must be an array.");
        }

        private static string RequireCanonicalString(JsonNode? node, string context)
        {
            if (node is not JsonValue value ||
                !value.TryGetValue<string>(out string? text) ||
                string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{context} requires an explicit semantic string.");
            }

            string trimmed = text.Trim();
            if (!string.Equals(text, trimmed, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{context} must not include leading or trailing whitespace.");
            }

            return text;
        }

        private static int RequireInt(JsonNode? node, string context)
        {
            if (node is not JsonValue value || !value.TryGetValue<int>(out int number))
            {
                throw new InvalidOperationException($"{context} requires an explicit integer field.");
            }

            return number;
        }

        private static void RejectUnknownObjectKeys(JsonObject node, string context, params string[] allowed)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in node)
            {
                bool known = false;
                for (int i = 0; i < allowed.Length; i++)
                {
                    if (string.Equals(pair.Key, allowed[i], StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    throw new InvalidOperationException($"{context} does not allow field '{pair.Key}'.");
                }
            }
        }
    }
}

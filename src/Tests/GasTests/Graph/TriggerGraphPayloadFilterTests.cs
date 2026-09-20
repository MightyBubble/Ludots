using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Ludots.Core.Gameplay.Calendar;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;
using Arch.Core;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// filters.payload 通用载荷订阅（历法场景）：entry 上声明 payload 键值过滤后，
    /// 「春始 / 春末 / 某月起止 / 指定日期」这类订阅在派发时按载荷精确匹配。
    /// 三层各证一段：编译把 authored payload 过滤带进 TriggerGraphEntry；挂载触发器
    /// CheckConditions 只对目标相位 / 日期的真实事件上下文返回 true；上下文由
    /// CalendarRuntime 经 FireGlobalEvent 真实发出（多历并存时按 CalendarId 隔离）。
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class TriggerGraphPayloadFilterTests
    {
        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
        }

        // ── Evaluator：payload 过滤语义 ──

        [Test]
        public void PayloadFilter_StringMatchesOnlyEqualValue()
        {
            var filters = PayloadFilters(("Calendar.PhaseId", "spring"));
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(Context(("Calendar.PhaseId", "spring")), filters), Is.True);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(Context(("Calendar.PhaseId", "summer")), filters), Is.False);
        }

        [Test]
        public void PayloadFilter_IntMatchesOnlyEqualValue()
        {
            var filters = PayloadFilters(("Calendar.DayIndex", 90));
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(Context(("Calendar.DayIndex", 90)), filters), Is.True);
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(Context(("Calendar.DayIndex", 91)), filters), Is.False);
        }

        [Test]
        public void PayloadFilter_MissingKeyNeverMatches()
        {
            var filters = PayloadFilters(("Calendar.CycleId", "season"));
            Assert.That(TriggerGraphEntryFiltersEvaluator.Matches(new ScriptContext(), filters), Is.False);
            Assert.That(
                TriggerGraphEntryFiltersEvaluator.Matches(Context(("Calendar.PhaseId", "spring")), filters),
                Is.False,
                "a sibling payload key present does not satisfy a missing filter key");
        }

        [Test]
        public void PayloadFilter_MultipleFiltersAllMustMatch()
        {
            var filters = PayloadFilters(("Calendar.CycleId", "season"), ("Calendar.PhaseId", "spring"));
            Assert.That(
                TriggerGraphEntryFiltersEvaluator.Matches(
                    Context(("Calendar.CycleId", "season"), ("Calendar.PhaseId", "spring")), filters),
                Is.True);
            Assert.That(
                TriggerGraphEntryFiltersEvaluator.Matches(
                    Context(("Calendar.CycleId", "month"), ("Calendar.PhaseId", "spring")), filters),
                Is.False);
        }

        // ── 编译：authored payload 过滤进 TriggerGraphEntry ──

        [Test]
        public void Compile_PayloadFilters_ReachCompiledEntries()
        {
            GraphControlFlowCompileResult result = CompileCalendarDoc("""
                {
                  "id": "Graph.Probe.Calendar.Payload",
                  "kind": "TriggerGraph",
                  "entries": [
                    {
                      "label": "on_spring_begin",
                      "event": "Calendar.CyclePhaseEntered",
                      "start": "act",
                      "filters": { "payload": { "Calendar.CycleId": "season", "Calendar.PhaseId": "spring" } }
                    },
                    {
                      "label": "on_exact_day",
                      "event": "Calendar.DayAdvanced",
                      "start": "act",
                      "filters": { "payload": { "Calendar.DayIndex": 90 } }
                    }
                  ],
                  "nodes": [ { "id": "act", "op": "HaltReturnInt" } ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """);

            Assert.That(result.Diagnostics.Where(d => d.Severity == GraphDiagnosticSeverity.Error).ToList(), Is.Empty,
                () => string.Join("\n", result.Diagnostics.Select(d => d.Message)));

            TriggerGraphEntry spring = result.Package!.Value.TriggerGraphEntries.Single(e => e.Label == "on_spring_begin");
            Assert.That(spring.Filters.Payload, Is.Not.Null);
            Assert.That(spring.Filters.Payload!.Select(f => (f.Key, f.StringValue, f.IntValue)), Is.EqualTo(new[]
            {
                ("Calendar.CycleId", "season", (int?)null),
                ("Calendar.PhaseId", "spring", (int?)null),
            }));

            TriggerGraphEntry exactDay = result.Package!.Value.TriggerGraphEntries.Single(e => e.Label == "on_exact_day");
            Assert.That(exactDay.Filters.Payload!.Single().IntValue, Is.EqualTo(90));
        }

        [TestCase("true")]
        [TestCase("1.5")]
        [TestCase("\"  \"")]
        public void Compile_PayloadFilter_WrongValueKind_Rejected(string rawValue)
        {
            GraphControlFlowCompileResult result = CompileCalendarDoc($$"""
                {
                  "id": "Graph.Probe.Calendar.Payload.Bad",
                  "kind": "TriggerGraph",
                  "entries": [
                    {
                      "label": "on_bad",
                      "event": "Calendar.CyclePhaseEntered",
                      "start": "act",
                      "filters": { "payload": { "Calendar.PhaseId": {{rawValue}} } }
                    }
                  ],
                  "nodes": [ { "id": "act", "op": "HaltReturnInt" } ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Severity == GraphDiagnosticSeverity.Error &&
                    d.Code == GraphDiagnosticCodes.InvalidEntryFilters),
                Is.True,
                $"raw value {rawValue} must fail compile");
        }

        // ── 端到端订阅：真实历法事件上下文 × 挂载触发器 CheckConditions ──

        [Test]
        public void CalendarSubscriptions_MatchOnlyTheirPhaseOrDay()
        {
            List<ScriptContext> entered = CaptureCalendarEvents(startDayIndex: 359, GameEvents.CalendarCyclePhaseEntered);
            List<ScriptContext> exited = CaptureCalendarEvents(startDayIndex: 359, GameEvents.CalendarCyclePhaseExited);
            List<ScriptContext> dayAdvanced = CaptureCalendarEvents(startDayIndex: 359, GameEvents.CalendarDayAdvanced);

            TriggerGraphMountTrigger onWinterEnd = MountEntry(
                "on_winter_end", GameEvents.CalendarCyclePhaseExited,
                ("Calendar.CalendarId", "calendar.solar360"), ("Calendar.CycleId", "season"), ("Calendar.PhaseId", "winter"));
            TriggerGraphMountTrigger onSpringBegin = MountEntry(
                "on_spring_begin", GameEvents.CalendarCyclePhaseEntered,
                ("Calendar.CalendarId", "calendar.solar360"), ("Calendar.CycleId", "season"), ("Calendar.PhaseId", "spring"));
            TriggerGraphMountTrigger onNewYearDay = MountEntry(
                "on_new_year_day", GameEvents.CalendarDayAdvanced,
                ("Calendar.CalendarId", "calendar.solar360"), ("Calendar.DayIndex", 360));

            // day 359→360：solar360 的 winter 退出、spring 进入、日序 360；阴阳历同日也在换相位。
            Assert.That(exited.Count(c => onWinterEnd.CheckConditions(c)), Is.EqualTo(1),
                "exactly one context is solar360's winter exit");
            Assert.That(entered.Count(c => onSpringBegin.CheckConditions(c)), Is.EqualTo(1),
                "exactly one context is solar360's spring entry; lunisolar phase entries on the same day must not match");
            Assert.That(dayAdvanced.Count(c => onNewYearDay.CheckConditions(c)), Is.EqualTo(1));

            // 同一批上下文对其他日期 / 相位的订阅一律不匹配。
            Assert.That(entered.Count(c => onWinterEnd.CheckConditions(c)), Is.EqualTo(0));
            Assert.That(exited.Count(c => onSpringBegin.CheckConditions(c)), Is.EqualTo(0));
            TriggerGraphMountTrigger onDay90 = MountEntry(
                "on_day_90", GameEvents.CalendarDayAdvanced,
                ("Calendar.CalendarId", "calendar.solar360"), ("Calendar.DayIndex", 90));
            Assert.That(dayAdvanced.Count(c => onDay90.CheckConditions(c)), Is.EqualTo(0),
                "day 360 contexts must not satisfy a day-90 subscription");
        }

        [Test]
        public void CalendarSubscriptions_MonthBoundary_SubscribesOneMonthOnly()
        {
            List<ScriptContext> entered = CaptureCalendarEvents(startDayIndex: 89, GameEvents.CalendarCyclePhaseEntered);

            TriggerGraphMountTrigger onMonth4Begin = MountEntry(
                "on_month4_begin", GameEvents.CalendarCyclePhaseEntered,
                ("Calendar.CalendarId", "calendar.solar360"), ("Calendar.CycleId", "month"), ("Calendar.PhaseId", "month.04"));
            TriggerGraphMountTrigger onMonth5Begin = MountEntry(
                "on_month5_begin", GameEvents.CalendarCyclePhaseEntered,
                ("Calendar.CalendarId", "calendar.solar360"), ("Calendar.CycleId", "month"), ("Calendar.PhaseId", "month.05"));

            // day 89→90：month.03 退出、month.04 进入；month.05 没动。
            Assert.That(entered.Count(c => onMonth4Begin.CheckConditions(c)), Is.EqualTo(1));
            Assert.That(entered.Count(c => onMonth5Begin.CheckConditions(c)), Is.EqualTo(0));
        }

        // ── helpers ──

        private static TriggerGraphEntryFilters PayloadFilters(params (string Key, object Value)[] pairs)
        {
            var filters = new List<TriggerGraphEntryPayloadFilter>();
            for (int i = 0; i < pairs.Length; i++)
            {
                filters.Add(pairs[i].Value is int intValue
                    ? new TriggerGraphEntryPayloadFilter(pairs[i].Key, null, intValue)
                    : new TriggerGraphEntryPayloadFilter(pairs[i].Key, (string)pairs[i].Value, null));
            }

            return new TriggerGraphEntryFilters(null, null, null, null, null, payload: filters);
        }

        private static ScriptContext Context(params (string Key, object Value)[] pairs)
        {
            var context = new ScriptContext();
            for (int i = 0; i < pairs.Length; i++)
            {
                if (pairs[i].Value is int intValue)
                {
                    context.Set(pairs[i].Key, intValue);
                }
                else
                {
                    context.Set(pairs[i].Key, (string)pairs[i].Value);
                }
            }

            return context;
        }

        private static GraphControlFlowCompileResult CompileCalendarDoc(string json)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
            };
            GraphControlFlowDocument doc = JsonSerializer.Deserialize<GraphControlFlowDocument>(json, options)!;
            return GraphControlFlowCompiler.Compile(doc, new EventSchemaRegistry());
        }

        /// <summary>用默认三历表跑一天跨界，捕获指定事件的真实派发上下文。</summary>
        private static List<ScriptContext> CaptureCalendarEvents(int startDayIndex, EventKey eventKey)
        {
            var manager = new TriggerManager { EventSchemas = new EventSchemaRegistry() };
            var captured = new List<ScriptContext>();
            manager.RegisterGlobalTriggers(new MapId("calendar_payload_probe_map"), new Trigger[]
            {
                new CapturingTrigger(eventKey, captured),
            });

            var registry = new CalendarDefinitionRegistry();
            foreach (CalendarDefinition calendar in CalendarConfigLoader.ParseCalendars(
                ParseDefaultCalendarsJson()))
            {
                registry.Register(calendar);
            }

            var runtime = new CalendarRuntime(
                new CalendarWorldConfig(
                    "Step",
                    TicksPerDay: 1,
                    StartDayIndex: startDayIndex,
                    ActiveCalendarId: "calendar.solar360",
                    DayPhases: new[] { new CalendarDayPhaseDefinition("dawn", "晓", 0) }),
                registry);
            runtime.Advance(1, () => new ScriptContext(), manager.FireGlobalEvent, manager.HasGlobalEventSubscribers);
            return captured;
        }

        private static System.Text.Json.Nodes.JsonArray ParseDefaultCalendarsJson()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj")))
                {
                    return (System.Text.Json.Nodes.JsonArray)System.Text.Json.Nodes.JsonNode.Parse(
                        File.ReadAllText(Path.Combine(dir.FullName, "assets", "Calendar", "calendars.json")))!;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
        }

        private static TriggerGraphMountTrigger MountEntry(
            string label,
            EventKey eventKey,
            params (string Key, object Value)[] payloadFilters)
        {
            var filters = new List<TriggerGraphEntryPayloadFilter>();
            for (int i = 0; i < payloadFilters.Length; i++)
            {
                filters.Add(payloadFilters[i].Value is int intValue
                    ? new TriggerGraphEntryPayloadFilter(payloadFilters[i].Key, null, intValue)
                    : new TriggerGraphEntryPayloadFilter(payloadFilters[i].Key, (string)payloadFilters[i].Value, null));
            }

            var entry = new TriggerGraphEntry(
                label,
                eventKey.Value,
                startPc: 0,
                once: false,
                new TriggerGraphEntryFilters(null, null, null, null, null, payload: filters));
            return new TriggerGraphMountTrigger(
                GraphIdRegistry.Register($"Graph.Probe.Calendar.{label}"),
                $"Graph.Probe.Calendar.{label}",
                entry,
                default);
        }

        private sealed class CapturingTrigger : Trigger
        {
            private readonly List<ScriptContext> _captured;

            public CapturingTrigger(EventKey eventKey, List<ScriptContext> captured)
            {
                EventKey = eventKey;
                _captured = captured;
            }

            public override Task ExecuteAsync(ScriptContext context)
            {
                _captured.Add(context);
                return System.Threading.Tasks.Task.CompletedTask;
            }
        }
    }
}

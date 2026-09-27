using System.Text.Json.Nodes;
using Ludots.Core.Gameplay.Calendar;
using NUnit.Framework;

namespace Ludots.Tests.CalendarCore;

[TestFixture]
public sealed class CalendarConfigLoaderTests
{
    [Test]
    public void ParseWorld_RejectsNonStepTickSource()
    {
        JsonObject node = ParseObject("""
            {
              "tickSource": "FixedFrame",
              "ticksPerDay": 20,
              "startDayIndex": 0,
              "activeCalendarId": "calendar.solar360",
              "dayPhases": [ { "id": "dawn", "label": "晓", "startPermille": 0 } ]
            }
            """);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CalendarConfigLoader.ParseWorld(node))!;
        Assert.That(ex.Message, Does.Contain("tickSource"));
        Assert.That(ex.Message, Does.Contain("Step"));
    }

    [Test]
    public void ParseWorld_RejectsMinutesPerDay()
    {
        JsonObject node = ParseObject("""
            {
              "tickSource": "Step",
              "ticksPerDay": 20,
              "startDayIndex": 0,
              "activeCalendarId": "calendar.solar360",
              "minutesPerDay": 1440,
              "dayPhases": [ { "id": "dawn", "label": "晓", "startPermille": 0 } ]
            }
            """);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CalendarConfigLoader.ParseWorld(node))!;
        Assert.That(ex.Message, Does.Contain("minutesPerDay"));
    }

    [Test]
    public void ParseCalendars_RejectsPhaseLengthMismatch()
    {
        JsonArray array = ParseArray("""
            [
              {
                "id": "calendar.broken",
                "yearLengthDays": 360,
                "eras": [ { "id": "era.founding", "label": "立国", "startDayIndex": 0 } ],
                "cycles": [
                  {
                    "id": "season",
                    "lengthDays": 360,
                    "phases": [
                      { "id": "spring", "label": "春", "lengthDays": 90 },
                      { "id": "summer", "label": "夏", "lengthDays": 90 }
                    ]
                  }
                ]
              }
            ]
            """);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CalendarConfigLoader.ParseCalendars(array))!;
        Assert.That(ex.Message, Does.Contain("sum to 360"));
    }

    [Test]
    public void ParseCalendars_RejectsUnknownField()
    {
        JsonArray array = ParseArray("""
            [
              {
                "id": "calendar.broken",
                "yearLengthDays": 360,
                "fallback": true,
                "eras": [ { "id": "era.founding", "label": "立国", "startDayIndex": 0 } ],
                "cycles": [
                  {
                    "id": "season",
                    "lengthDays": 90,
                    "phases": [ { "id": "spring", "label": "春", "lengthDays": 90 } ]
                  }
                ]
              }
            ]
            """);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CalendarConfigLoader.ParseCalendars(array))!;
        Assert.That(ex.Message, Does.Contain("fallback"));
    }

    [Test]
    public void ParseCalendars_LoadsShippedTables()
    {
        JsonArray array = ParseArray(CalendarFixtures.Solar360Json());
        IReadOnlyList<CalendarDefinition> calendars = CalendarConfigLoader.ParseCalendars(array);
        Assert.That(calendars.Select(c => c.Id), Is.EqualTo(new[]
        {
            "calendar.solar360",
            "calendar.lunisolar.zhang19",
            "calendar.regnal",
        }));
        Assert.That(calendars[0].Cycles.Select(c => c.Id), Is.EqualTo(new[]
        {
            "season", "month", "xun", "solarTerm", "festival",
        }));
        Assert.That(calendars[0].YearCycleId, Is.Null);
        Assert.That(calendars[1].YearCycleId, Is.EqualTo("year"));
        Assert.That(calendars[1].YearLengthDays, Is.Null);
        Assert.That(calendars[2].Cycles, Is.Empty);
    }

    [Test]
    public void ParseCalendars_RejectsYearModeWhenBothOrNeitherDeclared()
    {
        string both = """
            [
              {
                "id": "calendar.broken",
                "yearLengthDays": 360,
                "yearCycleId": "year",
                "eras": [ { "id": "era.founding", "label": "立国", "startDayIndex": 0 } ],
                "cycles": [
                  { "id": "year", "lengthDays": 30, "phases": [ { "id": "y1", "label": "一", "lengthDays": 30 } ] }
                ]
              }
            ]
            """;
        InvalidOperationException bothEx = Assert.Throws<InvalidOperationException>(
            () => CalendarConfigLoader.ParseCalendars(ParseArray(both)))!;
        Assert.That(bothEx.Message, Does.Contain("exactly one of yearLengthDays / yearCycleId"));

        string neither = """
            [
              {
                "id": "calendar.broken",
                "eras": [ { "id": "era.founding", "label": "立国", "startDayIndex": 0 } ],
                "cycles": []
              }
            ]
            """;
        InvalidOperationException neitherEx = Assert.Throws<InvalidOperationException>(
            () => CalendarConfigLoader.ParseCalendars(ParseArray(neither)))!;
        Assert.That(neitherEx.Message, Does.Contain("exactly one of yearLengthDays / yearCycleId"));
    }

    [Test]
    public void ParseCalendars_RejectsYearCycleIdNamingForeignCycle()
    {
        JsonArray array = ParseArray("""
            [
              {
                "id": "calendar.broken",
                "yearCycleId": "year",
                "eras": [ { "id": "era.founding", "label": "立国", "startDayIndex": 0 } ],
                "cycles": [
                  { "id": "season", "lengthDays": 90, "phases": [ { "id": "spring", "label": "春", "lengthDays": 90 } ] }
                ]
              }
            ]
            """);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CalendarConfigLoader.ParseCalendars(array))!;
        Assert.That(ex.Message, Does.Contain("yearCycleId 'year'"));
    }

    [Test]
    public void ParseCalendars_AcceptsEmptyCyclesForPureEraCalendar()
    {
        JsonArray array = ParseArray("""
            [
              {
                "id": "calendar.eraOnly",
                "yearLengthDays": 360,
                "eras": [ { "id": "era.founding", "label": "立国", "startDayIndex": 0 } ],
                "cycles": []
              }
            ]
            """);

        IReadOnlyList<CalendarDefinition> calendars = CalendarConfigLoader.ParseCalendars(array);
        Assert.That(calendars[0].Cycles, Is.Empty);
        Assert.That(calendars[0].YearLengthDays, Is.EqualTo(360));
    }

    private static JsonObject ParseObject(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static JsonArray ParseArray(string json) => (JsonArray)JsonNode.Parse(json)!;
}

using System.Text.Json.Nodes;
using Ludots.Core.Gameplay.Calendar;
using NUnit.Framework;

namespace Ludots.Tests.CalendarCore;

[TestFixture]
public sealed class CalendarConfigLoaderTests
{
    [Test]
    public void ParseClock_RejectsNonStepTickSource()
    {
        JsonObject node = ParseObject("""
            {
              "tickSource": "FixedFrame",
              "ticksPerDay": 20,
              "startDayIndex": 0,
              "activeCalendarId": "calendar.solar360",
              "minutesPerDay": 1440,
              "dayPhases": [ { "id": "dawn", "label": "晓", "startPermille": 0 } ]
            }
            """);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CalendarConfigLoader.ParseClock(node))!;
        Assert.That(ex.Message, Does.Contain("tickSource"));
        Assert.That(ex.Message, Does.Contain("Step"));
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
    public void ParseCalendars_LoadsShippedSolar360()
    {
        JsonArray array = ParseArray(CalendarFixtures.Solar360Json());
        IReadOnlyList<CalendarDefinition> calendars = CalendarConfigLoader.ParseCalendars(array);
        Assert.That(calendars.Count, Is.EqualTo(1));
        Assert.That(calendars[0].Id, Is.EqualTo("calendar.solar360"));
        Assert.That(calendars[0].Cycles.Count, Is.EqualTo(4));
    }

    private static JsonObject ParseObject(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static JsonArray ParseArray(string json) => (JsonArray)JsonNode.Parse(json)!;
}

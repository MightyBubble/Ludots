using System.Text.Json.Nodes;
using Ludots.Core.Gameplay.Calendar;

namespace Ludots.Tests.CalendarCore;

internal static class CalendarFixtures
{
    public static IReadOnlyList<CalendarDayPhaseDefinition> DefaultDayPhases()
    {
        return new[]
        {
            new CalendarDayPhaseDefinition("dawn", "晓", 0),
            new CalendarDayPhaseDefinition("day", "昼", 250),
            new CalendarDayPhaseDefinition("dusk", "暮", 750),
            new CalendarDayPhaseDefinition("night", "夜", 875),
        };
    }

    public static CalendarWorldConfig World(string activeCalendarId = "calendar.solar360", int ticksPerDay = 20, int startDayIndex = 0)
    {
        return new CalendarWorldConfig(
            TickSource: "Step",
            TicksPerDay: ticksPerDay,
            StartDayIndex: startDayIndex,
            ActiveCalendarId: activeCalendarId,
            DayPhases: DefaultDayPhases());
    }

    public static CalendarDefinition Solar360()
    {
        return ParseCalendar(Solar360Json());
    }

    public static CalendarDefinition Regnal()
    {
        return new CalendarDefinition(
            "calendar.regnal",
            360,
            new[]
            {
                new CalendarEraDefinition("era.founding", "立国", 0),
                new CalendarEraDefinition("era.expansion", "开疆", 3600),
            },
            Solar360().Cycles);
    }

    public static CalendarDefinitionRegistry Registry(params CalendarDefinition[] calendars)
    {
        var registry = new CalendarDefinitionRegistry();
        for (int i = 0; i < calendars.Length; i++)
        {
            registry.Register(calendars[i]);
        }

        return registry;
    }

    public static CalendarDefinition ParseCalendar(string json)
    {
        JsonArray array = (JsonArray)JsonNode.Parse(json)!;
        return CalendarConfigLoader.ParseCalendars(array)[0];
    }

    public static string Solar360Json()
    {
        return File.ReadAllText(Path.Combine(FindRepoRoot(), "assets", "Calendar", "calendars.json"));
    }

    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}

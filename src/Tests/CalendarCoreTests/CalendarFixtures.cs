using System.Text.Json.Nodes;
using Ludots.Core.Gameplay.Calendar;

namespace Ludots.Tests.CalendarCore;

internal static class CalendarFixtures
{
    public const string Solar360Id = "calendar.solar360";
    public const string LunisolarId = "calendar.lunisolar.zhang19";
    public const string RegnalId = "calendar.regnal";

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

    public static CalendarWorldConfig World(string activeCalendarId = Solar360Id, int ticksPerDay = 20, int startDayIndex = 0)
    {
        IReadOnlyList<CalendarDayPhaseDefinition> dayPhases = DefaultDayPhases();
        // 与装载器同一符号注册（ParseWorld 的职责在此由 fixture 代办；幂等）。
        for (int i = 0; i < dayPhases.Count; i++)
        {
            Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(dayPhases[i].Id);
        }

        Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(activeCalendarId);
        return new CalendarWorldConfig(
            TickSource: "Step",
            TicksPerDay: ticksPerDay,
            StartDayIndex: startDayIndex,
            ActiveCalendarId: activeCalendarId,
            DayPhases: dayPhases);
    }

    public static CalendarDefinition Solar360()
    {
        return ParseCalendar(Solar360Json(), Solar360Id);
    }

    public static CalendarDefinition Lunisolar()
    {
        return ParseCalendar(Solar360Json(), LunisolarId);
    }

    public static CalendarDefinition Regnal()
    {
        return ParseCalendar(Solar360Json(), RegnalId);
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

    public static CalendarDefinitionRegistry DefaultRegistry()
    {
        return Registry(Solar360(), Lunisolar(), Regnal());
    }

    public static CalendarDefinition ParseCalendar(string json, string calendarId)
    {
        JsonArray array = (JsonArray)JsonNode.Parse(json)!;
        foreach (JsonNode? node in array)
        {
            CalendarDefinition calendar = CalendarConfigLoader.ParseCalendars(
                new JsonArray(node!.DeepClone()))[0];
            if (string.Equals(calendar.Id, calendarId, System.StringComparison.Ordinal))
            {
                return calendar;
            }
        }

        throw new System.InvalidOperationException($"Calendar '{calendarId}' not found in default tables.");
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

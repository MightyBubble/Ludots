namespace TimeFlowShowcaseMod;

public static class TimeFlowShowcaseIds
{
    public const string StartupMapId = "timeflow_atb_wait";
    public const string AtbWaitMapId = "timeflow_atb_wait";
    public const string DotaUltMapId = "timeflow_dota_manual_ult";
    public const string BreakFeverMapId = "timeflow_break_fever";
    public const string SentinelPauseMapId = "timeflow_sentinel_pause";
    public const string Ck3MacroMapId = "timeflow_ck3_macro";
    public const string BadNorthMapId = "timeflow_bad_north";

    public static IReadOnlyList<string> AllMapIds { get; } = new[]
    {
        AtbWaitMapId,
        DotaUltMapId,
        BreakFeverMapId,
        SentinelPauseMapId,
        Ck3MacroMapId,
        BadNorthMapId
    };

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, AtbWaitMapId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mapId, DotaUltMapId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mapId, BreakFeverMapId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mapId, SentinelPauseMapId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mapId, Ck3MacroMapId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mapId, BadNorthMapId, StringComparison.OrdinalIgnoreCase);
    }

    public static TimeFlowScenarioKind ResolveScenario(string mapId)
    {
        return mapId switch
        {
            AtbWaitMapId => TimeFlowScenarioKind.AtbWait,
            DotaUltMapId => TimeFlowScenarioKind.DotaManualUlt,
            BreakFeverMapId => TimeFlowScenarioKind.BreakFever,
            SentinelPauseMapId => TimeFlowScenarioKind.SentinelCommandPause,
            Ck3MacroMapId => TimeFlowScenarioKind.Ck3Macro,
            BadNorthMapId => TimeFlowScenarioKind.BadNorthActivePause,
            _ => throw new InvalidOperationException($"Unknown timeflow showcase map '{mapId}'.")
        };
    }
}

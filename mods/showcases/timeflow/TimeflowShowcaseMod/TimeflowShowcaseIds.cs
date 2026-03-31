namespace TimeflowShowcaseMod;

public static class TimeflowShowcaseIds
{
    public const string ModId = "TimeflowShowcaseMod";
    public const string MapId = "timeflow_showcase";
    public const string InputContextId = "TimeflowShowcase.Controls";

    public const string ScenarioAtbActionId = "TimeflowScenarioAtb";
    public const string ScenarioAutoBattleActionId = "TimeflowScenarioAutoBattle";
    public const string ScenarioBreakFeverActionId = "TimeflowScenarioBreakFever";
    public const string ScenarioSentinelsActionId = "TimeflowScenarioSentinels";
    public const string ScenarioCk3ActionId = "TimeflowScenarioCk3";
    public const string ScenarioBadNorthActionId = "TimeflowScenarioBadNorth";
    public const string GlobalPauseActionId = "TimeflowGlobalPause";
    public const string GlobalBulletActionId = "TimeflowGlobalBullet";
    public const string ResetShowcaseActionId = "TimeflowReset";
    public const string PrimaryAActionId = "TimeflowPrimaryA";
    public const string PrimaryBActionId = "TimeflowPrimaryB";
    public const string PrimaryCActionId = "TimeflowPrimaryC";
    public const string PrimaryDActionId = "TimeflowPrimaryD";
    public const string OptionAActionId = "TimeflowOptionA";
    public const string OptionBActionId = "TimeflowOptionB";
    public const string OptionCActionId = "TimeflowOptionC";
    public const string TogglePauseActionId = "TimeflowTogglePause";
    public const string ConfirmActionId = "TimeflowConfirm";
    public const string Speed1ActionId = "TimeflowSpeed1";
    public const string Speed2ActionId = "TimeflowSpeed2";
    public const string Speed3ActionId = "TimeflowSpeed3";
    public const string Speed4ActionId = "TimeflowSpeed4";

    public static bool IsShowcaseMap(string? mapId) =>
        string.Equals(mapId, MapId, System.StringComparison.OrdinalIgnoreCase);
}

public enum TimeflowScenarioId : byte
{
    Atb = 0,
    AutoBattle = 1,
    BreakFever = 2,
    Sentinels = 3,
    CrusaderKings = 4,
    BadNorth = 5
}

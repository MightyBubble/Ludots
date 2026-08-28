namespace DualSeatPanelsShowcaseMod;

/// <summary>Stable ids shared by the showcase assets, runtime, and acceptance tests.</summary>
public static class DualSeatPanelsShowcaseIds
{
    public const string MapId = "dual_seat_panels_arena";

    public const string SeatZero = "seat.0";
    public const string SeatOne = "seat.1";

    public const string SeatZeroPanelId = "panel.dsp.seat0";
    public const string SeatOnePanelId = "panel.dsp.seat1";
    public const string SharedPanelId = "panel.dsp.shared";

    public const string ModifyEventId = "ui.dsp.modify";
    public const string ChargeEventId = "ui.dsp.charge";

    public const string BoostUsedEvent = "DSP.PanelBoost.Used";
    public const string SharedChargeUsedEvent = "DSP.SharedCharge.Used";

    public const string BoostAction = "DSP.SelfBoost";
    public const string StrikeAction = "DSP.SelfStrike";
    public const string PokeAction = "DSP.PokeOther";
    public const string ChargeAction = "DSP.SharedCharge";
    public const string RotateTurnAction = "DSP.RotateTurn";

    public static bool IsShowcaseMap(string? mapId) => mapId == MapId;
}

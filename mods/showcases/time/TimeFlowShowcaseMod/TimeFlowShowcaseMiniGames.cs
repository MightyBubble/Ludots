namespace TimeFlowShowcaseMod;

public sealed record TimeFlowMiniGameEntry(
    string ModId,
    string MapId,
    TimeFlowScenarioKind ScenarioKind,
    string MenuTitle,
    string Description);

public sealed record TimeFlowMiniGameDescriptor(
    TimeFlowScenarioKind ScenarioKind,
    string MenuTitle,
    string Pitch,
    string Goal,
    string WatchFor,
    string Success);

public sealed record TimeFlowActionPrompt(
    string Label,
    bool Active,
    bool Enabled);

public static class TimeFlowShowcaseMiniGames
{
    public const string AtbWaitEntryModId = "TimeFlowAtbWaitMiniMod";
    public const string DotaUltEntryModId = "TimeFlowDotaUltMiniMod";
    public const string BreakFeverEntryModId = "TimeFlowBreakFeverMiniMod";
    public const string SentinelPauseEntryModId = "TimeFlowSentinelPauseMiniMod";
    public const string Ck3MacroEntryModId = "TimeFlowCk3MacroMiniMod";
    public const string BadNorthEntryModId = "TimeFlowBadNorthMiniMod";

    public static IReadOnlyList<TimeFlowMiniGameEntry> EntryMods { get; } = new[]
    {
        new TimeFlowMiniGameEntry(
            AtbWaitEntryModId,
            TimeFlowShowcaseIds.AtbWaitMapId,
            TimeFlowScenarioKind.AtbWait,
            "ATB Wait Duel",
            "A classic wait-mode JRPG duel where the battle freezes while the ready hero picks an action."),
        new TimeFlowMiniGameEntry(
            DotaUltEntryModId,
            TimeFlowShowcaseIds.DotaUltMapId,
            TimeFlowScenarioKind.DotaManualUlt,
            "Manual Ult Freeze",
            "An auto-battle clash that hard-freezes at full energy before dropping into brief bullet time."),
        new TimeFlowMiniGameEntry(
            BreakFeverEntryModId,
            TimeFlowShowcaseIds.BreakFeverMapId,
            TimeFlowScenarioKind.BreakFever,
            "Break Fever Rush",
            "A burst window where the world slows down and the allied team gets a temporary overclock."),
        new TimeFlowMiniGameEntry(
            SentinelPauseEntryModId,
            TimeFlowShowcaseIds.SentinelPauseMapId,
            TimeFlowScenarioKind.SentinelCommandPause,
            "Sentinel Command Pause",
            "Realtime WT countdowns stop cold when a unit becomes ready so the next command can be chosen."),
        new TimeFlowMiniGameEntry(
            Ck3MacroEntryModId,
            TimeFlowShowcaseIds.Ck3MacroMapId,
            TimeFlowScenarioKind.Ck3Macro,
            "Realm Speed Ladder",
            "A macro strategy clock that pauses for planning, accelerates through quiet stretches, then stops for an event."),
        new TimeFlowMiniGameEntry(
            BadNorthEntryModId,
            TimeFlowShowcaseIds.BadNorthMapId,
            TimeFlowScenarioKind.BadNorthActivePause,
            "Island Active Pause",
            "An island defense drill where you can stop time mid-fight to redraw squad assignments.")
    };

    public static TimeFlowMiniGameDescriptor Describe(TimeFlowScenarioKind kind)
    {
        return kind switch
        {
            TimeFlowScenarioKind.AtbWait => new TimeFlowMiniGameDescriptor(
                kind,
                "ATB Wait Duel",
                "The hero and monsters race to fill their action gauges in realtime.",
                "Watch the first ally hit full charge, open a frozen command window, then strike before realtime resumes.",
                "The key timing change is a full stop while the command menu is open.",
                "Success is the ally landing the queued hit and the fight returning to normal speed."),
            TimeFlowScenarioKind.DotaManualUlt => new TimeFlowMiniGameDescriptor(
                kind,
                "Manual Ult Freeze",
                "Both sides trade blows automatically while the captain charges a manual ultimate.",
                "Reach full energy, freeze the battlefield for the ult commit, then cash out a short bullet-time finish.",
                "The key timing change is a hard freeze followed by a 30% speed slow-motion payout window.",
                "Success is the ultimate firing cleanly and the battle snapping back to baseline cadence."),
            TimeFlowScenarioKind.BreakFever => new TimeFlowMiniGameDescriptor(
                kind,
                "Break Fever Rush",
                "The team builds a break meter until a fever burst window opens.",
                "Fill the break bar, overclock the allied side, and burn the target during the slower fever state.",
                "The key timing change is a slower world plus faster local ally clocks during fever.",
                "Success is the fever window ending cleanly after the burst damage lands."),
            TimeFlowScenarioKind.SentinelCommandPause => new TimeFlowMiniGameDescriptor(
                kind,
                "Sentinel Command Pause",
                "WT countdowns tick in realtime until a pilot becomes ready to act.",
                "Wait for the next ready unit, stop combat, queue the attack, then resume the live battlefield.",
                "The key timing change is a full pause whenever a command decision is needed.",
                "Success is the queued attack resolving and WT resetting back into realtime flow."),
            TimeFlowScenarioKind.Ck3Macro => new TimeFlowMiniGameDescriptor(
                kind,
                "Realm Speed Ladder",
                "A paused strategy layer turns orders into a campaign that accelerates through safe stretches.",
                "Plan while paused, resume at 1x, climb the speed ladder, stop on the event card, then fast-forward again.",
                "The key timing change is switching between full pause and stepped macro speed levels.",
                "Success is the event being resolved and the campaign finishing its full speed ladder."),
            TimeFlowScenarioKind.BadNorthActivePause => new TimeFlowMiniGameDescriptor(
                kind,
                "Island Active Pause",
                "A small island defense runs in realtime until the plan needs a tactical correction.",
                "Freeze the battle to assign lanes, resume, then pause again when a flank forces a retarget.",
                "The key timing change is player-driven active pause reopening command windows mid-fight.",
                "Success is both squads stabilizing their lanes after the second resume."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    public static string DescribeBeat(TimeFlowShowcaseSnapshot snapshot)
    {
        return (snapshot.ScenarioKind, snapshot.Phase) switch
        {
            (TimeFlowScenarioKind.AtbWait, "ATB.Realtime") => "Gauge race in live realtime.",
            (TimeFlowScenarioKind.AtbWait, "ATB.CommandPause") => "Command window open and time is frozen.",

            (TimeFlowScenarioKind.DotaManualUlt, "Dota.AutoBattle") => "Auto-battle trading at normal speed.",
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.UltFreeze") => "Ultimate confirm freeze frame.",
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.BulletTime") => "Slow-motion aftermath after the ult.",

            (TimeFlowScenarioKind.BreakFever, "Break.Build") => "Building the break meter.",
            (TimeFlowScenarioKind.BreakFever, "Break.Fever") => "Fever burst is active.",

            (TimeFlowScenarioKind.SentinelCommandPause, "Sentinel.Realtime") => "WT timers ticking in realtime.",
            (TimeFlowScenarioKind.SentinelCommandPause, "Sentinel.CommandPause") => "Ready pilot pause window.",

            (TimeFlowScenarioKind.Ck3Macro, "CK3.Pause") => "Realm clock paused for planning.",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed1") => "Campaign moving at 1x.",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed2") => "Campaign moving at 2x.",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed3") => "Campaign moving at 3x.",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.EventPause") => "Event card stopped the realm clock.",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed4") => "Quiet stretch fast-forward at 4x.",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Complete") => "Campaign ladder finished.",

            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.ActivePause") => "Opening active-pause command window.",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.Realtime") => "Squads marching in live combat.",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.RevectorPause") => "Emergency pause for lane correction.",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.Finish") => "Final realtime cleanup after the retarget.",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.Complete") => "Island defense stabilized.",

            _ => snapshot.StatusLine
        };
    }

    public static string DescribeTimeShift(TimeFlowShowcaseTimeFlowSnapshot timeFlow)
    {
        if (timeFlow.SimulationScalePermille <= 0)
        {
            return "Time shift: full stop across simulation, GAS, physics, and navigation.";
        }

        if (timeFlow.SimulationScalePermille < 1000)
        {
            string world = $"{timeFlow.SimulationScalePermille / 10f:0}%";
            if (timeFlow.NavigationScalePermille != timeFlow.SimulationScalePermille)
            {
                string nav = $"{timeFlow.NavigationScalePermille / 10f:0}%";
                return $"Time shift: world slowed to {world}; navigation is separately throttled to {nav}.";
            }

            return $"Time shift: world slowed to {world} while the battle keeps advancing.";
        }

        return "Time shift: baseline realtime, no global slowdown active.";
    }

    public static string DescribeCast(TimeFlowShowcaseSnapshot snapshot)
    {
        return string.Join("  vs  ", snapshot.Actors
            .GroupBy(actor => actor.Team)
            .OrderBy(group => group.Key)
            .Select(group => string.Join(", ", group.Select(actor => actor.Name))));
    }

    public static string DescribePrimaryPrompt(TimeFlowShowcaseSnapshot snapshot)
    {
        return (snapshot.ScenarioKind, snapshot.Phase) switch
        {
            (TimeFlowScenarioKind.AtbWait, "ATB.Realtime") => "WAIT FOR FULL ATB",
            (TimeFlowScenarioKind.AtbWait, "ATB.CommandPause") => "CHOOSE ATTACK IN PAUSE",
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.AutoBattle") => "BUILD CAPTAIN ULT",
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.UltFreeze") => "COMMIT THE ULT NOW",
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.BulletTime") => "READ THE SLOW FINISH",
            (TimeFlowScenarioKind.BreakFever, "Break.Build") => "FILL THE BREAK BAR",
            (TimeFlowScenarioKind.BreakFever, "Break.Fever") => "BURN INSIDE FEVER",
            (TimeFlowScenarioKind.SentinelCommandPause, "Sentinel.Realtime") => "WAIT FOR A READY UNIT",
            (TimeFlowScenarioKind.SentinelCommandPause, "Sentinel.CommandPause") => "QUEUE THE INTERCEPT",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Pause") => "PLAN BEFORE RESUME",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.EventPause") => "RESOLVE THE EVENT CARD",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Complete") => "SPEED LADDER COMPLETE",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.ActivePause") => "ASSIGN BOTH SQUADS",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.Realtime") => "WATCH LIVE LANDINGS",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.RevectorPause") => "RETARGET THE FLANK",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.Complete") => "LANES STABILIZED",
            _ => DescribeBeat(snapshot)
        };
    }

    public static string DescribeFocusChip(TimeFlowShowcaseSnapshot snapshot)
    {
        return (snapshot.ScenarioKind, snapshot.Phase) switch
        {
            (TimeFlowScenarioKind.AtbWait, "ATB.Realtime") => "FOCUS: ALLY CHARGE",
            (TimeFlowScenarioKind.AtbWait, "ATB.CommandPause") => $"FOCUS: {snapshot.SelectedActor.ToUpperInvariant()} READY",
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.AutoBattle") => "FOCUS: ENERGY 100%",
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.UltFreeze") => "FOCUS: CAST LOCK",
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.BulletTime") => "FOCUS: PAYOUT",
            (TimeFlowScenarioKind.BreakFever, "Break.Build") => $"FOCUS: BREAK {snapshot.BreakGauge:0}%",
            (TimeFlowScenarioKind.BreakFever, "Break.Fever") => "FOCUS: ALLY OVERCLOCK",
            (TimeFlowScenarioKind.SentinelCommandPause, "Sentinel.Realtime") => "FOCUS: READY ETA",
            (TimeFlowScenarioKind.SentinelCommandPause, "Sentinel.CommandPause") => "FOCUS: READY UNIT",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.EventPause") => "FOCUS: EVENT STOP",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Complete") => "FOCUS: LOOP CLEAR",
            (TimeFlowScenarioKind.Ck3Macro, _) => "FOCUS: SPEED STEP",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.ActivePause") => "FOCUS: OPENING PLAN",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.RevectorPause") => "FOCUS: FLANK FIX",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.Complete") => "FOCUS: BEACH HELD",
            (TimeFlowScenarioKind.BadNorthActivePause, _) => "FOCUS: LIVE LANES",
            _ => "FOCUS: TIMING"
        };
    }

    public static string DescribeBeatChip(TimeFlowShowcaseSnapshot snapshot)
    {
        return (snapshot.ScenarioKind, snapshot.Phase) switch
        {
            (TimeFlowScenarioKind.AtbWait, "ATB.CommandPause") => "BEAT: WAIT MODE",
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.UltFreeze") => "BEAT: HARD FREEZE",
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.BulletTime") => "BEAT: BULLET TIME",
            (TimeFlowScenarioKind.BreakFever, "Break.Fever") => "BEAT: FEVER",
            (TimeFlowScenarioKind.SentinelCommandPause, "Sentinel.CommandPause") => "BEAT: COMMAND PAUSE",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Pause") => "BEAT: PAUSED",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.EventPause") => "BEAT: EVENT PAUSE",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Complete") => "BEAT: COMPLETE",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.ActivePause") => "BEAT: ACTIVE PAUSE",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.RevectorPause") => "BEAT: RE-PAUSE",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.Complete") => "BEAT: COMPLETE",
            _ => $"BEAT: {DescribeTimeBadge(snapshot)}"
        };
    }

    public static string DescribeTimeChip(TimeFlowShowcaseSnapshot snapshot)
    {
        if (snapshot.TimeFlow.SimulationScalePermille <= 0)
        {
            return "SIM 0%";
        }

        if (snapshot.TimeFlow.SimulationScalePermille < 1000)
        {
            return $"SIM {snapshot.TimeFlow.SimulationScalePermille / 10f:0}%";
        }

        return "SIM 100%";
    }

    public static string DescribePrimaryAction(TimeFlowShowcaseSnapshot snapshot)
    {
        return (snapshot.ScenarioKind, snapshot.Phase) switch
        {
            (TimeFlowScenarioKind.AtbWait, "ATB.Realtime") => "WAIT FOR AN ALLY TO CAP ATB",
            (TimeFlowScenarioKind.AtbWait, "ATB.CommandPause") => "PRESS 1 STRIKE, THEN SPACE",
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.AutoBattle") => "R LOCKED UNTIL CAPTAIN HITS 100% ENERGY",
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.UltFreeze") => "R IS LIVE. FIRE THE ULT NOW",
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.BulletTime") => "ULT LANDED. READ THE SLOW-MO PAYOUT",
            (TimeFlowScenarioKind.BreakFever, "Break.Build") => "F LOCKED UNTIL BREAK BAR HITS 100%",
            (TimeFlowScenarioKind.BreakFever, "Break.Fever") => "F IS LIVE. BURST WHILE FEVER HOLDS",
            (TimeFlowScenarioKind.SentinelCommandPause, "Sentinel.Realtime") => "1 AND SPACE LOCKED UNTIL A PILOT IS READY",
            (TimeFlowScenarioKind.SentinelCommandPause, "Sentinel.CommandPause") => "1 INTERCEPT IS LIVE. SPACE RESUMES",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Pause") => "PRESS SPACE TO START THE CLOCK",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed1") => "PRESS 2-4 TO FAST-FORWARD",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed2") => "WATCH FOR THE EVENT PAUSE",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed3") => "HOLD THE SPEED LADDER",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.EventPause") => "ACK THE EVENT, THEN RESUME",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed4") => "RIDE THE QUIET FAST-FORWARD",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Complete") => "MACRO LOOP COMPLETE",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.ActivePause") => "ASSIGN SQUADS BEFORE RESUME",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.Realtime") => "WATCH FOR THE NEXT PAUSE WINDOW",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.RevectorPause") => "RETARGET ARCHERS TO THE FLANK",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.Complete") => "ACTIVE-PAUSE LOOP COMPLETE",
            _ => "READ THE CURRENT TIMING STATE"
        };
    }

    public static string DescribeTimeBadge(TimeFlowShowcaseSnapshot snapshot)
    {
        if (snapshot.TimeFlow.SimulationScalePermille <= 0)
        {
            return "PAUSED";
        }

        return (snapshot.ScenarioKind, snapshot.Phase) switch
        {
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.BulletTime") => "BULLET TIME",
            (TimeFlowScenarioKind.BreakFever, "Break.Fever") => "FEVER WINDOW",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed1") => "1X",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed2") => "2X",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed3") => "3X",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed4") => "4X",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Complete") => "COMPLETE",
            _ when snapshot.TimeFlow.SimulationScalePermille < 1000 => $"SLOW {snapshot.TimeFlow.SimulationScalePermille / 10f:0}%",
            _ => "LIVE"
        };
    }

    public static IReadOnlyList<TimeFlowActionPrompt> DescribeActionPrompts(TimeFlowShowcaseSnapshot snapshot)
    {
        return (snapshot.ScenarioKind, snapshot.Phase) switch
        {
            (TimeFlowScenarioKind.AtbWait, "ATB.Realtime") => new[]
            {
                new TimeFlowActionPrompt("WATCH ATB", true, true),
                new TimeFlowActionPrompt("WAIT READY", false, true),
                new TimeFlowActionPrompt("1 STRIKE", false, false)
            },
            (TimeFlowScenarioKind.AtbWait, "ATB.CommandPause") => new[]
            {
                new TimeFlowActionPrompt("1 STRIKE", true, true),
                new TimeFlowActionPrompt("SPACE CONFIRM", false, true),
                new TimeFlowActionPrompt("READY NOW", false, true)
            },
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.AutoBattle") => new[]
            {
                new TimeFlowActionPrompt("AUTO BUILD", true, true),
                new TimeFlowActionPrompt("R ULT LOCKED", false, false),
                new TimeFlowActionPrompt("WATCH 100%", false, true)
            },
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.UltFreeze") => new[]
            {
                new TimeFlowActionPrompt("R MANUAL ULT", true, true),
                new TimeFlowActionPrompt("ULT READY", false, true),
                new TimeFlowActionPrompt("CAST LOCK", false, true)
            },
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.BulletTime") => new[]
            {
                new TimeFlowActionPrompt("SLOW FINISH", true, true),
                new TimeFlowActionPrompt("WATCH IMPACT", false, true),
                new TimeFlowActionPrompt("R SPENT", false, false)
            },
            (TimeFlowScenarioKind.BreakFever, "Break.Build") => new[]
            {
                new TimeFlowActionPrompt("BUILD BREAK", true, true),
                new TimeFlowActionPrompt("F FEVER LOCKED", false, false),
                new TimeFlowActionPrompt("WATCH 100%", false, true)
            },
            (TimeFlowScenarioKind.BreakFever, "Break.Fever") => new[]
            {
                new TimeFlowActionPrompt("F FEVER", true, true),
                new TimeFlowActionPrompt("BURST NOW", false, true),
                new TimeFlowActionPrompt("WINDOW LIVE", false, true)
            },
            (TimeFlowScenarioKind.SentinelCommandPause, "Sentinel.Realtime") => new[]
            {
                new TimeFlowActionPrompt("WATCH ETA", true, true),
                new TimeFlowActionPrompt("1 INTERCEPT LOCKED", false, false),
                new TimeFlowActionPrompt("SPACE RESUME LOCKED", false, false)
            },
            (TimeFlowScenarioKind.SentinelCommandPause, "Sentinel.CommandPause") => new[]
            {
                new TimeFlowActionPrompt("1 INTERCEPT", true, true),
                new TimeFlowActionPrompt("SPACE RESUME", false, true),
                new TimeFlowActionPrompt("READY NOW", false, true)
            },
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Pause") => new[]
            {
                new TimeFlowActionPrompt("SPACE START", true, true),
                new TimeFlowActionPrompt("1-4 SPEED", false, true),
                new TimeFlowActionPrompt("PLAN", false, true)
            },
            (TimeFlowScenarioKind.Ck3Macro, "CK3.EventPause") => new[]
            {
                new TimeFlowActionPrompt("ACK EVENT", true, true),
                new TimeFlowActionPrompt("SPACE RESUME", false, true),
                new TimeFlowActionPrompt("PAUSED", false, true)
            },
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Complete") => new[]
            {
                new TimeFlowActionPrompt("LADDER CLEAR", true, true),
                new TimeFlowActionPrompt("PAUSE", false, true),
                new TimeFlowActionPrompt("END", false, true)
            },
            (TimeFlowScenarioKind.Ck3Macro, _) => new[]
            {
                new TimeFlowActionPrompt("1-4 SPEED", true, true),
                new TimeFlowActionPrompt("SPACE PAUSE", false, true),
                new TimeFlowActionPrompt("WATCH EVENT", false, true)
            },
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.ActivePause") => new[]
            {
                new TimeFlowActionPrompt("1 PIKES", true, true),
                new TimeFlowActionPrompt("2 ARCHERS", false, true),
                new TimeFlowActionPrompt("Q/E LANE", false, true)
            },
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.Realtime") => new[]
            {
                new TimeFlowActionPrompt("LIVE FIGHT", true, true),
                new TimeFlowActionPrompt("WATCH FLANK", false, true),
                new TimeFlowActionPrompt("SPACE PAUSE", false, true)
            },
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.RevectorPause") => new[]
            {
                new TimeFlowActionPrompt("SPACE PAUSE", true, true),
                new TimeFlowActionPrompt("2 ARCHERS", false, true),
                new TimeFlowActionPrompt("RETARGET", false, true)
            },
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.Complete") => new[]
            {
                new TimeFlowActionPrompt("LANES HELD", true, true),
                new TimeFlowActionPrompt("RESUME", false, true),
                new TimeFlowActionPrompt("CLEAR", false, true)
            },
            _ => Array.Empty<TimeFlowActionPrompt>()
        };
    }
}

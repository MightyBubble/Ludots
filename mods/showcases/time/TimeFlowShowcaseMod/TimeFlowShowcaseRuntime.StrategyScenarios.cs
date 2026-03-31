namespace TimeFlowShowcaseMod;

public sealed partial class TimeFlowShowcaseRuntime
{
    private void AdvanceCk3Macro(ShowcaseState state)
    {
        ShowcaseActor northArmy = FindActor(state, "North Army");
        ShowcaseActor southArmy = FindActor(state, "South Army");
        ShowcaseActor raid = FindActor(state, "Border Raid");

        if (state.Phase.StartsWith("CK3.Speed", StringComparison.Ordinal))
        {
            AdvanceMovement(northArmy, 18f);
            AdvanceMovement(southArmy, 16f);
            AdvanceMovement(raid, 14f);
        }

        if (string.Equals(state.Phase, "CK3.Speed1", StringComparison.Ordinal) && state.PhaseFixedTicks >= 18)
        {
            ReplaceProfile(state, "showcase.ck3_speed_2", "CK3.Speed2");
            state.StatusLine = "Macro speed advanced to 2x for long-range campaigning.";
            PushEvent(state, "[CK3] Speed ladder switched from 1x to 2x.");
        }
        else if (string.Equals(state.Phase, "CK3.Speed2", StringComparison.Ordinal) && state.PhaseFixedTicks >= 18)
        {
            ReplaceProfile(state, "showcase.ck3_speed_3", "CK3.Speed3");
            state.StatusLine = "Macro speed advanced to 3x while the frontier stayed quiet.";
            PushEvent(state, "[CK3] Speed ladder switched from 2x to 3x.");
        }
        else if (string.Equals(state.Phase, "CK3.Speed3", StringComparison.Ordinal) && state.PhaseFixedTicks >= 18)
        {
            ReplaceProfile(state, "showcase.ck3_pause", "CK3.EventPause");
            state.StatusLine = "A border event paused the realm clock and surfaced the decision card.";
            PushEvent(state, "[CK3] Event popup interrupted macro time and paused the realm.");
        }
        else if (string.Equals(state.Phase, "CK3.Speed4", StringComparison.Ordinal) && state.PhaseFixedTicks >= 18)
        {
            ReleaseProfile(state, "CK3.Complete");
            state.StatusLine = "Macro time completed its full ladder and returned to baseline.";
            PushEvent(state, "[CK3] Macro sweep completed after the event acknowledgement.");
        }
    }

    private void AdvanceCk3MacroUi(ShowcaseState state)
    {
        ShowcaseActor northArmy = FindActor(state, "North Army");
        ShowcaseActor southArmy = FindActor(state, "South Army");
        ShowcaseActor raid = FindActor(state, "Border Raid");

        if (string.Equals(state.Phase, "CK3.Pause", StringComparison.Ordinal) && state.PhaseUiFrames == 8)
        {
            northArmy.TargetX = 720f;
            northArmy.TargetY = 220f;
            northArmy.OrdersQueued = 2;
            southArmy.TargetX = 780f;
            southArmy.TargetY = 420f;
            southArmy.OrdersQueued = 2;
            raid.TargetX = 980f;
            raid.TargetY = 320f;
            raid.OrdersQueued = 1;
            state.StatusLine = "Armies were given march orders while paused; nothing moves until macro time resumes.";
            PushEvent(state, "[CK3] Paused order planning assigned march routes and a raid response.");
        }

        if (string.Equals(state.Phase, "CK3.Pause", StringComparison.Ordinal) && state.PhaseUiFrames >= 18)
        {
            ReplaceProfile(state, "showcase.ck3_speed_1", "CK3.Speed1");
            state.StatusLine = "Macro time resumed at 1x. The campaign started moving.";
            PushEvent(state, "[CK3] Resume from pause at 1x.");
            return;
        }

        if (string.Equals(state.Phase, "CK3.EventPause", StringComparison.Ordinal) && state.PhaseUiFrames == 8)
        {
            northArmy.OrdersQueued++;
            state.StatusLine = "The event card offers a response that queues a final march adjustment while the realm stays frozen.";
            PushEvent(state, "[CK3] Event acknowledgement queued one extra order while paused.");
        }

        if (string.Equals(state.Phase, "CK3.EventPause", StringComparison.Ordinal) && state.PhaseUiFrames >= 16)
        {
            ReplaceProfile(state, "showcase.ck3_speed_4", "CK3.Speed4");
            state.StatusLine = "Event acknowledged. Macro time jumped to 4x for the uneventful stretch.";
            PushEvent(state, "[CK3] Event resolved -> jump to 4x.");
        }
    }

    private void AdvanceBadNorthActivePause(ShowcaseState state)
    {
        ShowcaseActor pikes = FindActor(state, "Pikes");
        ShowcaseActor archers = FindActor(state, "Archers");
        ShowcaseActor raiders = FindActor(state, "Raiders");
        ShowcaseActor flankers = FindActor(state, "Flankers");

        if (string.Equals(state.Phase, "BadNorth.Realtime", StringComparison.Ordinal) ||
            string.Equals(state.Phase, "BadNorth.Finish", StringComparison.Ordinal))
        {
            AdvanceMovement(pikes, 24f);
            AdvanceMovement(archers, 26f);
            AdvanceMovement(raiders, 22f);
            AdvanceMovement(flankers, 24f);
        }

        if (string.Equals(state.Phase, "BadNorth.Realtime", StringComparison.Ordinal) && state.PhaseFixedTicks >= 20)
        {
            ReplaceProfile(state, "showcase.badnorth_pause", "BadNorth.RevectorPause");
            state.StatusLine = "A new raider lane forced an active-pause revector moment.";
            PushEvent(state, "[BADNORTH] Raiders shifted lanes -> active pause reopened for squad reassignment.");
        }
        else if (string.Equals(state.Phase, "BadNorth.Finish", StringComparison.Ordinal) && state.PhaseFixedTicks >= 20)
        {
            ReleaseProfile(state, "BadNorth.Complete");
            state.StatusLine = "Active-pause command cycle completed. Squads held the landing zones after the second resume.";
            PushEvent(state, "[BADNORTH] Final resume stabilized both squad lanes.");
        }
    }

    private void AdvanceBadNorthActivePauseUi(ShowcaseState state)
    {
        ShowcaseActor pikes = FindActor(state, "Pikes");
        ShowcaseActor archers = FindActor(state, "Archers");
        ShowcaseActor raiders = FindActor(state, "Raiders");
        ShowcaseActor flankers = FindActor(state, "Flankers");

        if (string.Equals(state.Phase, "BadNorth.ActivePause", StringComparison.Ordinal) && state.PhaseUiFrames == 8)
        {
            pikes.TargetX = 620f;
            pikes.TargetY = 200f;
            pikes.OrdersQueued = 1;
            archers.TargetX = 620f;
            archers.TargetY = 400f;
            archers.OrdersQueued = 1;
            raiders.TargetX = 980f;
            raiders.TargetY = 220f;
            flankers.TargetX = 980f;
            flankers.TargetY = 420f;
            state.StatusLine = "Both squads got landing-zone assignments while the island stayed paused.";
            PushEvent(state, "[BADNORTH] Initial active pause assigned pikes north and archers south.");
        }

        if (string.Equals(state.Phase, "BadNorth.ActivePause", StringComparison.Ordinal) && state.PhaseUiFrames >= 16)
        {
            ReplaceProfile(state, "showcase.badnorth_resume", "BadNorth.Realtime");
            state.StatusLine = "Realtime resumed and both squads started marching to their zones.";
            PushEvent(state, "[BADNORTH] Resume after initial active pause.");
            return;
        }

        if (string.Equals(state.Phase, "BadNorth.RevectorPause", StringComparison.Ordinal) && state.PhaseUiFrames == 8)
        {
            archers.TargetX = 760f;
            archers.TargetY = 320f;
            archers.OrdersQueued++;
            flankers.TargetX = 1060f;
            flankers.TargetY = 360f;
            state.StatusLine = "Archers were pulled inward to cover a flanking boat while time stayed frozen.";
            PushEvent(state, "[BADNORTH] Mid-fight active pause retargeted archers to the flanking lane.");
        }

        if (string.Equals(state.Phase, "BadNorth.RevectorPause", StringComparison.Ordinal) && state.PhaseUiFrames >= 16)
        {
            ReplaceProfile(state, "showcase.badnorth_resume", "BadNorth.Finish");
            state.StatusLine = "Second resume committed the last retarget and let the island defense settle.";
            PushEvent(state, "[BADNORTH] Resume after lane correction.");
        }
    }

    private static ShowcaseState CreateState(TimeFlowScenarioKind kind)
    {
        return kind switch
        {
            TimeFlowScenarioKind.AtbWait => new ShowcaseState
            {
                Kind = kind,
                Title = "ATB Wait Mode",
                InspirationLine = "Classic wait-mode ATB: gauges fill in realtime, then the battle freezes while the command window is open.",
                Phase = "ATB.Realtime",
                StatusLine = "ATB gauges are filling in realtime.",
                Actors =
                {
                    new ShowcaseActor("Knight", 1, 240f, 210f, 100f, speed: 6.0f),
                    new ShowcaseActor("Mage", 1, 240f, 320f, 85f, speed: 5.2f),
                    new ShowcaseActor("Goblin", 2, 980f, 220f, 92f, speed: 4.6f),
                    new ShowcaseActor("Ogre", 2, 980f, 340f, 128f, speed: 3.8f)
                }
            },
            TimeFlowScenarioKind.DotaManualUlt => new ShowcaseState
            {
                Kind = kind,
                Title = "Auto-Battle Manual Ult",
                InspirationLine = "Dota Legends style: auto-battle meters fill in realtime, then a manual ultimate freeze frame suspends the battlefield before release.",
                Phase = "Dota.AutoBattle",
                StatusLine = "Energy meters are filling in auto-battle.",
                Actors =
                {
                    new ShowcaseActor("Captain", 1, 240f, 280f, 110f, speed: 0f),
                    new ShowcaseActor("Brute", 2, 980f, 280f, 140f, speed: 0f)
                }
            },
            TimeFlowScenarioKind.BreakFever => new ShowcaseState
            {
                Kind = kind,
                Title = "Break Fever Burst",
                InspirationLine = "Break / fever burst window: the world slows down while allied actors get local overclocks inside the break state.",
                Phase = "Break.Build",
                StatusLine = "Break meter is charging.",
                Actors =
                {
                    new ShowcaseActor("Striker", 1, 220f, 220f, 120f, speed: 0f),
                    new ShowcaseActor("Support", 1, 220f, 340f, 95f, speed: 0f),
                    new ShowcaseActor("Guardian", 2, 980f, 280f, 180f, speed: 0f)
                }
            },
            TimeFlowScenarioKind.SentinelCommandPause => new ShowcaseState
            {
                Kind = kind,
                Title = "13 Sentinels Command Pause",
                InspirationLine = "WT clocks drain in realtime until a unit is ready, then the battlefield pauses for command resolution.",
                Phase = "Sentinel.Realtime",
                StatusLine = "WT clocks are draining toward the next command pause.",
                Actors =
                {
                    new ShowcaseActor("Aegis", 1, 220f, 220f, 125f, speed: 0f) { WaitTicks = 42 },
                    new ShowcaseActor("Gunner", 1, 220f, 360f, 110f, speed: 0f) { WaitTicks = 74 },
                    new ShowcaseActor("Drone Swarm", 2, 980f, 280f, 160f, speed: 0f) { WaitTicks = 999 }
                }
            },
            TimeFlowScenarioKind.Ck3Macro => new ShowcaseState
            {
                Kind = kind,
                Title = "CK3 Macro Speed Ladder",
                InspirationLine = "Realm-level pause and speed ladder: pause for planning, run 1x/2x/3x, pause on events, then fast-forward through quiet stretches.",
                Phase = "CK3.Pause",
                StatusLine = "The realm clock is paused while the opening campaign orders are being queued.",
                Actors =
                {
                    new ShowcaseActor("North Army", 1, 220f, 220f, 100f, speed: 0f),
                    new ShowcaseActor("South Army", 1, 220f, 420f, 100f, speed: 0f),
                    new ShowcaseActor("Border Raid", 2, 1120f, 320f, 88f, speed: 0f)
                }
            },
            TimeFlowScenarioKind.BadNorthActivePause => new ShowcaseState
            {
                Kind = kind,
                Title = "Bad North Active Pause",
                InspirationLine = "Island-defense active pause: freeze realtime, assign squad lanes, resume, then re-pause when a flank breaks the plan.",
                Phase = "BadNorth.ActivePause",
                StatusLine = "The island is in active pause while the first squad orders are being assigned.",
                Actors =
                {
                    new ShowcaseActor("Pikes", 1, 220f, 200f, 100f, speed: 0f),
                    new ShowcaseActor("Archers", 1, 220f, 400f, 100f, speed: 0f),
                    new ShowcaseActor("Raiders", 2, 1100f, 220f, 92f, speed: 0f),
                    new ShowcaseActor("Flankers", 2, 1100f, 420f, 92f, speed: 0f)
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }
}

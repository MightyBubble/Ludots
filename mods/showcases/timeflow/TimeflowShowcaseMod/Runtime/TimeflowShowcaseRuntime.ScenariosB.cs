using System;
using System.Linq;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Input.Runtime;

namespace TimeflowShowcaseMod.Runtime;

public sealed partial class TimeflowShowcaseRuntime
{
    private void HandleSentinelsInput(TimeflowShowcaseScenarioState state, IInputActionReader input)
    {
        if (Pressed(input, TimeflowShowcaseIds.TogglePauseActionId))
        {
            state.TerminalPaused = !state.TerminalPaused;
            if (state.TerminalPaused)
            {
                AcquirePause("sentinels.pause", TimeFlowDomainIds.Simulation, "SentinelTerminal");
                state.LocalPauseActive = true;
                state.Phase = "Sentinel.Terminal";
                PushEvent(state, "[13S] Terminal pause opened.");
            }
            else
            {
                ReleaseToken("sentinels.pause");
                state.LocalPauseActive = false;
                state.Phase = "Sentinel.Live";
                PushEvent(state, "[13S] Terminal pause closed.");
            }
        }

        if (!state.TerminalPaused)
        {
            return;
        }

        if (Pressed(input, TimeflowShowcaseIds.PrimaryAActionId))
        {
            QueueCommand(state, "Aegis -> Demolisher Blade");
        }
        else if (Pressed(input, TimeflowShowcaseIds.PrimaryBActionId))
        {
            QueueCommand(state, "Gunner -> Sentry Guns");
        }
        else if (Pressed(input, TimeflowShowcaseIds.PrimaryCActionId))
        {
            QueueCommand(state, "Missiles -> Barrage");
        }
        else if (Pressed(input, TimeflowShowcaseIds.PrimaryDActionId))
        {
            QueueCommand(state, "All -> Intercept");
        }

        if (Pressed(input, TimeflowShowcaseIds.ConfirmActionId) && state.CommandQueue.Count > 0)
        {
            ExecuteSentinelQueue(state);
        }
    }

    private void AdvanceSentinels(TimeflowShowcaseScenarioState state)
    {
        foreach (TimeflowActorState actor in state.Actors.Where(actor => actor.Team == 1))
        {
            actor.WaitTicks = Math.Max(0, actor.WaitTicks - Math.Max(1, (int)MathF.Round(GetScale(actor.DomainName))));
            actor.Status = actor.WaitTicks == 0 ? "Ready" : string.Empty;
        }

        if (state.TerminalPaused)
        {
            return;
        }

        TimeflowActorState? ready = state.Actors
            .Where(actor => actor.Team == 1 && actor.WaitTicks == 0)
            .OrderBy(actor => actor.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (ready != null)
        {
            AcquirePause("sentinels.pause", TimeFlowDomainIds.Simulation, "SentinelReadyPause");
            state.TerminalPaused = true;
            state.LocalPauseActive = true;
            state.Phase = "Sentinel.Terminal";
            state.StatusLine = $"{ready.Name} is ready. Terminal pause opened for queued commands.";
            PushEvent(state, $"[13S] {ready.Name} WT reached zero -> terminal pause.");
        }
    }

    private void QueueCommand(TimeflowShowcaseScenarioState state, string command)
    {
        if (state.CommandQueue.Count >= 6)
        {
            return;
        }

        state.CommandQueue.Add(command);
        state.StatusLine = $"{state.CommandQueue.Count} command(s) queued in the terminal.";
        PushEvent(state, $"[13S] Queued {command}.");
    }

    private void ExecuteSentinelQueue(TimeflowShowcaseScenarioState state)
    {
        int damage = state.CommandQueue.Count * 12;
        FindActor(state, "Kaiju Wave").Health = MathF.Max(0f, FindActor(state, "Kaiju Wave").Health - damage);
        foreach (TimeflowActorState actor in state.Actors.Where(actor => actor.Team == 1))
        {
            actor.WaitTicks = 120 + (state.CommandQueue.Count * 10);
        }

        state.CommandQueue.Clear();
        state.TerminalPaused = false;
        state.LocalPauseActive = false;
        ReleaseToken("sentinels.pause");
        state.Phase = "Sentinel.Live";
        state.PhaseFixedTicks = 0;
        state.PhasePresentationFrames = 0;
        state.StatusLine = $"Queued commands resolved for {damage:0} total damage, then realtime resumed.";
        PushEvent(state, $"[13S] Executed queued terminal plan for {damage:0} damage.");
    }

    private void HandleCk3Input(TimeflowShowcaseScenarioState state, IInputActionReader input)
    {
        if (Pressed(input, TimeflowShowcaseIds.TogglePauseActionId))
        {
            SetCk3Speed(state, 0);
        }
        else if (Pressed(input, TimeflowShowcaseIds.Speed1ActionId))
        {
            SetCk3Speed(state, 1);
        }
        else if (Pressed(input, TimeflowShowcaseIds.Speed2ActionId))
        {
            SetCk3Speed(state, 2);
        }
        else if (Pressed(input, TimeflowShowcaseIds.Speed3ActionId))
        {
            SetCk3Speed(state, 3);
        }
        else if (Pressed(input, TimeflowShowcaseIds.Speed4ActionId))
        {
            SetCk3Speed(state, 4);
        }

        if (Pressed(input, TimeflowShowcaseIds.ConfirmActionId) && state.PopupPauseActive)
        {
            state.PopupPauseActive = false;
            state.EventQueued = false;
            state.PopupCount++;
            ReleaseToken("ck3.popup.pause");
            SetCk3Speed(state, Math.Max(1, state.MacroSpeedLevel));
            FindActor(state, "Event Queue").Status = "Popup acknowledged";
            state.StatusLine = $"Event popup #{state.PopupCount} acknowledged. Realm clock resumed.";
            PushEvent(state, $"[CK3] Acknowledged event popup #{state.PopupCount}.");
        }
    }

    private void AdvanceCk3(TimeflowShowcaseScenarioState state)
    {
        TimeflowActorState clock = FindActor(state, "Realm Clock");
        if (state.MacroSpeedLevel > 0)
        {
            int month = 1 + ((state.FixedTick / 24) % 12);
            int year = 1066 + (state.FixedTick / 288);
            clock.Status = $"{GetMonthName(month)} {year}";
            FindActor(state, "Marshal").OrdersQueued = state.MacroSpeedLevel;
            FindActor(state, "Steward").OrdersQueued = Math.Max(1, state.MacroSpeedLevel - 1);
        }

        if (!state.EventQueued && state.MacroSpeedLevel >= 3 && state.PhaseFixedTicks >= 16)
        {
            AcquirePause("ck3.popup.pause", TimeFlowDomainIds.Simulation, "Ck3EventPause");
            state.PopupPauseActive = true;
            state.EventQueued = true;
            state.Phase = "CK3.Popup";
            FindActor(state, "Event Queue").Status = "Vassal petition";
            state.StatusLine = "A realm event popup interrupted the macro clock and forced a pause.";
            PushEvent(state, "[CK3] Event popup forced a pause.");
        }
    }

    private void SetCk3Speed(TimeflowShowcaseScenarioState state, int speedLevel)
    {
        state.MacroSpeedLevel = speedLevel;
        ReleaseToken("ck3.speed");
        ReleaseToken("ck3.pause");

        if (speedLevel <= 0)
        {
            AcquirePause("ck3.pause", TimeFlowDomainIds.Simulation, "Ck3Pause");
            state.Phase = "CK3.Paused";
            state.StatusLine = "Realm simulation paused.";
            PushEvent(state, "[CK3] Macro clock paused.");
            return;
        }

        int permille = speedLevel switch
        {
            1 => 1000,
            2 => 2000,
            3 => 3000,
            4 => 4000,
            _ => 1000
        };
        AcquireScale("ck3.speed", TimeFlowDomainIds.Simulation, permille, "Ck3Speed");
        state.Phase = $"CK3.Speed{speedLevel}";
        state.PhaseFixedTicks = 0;
        state.PhasePresentationFrames = 0;
        state.StatusLine = $"Macro clock running at speed {speedLevel}.";
        PushEvent(state, $"[CK3] Macro clock set to {speedLevel}x.");
    }

    private void HandleBadNorthInput(TimeflowShowcaseScenarioState state, IInputActionReader input)
    {
        if (Pressed(input, TimeflowShowcaseIds.TogglePauseActionId))
        {
            state.LocalPauseActive = !state.LocalPauseActive;
            if (state.LocalPauseActive)
            {
                AcquirePause("badnorth.pause", TimeFlowDomainIds.Simulation, "BadNorthActivePause");
                state.Phase = "BadNorth.ActivePause";
                state.StatusLine = "Active pause engaged. Reassign squads before resuming.";
                PushEvent(state, "[BN] Active pause engaged.");
            }
            else
            {
                ReleaseToken("badnorth.pause");
                state.Phase = "BadNorth.Live";
                state.StatusLine = "Active pause released. Squads are executing queued routes.";
                PushEvent(state, "[BN] Active pause released.");
            }
        }

        if (Pressed(input, TimeflowShowcaseIds.Speed1ActionId))
        {
            state.SelectedIndex = 0;
        }
        else if (Pressed(input, TimeflowShowcaseIds.Speed2ActionId))
        {
            state.SelectedIndex = 1;
        }
        else if (Pressed(input, TimeflowShowcaseIds.Speed3ActionId))
        {
            state.SelectedIndex = 2;
        }

        if (!state.LocalPauseActive)
        {
            return;
        }

        if (Pressed(input, TimeflowShowcaseIds.OptionAActionId))
        {
            AssignBadNorthLane(state, 0, 980f, 180f, "North beach");
        }
        else if (Pressed(input, TimeflowShowcaseIds.OptionBActionId))
        {
            AssignBadNorthLane(state, 1, 980f, 300f, "Mid ridge");
        }
        else if (Pressed(input, TimeflowShowcaseIds.OptionCActionId))
        {
            AssignBadNorthLane(state, 2, 980f, 420f, "South beach");
        }
    }

    private void AssignBadNorthLane(TimeflowShowcaseScenarioState state, int orderBias, float targetX, float targetY, string laneName)
    {
        TimeflowActorState squad = state.Actors[state.SelectedIndex];
        squad.TargetX = targetX;
        squad.TargetY = targetY + (orderBias * 16f);
        squad.OrdersQueued++;
        squad.Status = laneName;
        state.StatusLine = $"{squad.Name} assigned to {laneName} while paused.";
        PushEvent(state, $"[BN] {squad.Name} assigned to {laneName}.");
    }

    private void AdvanceBadNorth(TimeflowShowcaseScenarioState state)
    {
        if (!state.LocalPauseActive)
        {
            for (int i = 0; i < 3; i++)
            {
                AdvanceMovement(state.Actors[i], 28f * GetScale(state.Actors[i].DomainName));
            }

            TimeflowActorState raiders = FindActor(state, "Raiders");
            raiders.X -= 18f * GetScale(raiders.DomainName);
            raiders.Status = "Advancing";
            if (state.FixedTick % 24 == 0)
            {
                TimeflowActorState closest = state.Actors.Take(3).OrderBy(actor => MathF.Abs(actor.X - raiders.X)).First();
                closest.Health = MathF.Max(0f, closest.Health - 6f);
                PushEvent(state, $"[BN] Raiders pressured {closest.Name} for 6.");
            }
        }
    }

    private void AdvanceMovement(TimeflowActorState actor, float stepCm)
    {
        float dx = actor.TargetX - actor.X;
        float dy = actor.TargetY - actor.Y;
        float distance = MathF.Sqrt((dx * dx) + (dy * dy));
        if (distance <= 0.01f)
        {
            return;
        }

        if (distance <= stepCm)
        {
            actor.X = actor.TargetX;
            actor.Y = actor.TargetY;
            return;
        }

        float ratio = stepCm / distance;
        actor.X += dx * ratio;
        actor.Y += dy * ratio;
    }

    private float GetScale(string domainName)
    {
        return _timeFlow == null
            ? 1f
            : _timeFlow.GetEffectiveScalePermille(domainName) / 1000f;
    }

    private static TimeflowActorState FindActor(TimeflowShowcaseScenarioState state, string name)
    {
        return state.Actors.First(actor => string.Equals(actor.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static TimeflowActorState FindWeakestEnemy(TimeflowShowcaseScenarioState state, int team)
    {
        int enemyTeam = team == 1 ? 2 : 1;
        return state.Actors
            .Where(actor => actor.Team == enemyTeam)
            .OrderBy(actor => actor.Health)
            .First();
    }

    private static void PushEvent(TimeflowShowcaseScenarioState state, string line)
    {
        state.RecentEvents.Add(line);
        while (state.RecentEvents.Count > 6)
        {
            state.RecentEvents.RemoveAt(0);
        }
    }

    private static string GetMonthName(int month)
    {
        return month switch
        {
            1 => "Jan",
            2 => "Feb",
            3 => "Mar",
            4 => "Apr",
            5 => "May",
            6 => "Jun",
            7 => "Jul",
            8 => "Aug",
            9 => "Sep",
            10 => "Oct",
            11 => "Nov",
            12 => "Dec",
            _ => "Jan"
        };
    }
}

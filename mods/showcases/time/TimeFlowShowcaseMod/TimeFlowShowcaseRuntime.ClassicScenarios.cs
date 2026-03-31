namespace TimeFlowShowcaseMod;

public sealed partial class TimeFlowShowcaseRuntime
{
    private void AdvanceAtbWait(ShowcaseState state)
    {
        if (!string.Equals(state.Phase, "ATB.Realtime", StringComparison.Ordinal))
        {
            return;
        }

        foreach (ShowcaseActor actor in state.Actors)
        {
            actor.Charge = MathF.Min(100f, actor.Charge + actor.Speed * actor.LocalTimeScale);
        }

        ShowcaseActor? ready = state.Actors
            .Where(actor => actor.Team == 1 && actor.Charge >= 100f)
            .OrderByDescending(actor => actor.Charge)
            .FirstOrDefault();
        if (ready == null)
        {
            state.StatusLine = "ATB gauges are filling in realtime. Wait-mode pause will trigger on the next ready ally.";
            return;
        }

        ActivateProfile(state, "showcase.atb_wait_pause", "ATB.CommandPause");
        state.SelectedActor = ready.Name;
        state.StatusLine = $"{ready.Name} reached full charge. Wait-mode pause opened the command window.";
        PushEvent(state, $"[ATB] {ready.Name} reached 100 charge -> pause and command selection.");
    }

    private void AdvanceAtbWaitUi(ShowcaseState state)
    {
        if (!string.Equals(state.Phase, "ATB.CommandPause", StringComparison.Ordinal) || state.PhaseUiFrames < 18)
        {
            return;
        }

        ShowcaseActor actor = FindActor(state, state.SelectedActor ?? "Knight");
        ShowcaseActor target = FindWeakestEnemy(state, actor.Team);
        target.Health = MathF.Max(0f, target.Health - 18f);
        actor.Charge = 0f;
        actor.WaitTicks = 70;
        ReleaseProfile(state, "ATB.Realtime");
        state.StatusLine = $"{actor.Name} confirmed Strike during pause and resumed the battle.";
        PushEvent(state, $"[ATB] {actor.Name} acted on {target.Name} for 18 damage -> resume realtime.");
    }

    private void AdvanceDotaManualUlt(ShowcaseState state)
    {
        ShowcaseActor captain = FindActor(state, "Captain");
        ShowcaseActor brute = FindActor(state, "Brute");

        if (string.Equals(state.Phase, "Dota.AutoBattle", StringComparison.Ordinal))
        {
            captain.Energy = MathF.Min(100f, captain.Energy + 6f);
            brute.Energy = MathF.Min(100f, brute.Energy + 4f);

            if (state.PhaseFixedTicks % 18 == 0)
            {
                brute.Health = MathF.Max(0f, brute.Health - 4f);
            }

            if (captain.Energy >= 100f)
            {
                ActivateProfile(state, "showcase.dota_ult_freeze", "Dota.UltFreeze");
                state.StatusLine = "Ultimate meter is full. Freeze frame is holding while the player commits the manual ult.";
                PushEvent(state, "[DOTA] Captain reached 100 energy -> freeze frame before manual ultimate.");
            }

            return;
        }

        if (string.Equals(state.Phase, "Dota.BulletTime", StringComparison.Ordinal) && state.PhaseFixedTicks >= 28)
        {
            ReleaseProfile(state, "Dota.AutoBattle");
            state.StatusLine = "Bullet time expired. Auto battle returned to baseline cadence.";
            PushEvent(state, "[DOTA] Bullet time expired -> back to baseline auto battle.");
        }
    }

    private void AdvanceDotaManualUltUi(ShowcaseState state)
    {
        if (!string.Equals(state.Phase, "Dota.UltFreeze", StringComparison.Ordinal) || state.PhaseUiFrames < 12)
        {
            return;
        }

        ShowcaseActor captain = FindActor(state, "Captain");
        ShowcaseActor brute = FindActor(state, "Brute");
        captain.Energy = 0f;
        brute.Health = MathF.Max(0f, brute.Health - 36f);
        ReplaceProfile(state, "showcase.bullet_time_30", "Dota.BulletTime");
        state.StatusLine = "The ult resolved and the encounter dropped into short-form bullet time.";
        PushEvent(state, "[DOTA] Ultimate fired for 36 damage -> brief bullet time window engaged.");
    }

    private void AdvanceBreakFever(ShowcaseState state)
    {
        ShowcaseActor striker = FindActor(state, "Striker");
        ShowcaseActor support = FindActor(state, "Support");
        ShowcaseActor guardian = FindActor(state, "Guardian");

        if (string.Equals(state.Phase, "Break.Build", StringComparison.Ordinal))
        {
            state.BreakGauge = MathF.Min(100f, state.BreakGauge + 3.2f);
            striker.Charge += 5f;
            support.Charge += 4f;
            guardian.Charge += 2f;

            if (state.BreakGauge >= 100f)
            {
                striker.LocalTimeScale = 2.4f;
                support.LocalTimeScale = 2.1f;
                guardian.LocalTimeScale = 0.55f;
                ActivateProfile(state, "showcase.break_fever", "Break.Fever");
                state.StatusLine = "Break meter is full. Burst fever slowed the world and overclocked allied actions.";
                PushEvent(state, "[BREAK] Meter filled -> burst fever active with ally overclock and slower navigation.");
            }

            return;
        }

        if (!string.Equals(state.Phase, "Break.Fever", StringComparison.Ordinal))
        {
            return;
        }

        striker.Charge += 7f * striker.LocalTimeScale;
        support.Charge += 5f * support.LocalTimeScale;
        guardian.Charge += 2f * guardian.LocalTimeScale;
        if (state.PhaseFixedTicks % 10 == 0)
        {
            guardian.Health = MathF.Max(0f, guardian.Health - 7f);
        }

        if (state.PhaseFixedTicks >= 42)
        {
            striker.LocalTimeScale = 1f;
            support.LocalTimeScale = 1f;
            guardian.LocalTimeScale = 1f;
            ReleaseProfile(state, "Break.Build");
            state.BreakGauge = 0f;
            state.StatusLine = "Burst fever ended. The world returned to baseline rates and local overclocks were cleared.";
            PushEvent(state, "[BREAK] Fever window ended -> restore baseline world and local rates.");
        }
    }

    private void AdvanceBreakFeverUi(ShowcaseState state)
    {
        if (string.Equals(state.Phase, "Break.Build", StringComparison.Ordinal))
        {
            state.StatusLine = $"Break meter building: {state.BreakGauge:0}/100.";
        }
    }

    private void AdvanceSentinelPause(ShowcaseState state)
    {
        foreach (ShowcaseActor actor in state.Actors)
        {
            actor.WaitTicks = Math.Max(0, actor.WaitTicks - Math.Max(1, (int)MathF.Round(actor.LocalTimeScale)));
        }

        if (!string.Equals(state.Phase, "Sentinel.Realtime", StringComparison.Ordinal))
        {
            return;
        }

        ShowcaseActor? ready = state.Actors
            .Where(actor => actor.Team == 1 && actor.WaitTicks == 0)
            .OrderBy(actor => actor.Name)
            .FirstOrDefault();
        if (ready == null)
        {
            state.StatusLine = "WT clocks are draining in realtime until the next command pause.";
            return;
        }

        state.SelectedActor = ready.Name;
        ActivateProfile(state, "showcase.sentinel_command_pause", "Sentinel.CommandPause");
        state.StatusLine = $"{ready.Name} is ready. The battle paused for command resolution.";
        PushEvent(state, $"[13S] {ready.Name} WT reached zero -> pause and queue command.");
    }

    private void AdvanceSentinelPauseUi(ShowcaseState state)
    {
        if (!string.Equals(state.Phase, "Sentinel.CommandPause", StringComparison.Ordinal) || state.PhaseUiFrames < 14)
        {
            return;
        }

        ShowcaseActor actor = FindActor(state, state.SelectedActor ?? "Aegis");
        ShowcaseActor target = FindWeakestEnemy(state, actor.Team);
        target.Health = MathF.Max(0f, target.Health - 22f);
        actor.WaitTicks = 150;
        ReleaseProfile(state, "Sentinel.Realtime");
        state.StatusLine = $"{actor.Name} committed an area strike, reset WT, and resumed realtime combat.";
        PushEvent(state, $"[13S] {actor.Name} fired intercept skill on {target.Name} for 22 damage -> resume.");
    }
}

using System;
using System.Linq;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Input.Runtime;

namespace TimeflowShowcaseMod.Runtime;

public sealed partial class TimeflowShowcaseRuntime
{
    private void HandleScenarioInput(TimeflowShowcaseScenarioState state, IInputActionReader input)
    {
        switch (state.ScenarioId)
        {
            case TimeflowScenarioId.Atb:
                HandleAtbInput(state, input);
                break;
            case TimeflowScenarioId.AutoBattle:
                HandleAutoBattleInput(state, input);
                break;
            case TimeflowScenarioId.BreakFever:
                HandleBreakFeverInput(state, input);
                break;
            case TimeflowScenarioId.Sentinels:
                HandleSentinelsInput(state, input);
                break;
            case TimeflowScenarioId.CrusaderKings:
                HandleCk3Input(state, input);
                break;
            case TimeflowScenarioId.BadNorth:
                HandleBadNorthInput(state, input);
                break;
        }
    }

    private void AdvanceScenario(TimeflowShowcaseScenarioState state)
    {
        switch (state.ScenarioId)
        {
            case TimeflowScenarioId.Atb:
                AdvanceAtb(state);
                break;
            case TimeflowScenarioId.AutoBattle:
                AdvanceAutoBattle(state);
                break;
            case TimeflowScenarioId.BreakFever:
                AdvanceBreakFever(state);
                break;
            case TimeflowScenarioId.Sentinels:
                AdvanceSentinels(state);
                break;
            case TimeflowScenarioId.CrusaderKings:
                AdvanceCk3(state);
                break;
            case TimeflowScenarioId.BadNorth:
                AdvanceBadNorth(state);
                break;
        }
    }

    private TimeflowShowcaseScenarioState CreateState(TimeflowScenarioId scenarioId)
    {
        var state = scenarioId switch
        {
            TimeflowScenarioId.Atb => new TimeflowShowcaseScenarioState
            {
                ScenarioId = scenarioId,
                Title = "ATB Wait Mode",
                InspirationLine = "Classic ATB wait mode: gauges fill in realtime, then combat pauses until the selected command resolves.",
                Phase = "ATB.Realtime",
                StatusLine = "Party and enemy gauges are filling in realtime.",
                ControlsLine = "Q/W/E spend ready ally turns. Z party haste. X enemy slow."
            },
            TimeflowScenarioId.AutoBattle => new TimeflowShowcaseScenarioState
            {
                ScenarioId = scenarioId,
                Title = "Manual Ult Freeze + Bullet Time",
                InspirationLine = "Auto-battle timing: auto attacks keep rolling, ult bars cap out, manual release creates a freeze-frame then a short slow-motion payoff.",
                Phase = "AutoBattle.Live",
                StatusLine = "Heroes are trading auto attacks and building energy.",
                ControlsLine = "Q/W/E trigger hero ultimates when a bar is full."
            },
            TimeflowScenarioId.BreakFever => new TimeflowShowcaseScenarioState
            {
                ScenarioId = scenarioId,
                Title = "Break Fever Burst Window",
                InspirationLine = "Break meter converts into a burst window: the baseline world slows, allied action cadence spikes, and systems can be retimed independently.",
                Phase = "Break.Build",
                StatusLine = "Break gauge is building toward a fever burst.",
                ControlsLine = "Q spends striker burst. W spends support burst. Fever auto-triggers at 100."
            },
            TimeflowScenarioId.Sentinels => new TimeflowShowcaseScenarioState
            {
                ScenarioId = scenarioId,
                Title = "13 Sentinels Command Pause",
                InspirationLine = "Realtime pressure drains WT. When the terminal opens, the encounter pauses while orders are queued, then resumes to execute them.",
                Phase = "Sentinel.Live",
                StatusLine = "WT clocks are draining toward the next terminal pause.",
                ControlsLine = "P toggle terminal pause. Q/W/E/R queue commands. Enter confirm and resume."
            },
            TimeflowScenarioId.CrusaderKings => new TimeflowShowcaseScenarioState
            {
                ScenarioId = scenarioId,
                Title = "Crusader Kings III Macro Clock",
                InspirationLine = "Grand strategy clock: the world can be fully paused, resumed at several macro speeds, and interrupted by event popups that force a pause.",
                Phase = "CK3.Paused",
                StatusLine = "Realm simulation is paused at macro speed 0.",
                ControlsLine = "1/2/3/4 set macro speed. P pause. Enter acknowledge event popup.",
                MacroSpeedLevel = 0
            },
            TimeflowScenarioId.BadNorth => new TimeflowShowcaseScenarioState
            {
                ScenarioId = scenarioId,
                Title = "Bad North Active Pause",
                InspirationLine = "Active pause tactics: freeze the field, select a squad, assign a lane, then resume while raiders keep their own cadence.",
                Phase = "BadNorth.Live",
                StatusLine = "Squads are skirmishing in realtime. Active pause is available at any moment.",
                ControlsLine = "P toggle active pause. 1/2/3 select squad. Z/X/C assign lane while paused.",
                SelectedIndex = 0
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenarioId), scenarioId, null)
        };

        switch (scenarioId)
        {
            case TimeflowScenarioId.Atb:
                state.Actors.Add(new TimeflowActorState("Knight", 1, "showcase.atb.party", 240f, 200f, 120f, 5.4f));
                state.Actors.Add(new TimeflowActorState("Mage", 1, "showcase.atb.party", 240f, 320f, 92f, 6.2f));
                state.Actors.Add(new TimeflowActorState("Goblin", 2, "showcase.atb.enemy", 1040f, 220f, 88f, 4.4f));
                state.Actors.Add(new TimeflowActorState("Ogre", 2, "showcase.atb.enemy", 1040f, 340f, 138f, 3.4f));
                break;
            case TimeflowScenarioId.AutoBattle:
                state.Actors.Add(new TimeflowActorState("Captain", 1, "showcase.autobattle.heroes", 220f, 180f, 128f, 0f));
                state.Actors.Add(new TimeflowActorState("Oracle", 1, "showcase.autobattle.heroes", 220f, 300f, 96f, 0f));
                state.Actors.Add(new TimeflowActorState("Ranger", 1, "showcase.autobattle.heroes", 220f, 420f, 92f, 0f));
                state.Actors.Add(new TimeflowActorState("Brute", 2, "showcase.autobattle.enemies", 1040f, 300f, 260f, 0f));
                break;
            case TimeflowScenarioId.BreakFever:
                state.Actors.Add(new TimeflowActorState("Striker", 1, "showcase.breakfever.party", 220f, 220f, 124f, 0f));
                state.Actors.Add(new TimeflowActorState("Support", 1, "showcase.breakfever.party", 220f, 340f, 104f, 0f));
                state.Actors.Add(new TimeflowActorState("Guardian", 2, "showcase.breakfever.boss", 1060f, 280f, 240f, 0f));
                break;
            case TimeflowScenarioId.Sentinels:
                state.Actors.Add(new TimeflowActorState("Aegis", 1, "showcase.sentinels.squad", 220f, 180f, 130f, 0f) { WaitTicks = 36 });
                state.Actors.Add(new TimeflowActorState("Gunner", 1, "showcase.sentinels.squad", 220f, 300f, 114f, 0f) { WaitTicks = 72 });
                state.Actors.Add(new TimeflowActorState("Missiles", 1, "showcase.sentinels.squad", 220f, 420f, 96f, 0f) { WaitTicks = 98 });
                state.Actors.Add(new TimeflowActorState("Kaiju Wave", 2, "showcase.sentinels.wave", 1100f, 300f, 220f, 0f) { WaitTicks = 999 });
                break;
            case TimeflowScenarioId.CrusaderKings:
                state.Actors.Add(new TimeflowActorState("Realm Clock", 0, "showcase.ck3", 0f, 0f, 0f, 0f) { Status = "Jan 1066" });
                state.Actors.Add(new TimeflowActorState("Marshal", 1, "showcase.ck3", 220f, 180f, 100f, 0f));
                state.Actors.Add(new TimeflowActorState("Steward", 1, "showcase.ck3", 220f, 320f, 100f, 0f));
                state.Actors.Add(new TimeflowActorState("Event Queue", 0, "showcase.ck3", 220f, 460f, 0f, 0f) { Status = "No popup" });
                break;
            case TimeflowScenarioId.BadNorth:
                state.Actors.Add(new TimeflowActorState("Pike", 1, "showcase.badnorth.squads", 220f, 180f, 100f, 0f));
                state.Actors.Add(new TimeflowActorState("Archers", 1, "showcase.badnorth.squads", 220f, 300f, 88f, 0f));
                state.Actors.Add(new TimeflowActorState("Infantry", 1, "showcase.badnorth.squads", 220f, 420f, 116f, 0f));
                state.Actors.Add(new TimeflowActorState("Raiders", 2, "showcase.badnorth.raiders", 1100f, 300f, 240f, 0f));
                break;
        }

        ApplyBuiltInScales(state);
        return state;
    }

    private void ApplyBuiltInScales(TimeflowShowcaseScenarioState state)
    {
        ReleaseToken(BuiltinGasKey);
        ReleaseToken(BuiltinPhysicsKey);
        ReleaseToken(BuiltinNavigationKey);

        switch (state.ScenarioId)
        {
            case TimeflowScenarioId.Atb:
                AcquireScale(BuiltinGasKey, TimeFlowDomainIds.Gas, 1000, "AtbGas");
                AcquireScale(BuiltinPhysicsKey, TimeFlowDomainIds.Physics2D, 1000, "AtbPhysics");
                AcquireScale(BuiltinNavigationKey, TimeFlowDomainIds.Navigation2D, 1000, "AtbNav");
                break;
            case TimeflowScenarioId.AutoBattle:
                AcquireScale(BuiltinGasKey, TimeFlowDomainIds.Gas, 1200, "AutoBattleGas");
                AcquireScale(BuiltinPhysicsKey, TimeFlowDomainIds.Physics2D, 900, "AutoBattlePhysics");
                AcquireScale(BuiltinNavigationKey, TimeFlowDomainIds.Navigation2D, 900, "AutoBattleNav");
                break;
            case TimeflowScenarioId.BreakFever:
                AcquireScale(BuiltinGasKey, TimeFlowDomainIds.Gas, 1100, "BreakGas");
                AcquireScale(BuiltinPhysicsKey, TimeFlowDomainIds.Physics2D, 900, "BreakPhysics");
                AcquireScale(BuiltinNavigationKey, TimeFlowDomainIds.Navigation2D, 900, "BreakNav");
                break;
            case TimeflowScenarioId.Sentinels:
                AcquireScale(BuiltinGasKey, TimeFlowDomainIds.Gas, 1000, "SentinelGas");
                AcquireScale(BuiltinPhysicsKey, TimeFlowDomainIds.Physics2D, 800, "SentinelPhysics");
                AcquireScale(BuiltinNavigationKey, TimeFlowDomainIds.Navigation2D, 800, "SentinelNav");
                break;
            case TimeflowScenarioId.CrusaderKings:
                AcquireScale(BuiltinGasKey, TimeFlowDomainIds.Gas, 1000, "Ck3Gas");
                AcquireScale(BuiltinPhysicsKey, TimeFlowDomainIds.Physics2D, 500, "Ck3Physics");
                AcquireScale(BuiltinNavigationKey, TimeFlowDomainIds.Navigation2D, 500, "Ck3Nav");
                break;
            case TimeflowScenarioId.BadNorth:
                AcquireScale(BuiltinGasKey, TimeFlowDomainIds.Gas, 1000, "BadNorthGas");
                AcquireScale(BuiltinPhysicsKey, TimeFlowDomainIds.Physics2D, 700, "BadNorthPhysics");
                AcquireScale(BuiltinNavigationKey, TimeFlowDomainIds.Navigation2D, 700, "BadNorthNav");
                break;
        }
    }

    private void HandleAtbInput(TimeflowShowcaseScenarioState state, IInputActionReader input)
    {
        if (Pressed(input, TimeflowShowcaseIds.OptionAActionId))
        {
            state.PartyHasteActive = !state.PartyHasteActive;
            if (state.PartyHasteActive)
            {
                AcquireScale("atb.party.haste", "showcase.atb.party", 1450, "PartyHaste");
                PushEvent(state, "[ATB] Party haste enabled.");
            }
            else
            {
                ReleaseToken("atb.party.haste");
                PushEvent(state, "[ATB] Party haste disabled.");
            }
        }

        if (Pressed(input, TimeflowShowcaseIds.OptionBActionId))
        {
            state.EnemySlowActive = !state.EnemySlowActive;
            if (state.EnemySlowActive)
            {
                AcquireScale("atb.enemy.slow", "showcase.atb.enemy", 650, "EnemySlow");
                PushEvent(state, "[ATB] Enemy slow enabled.");
            }
            else
            {
                ReleaseToken("atb.enemy.slow");
                PushEvent(state, "[ATB] Enemy slow disabled.");
            }
        }

        if (!state.LocalPauseActive)
        {
            return;
        }

        if (Pressed(input, TimeflowShowcaseIds.PrimaryAActionId))
        {
            ResolveAtbAction(state, 0, 18f, "Strike");
        }
        else if (Pressed(input, TimeflowShowcaseIds.PrimaryBActionId))
        {
            ResolveAtbAction(state, 1, 24f, "Fire");
        }
        else if (Pressed(input, TimeflowShowcaseIds.PrimaryCActionId))
        {
            ResolveAtbAction(state, 0, 0f, "Defend");
            FindActor(state, "Knight").Health = MathF.Min(140f, FindActor(state, "Knight").Health + 6f);
        }
    }

    private void ResolveAtbAction(TimeflowShowcaseScenarioState state, int actorIndex, float damage, string actionName)
    {
        TimeflowActorState actor = state.Actors[actorIndex];
        TimeflowActorState target = FindWeakestEnemy(state, actor.Team);
        target.Health = MathF.Max(0f, target.Health - damage);
        actor.Charge = 0f;
        actor.WaitTicks = 84;
        actor.Status = actionName;
        ReleaseToken("atb.pause");
        state.LocalPauseActive = false;
        state.Phase = "ATB.Realtime";
        state.PhaseFixedTicks = 0;
        state.PhasePresentationFrames = 0;
        state.StatusLine = $"{actor.Name} committed {actionName} and combat resumed.";
        PushEvent(state, $"[ATB] {actor.Name} used {actionName} on {target.Name} for {damage:0}.");
    }

    private void AdvanceAtb(TimeflowShowcaseScenarioState state)
    {
        foreach (TimeflowActorState actor in state.Actors)
        {
            float scale = GetScale(actor.DomainName);
            actor.Charge = MathF.Min(100f, actor.Charge + (actor.Speed * scale));
            actor.WaitTicks = Math.Max(0, actor.WaitTicks - 1);
            actor.Status = actor.Charge >= 100f ? "Ready" : string.Empty;
        }

        if (!state.LocalPauseActive)
        {
            TimeflowActorState? ready = state.Actors
                .Where(actor => actor.Team == 1 && actor.Charge >= 100f)
                .OrderByDescending(actor => actor.Charge)
                .FirstOrDefault();
            if (ready != null)
            {
                AcquirePause("atb.pause", TimeFlowDomainIds.Simulation, "AtbCommandPause");
                state.LocalPauseActive = true;
                state.Phase = "ATB.Wait";
                state.PhaseFixedTicks = 0;
                state.PhasePresentationFrames = 0;
                state.StatusLine = $"{ready.Name} reached full charge. Wait-mode pause opened command selection.";
                PushEvent(state, $"[ATB] {ready.Name} ready -> simulation paused for command input.");
            }
        }
    }

    private void HandleAutoBattleInput(TimeflowShowcaseScenarioState state, IInputActionReader input)
    {
        if (Pressed(input, TimeflowShowcaseIds.PrimaryAActionId))
        {
            TryResolveUlt(state, "Captain", 42f);
        }
        else if (Pressed(input, TimeflowShowcaseIds.PrimaryBActionId))
        {
            TryResolveUlt(state, "Oracle", 34f);
        }
        else if (Pressed(input, TimeflowShowcaseIds.PrimaryCActionId))
        {
            TryResolveUlt(state, "Ranger", 28f);
        }
    }

    private void TryResolveUlt(TimeflowShowcaseScenarioState state, string actorName, float damage)
    {
        TimeflowActorState actor = FindActor(state, actorName);
        if (actor.Energy < 100f)
        {
            state.StatusLine = $"{actorName} is not full yet. Ult stays unavailable.";
            return;
        }

        actor.Energy = 0f;
        FindActor(state, "Brute").Health = MathF.Max(0f, FindActor(state, "Brute").Health - damage);
        AcquireScale("autobattle.bullet", TimeFlowDomainIds.Simulation, 350, "AutoBattleBullet");
        AcquireScale("autobattle.heroes.burst", "showcase.autobattle.heroes", 1350, "HeroesBurst");
        state.LocalPauseActive = false;
        state.Phase = "AutoBattle.Bullet";
        state.PhaseFixedTicks = 0;
        state.PhasePresentationFrames = 0;
        state.StatusLine = $"{actorName} ult resolved into a short bullet-time payoff.";
        PushEvent(state, $"[AUTO] {actorName} ultimate landed for {damage:0}. Bullet time engaged.");
    }

    private void AdvanceAutoBattle(TimeflowShowcaseScenarioState state)
    {
        foreach (TimeflowActorState actor in state.Actors.Where(actor => actor.Team == 1))
        {
            actor.Energy = MathF.Min(100f, actor.Energy + (4.8f * GetScale(actor.DomainName)));
        }

        if (state.FixedTick % 18 == 0)
        {
            FindActor(state, "Brute").Health = MathF.Max(0f, FindActor(state, "Brute").Health - 4f);
            PushEvent(state, "[AUTO] Basic attack volley chipped Brute for 4.");
        }

        if (state.Phase == "AutoBattle.Bullet" && state.PhaseFixedTicks >= 24)
        {
            ReleaseToken("autobattle.bullet");
            ReleaseToken("autobattle.heroes.burst");
            state.Phase = "AutoBattle.Live";
            state.PhaseFixedTicks = 0;
            state.PhasePresentationFrames = 0;
            state.StatusLine = "Bullet time expired. Auto battle returned to baseline cadence.";
            PushEvent(state, "[AUTO] Bullet time expired.");
        }
    }

    private void HandleBreakFeverInput(TimeflowShowcaseScenarioState state, IInputActionReader input)
    {
        if (!state.FeverActive)
        {
            return;
        }

        if (Pressed(input, TimeflowShowcaseIds.PrimaryAActionId))
        {
            FindActor(state, "Guardian").Health = MathF.Max(0f, FindActor(state, "Guardian").Health - 20f);
            PushEvent(state, "[BREAK] Striker spent fever action for 20 damage.");
        }
        else if (Pressed(input, TimeflowShowcaseIds.PrimaryBActionId))
        {
            FindActor(state, "Support").Health = MathF.Min(140f, FindActor(state, "Support").Health + 10f);
            PushEvent(state, "[BREAK] Support converted fever time into sustain.");
        }
    }

    private void AdvanceBreakFever(TimeflowShowcaseScenarioState state)
    {
        if (!state.FeverActive)
        {
            state.BreakGauge = MathF.Min(100f, state.BreakGauge + 3.4f);
            if (state.BreakGauge >= 100f)
            {
                state.FeverActive = true;
                state.Phase = "Break.Fever";
                state.PhaseFixedTicks = 0;
                state.PhasePresentationFrames = 0;
                AcquireScale("break.world.slow", TimeFlowDomainIds.Simulation, 550, "BreakWorldSlow");
                AcquireScale("break.party.haste", "showcase.breakfever.party", 2200, "BreakPartyBurst");
                AcquireScale("break.boss.slow", "showcase.breakfever.boss", 650, "BreakBossSlow");
                AcquireScale("break.gas.boost", TimeFlowDomainIds.Gas, 1800, "BreakGasBoost");
                AcquireScale("break.nav.slow", TimeFlowDomainIds.Navigation2D, 450, "BreakNavSlow");
                state.StatusLine = "Break gauge filled. Fever burst is live with split gas/navigation pacing.";
                PushEvent(state, "[BREAK] Gauge filled -> fever burst active.");
            }
            return;
        }

        foreach (TimeflowActorState actor in state.Actors.Where(actor => actor.Team == 1))
        {
            actor.Charge += 6f * GetScale(actor.DomainName);
        }

        if (state.PhaseFixedTicks % 10 == 0)
        {
            FindActor(state, "Guardian").Health = MathF.Max(0f, FindActor(state, "Guardian").Health - 8f);
        }

        if (state.PhaseFixedTicks >= 40)
        {
            state.FeverActive = false;
            state.BreakGauge = 0f;
            ReleaseToken("break.world.slow");
            ReleaseToken("break.party.haste");
            ReleaseToken("break.boss.slow");
            ReleaseToken("break.gas.boost");
            ReleaseToken("break.nav.slow");
            state.Phase = "Break.Build";
            state.PhaseFixedTicks = 0;
            state.PhasePresentationFrames = 0;
            state.StatusLine = "Fever expired. The world returned to baseline rates.";
            PushEvent(state, "[BREAK] Fever burst ended.");
        }
    }
}

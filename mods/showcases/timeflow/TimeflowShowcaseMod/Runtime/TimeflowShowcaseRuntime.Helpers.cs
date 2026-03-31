using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay.Camera;

namespace TimeflowShowcaseMod.Runtime;

public sealed partial class TimeflowShowcaseRuntime
{
    private void EnsureDomains()
    {
        EnsureDomain("showcase.atb", "showcase.atb.party", "showcase.atb.enemy");
        EnsureDomain("showcase.autobattle", "showcase.autobattle.heroes", "showcase.autobattle.enemies");
        EnsureDomain("showcase.breakfever", "showcase.breakfever.party", "showcase.breakfever.boss");
        EnsureDomain("showcase.sentinels", "showcase.sentinels.squad", "showcase.sentinels.wave");
        EnsureDomain("showcase.ck3");
        EnsureDomain("showcase.badnorth", "showcase.badnorth.squads", "showcase.badnorth.raiders");
    }

    private void EnsureDomain(string root, string? childA = null, string? childB = null)
    {
        _timeFlow!.EnsureDomain(root);
        if (!string.IsNullOrWhiteSpace(childA)) _timeFlow.EnsureDomain(childA!, root);
        if (!string.IsNullOrWhiteSpace(childB)) _timeFlow.EnsureDomain(childB!, root);
    }

    private void FocusCamera(TimeflowScenarioId scenarioId)
    {
        if (_engine == null)
        {
            return;
        }

        Vector2 target = FocusTargets[(int)scenarioId];
        _engine.SetService(
            CoreServiceKeys.CameraPoseRequest,
            new CameraPoseRequest
            {
                TargetCm = target,
                Pitch = 50f,
                DistanceCm = 4800f,
                FovYDeg = 56f
            });
    }

    private void AdvanceAutomation()
    {
        if (_automation == null || _state == null)
        {
            return;
        }

        if (_state.ScenarioId != _automation.TargetScenario)
        {
            FocusScenario(_automation.TargetScenario);
            return;
        }

        switch (_automation.TargetScenario)
        {
            case TimeflowScenarioId.Sentinels:
                if (_state.Phase == "Sentinels.CommandPause" && _automation.Stage == 0)
                {
                    QueueSentinelCommand(0);
                    QueueSentinelCommand(1);
                    QueueSentinelCommand(2);
                    _automation.Stage = 1;
                }
                break;
            case TimeflowScenarioId.CrusaderKings:
                if (_automation.Stage == 0)
                {
                    SetMacroSpeed(4);
                    _automation.Stage = 1;
                }
                break;
            case TimeflowScenarioId.BadNorth:
                if (_automation.Stage == 0)
                {
                    ToggleBadNorthPause();
                    SelectBadNorthSquad(0);
                    AssignBadNorthLane(0);
                    SelectBadNorthSquad(1);
                    AssignBadNorthLane(1);
                    SelectBadNorthSquad(2);
                    AssignBadNorthLane(2);
                    _automation.Stage = 1;
                }
                break;
        }
    }

    private static TimeflowAutomation? TryCreateAutomation()
    {
        string? value = Environment.GetEnvironmentVariable("LUDOTS_TIMEFLOW_AUTODEMO_SCENARIO");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "atb" => new TimeflowAutomation(TimeflowScenarioId.Atb),
            "auto" or "autobattle" => new TimeflowAutomation(TimeflowScenarioId.AutoBattle),
            "break" => new TimeflowAutomation(TimeflowScenarioId.BreakFever),
            "sentinels" or "13s" => new TimeflowAutomation(TimeflowScenarioId.Sentinels),
            "ck3" => new TimeflowAutomation(TimeflowScenarioId.CrusaderKings),
            "badnorth" => new TimeflowAutomation(TimeflowScenarioId.BadNorth),
            _ => null
        };
    }

    private void ReleaseAllTokens()
    {
        if (_timeFlow == null)
        {
            return;
        }

        if (_globalPauseToken.IsValid)
        {
            _timeFlow.ReleaseToken(_globalPauseToken);
            _globalPauseToken = TimeFlowToken.Invalid;
        }

        if (_globalBulletToken.IsValid)
        {
            _timeFlow.ReleaseToken(_globalBulletToken);
            _globalBulletToken = TimeFlowToken.Invalid;
        }

        ReleaseScenarioTokens();
    }

    private void ReleaseScenarioTokens()
    {
        if (_timeFlow == null || _state == null)
        {
            return;
        }

        ClearToken(ref _state.RootPauseToken);
        ClearToken(ref _state.RootScaleToken);
        ClearToken(ref _state.DomainAToken);
        ClearToken(ref _state.DomainBToken);
        ClearToken(ref _state.DomainCToken);
    }

    private void ClearToken(ref TimeFlowToken token)
    {
        if (_timeFlow != null && token.IsValid)
        {
            _timeFlow.ReleaseToken(token);
            token = TimeFlowToken.Invalid;
        }
    }

    private void SetScaleToken(ref TimeFlowToken token, string domain, int scalePermille, string owner, string reason)
    {
        ClearToken(ref token);
        token = _timeFlow!.AcquireScaleToken(domain, scalePermille, owner, reason);
    }

    private void SetPauseToken(ref TimeFlowToken token, string domain, string owner, string reason)
    {
        ClearToken(ref token);
        token = _timeFlow!.AcquirePauseToken(domain, owner, reason);
    }

    private void ToggleScale(ref TimeFlowToken token, string domain, int scalePermille, string reason, string eventText)
    {
        if (_state == null)
        {
            return;
        }

        if (token.IsValid)
        {
            ClearToken(ref token);
            PushEvent(_state, $"{eventText} Released.");
        }
        else
        {
            SetScaleToken(ref token, domain, scalePermille, ScenarioOwner, reason);
            PushEvent(_state, $"{eventText} Engaged.");
        }
    }

    private void SetPhase(TimeflowScenarioState state, string phase, string status)
    {
        state.Phase = phase;
        state.StatusLine = status;
        state.PhaseFixedTicks = 0;
        state.PhaseUiFrames = 0;
    }

    private void TickCommonWaits(TimeflowScenarioState state)
    {
        foreach (TimeflowActorState actor in state.Actors)
        {
            if (actor.WaitTicks > 0)
            {
                actor.WaitTicks--;
            }
        }
    }

    private float ReadScale(string? domain)
    {
        if (_timeFlow == null || string.IsNullOrWhiteSpace(domain))
        {
            return 1f;
        }

        return _timeFlow.GetEffectiveScalePermille(domain) / 1000f;
    }

    private string BuildDomainLine(string domain)
    {
        TimeFlowDomainSnapshot snapshot = _domainSnapshots.First(x => string.Equals(x.Name, domain, StringComparison.OrdinalIgnoreCase));
        return $"{snapshot.Name}: base={snapshot.BaseScalePermille / 1000f:0.00}x effective={snapshot.EffectiveScalePermille / 1000f:0.00}x paused={snapshot.Paused} modifiers={snapshot.ModifierCount}";
    }

    private static void AdvanceTowards(TimeflowActorState actor, float stepCm)
    {
        float deltaX = actor.TargetX - actor.X;
        float deltaY = actor.TargetY - actor.Y;
        float distance = MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (distance <= 0.001f)
        {
            actor.OrdersQueued = 0;
            return;
        }

        if (distance <= stepCm)
        {
            actor.X = actor.TargetX;
            actor.Y = actor.TargetY;
            actor.OrdersQueued = 0;
            return;
        }

        float ratio = stepCm / distance;
        actor.X += deltaX * ratio;
        actor.Y += deltaY * ratio;
    }

    private static TimeflowActorState FindActor(TimeflowScenarioState state, string name)
    {
        return state.Actors.First(actor => string.Equals(actor.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static TimeflowActorState FindWeakestEnemy(TimeflowScenarioState state, int actorTeam)
    {
        int enemyTeam = actorTeam == 1 ? 2 : 1;
        return state.Actors.Where(actor => actor.Team == enemyTeam).OrderBy(actor => actor.Health).First();
    }

    private void PushGlobalEvent(string line)
    {
        if (_state != null)
        {
            PushEvent(_state, line);
        }

        _uiRevision++;
    }

    private static void PushEvent(TimeflowScenarioState state, string line)
    {
        state.RecentEvents.Add(line);
        while (state.RecentEvents.Count > 10)
        {
            state.RecentEvents.RemoveAt(0);
        }
    }

    private void EnsureAttached()
    {
        if (_engine == null || _timeFlow == null)
        {
            throw new InvalidOperationException("Timeflow showcase runtime is not attached to an active engine.");
        }
    }
}

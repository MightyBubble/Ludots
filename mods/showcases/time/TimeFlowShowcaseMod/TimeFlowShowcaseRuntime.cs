using System.Collections.ObjectModel;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace TimeFlowShowcaseMod;

public sealed partial class TimeFlowShowcaseRuntime
{
    private const string TimeFlowOwner = "TimeFlowShowcaseMod";

    private GameEngine? _engine;
    private TimeFlowProfileBridge? _timeFlow;
    private ShowcaseState? _state;
    private int _uiRevision;

    internal void Attach(GameEngine engine, TimeFlowProfileBridge timeFlow)
    {
        _engine = engine;
        _timeFlow = timeFlow;
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null || _timeFlow == null || _engine != engine)
        {
            return Task.CompletedTask;
        }

        string? mapId = engine.CurrentMapSession?.MapId.Value;
        if (!TimeFlowShowcaseIds.IsShowcaseMap(mapId))
        {
            ClearScenario();
            return Task.CompletedTask;
        }

        _timeFlow.ClearOwner(TimeFlowOwner);
        _state = CreateState(TimeFlowShowcaseIds.ResolveScenario(mapId!));
        _state.MapId = mapId!;
        _uiRevision++;
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        string mapId = context.Get(CoreServiceKeys.MapId).Value;
        if (TimeFlowShowcaseIds.IsShowcaseMap(mapId))
        {
            ClearScenario();
        }

        return Task.CompletedTask;
    }

    public void AdvanceFixedStep(GameEngine engine)
    {
        if (_state == null || _engine != engine || _timeFlow == null)
        {
            return;
        }

        _state.FixedTick++;
        _state.PhaseFixedTicks++;

        switch (_state.Kind)
        {
            case TimeFlowScenarioKind.AtbWait:
                AdvanceAtbWait(_state);
                break;
            case TimeFlowScenarioKind.DotaManualUlt:
                AdvanceDotaManualUlt(_state);
                break;
            case TimeFlowScenarioKind.BreakFever:
                AdvanceBreakFever(_state);
                break;
            case TimeFlowScenarioKind.SentinelCommandPause:
                AdvanceSentinelPause(_state);
                break;
            case TimeFlowScenarioKind.Ck3Macro:
                AdvanceCk3Macro(_state);
                break;
            case TimeFlowScenarioKind.BadNorthActivePause:
                AdvanceBadNorthActivePause(_state);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        _uiRevision++;
    }

    public void AdvancePresentationFrame(GameEngine engine, float dt)
    {
        if (_state == null || _engine != engine || _timeFlow == null)
        {
            return;
        }

        _state.PresentationFrame++;
        _state.PhaseUiFrames++;

        switch (_state.Kind)
        {
            case TimeFlowScenarioKind.AtbWait:
                AdvanceAtbWaitUi(_state);
                break;
            case TimeFlowScenarioKind.DotaManualUlt:
                AdvanceDotaManualUltUi(_state);
                break;
            case TimeFlowScenarioKind.BreakFever:
                AdvanceBreakFeverUi(_state);
                break;
            case TimeFlowScenarioKind.SentinelCommandPause:
                AdvanceSentinelPauseUi(_state);
                break;
            case TimeFlowScenarioKind.Ck3Macro:
                AdvanceCk3MacroUi(_state);
                break;
            case TimeFlowScenarioKind.BadNorthActivePause:
                AdvanceBadNorthActivePauseUi(_state);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        _uiRevision++;
    }

    public TimeFlowShowcaseSnapshot? GetSnapshot()
    {
        if (_state == null || _timeFlow == null)
        {
            return null;
        }

        List<TimeFlowShowcaseActorSnapshot> actors = _state.Actors
            .Select(actor => new TimeFlowShowcaseActorSnapshot
            {
                Name = actor.Name,
                Team = actor.Team,
                X = actor.X,
                Y = actor.Y,
                Health = actor.Health,
                Charge = actor.Charge,
                Energy = actor.Energy,
                WaitTicks = actor.WaitTicks,
                OrdersQueued = actor.OrdersQueued
            })
            .ToList();

        return new TimeFlowShowcaseSnapshot
        {
            MapId = _state.MapId,
            ScenarioKind = _state.Kind,
            ScenarioTitle = _state.Title,
            InspirationLine = _state.InspirationLine,
            Phase = _state.Phase,
            FixedTick = _state.FixedTick,
            PresentationFrame = _state.PresentationFrame,
            StatusLine = _state.StatusLine,
            SelectedActor = _state.SelectedActor ?? string.Empty,
            BreakGauge = _state.BreakGauge,
            UiRevision = _uiRevision,
            TimeFlow = _timeFlow.Snapshot(),
            Actors = new ReadOnlyCollection<TimeFlowShowcaseActorSnapshot>(actors),
            RecentEvents = new ReadOnlyCollection<string>(_state.RecentEvents.ToList())
        };
    }

    private void ActivateProfile(ShowcaseState state, string profileId, string nextPhase)
    {
        state.ActiveProfileHandle = _timeFlow!.ActivateProfile(profileId, TimeFlowOwner, priority: 100);
        state.ActiveProfileId = profileId;
        state.Phase = nextPhase;
        state.PhaseFixedTicks = 0;
        state.PhaseUiFrames = 0;
    }

    private void ReplaceProfile(ShowcaseState state, string profileId, string nextPhase)
    {
        _timeFlow!.ClearOwner(TimeFlowOwner);
        state.ActiveProfileHandle = _timeFlow.ActivateProfile(profileId, TimeFlowOwner, priority: 100);
        state.ActiveProfileId = profileId;
        state.Phase = nextPhase;
        state.PhaseFixedTicks = 0;
        state.PhaseUiFrames = 0;
    }

    private void ReleaseProfile(ShowcaseState state, string nextPhase)
    {
        _timeFlow!.ClearOwner(TimeFlowOwner);
        state.ActiveProfileHandle = null;
        state.ActiveProfileId = null;
        state.Phase = nextPhase;
        state.PhaseFixedTicks = 0;
        state.PhaseUiFrames = 0;
    }

    private void ClearScenario()
    {
        _timeFlow?.ClearOwner(TimeFlowOwner);
        _state = null;
        _uiRevision++;
    }

    private static ShowcaseActor FindActor(ShowcaseState state, string name)
    {
        return state.Actors.First(actor => string.Equals(actor.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static ShowcaseActor FindWeakestEnemy(ShowcaseState state, int team)
    {
        int enemyTeam = team == 1 ? 2 : 1;
        return state.Actors
            .Where(actor => actor.Team == enemyTeam)
            .OrderBy(actor => actor.Health)
            .First();
    }

    private static void PushEvent(ShowcaseState state, string line)
    {
        state.RecentEvents.Add(line);
        while (state.RecentEvents.Count > 10)
        {
            state.RecentEvents.RemoveAt(0);
        }
    }

    private void AdvanceMovement(ShowcaseActor actor, float stepCm)
    {
        if (Math.Abs(actor.TargetX - actor.X) < 0.01f && Math.Abs(actor.TargetY - actor.Y) < 0.01f)
        {
            return;
        }

        float deltaX = actor.TargetX - actor.X;
        float deltaY = actor.TargetY - actor.Y;
        float distance = MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (distance <= stepCm || distance <= 0.001f)
        {
            actor.X = actor.TargetX;
            actor.Y = actor.TargetY;
            return;
        }

        float ratio = stepCm / distance;
        actor.X += deltaX * ratio;
        actor.Y += deltaY * ratio;
    }

    private sealed class ShowcaseState
    {
        public required TimeFlowScenarioKind Kind { get; init; }
        public required string Title { get; init; }
        public required string InspirationLine { get; init; }
        public required string Phase { get; set; }
        public required string StatusLine { get; set; }
        public string MapId { get; set; } = string.Empty;
        public List<ShowcaseActor> Actors { get; } = new();
        public List<string> RecentEvents { get; } = new();
        public int FixedTick { get; set; }
        public int PresentationFrame { get; set; }
        public int PhaseFixedTicks { get; set; }
        public int PhaseUiFrames { get; set; }
        public string? ActiveProfileId { get; set; }
        public int? ActiveProfileHandle { get; set; }
        public string? SelectedActor { get; set; }
        public float BreakGauge { get; set; }
    }

    private sealed class ShowcaseActor
    {
        public ShowcaseActor(string name, int team, float x, float y, float health, float speed)
        {
            Name = name;
            Team = team;
            X = x;
            Y = y;
            Health = health;
            Speed = speed;
            TargetX = x;
            TargetY = y;
        }

        public string Name { get; }
        public int Team { get; }
        public float X { get; set; }
        public float Y { get; set; }
        public float TargetX { get; set; }
        public float TargetY { get; set; }
        public float Health { get; set; }
        public float Charge { get; set; }
        public float Energy { get; set; }
        public int WaitTicks { get; set; }
        public float Speed { get; }
        public float LocalTimeScale { get; set; } = 1f;
        public int OrdersQueued { get; set; }
    }
}

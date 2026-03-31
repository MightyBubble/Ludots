using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace TimeflowShowcaseMod.Runtime;

public sealed partial class TimeflowShowcaseRuntime
{
    private const string GlobalPauseKey = "global.pause";
    private const string GlobalBulletKey = "global.bullet";
    private const string BuiltinGasKey = "builtin.gas";
    private const string BuiltinPhysicsKey = "builtin.physics";
    private const string BuiltinNavigationKey = "builtin.navigation";
    private const string ShowcaseOwner = "TimeflowShowcaseMod";

    private static readonly Vector4 PanelFill = new(0.06f, 0.08f, 0.11f, 0.93f);
    private static readonly Vector4 PanelBorder = new(0.36f, 0.62f, 0.78f, 0.96f);
    private static readonly Vector4 TitleColor = new(0.97f, 0.87f, 0.46f, 1f);
    private static readonly Vector4 TextColor = new(0.90f, 0.95f, 0.98f, 1f);
    private static readonly Vector4 HintColor = new(0.67f, 0.81f, 0.92f, 1f);
    private static readonly Vector4 AccentColor = new(0.78f, 0.98f, 0.70f, 1f);
    private static readonly Vector4 MutedColor = new(0.66f, 0.70f, 0.74f, 1f);

    private readonly Dictionary<string, TimeFlowToken> _tokens = new(StringComparer.Ordinal);
    private readonly List<TimeFlowDomainSnapshot> _domainSnapshots = new();
    private readonly HashSet<string> _trackedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        TimeFlowDomainIds.Simulation,
        TimeFlowDomainIds.Gas,
        TimeFlowDomainIds.Physics2D,
        TimeFlowDomainIds.Navigation2D,
        TimeFlowDomainIds.Tasks
    };

    private GameEngine? _engine;
    private TimeFlowService? _timeFlow;
    private TimeflowShowcaseScenarioState? _state;
    private bool _inputContextActive;
    private int _uiRevision;

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        _engine = engine;
        _timeFlow = engine.GetService(CoreServiceKeys.TimeFlow)
            ?? throw new InvalidOperationException("TimeflowShowcaseMod requires CoreServiceKeys.TimeFlow.");
        EnsureDomains(_timeFlow);

        PlayerInputHandler? input = context.Get(CoreServiceKeys.InputHandler);
        if (TimeflowShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            ActivateInputContext(input);
            ResetScenario(engine, TimeflowScenarioId.Atb);
        }
        else
        {
            DeactivateInputContext(input);
            ClearAllTokens();
            _state = null;
            _uiRevision++;
        }

        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        string mapId = context.Get(CoreServiceKeys.MapId).Value;
        if (!TimeflowShowcaseIds.IsShowcaseMap(mapId))
        {
            return Task.CompletedTask;
        }

        DeactivateInputContext(context.Get(CoreServiceKeys.InputHandler));
        ClearAllTokens();
        _state = null;
        _uiRevision++;
        return Task.CompletedTask;
    }

    public bool IsActive(GameEngine engine)
    {
        return _engine == engine &&
               _timeFlow != null &&
               _state != null &&
               TimeflowShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value);
    }

    public void Update(GameEngine engine, float dt)
    {
        if (!IsActive(engine) || _state == null)
        {
            return;
        }

        _state.FixedTick++;
        _state.PhaseFixedTicks++;
        AdvanceScenario(_state);
        _uiRevision++;
    }

    public void HandleLiveInput(GameEngine engine)
    {
        if (!IsActive(engine) || _state == null)
        {
            return;
        }

        if (engine.GetService(CoreServiceKeys.InputHandler) is not PlayerInputHandler input)
        {
            return;
        }

        if (Pressed(input, TimeflowShowcaseIds.ScenarioAtbActionId))
        {
            ResetScenario(engine, TimeflowScenarioId.Atb);
            return;
        }

        if (Pressed(input, TimeflowShowcaseIds.ScenarioAutoBattleActionId))
        {
            ResetScenario(engine, TimeflowScenarioId.AutoBattle);
            return;
        }

        if (Pressed(input, TimeflowShowcaseIds.ScenarioBreakFeverActionId))
        {
            ResetScenario(engine, TimeflowScenarioId.BreakFever);
            return;
        }

        if (Pressed(input, TimeflowShowcaseIds.ScenarioSentinelsActionId))
        {
            ResetScenario(engine, TimeflowScenarioId.Sentinels);
            return;
        }

        if (Pressed(input, TimeflowShowcaseIds.ScenarioCk3ActionId))
        {
            ResetScenario(engine, TimeflowScenarioId.CrusaderKings);
            return;
        }

        if (Pressed(input, TimeflowShowcaseIds.ScenarioBadNorthActionId))
        {
            ResetScenario(engine, TimeflowScenarioId.BadNorth);
            return;
        }

        if (Pressed(input, TimeflowShowcaseIds.ResetShowcaseActionId))
        {
            ResetScenario(engine, _state.ScenarioId);
            return;
        }

        if (Pressed(input, TimeflowShowcaseIds.GlobalPauseActionId))
        {
            ToggleGlobalPause();
        }

        if (Pressed(input, TimeflowShowcaseIds.GlobalBulletActionId))
        {
            ToggleGlobalBullet();
        }

        HandleScenarioInput(_state, input);
        _uiRevision++;
    }

    public void ResetScenario(GameEngine engine, TimeflowScenarioId scenarioId)
    {
        _engine = engine;
        _timeFlow ??= engine.GetService(CoreServiceKeys.TimeFlow)
            ?? throw new InvalidOperationException("TimeflowShowcaseMod requires CoreServiceKeys.TimeFlow.");

        EnsureDomains(_timeFlow);
        ClearAllTokens();
        _state = CreateState(scenarioId);
        _state.FocusIndex = (int)scenarioId;
        ApplyCameraFocus(engine, scenarioId);
        PushEvent(_state, $"[RESET] Loaded scenario {scenarioId}.");
        _uiRevision++;
    }

    public void TriggerAction(GameEngine engine, string actionId)
    {
        if (!IsActive(engine) || _state == null)
        {
            return;
        }

        HandleScenarioInput(_state, new TestActionReader(actionId));
        _uiRevision++;
    }

    public TimeflowShowcaseSnapshot CaptureSnapshot()
    {
        if (_state == null || _timeFlow == null)
        {
            return new TimeflowShowcaseSnapshot();
        }

        _timeFlow.FillSnapshots(_domainSnapshots);
        var tracked = _domainSnapshots
            .Where(snapshot => _trackedDomains.Contains(snapshot.Name))
            .OrderBy(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var actors = _state.Actors
            .Select(actor => new TimeflowActorSnapshot
            {
                Name = actor.Name,
                Team = actor.Team,
                DomainName = actor.DomainName,
                X = actor.X,
                Y = actor.Y,
                Health = actor.Health,
                Charge = actor.Charge,
                Energy = actor.Energy,
                WaitTicks = actor.WaitTicks,
                OrdersQueued = actor.OrdersQueued,
                Status = actor.Status,
                EffectiveScalePermille = _timeFlow.GetEffectiveScalePermille(actor.DomainName)
            })
            .ToList();

        return new TimeflowShowcaseSnapshot
        {
            ScenarioId = _state.ScenarioId,
            ScenarioTitle = _state.Title,
            InspirationLine = _state.InspirationLine,
            Phase = _state.Phase,
            StatusLine = _state.StatusLine,
            ControlsLine = _state.ControlsLine,
            FixedTick = _state.FixedTick,
            PresentationFrame = _state.PresentationFrame,
            UiRevision = _uiRevision,
            FocusIndex = _state.FocusIndex,
            GlobalPauseActive = _state.GlobalPauseActive,
            GlobalBulletActive = _state.GlobalBulletActive,
            LocalPauseActive = _state.LocalPauseActive,
            BreakGauge = _state.BreakGauge,
            MacroSpeedLevel = _state.MacroSpeedLevel,
            Actors = new ReadOnlyCollection<TimeflowActorSnapshot>(actors),
            RecentEvents = new ReadOnlyCollection<string>(_state.RecentEvents.ToList()),
            CommandQueue = new ReadOnlyCollection<string>(_state.CommandQueue.ToList()),
            Domains = new ReadOnlyCollection<TimeFlowDomainSnapshot>(tracked)
        };
    }

    public void DrawOverlay(GameEngine engine)
    {
        if (!IsActive(engine) || _state == null || _timeFlow == null)
        {
            return;
        }

        if (engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay)
        {
            return;
        }

        _state.PresentationFrame++;
        _state.PhasePresentationFrames++;
        TimeflowShowcaseSnapshot snapshot = CaptureSnapshot();

        int panelX = 22;
        int panelY = 22;
        overlay.AddRect(panelX, panelY, 860, 620, PanelFill, PanelBorder, stableId: 62000, dirtySerial: snapshot.UiRevision);
        overlay.AddText(panelX + 18, panelY + 22, $"Universal Timeflow Showcase | {snapshot.ScenarioTitle}", 22, TitleColor, stableId: 62001, dirtySerial: snapshot.UiRevision);
        overlay.AddText(panelX + 18, panelY + 48, snapshot.InspirationLine, 13, HintColor, stableId: 62002, dirtySerial: snapshot.UiRevision);
        overlay.AddText(panelX + 18, panelY + 74, "F1-F6 switch scenario | Space hard stop | Tab global bullet time | Backspace reset", 13, AccentColor, stableId: 62003, dirtySerial: snapshot.UiRevision);
        overlay.AddText(panelX + 18, panelY + 98, snapshot.ControlsLine, 13, HintColor, stableId: 62004, dirtySerial: snapshot.UiRevision);
        overlay.AddText(panelX + 18, panelY + 122, $"Phase={snapshot.Phase} Tick={snapshot.FixedTick} Frame={snapshot.PresentationFrame} Focus={snapshot.FocusIndex + 1}", 14, TextColor, stableId: 62005, dirtySerial: snapshot.UiRevision);
        overlay.AddText(panelX + 18, panelY + 146, $"Status: {snapshot.StatusLine}", 14, TextColor, stableId: 62006, dirtySerial: snapshot.UiRevision);
        overlay.AddText(panelX + 18, panelY + 170, $"GlobalPause={snapshot.GlobalPauseActive} GlobalBullet={snapshot.GlobalBulletActive} LocalPause={snapshot.LocalPauseActive} Break={snapshot.BreakGauge:0} CK3Speed={snapshot.MacroSpeedLevel}", 14, TextColor, stableId: 62007, dirtySerial: snapshot.UiRevision);

        int domainY = panelY + 206;
        overlay.AddText(panelX + 18, domainY, "Time domains", 16, AccentColor, stableId: 62010, dirtySerial: snapshot.UiRevision);
        for (int i = 0; i < snapshot.Domains.Count && i < 8; i++)
        {
            TimeFlowDomainSnapshot domain = snapshot.Domains[i];
            string line = $"{domain.Name,-28} scale={domain.EffectiveScalePermille,4} paused={domain.Paused,-5} mods={domain.ModifierCount}";
            overlay.AddText(panelX + 18, domainY + 24 + (i * 18), line, 13, domain.Paused ? MutedColor : TextColor, stableId: 62020 + i, dirtySerial: snapshot.UiRevision);
        }

        int actorY = panelY + 380;
        overlay.AddText(panelX + 18, actorY, "Actors", 16, AccentColor, stableId: 62040, dirtySerial: snapshot.UiRevision);
        for (int i = 0; i < snapshot.Actors.Count && i < 4; i++)
        {
            TimeflowActorSnapshot actor = snapshot.Actors[i];
            string line = $"{actor.Name,-16} T{actor.Team} HP={actor.Health,5:0} Charge={actor.Charge,5:0} Energy={actor.Energy,5:0} WT={actor.WaitTicks,4} x{actor.EffectiveScalePermille / 1000f:0.00} {actor.Status}";
            overlay.AddText(panelX + 18, actorY + 24 + (i * 18), line, 13, TextColor, stableId: 62050 + i, dirtySerial: snapshot.UiRevision);
        }

        int eventY = panelY + 494;
        overlay.AddText(panelX + 18, eventY, "Recent events", 16, AccentColor, stableId: 62070, dirtySerial: snapshot.UiRevision);
        for (int i = 0; i < snapshot.RecentEvents.Count && i < 5; i++)
        {
            overlay.AddText(panelX + 18, eventY + 24 + (i * 18), snapshot.RecentEvents[i], 13, HintColor, stableId: 62080 + i, dirtySerial: snapshot.UiRevision);
        }

        if (snapshot.CommandQueue.Count > 0)
        {
            int queueX = panelX + 510;
            int queueY = panelY + 380;
            overlay.AddText(queueX, queueY, "Queued commands", 16, AccentColor, stableId: 62120, dirtySerial: snapshot.UiRevision);
            for (int i = 0; i < snapshot.CommandQueue.Count && i < 6; i++)
            {
                overlay.AddText(queueX, queueY + 24 + (i * 18), snapshot.CommandQueue[i], 13, TextColor, stableId: 62130 + i, dirtySerial: snapshot.UiRevision);
            }
        }
    }

    private void EnsureDomains(TimeFlowService timeFlow)
    {
        timeFlow.EnsureDomain("showcase.atb", TimeFlowDomainIds.Simulation);
        timeFlow.EnsureDomain("showcase.atb.party", "showcase.atb");
        timeFlow.EnsureDomain("showcase.atb.enemy", "showcase.atb");
        timeFlow.EnsureDomain("showcase.autobattle", TimeFlowDomainIds.Simulation);
        timeFlow.EnsureDomain("showcase.autobattle.heroes", "showcase.autobattle");
        timeFlow.EnsureDomain("showcase.autobattle.enemies", "showcase.autobattle");
        timeFlow.EnsureDomain("showcase.breakfever", TimeFlowDomainIds.Simulation);
        timeFlow.EnsureDomain("showcase.breakfever.party", "showcase.breakfever");
        timeFlow.EnsureDomain("showcase.breakfever.boss", "showcase.breakfever");
        timeFlow.EnsureDomain("showcase.sentinels", TimeFlowDomainIds.Simulation);
        timeFlow.EnsureDomain("showcase.sentinels.squad", "showcase.sentinels");
        timeFlow.EnsureDomain("showcase.sentinels.wave", "showcase.sentinels");
        timeFlow.EnsureDomain("showcase.ck3", TimeFlowDomainIds.Simulation);
        timeFlow.EnsureDomain("showcase.badnorth", TimeFlowDomainIds.Simulation);
        timeFlow.EnsureDomain("showcase.badnorth.squads", "showcase.badnorth");
        timeFlow.EnsureDomain("showcase.badnorth.raiders", "showcase.badnorth");

        _trackedDomains.UnionWith(new[]
        {
            "showcase.atb.party",
            "showcase.atb.enemy",
            "showcase.autobattle.heroes",
            "showcase.autobattle.enemies",
            "showcase.breakfever.party",
            "showcase.breakfever.boss",
            "showcase.sentinels.squad",
            "showcase.sentinels.wave",
            "showcase.ck3",
            "showcase.badnorth.squads",
            "showcase.badnorth.raiders"
        });
    }

    private void ActivateInputContext(PlayerInputHandler? input)
    {
        if (input == null || _inputContextActive)
        {
            return;
        }

        EnsureShowcaseInputSchema(input);
        input.PushContext(TimeflowShowcaseIds.InputContextId);
        _inputContextActive = true;
    }

    private void DeactivateInputContext(PlayerInputHandler? input)
    {
        if (input == null || !_inputContextActive)
        {
            return;
        }

        input.PopContext(TimeflowShowcaseIds.InputContextId);
        _inputContextActive = false;
    }

    private static void EnsureShowcaseInputSchema(PlayerInputHandler input)
    {
        if (!input.HasContext(TimeflowShowcaseIds.InputContextId))
        {
            throw new InvalidOperationException($"Missing input context: {TimeflowShowcaseIds.InputContextId}");
        }
    }

    private void ToggleGlobalPause()
    {
        if (_state == null)
        {
            return;
        }

        if (_state.GlobalPauseActive)
        {
            ReleaseToken(GlobalPauseKey);
            _state.GlobalPauseActive = false;
            _state.StatusLine = "Global hard stop released.";
            PushEvent(_state, "[GLOBAL] Hard stop released.");
        }
        else
        {
            AcquirePause(GlobalPauseKey, TimeFlowDomainIds.Simulation, "GlobalHardStop");
            _state.GlobalPauseActive = true;
            _state.StatusLine = "Global hard stop engaged on simulation root.";
            PushEvent(_state, "[GLOBAL] Hard stop engaged.");
        }
    }

    private void ToggleGlobalBullet()
    {
        if (_state == null)
        {
            return;
        }

        if (_state.GlobalBulletActive)
        {
            ReleaseToken(GlobalBulletKey);
            _state.GlobalBulletActive = false;
            _state.StatusLine = "Global bullet time cleared.";
            PushEvent(_state, "[GLOBAL] Bullet time cleared.");
        }
        else
        {
            AcquireScale(GlobalBulletKey, TimeFlowDomainIds.Simulation, 300, "GlobalBulletTime");
            _state.GlobalBulletActive = true;
            _state.StatusLine = "Global bullet time engaged at 0.30x.";
            PushEvent(_state, "[GLOBAL] Bullet time engaged.");
        }
    }

    private void ClearAllTokens()
    {
        foreach (TimeFlowToken token in _tokens.Values)
        {
            _timeFlow?.ReleaseToken(token);
        }

        _tokens.Clear();
    }

    private void AcquireScale(string key, string domainName, int scalePermille, string reason)
    {
        if (_timeFlow == null)
        {
            return;
        }

        ReleaseToken(key);
        _tokens[key] = _timeFlow.AcquireScaleToken(domainName, scalePermille, ShowcaseOwner, reason);
    }

    private void AcquirePause(string key, string domainName, string reason)
    {
        if (_timeFlow == null)
        {
            return;
        }

        ReleaseToken(key);
        _tokens[key] = _timeFlow.AcquirePauseToken(domainName, ShowcaseOwner, reason);
    }

    private void ReleaseToken(string key)
    {
        if (_timeFlow == null)
        {
            return;
        }

        if (_tokens.Remove(key, out TimeFlowToken token))
        {
            _timeFlow.ReleaseToken(token);
        }
    }

    private void ApplyCameraFocus(GameEngine engine, TimeflowScenarioId scenarioId)
    {
        Vector2 target = scenarioId switch
        {
            TimeflowScenarioId.Atb => new Vector2(700f, 700f),
            TimeflowScenarioId.AutoBattle => new Vector2(2100f, 700f),
            TimeflowScenarioId.BreakFever => new Vector2(3500f, 700f),
            TimeflowScenarioId.Sentinels => new Vector2(700f, 2100f),
            TimeflowScenarioId.CrusaderKings => new Vector2(2100f, 2100f),
            TimeflowScenarioId.BadNorth => new Vector2(3500f, 2100f),
            _ => new Vector2(700f, 700f)
        };

        engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
        {
            TargetCm = target,
            Pitch = 48f,
            DistanceCm = 6400f,
            FovYDeg = 54f
        });
    }

    private static bool Pressed(IInputActionReader input, string actionId) => input.PressedThisFrame(actionId);

    private sealed class TestActionReader : IInputActionReader
    {
        private readonly string _actionId;

        public TestActionReader(string actionId)
        {
            _actionId = actionId;
        }

        public T ReadAction<T>(string actionId) where T : struct => default;
        public bool IsDown(string actionId) => string.Equals(actionId, _actionId, StringComparison.Ordinal);
        public bool PressedThisFrame(string actionId) => string.Equals(actionId, _actionId, StringComparison.Ordinal);
        public bool ReleasedThisFrame(string actionId) => false;
    }
}

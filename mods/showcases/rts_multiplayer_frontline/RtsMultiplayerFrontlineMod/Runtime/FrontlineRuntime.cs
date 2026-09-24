using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using RtsMultiplayerFrontlineMod.Systems;

namespace RtsMultiplayerFrontlineMod.Runtime;

public readonly record struct FrontlineMatchSnapshot(
    int CommittedTick,
    FrontlineMatchPhase Phase,
    int CountdownRemainingTicks,
    FrontlineMatchOutcome Outcome,
    int WinningSideIndex,
    bool SideOneReady,
    bool SideTwoReady,
    bool SideOneConnected,
    bool SideTwoConnected);

public sealed class FrontlineRuntime
{
    private readonly IModContext _context;
    private readonly bool[] _connected = { true, true };
    private readonly bool[] _ready = new bool[2];
    private readonly int[] _disconnectTicks = new int[2];
    private FrontlineConfig? _config;
    private bool _installed;
    private bool _active;
    private int _committedTick;
    private int _countdownRemainingTicks;
    private FrontlineMatchPhase _phase;
    private FrontlineMatchOutcome _outcome;
    private int _winningSideIndex = -1;

    public FrontlineRuntime(IModContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public bool IsActive => _active;
    public bool CanAdvanceGameplay => _active && _phase == FrontlineMatchPhase.InProgress && _outcome == FrontlineMatchOutcome.InProgress;
    public FrontlineConfig Config => _config
        ?? throw new InvalidOperationException("RTS Frontline config has not been loaded.");
    public FrontlineMatchSnapshot Snapshot => new(
        _committedTick,
        _phase,
        _countdownRemainingTicks,
        _outcome,
        _winningSideIndex,
        _ready[0],
        _ready[1],
        _connected[0],
        _connected[1]);

    public Task HandleGameStartAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            throw new InvalidOperationException("RTS Frontline requires GameEngine on GameStart.");
        }

        EnsureConfig(engine);
        InstallSystems(engine);
        engine.GlobalContext["rts.multiplayer.frontline.runtime"] = this;
        return Task.CompletedTask;
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            throw new InvalidOperationException("RTS Frontline requires GameEngine on map focus.");
        }

        EnsureConfig(engine);
        _active = string.Equals(engine.CurrentMapSession?.MapId.Value, Config.MapId, StringComparison.Ordinal);
        if (_active)
        {
            ResetMatch();
        }

        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (string.Equals(context.Get(CoreServiceKeys.MapId).Value, Config.MapId, StringComparison.Ordinal))
        {
            _active = false;
        }

        return Task.CompletedTask;
    }

    public void SetParticipantConnected(int sideIndex, bool connected)
    {
        if ((uint)sideIndex >= (uint)_connected.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(sideIndex));
        }

        if (_connected[sideIndex] == connected)
        {
            return;
        }

        _connected[sideIndex] = connected;
        _disconnectTicks[sideIndex] = 0;
        if (!connected)
        {
            _ready[sideIndex] = false;
            if (_phase != FrontlineMatchPhase.InProgress && _phase != FrontlineMatchPhase.Completed)
            {
                CancelCountdown();
            }
        }
    }

    public void SetParticipantReady(int sideIndex, bool ready)
    {
        ValidateSideIndex(sideIndex);
        if (ready && !_connected[sideIndex])
        {
            throw new InvalidOperationException($"RTS Frontline side {sideIndex} cannot become ready while disconnected.");
        }
        if (_phase == FrontlineMatchPhase.InProgress || _phase == FrontlineMatchPhase.Completed)
        {
            throw new InvalidOperationException("RTS Frontline readiness cannot change after the battle starts.");
        }

        _ready[sideIndex] = ready;
        if (!_ready[0] || !_ready[1] || !_connected[0] || !_connected[1])
        {
            CancelCountdown();
            return;
        }

        if (_phase != FrontlineMatchPhase.Countdown)
        {
            _phase = FrontlineMatchPhase.Countdown;
            _countdownRemainingTicks = Config.ReadyCountdownTicks;
        }
    }

    internal bool AdvanceFixedTick()
    {
        if (_phase == FrontlineMatchPhase.WaitingForPlayers)
        {
            return false;
        }

        if (_phase == FrontlineMatchPhase.Countdown)
        {
            if (!_ready[0] || !_ready[1] || !_connected[0] || !_connected[1])
            {
                CancelCountdown();
                return false;
            }

            _countdownRemainingTicks--;
            if (_countdownRemainingTicks <= 0)
            {
                _countdownRemainingTicks = 0;
                _phase = FrontlineMatchPhase.InProgress;
            }
            return false;
        }

        if (_phase != FrontlineMatchPhase.InProgress)
        {
            return false;
        }

        _committedTick++;
        for (int i = 0; i < _connected.Length; i++)
        {
            if (!_connected[i])
            {
                _disconnectTicks[i]++;
            }
        }

        return true;
    }

    internal bool IsDisconnectedPastGrace(int sideIndex) =>
        !_connected[sideIndex] && _disconnectTicks[sideIndex] >= Config.DisconnectGraceTicks;

    internal void CommitOutcome(FrontlineMatchOutcome outcome, int winningSideIndex)
    {
        if (_outcome != FrontlineMatchOutcome.InProgress)
        {
            return;
        }

        _outcome = outcome;
        _winningSideIndex = winningSideIndex;
        _phase = FrontlineMatchPhase.Completed;
    }

    private void EnsureConfig(GameEngine engine)
    {
        if (_config != null)
        {
            return;
        }

        ConfigPipeline pipeline = engine.ConfigPipeline
            ?? throw new InvalidOperationException("RTS Frontline requires ConfigPipeline.");
        _config = new FrontlineConfigLoader(pipeline).Load(engine.ConfigCatalog, engine.ConfigConflictReport);
        float configuredDeltaTime = 1f / _config.SimulationTickRateHz;
        if (MathF.Abs(Ludots.Core.Engine.Time.FixedDeltaTime - configuredDeltaTime) > 0.000001f)
        {
            throw new InvalidOperationException(
                $"RTS Frontline requires {_config.SimulationTickRateHz}Hz fixed simulation; " +
                $"engine is configured for {1f / Ludots.Core.Engine.Time.FixedDeltaTime:0.###}Hz.");
        }
    }

    private void InstallSystems(GameEngine engine)
    {
        if (_installed)
        {
            return;
        }

        OrderQueue orderQueue = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("RTS Frontline requires OrderQueue.");
        OrderTypeRegistry orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("RTS Frontline requires OrderTypeRegistry.");
        TagOps tagOps = engine.GetService(CoreServiceKeys.TagOps)
            ?? throw new InvalidOperationException("RTS Frontline requires TagOps.");
        EffectRequestQueue effectRequests = engine.GetService(CoreServiceKeys.EffectRequestQueue)
            ?? throw new InvalidOperationException("RTS Frontline requires EffectRequestQueue.");
        RuntimeEntityLifecycleQueue lifecycle = engine.GetService(CoreServiceKeys.RuntimeEntityLifecycleQueue)
            ?? throw new InvalidOperationException("RTS Frontline requires RuntimeEntityLifecycleQueue.");

        var trainGuard = new FrontlineTrainingAdmissionSystem(engine.World, this, orderTypes);
        engine.InsertSystemBeforeRequired<AbilitySystem>(trainGuard, SystemGroup.AbilityActivation);
        engine.InsertSystemBeforeRequired<AbilitySystem>(
            new FrontlinePreMatchOrderGateSystem(engine.World, this, orderTypes),
            SystemGroup.AbilityActivation);
        engine.RegisterSystem(new FrontlineTagBindingSystem(engine.World, this, tagOps), SystemGroup.AbilityActivation);
        engine.RegisterSystem(new FrontlineHarvestSystem(engine.World, this, orderQueue, orderTypes), SystemGroup.AbilityActivation);
        engine.RegisterSystem(new FrontlineCombatSystem(engine.World, this, orderQueue, orderTypes, effectRequests), SystemGroup.AbilityActivation);
        engine.RegisterSystem(new FrontlineDeathAndMatchSystem(engine.World, this, lifecycle), SystemGroup.Cleanup);
        engine.RegisterPresentationSystem(new FrontlinePresentationSystem(engine, this));
        _installed = true;
    }

    private void ResetMatch()
    {
        _committedTick = 0;
        _countdownRemainingTicks = 0;
        _phase = FrontlineMatchPhase.WaitingForPlayers;
        _outcome = FrontlineMatchOutcome.InProgress;
        _winningSideIndex = -1;
        for (int i = 0; i < _connected.Length; i++)
        {
            _connected[i] = true;
            _ready[i] = false;
            _disconnectTicks[i] = 0;
        }
    }

    private void CancelCountdown()
    {
        _phase = FrontlineMatchPhase.WaitingForPlayers;
        _countdownRemainingTicks = 0;
    }

    private static void ValidateSideIndex(int sideIndex)
    {
        if ((uint)sideIndex >= 2u)
        {
            throw new ArgumentOutOfRangeException(nameof(sideIndex));
        }
    }
}

using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.ActionLoops;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Modding;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
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

public readonly record struct FrontlineMatchResolutionSnapshot(
    int CommittedTick,
    FrontlineMatchResolutionReason Reason,
    FrontlineMatchOutcome Outcome,
    int WinningSideIndex,
    float SideOneCoreHealth,
    float SideTwoCoreHealth);

public readonly record struct FrontlineOpeningViewSnapshot(
    bool HasFocusTarget,
    Vector2 FocusTargetCm,
    int CapturedVisibilityRevision,
    bool IsReady,
    int ReadyVisibilityRevision);

public sealed class FrontlineRuntime : IGameplayActionLoopGate
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
    private FrontlineMatchResolutionSnapshot? _resolution;
    private NetworkProcessRole _networkRole;
    private NetworkGameplayCommandGate? _networkGameplayCommandGate;
    private ulong _networkRoomSessionEpoch;
    private ulong _lastNetworkRoomRevision;
    private bool _durationCoreHealthCaptured;
    private float _durationSideOneCoreHealth;
    private float _durationSideTwoCoreHealth;
    private bool _hasOpeningFocusTarget;
    private Vector2 _openingFocusTargetCm;
    private int _openingFocusCapturedVisibilityRevision = -1;
    private bool _openingViewReady;
    private int _openingViewReadyVisibilityRevision = -1;
    private object? _validatedOpeningSession;
    private FrontlineTagBinder? _tagBinder;

    public FrontlineRuntime(IModContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public bool IsActive => _active;
    public bool IsNetworked => _networkRole != NetworkProcessRole.Standalone;
    public bool CanAdvanceGameplay => _active && _phase == FrontlineMatchPhase.InProgress && _outcome == FrontlineMatchOutcome.InProgress;
    public FrontlineConfig Config => _config
        ?? throw new InvalidOperationException("RTS Frontline config has not been loaded.");
    internal FrontlineTagBinder TagBinder => _tagBinder
        ?? throw new InvalidOperationException("RTS Frontline tag binding is not installed.");
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
    public FrontlineMatchResolutionSnapshot Resolution => _resolution
        ?? throw new InvalidOperationException("RTS Frontline has not committed a match resolution.");
    public FrontlineOpeningViewSnapshot OpeningView => new(
        _hasOpeningFocusTarget,
        _openingFocusTargetCm,
        _openingFocusCapturedVisibilityRevision,
        _openingViewReady,
        _openingViewReadyVisibilityRevision);

    public Task HandleGameStartAsync(ScriptContext context)
    {
        if (context.Get(CoreServiceKeys.Engine) is not GameEngine engine)
        {
            throw new InvalidOperationException("RTS Frontline requires GameEngine on GameStart.");
        }

        EnsureConfig(engine);
        _networkRole = engine.GetService(CoreServiceKeys.NetworkProcessRole);
        if (_networkRole == NetworkProcessRole.AuthoritativeServer)
        {
            _networkGameplayCommandGate = engine.GetService(CoreServiceKeys.NetworkGameplayCommandGate)
                ?? throw new InvalidOperationException("RTS Frontline authoritative server requires the Core network gameplay command gate.");
        }
        InstallSystems(engine);
        engine.GlobalContext["rts.multiplayer.frontline.runtime"] = this;
        return Task.CompletedTask;
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.Get(CoreServiceKeys.Engine) is not GameEngine engine)
        {
            throw new InvalidOperationException("RTS Frontline requires GameEngine on map focus.");
        }

        EnsureConfig(engine);
        _active = string.Equals(engine.CurrentMapSession?.MapId.Value, Config.MapId, StringComparison.Ordinal);
        if (_active)
        {
            object session = engine.CurrentMapSession
                ?? throw new InvalidOperationException("RTS Frontline requires a focused map session.");
            if (!ReferenceEquals(_validatedOpeningSession, session))
            {
                FrontlineOpeningAuthoring.Validate(engine, Config);
                _validatedOpeningSession = session;
            }
            ResetMatch();
        }

        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (string.Equals(context.Get(CoreServiceKeys.MapId).Value, Config.MapId, StringComparison.Ordinal))
        {
            _active = false;
            _validatedOpeningSession = null;
        }

        return Task.CompletedTask;
    }

    public void SetParticipantConnected(int sideIndex, bool connected)
    {
        RequireStandaloneRoomControl();
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
        RequireStandaloneRoomControl();
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
        if (_networkRole == NetworkProcessRole.ReplicatedClient)
        {
            return false;
        }

        if (_networkRole == NetworkProcessRole.AuthoritativeServer)
        {
            if (_phase != FrontlineMatchPhase.InProgress || _outcome != FrontlineMatchOutcome.InProgress)
            {
                return false;
            }

            AdvanceMatchTick();
            return true;
        }

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

        AdvanceMatchTick();

        return true;
    }

    internal bool IsDisconnectedPastGrace(int sideIndex) =>
        !_connected[sideIndex] && _disconnectTicks[sideIndex] >= Config.DisconnectGraceTicks;

    internal bool HasParticipantAwaitingReconnect => !_connected[0] || !_connected[1];

    internal bool HasDurationCoreHealthSnapshot => _durationCoreHealthCaptured;

    internal void CaptureOpeningFocusTarget(Vector2 targetCm, int visibilityRevision)
    {
        if (!float.IsFinite(targetCm.X) || !float.IsFinite(targetCm.Y) || visibilityRevision < 0)
        {
            throw new InvalidOperationException(
                "RTS Frontline opening view requires a finite focus target and initialized culling revision.");
        }
        if (_hasOpeningFocusTarget)
        {
            if (_openingFocusTargetCm != targetCm)
            {
                throw new InvalidOperationException(
                    "RTS Frontline opening camera focus target changed before the opening view became ready.");
            }
            return;
        }

        _hasOpeningFocusTarget = true;
        _openingFocusTargetCm = targetCm;
        _openingFocusCapturedVisibilityRevision = visibilityRevision;
    }

    internal void MarkOpeningViewReady(int visibilityRevision)
    {
        if (!_hasOpeningFocusTarget || visibilityRevision <= _openingFocusCapturedVisibilityRevision)
        {
            throw new InvalidOperationException(
                "RTS Frontline opening view cannot become ready before culling advances at the focused target.");
        }

        _openingViewReady = true;
        _openingViewReadyVisibilityRevision = visibilityRevision;
    }

    internal void CaptureDurationCoreHealth(float sideOneHealth, float sideTwoHealth)
    {
        if (_durationCoreHealthCaptured)
        {
            return;
        }

        if (!float.IsFinite(sideOneHealth) || !float.IsFinite(sideTwoHealth))
        {
            throw new InvalidOperationException("RTS Frontline duration snapshot requires finite command-core health.");
        }

        _durationSideOneCoreHealth = sideOneHealth;
        _durationSideTwoCoreHealth = sideTwoHealth;
        _durationCoreHealthCaptured = true;
    }

    internal void CommitDurationOutcome()
    {
        if (!_durationCoreHealthCaptured)
        {
            throw new InvalidOperationException("RTS Frontline cannot resolve match duration before core health is captured.");
        }

        if (_durationSideOneCoreHealth == _durationSideTwoCoreHealth)
        {
            CommitOutcome(new FrontlineMatchResolutionSnapshot(
                _committedTick,
                FrontlineMatchResolutionReason.Duration,
                FrontlineMatchOutcome.Draw,
                -1,
                _durationSideOneCoreHealth,
                _durationSideTwoCoreHealth));
        }
        else if (_durationSideOneCoreHealth > _durationSideTwoCoreHealth)
        {
            CommitOutcome(new FrontlineMatchResolutionSnapshot(
                _committedTick,
                FrontlineMatchResolutionReason.Duration,
                FrontlineMatchOutcome.SideOneVictory,
                0,
                _durationSideOneCoreHealth,
                _durationSideTwoCoreHealth));
        }
        else
        {
            CommitOutcome(new FrontlineMatchResolutionSnapshot(
                _committedTick,
                FrontlineMatchResolutionReason.Duration,
                FrontlineMatchOutcome.SideTwoVictory,
                1,
                _durationSideOneCoreHealth,
                _durationSideTwoCoreHealth));
        }
    }

    internal void ApplyNetworkRoomSnapshot(
        in NetworkRoomSnapshotHeader header,
        ReadOnlySpan<NetworkRoomSeatSnapshot> seats)
    {
        if (_networkRole != NetworkProcessRole.AuthoritativeServer)
        {
            throw new InvalidOperationException("Only the authoritative Frontline runtime may consume server room snapshots.");
        }

        if (header.SeatCount != Config.Sides.Length || seats.Length != Config.Sides.Length)
        {
            throw new InvalidOperationException(
                $"RTS Frontline requires {Config.Sides.Length} network room seats; received {seats.Length}.");
        }

        if (_networkRoomSessionEpoch == 0)
        {
            _networkRoomSessionEpoch = header.SessionEpoch.Value;
        }
        else if (_networkRoomSessionEpoch != header.SessionEpoch.Value)
        {
            throw new InvalidOperationException("RTS Frontline room session epoch changed while the map remained active.");
        }

        if (header.Revision < _lastNetworkRoomRevision)
        {
            throw new InvalidOperationException("RTS Frontline room snapshot revision regressed.");
        }

        if (header.Revision == _lastNetworkRoomRevision)
        {
            return;
        }

        for (int i = 0; i < seats.Length; i++)
        {
            ref readonly NetworkRoomSeatSnapshot seat = ref seats[i];
            if (seat.Slot != i ||
                (seat.ConnectionState != NetworkRoomSeatConnectionState.Empty &&
                 seat.PlayerId.Value != Config.Sides[i].PlayerId))
            {
                throw new InvalidOperationException($"RTS Frontline room seat {i} does not match its configured player.");
            }

            bool connected = seat.ConnectionState == NetworkRoomSeatConnectionState.Connected;
            if (_connected[i] != connected)
            {
                _disconnectTicks[i] = 0;
            }

            _connected[i] = connected;
            _ready[i] = seat.ReadyState == NetworkRoomReadyState.Ready;
        }

        if (header.Phase == NetworkRoomPhase.Started)
        {
            if (_phase != FrontlineMatchPhase.Completed)
            {
                _phase = FrontlineMatchPhase.InProgress;
                _countdownRemainingTicks = 0;
            }
        }
        else
        {
            if (_phase is FrontlineMatchPhase.InProgress or FrontlineMatchPhase.Completed)
            {
                throw new InvalidOperationException("RTS Frontline room phase regressed after the battle started.");
            }

            _phase = header.Phase == NetworkRoomPhase.Countdown
                ? FrontlineMatchPhase.Countdown
                : FrontlineMatchPhase.WaitingForPlayers;
            _countdownRemainingTicks = checked((int)header.CountdownRemainingTicks);
        }

        _lastNetworkRoomRevision = header.Revision;
    }

    internal void CommitOutcome(in FrontlineMatchResolutionSnapshot resolution)
    {
        if (_outcome != FrontlineMatchOutcome.InProgress)
        {
            throw new InvalidOperationException("RTS Frontline match resolution was already committed.");
        }

        ValidateResolution(in resolution);

        _resolution = resolution;
        _outcome = resolution.Outcome;
        _winningSideIndex = resolution.WinningSideIndex;
        _phase = FrontlineMatchPhase.Completed;
        _networkGameplayCommandGate?.CompleteMatch();
    }

    private void ValidateResolution(in FrontlineMatchResolutionSnapshot resolution)
    {
        if (resolution.CommittedTick != _committedTick)
        {
            throw new InvalidOperationException(
                $"RTS Frontline resolution tick {resolution.CommittedTick} differs from committed tick {_committedTick}.");
        }
        if (resolution.Reason is < FrontlineMatchResolutionReason.CoreDestroyed or > FrontlineMatchResolutionReason.Disconnect)
        {
            throw new InvalidOperationException($"RTS Frontline resolution reason {resolution.Reason} is invalid.");
        }
        if (!float.IsFinite(resolution.SideOneCoreHealth) || !float.IsFinite(resolution.SideTwoCoreHealth))
        {
            throw new InvalidOperationException("RTS Frontline resolution requires finite command-core health.");
        }

        bool validOutcome = resolution.Outcome switch
        {
            FrontlineMatchOutcome.SideOneVictory => resolution.WinningSideIndex == 0,
            FrontlineMatchOutcome.SideTwoVictory => resolution.WinningSideIndex == 1,
            FrontlineMatchOutcome.Draw => resolution.WinningSideIndex == -1,
            _ => false,
        };
        if (!validOutcome)
        {
            throw new InvalidOperationException(
                $"RTS Frontline resolution outcome {resolution.Outcome} conflicts with winner {resolution.WinningSideIndex}.");
        }

        bool sideOneDestroyed = resolution.SideOneCoreHealth <= 0f;
        bool sideTwoDestroyed = resolution.SideTwoCoreHealth <= 0f;
        if (resolution.Reason == FrontlineMatchResolutionReason.CoreDestroyed)
        {
            FrontlineMatchOutcome healthOutcome = sideOneDestroyed && sideTwoDestroyed
                ? FrontlineMatchOutcome.Draw
                : sideOneDestroyed
                    ? FrontlineMatchOutcome.SideTwoVictory
                    : sideTwoDestroyed
                        ? FrontlineMatchOutcome.SideOneVictory
                        : FrontlineMatchOutcome.InProgress;
            if (resolution.Outcome != healthOutcome)
            {
                throw new InvalidOperationException(
                    "RTS Frontline core-destruction resolution conflicts with final command-core health.");
            }
        }
        else if (sideOneDestroyed || sideTwoDestroyed)
        {
            throw new InvalidOperationException(
                "RTS Frontline duration or disconnect resolution cannot contain a destroyed command core.");
        }
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
        OrderRuleRegistry orderRules = engine.GetService(CoreServiceKeys.OrderRuleRegistry)
            ?? throw new InvalidOperationException("RTS Frontline requires OrderRuleRegistry.");
        OrderBufferSystem orderBufferSystem = engine.GetService(CoreServiceKeys.OrderBufferSystem)
            ?? throw new InvalidOperationException("RTS Frontline requires OrderBufferSystem.");
        TagOps tagOps = engine.GetService(CoreServiceKeys.TagOps)
            ?? throw new InvalidOperationException("RTS Frontline requires TagOps.");
        EffectRequestQueue effectRequests = engine.GetService(CoreServiceKeys.EffectRequestQueue)
            ?? throw new InvalidOperationException("RTS Frontline requires EffectRequestQueue.");
        var trainGuard = new FrontlineTrainingAdmissionSystem(engine.World, this, orderTypes, tagOps);
        orderRules.RegisterAdmissionValidator(
            orderTypes.GetId(Config.CastAbilityOrderTypeKey),
            trainGuard);
        engine.InsertSystemBeforeRequired<OrderBufferSystem>(trainGuard, SystemGroup.AbilityActivation);
        engine.InsertSystemBeforeRequired<OrderBufferSystem>(
            new FrontlinePreMatchOrderGateSystem(engine.World, this, orderBufferSystem),
            SystemGroup.AbilityActivation);
        _tagBinder = new FrontlineTagBinder(Config, tagOps);
        // capabilityId: rts-frontline.tag-binding
        engine.RegisterSystem(new FrontlineTagBindingSystem(engine.World, this, _tagBinder), SystemGroup.RuntimeEntityBinding);
        // capabilityId: rts-frontline.resource-transport
        engine.RegisterSystem(
            new ResourceTransportSystem(engine.World, orderQueue, orderTypes, this, tagOps),
            SystemGroup.AbilityActivation);
        // capabilityId: rts-frontline.direct-attack
        engine.RegisterSystem(
            new DirectAttackSystem(engine.World, orderQueue, orderTypes, effectRequests, this),
            SystemGroup.AbilityActivation);
        // capabilityId: rts-frontline.death-and-match
        engine.RegisterSystem(
            new FrontlineDeathAndMatchSystem(engine.World, this, orderBufferSystem),
            SystemGroup.Cleanup);
        if (_networkRole == NetworkProcessRole.AuthoritativeServer)
        {
            NetworkRuntimeStateObserver observer = engine.GetService(CoreServiceKeys.NetworkRuntimeStateObserver)
                ?? throw new InvalidOperationException("RTS Frontline authoritative server requires the Core network room observer.");
            if (observer.SeatCapacity != Config.Sides.Length)
            {
                throw new InvalidOperationException("RTS Frontline network room capacity does not match the configured sides.");
            }

            // capabilityId: rts-frontline.network-room-sync
            engine.RegisterSystem(
                new FrontlineNetworkRoomSynchronizationSystem(this, observer),
                SystemGroup.SchemaUpdate);
        }
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
        _resolution = null;
        _networkRoomSessionEpoch = 0;
        _lastNetworkRoomRevision = 0;
        _durationCoreHealthCaptured = false;
        _durationSideOneCoreHealth = 0f;
        _durationSideTwoCoreHealth = 0f;
        _hasOpeningFocusTarget = false;
        _openingFocusTargetCm = default;
        _openingFocusCapturedVisibilityRevision = -1;
        _openingViewReady = false;
        _openingViewReadyVisibilityRevision = -1;
        for (int i = 0; i < _connected.Length; i++)
        {
            _connected[i] = _networkRole == NetworkProcessRole.Standalone;
            _ready[i] = false;
            _disconnectTicks[i] = 0;
        }
    }

    private void CancelCountdown()
    {
        _phase = FrontlineMatchPhase.WaitingForPlayers;
        _countdownRemainingTicks = 0;
    }

    private void AdvanceMatchTick()
    {
        _committedTick++;
        for (int i = 0; i < _connected.Length; i++)
        {
            if (!_connected[i])
            {
                _disconnectTicks[i]++;
            }
        }
    }

    private void RequireStandaloneRoomControl()
    {
        if (_networkRole != NetworkProcessRole.Standalone)
        {
            throw new InvalidOperationException("Networked Frontline room state is owned by the Core session registry.");
        }
    }

    private static void ValidateSideIndex(int sideIndex)
    {
        if ((uint)sideIndex >= 2u)
        {
            throw new ArgumentOutOfRangeException(nameof(sideIndex));
        }
    }
}

internal static class FrontlineOpeningAuthoring
{
    private static readonly QueryDescription OpeningGameplayQuery = new QueryDescription()
        .WithAll<MapEntity, FrontlineParticipant, Team, PlayerOwner>()
        .WithAny<FrontlineCore, FrontlineHarvester, FrontlineInfantry>();

    internal static void Validate(GameEngine engine, FrontlineConfig config)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(config);
        if (engine.CurrentMapSession == null ||
            !string.Equals(engine.CurrentMapSession.MapId.Value, config.MapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"RTS Frontline opening validation requires focused map '{config.MapId}'.");
        }

        int crystalAttributeId = AttributeRegistry.GetId(config.CrystalAttribute);
        if (crystalAttributeId == AttributeRegistry.InvalidId)
        {
            throw new InvalidOperationException(
                $"RTS Frontline opening validation requires registered attribute '{config.CrystalAttribute}'.");
        }

        Span<int> coreCounts = stackalloc int[config.Sides.Length];
        Span<int> harvesterCounts = stackalloc int[config.Sides.Length];
        Span<int> infantryCounts = stackalloc int[config.Sides.Length];
        Span<float> coreCrystals = stackalloc float[config.Sides.Length];
        coreCrystals.Fill(float.NaN);

        World world = engine.World;
        foreach (ref Chunk chunk in world.Query(in OpeningGameplayQuery))
        {
            ref Entity firstEntity = ref chunk.Entity(0);
            ReadOnlySpan<MapEntity> mapEntities = chunk.GetSpan<MapEntity>();
            ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            ReadOnlySpan<Team> teams = chunk.GetSpan<Team>();
            ReadOnlySpan<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
            foreach (int index in chunk)
            {
                if (!string.Equals(mapEntities[index].MapId.Value, config.MapId, StringComparison.Ordinal))
                {
                    continue;
                }

                int sideIndex = participants[index].SideIndex;
                if ((uint)sideIndex >= (uint)config.Sides.Length)
                {
                    throw new InvalidOperationException(
                        $"RTS Frontline map-authored opening entity declares invalid side {sideIndex}.");
                }

                FrontlineSideConfig side = config.Sides[sideIndex];
                if (teams[index].Id != side.TeamId || owners[index].PlayerId != side.PlayerId)
                {
                    throw new InvalidOperationException(
                        $"RTS Frontline map-authored side {sideIndex} does not match configured team/player identity.");
                }

                Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref firstEntity, index);
                bool isCore = world.Has<FrontlineCore>(entity);
                bool isHarvester = world.Has<FrontlineHarvester>(entity);
                bool isInfantry = world.Has<FrontlineInfantry>(entity);
                int roleCount = (isCore ? 1 : 0) + (isHarvester ? 1 : 0) + (isInfantry ? 1 : 0);
                if (roleCount != 1)
                {
                    throw new InvalidOperationException(
                        "RTS Frontline map-authored opening entity must declare exactly one gameplay role.");
                }

                if (isHarvester)
                {
                    harvesterCounts[sideIndex]++;
                    continue;
                }

                if (isInfantry)
                {
                    infantryCounts[sideIndex]++;
                    continue;
                }

                coreCounts[sideIndex]++;
                if (!world.TryGet(entity, out AttributeBuffer attributes) ||
                    !attributes.HasAttribute(crystalAttributeId))
                {
                    throw new InvalidOperationException(
                        $"RTS Frontline side {sideIndex} command core must author '{config.CrystalAttribute}'.");
                }

                float crystals = attributes.GetCurrent(crystalAttributeId);
                if (!float.IsFinite(crystals) || crystals < 0f)
                {
                    throw new InvalidOperationException(
                        $"RTS Frontline side {sideIndex} command core has invalid starting crystals {crystals}.");
                }

                coreCrystals[sideIndex] = crystals;
            }
        }

        for (int sideIndex = 0; sideIndex < config.Sides.Length; sideIndex++)
        {
            if (coreCounts[sideIndex] != 1 || harvesterCounts[sideIndex] <= 0 || infantryCounts[sideIndex] <= 0)
            {
                throw new InvalidOperationException(
                    $"RTS Frontline map-authored side {sideIndex} requires exactly one core and positive starting forces; " +
                    $"found cores={coreCounts[sideIndex]}, harvesters={harvesterCounts[sideIndex]}, infantry={infantryCounts[sideIndex]}.");
            }
        }

        for (int sideIndex = 1; sideIndex < config.Sides.Length; sideIndex++)
        {
            if (harvesterCounts[sideIndex] != harvesterCounts[0] ||
                infantryCounts[sideIndex] != infantryCounts[0] ||
                coreCrystals[sideIndex] != coreCrystals[0])
            {
                throw new InvalidOperationException(
                    "RTS Frontline map-authored opening must be mirrored across both sides; " +
                    $"side 0 has crystals={coreCrystals[0]}, harvesters={harvesterCounts[0]}, infantry={infantryCounts[0]}, " +
                    $"side {sideIndex} has crystals={coreCrystals[sideIndex]}, harvesters={harvesterCounts[sideIndex]}, infantry={infantryCounts[sideIndex]}.");
            }
        }
    }
}

using Ludots.Core.Client;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Map;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using RtsMultiplayerFrontlineMod.Runtime;

namespace RtsMultiplayerFrontlineMod.Systems;

internal sealed class FrontlineNetworkRoomSynchronizationSystem : ISystem<float>
{
    private readonly FrontlineRuntime _runtime;
    private readonly NetworkRuntimeStateObserver _observer;

    public FrontlineNetworkRoomSynchronizationSystem(
        FrontlineRuntime runtime,
        NetworkRuntimeStateObserver observer)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_runtime.IsActive || !_observer.HasRoomSnapshot)
        {
            return;
        }

        Span<NetworkRoomSeatSnapshot> seats = stackalloc NetworkRoomSeatSnapshot[2];
        if (!_observer.TryCopyRoomSeats(seats, out int seatCount) || seatCount != seats.Length)
        {
            throw new InvalidOperationException("RTS Frontline could not copy the complete two-seat network room snapshot.");
        }

        NetworkRoomSnapshotHeader header = _observer.LastRoomSnapshot;
        _runtime.ApplyNetworkRoomSnapshot(in header, seats);
    }
}

internal sealed class FrontlinePreMatchOrderGateSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<FrontlineParticipant, OrderBuffer>();

    private readonly FrontlineRuntime _runtime;
    private readonly OrderBufferSystem _orders;

    public FrontlinePreMatchOrderGateSystem(World world, FrontlineRuntime runtime, OrderBufferSystem orders) : base(world)
    {
        _runtime = runtime;
        _orders = orders;
    }

    public override void Update(in float dt)
    {
        if (!_runtime.IsActive || _runtime.CanAdvanceGameplay)
        {
            return;
        }

        foreach (ref Chunk chunk in World.Query(in Query))
        {
            Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (!buffers[index].IsEmpty || buffers[index].HasPending)
                {
                    if (_runtime.IsNetworked)
                    {
                        if (_runtime.Snapshot.Phase == FrontlineMatchPhase.InProgress)
                        {
                            continue;
                        }

                        if (_runtime.Snapshot.Phase == FrontlineMatchPhase.Completed)
                        {
                            _orders.TryCancelAll(Unsafe.Add(ref first, index));
                            continue;
                        }

                        throw new InvalidOperationException(
                            "RTS Frontline network command bypassed the typed Core gameplay command gate.");
                    }

                    _orders.TryCancelAll(Unsafe.Add(ref first, index));
                }
            }
        }
    }
}

internal sealed class FrontlineTagBinder
{
    private readonly FrontlineConfig _config;
    private readonly TagOps _tagOps;
    private readonly int _harvesterTagId;
    private readonly int _infantryTagId;
    private readonly int _crystalNodeTagId;

    public FrontlineTagBinder(FrontlineConfig config, TagOps tagOps)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _tagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
        _harvesterTagId = TagRegistry.Register(config.HarvesterTag);
        _infantryTagId = TagRegistry.Register(config.InfantryTag);
        _crystalNodeTagId = TagRegistry.Register(config.CrystalNodeTag);
    }

    public void BindParticipant(
        World world,
        Entity entity,
        ref FrontlineParticipant participant,
        in Team team,
        ref FrontlineTagBindingState state)
    {
        if (state.IsBound != 0)
        {
            return;
        }

        participant.SideIndex = _config.ResolveSideIndex(team.Id);
        int tagId = world.Has<FrontlineHarvester>(entity)
            ? _harvesterTagId
            : world.Has<FrontlineInfantry>(entity)
                ? _infantryTagId
                : 0;
        if (tagId > 0)
        {
            _tagOps.AddTag(world, entity, tagId);
        }

        state.IsBound = 1;
    }

    public void BindCrystalNode(
        World world,
        Entity entity,
        ref FrontlineTagBindingState state)
    {
        if (state.IsBound != 0)
        {
            return;
        }

        _tagOps.AddTag(world, entity, _crystalNodeTagId);
        state.IsBound = 1;
    }

    public void BindReplicatedEntity(World world, Entity entity)
    {
        if (world.Has<FrontlineCrystalNode>(entity))
        {
            BindCrystalNode(
                world,
                entity,
                ref world.Get<FrontlineTagBindingState>(entity));
            return;
        }

        if (!world.Has<FrontlineParticipant>(entity) || !world.Has<Team>(entity))
        {
            throw new InvalidOperationException("RTS Frontline replicated gameplay entity is missing participant identity.");
        }

        BindParticipant(
            world,
            entity,
            ref world.Get<FrontlineParticipant>(entity),
            in world.Get<Team>(entity),
            ref world.Get<FrontlineTagBindingState>(entity));
    }
}

internal sealed class FrontlineTagBindingSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<FrontlineParticipant, Team, GameplayTagContainer, TagCountContainer, FrontlineTagBindingState>();
    private static readonly QueryDescription NodeQuery = new QueryDescription()
        .WithAll<FrontlineCrystalNode, GameplayTagContainer, TagCountContainer, FrontlineTagBindingState>();

    private readonly FrontlineRuntime _runtime;
    private readonly FrontlineTagBinder _binder;

    public FrontlineTagBindingSystem(
        World world,
        FrontlineRuntime runtime,
        FrontlineTagBinder binder) : base(world)
    {
        _runtime = runtime;
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
    }

    public override void Update(in float dt)
    {
        if (!_runtime.IsActive)
        {
            return;
        }

        foreach (ref Chunk chunk in World.Query(in Query))
        {
            Span<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            Span<Team> teams = chunk.GetSpan<Team>();
            Span<FrontlineTagBindingState> states = chunk.GetSpan<FrontlineTagBindingState>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref first, index);
                _binder.BindParticipant(
                    World,
                    entity,
                    ref participants[index],
                    in teams[index],
                    ref states[index]);
            }
        }

        foreach (ref Chunk chunk in World.Query(in NodeQuery))
        {
            Span<FrontlineTagBindingState> states = chunk.GetSpan<FrontlineTagBindingState>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                _binder.BindCrystalNode(World, Unsafe.Add(ref first, index), ref states[index]);
            }
        }
    }
}

internal sealed class FrontlineTrainingAdmissionSystem : BaseSystem<World, float>, IOrderAdmissionValidator
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<FrontlineCore, FrontlineCoreState, OrderBuffer, AbilityStateBuffer, AttributeBuffer>();

    private readonly FrontlineRuntime _runtime;
    private readonly OrderTypeRegistry _orderTypes;
    private readonly int _castAbilityOrderTypeId;
    private readonly int _trainAbilityId;
    private readonly int _crystalAttributeId;
    private readonly TagOps _tagOps;

    public FrontlineTrainingAdmissionSystem(
        World world,
        FrontlineRuntime runtime,
        OrderTypeRegistry orderTypes,
        TagOps tagOps) : base(world)
    {
        _runtime = runtime;
        _orderTypes = orderTypes;
        _tagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
        _castAbilityOrderTypeId = orderTypes.GetId(runtime.Config.CastAbilityOrderTypeKey);
        _trainAbilityId = AbilityIdRegistry.GetId(runtime.Config.TrainAbilityId);
        _crystalAttributeId = AttributeRegistry.GetId(runtime.Config.CrystalAttribute);
        if (_crystalAttributeId == AttributeRegistry.InvalidId)
        {
            throw new InvalidOperationException(
                $"RTS Frontline crystal attribute '{runtime.Config.CrystalAttribute}' is not registered at mod load.");
        }
        if (_trainAbilityId <= 0)
        {
            throw new InvalidOperationException($"RTS Frontline train ability '{runtime.Config.TrainAbilityId}' is not registered.");
        }
    }

    public override void Update(in float dt)
    {
        if (!_runtime.CanAdvanceGameplay)
        {
            return;
        }

        foreach (ref Chunk chunk in World.Query(in Query))
        {
            Span<FrontlineCoreState> coreStates = chunk.GetSpan<FrontlineCoreState>();
            Span<OrderBuffer> orders = chunk.GetSpan<OrderBuffer>();
            Span<AbilityStateBuffer> abilities = chunk.GetSpan<AbilityStateBuffer>();
            Span<AttributeBuffer> attributes = chunk.GetSpan<AttributeBuffer>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                ref OrderBuffer buffer = ref orders[index];
                if (!buffer.HasActive || buffer.ActiveOrder.Order.OrderTypeId != _castAbilityOrderTypeId)
                {
                    continue;
                }

                Order order = buffer.ActiveOrder.Order;
                int slot = order.Args.I0;
                if (abilities[index].Get(slot).AbilityId != _trainAbilityId ||
                    coreStates[index].LastHandledTrainOrderId == order.OrderId)
                {
                    continue;
                }

                Entity core = Unsafe.Add(ref first, index);
                coreStates[index].LastHandledTrainOrderId = order.OrderId;
                float crystals = attributes[index].GetCurrent(_crystalAttributeId);
                if (crystals < _runtime.Config.TrainCostCrystals)
                {
                    throw new InvalidOperationException(
                        $"Admitted Frontline training order {order.OrderId} has {crystals} crystals, below its reserved cost {_runtime.Config.TrainCostCrystals}.");
                }

                AttributeMutationOps.SetCurrent(
                    World,
                    core,
                    _crystalAttributeId,
                    crystals - _runtime.Config.TrainCostCrystals,
                    _tagOps);
                coreStates[index].LastTrainResult = FrontlineTrainResult.Accepted;
            }
        }
    }

    public OrderSubmitResult Validate(
        World world,
        Entity entity,
        in Order order,
        in OrderBuffer buffer)
    {
        if (order.OrderTypeId != _castAbilityOrderTypeId)
        {
            return OrderSubmitResult.Activated;
        }

        if (!world.IsAlive(entity) || !world.Has<AbilityStateBuffer>(entity))
        {
            return OrderSubmitResult.RejectedInvalidActor;
        }

        AbilityStateBuffer abilities = world.Get<AbilityStateBuffer>(entity);
        int slot = order.Args.I0;
        if (abilities.Get(slot).AbilityId != _trainAbilityId)
        {
            return OrderSubmitResult.Activated;
        }


        if (!world.Has<FrontlineCoreState>(entity) || !world.Has<AttributeBuffer>(entity))
        {
            throw new InvalidOperationException(
                $"Frontline training actor {entity.Id} is missing its core state or crystal attribute buffer.");
        }

        int reservedTrainCount = 0;
        if (buffer.HasPending && IsTrainOrder(in buffer.PendingOrder.Order, in abilities))
        {
            reservedTrainCount++;
        }

        for (int i = 0; i < buffer.QueuedCount; i++)
        {
            Order queued = buffer.GetQueued(i).Order;
            if (IsTrainOrder(in queued, in abilities))
            {
                reservedTrainCount++;
            }
        }

        if (buffer.HasActive && IsTrainOrder(in buffer.ActiveOrder.Order, in abilities))
        {
            FrontlineCoreState state = world.Get<FrontlineCoreState>(entity);
            if (state.LastHandledTrainOrderId != buffer.ActiveOrder.Order.OrderId)
            {
                reservedTrainCount++;
            }
        }

        float crystals = world.Get<AttributeBuffer>(entity).GetCurrent(_crystalAttributeId);
        float availableAfterReservations = crystals -
            (reservedTrainCount * _runtime.Config.TrainCostCrystals);
        return availableAfterReservations >= _runtime.Config.TrainCostCrystals
            ? OrderSubmitResult.Activated
            : OrderSubmitResult.RejectedByRule;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsTrainOrder(in Order order, in AbilityStateBuffer abilities) =>
        order.OrderTypeId == _castAbilityOrderTypeId &&
        abilities.Get(order.Args.I0).AbilityId == _trainAbilityId;
}

internal sealed class FrontlineDeathAndMatchSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription DeathQuery = new QueryDescription()
        .WithAll<FrontlineDeathState, AttributeBuffer>()
        .WithNone<PresentationDestroyPending>();
    private static readonly QueryDescription CoreQuery = new QueryDescription()
        .WithAll<FrontlineCore, FrontlineParticipant, AttributeBuffer>();

    private readonly FrontlineRuntime _runtime;
    private readonly OrderBufferSystem _orders;
    private readonly CommandBuffer _commandBuffer = new();
    private readonly int _healthAttributeId;

    public FrontlineDeathAndMatchSystem(
        World world,
        FrontlineRuntime runtime,
        OrderBufferSystem orders) : base(world)
    {
        _runtime = runtime;
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        _healthAttributeId = AttributeRegistry.Register(runtime.Config.HealthAttribute);
    }

    public override void Update(in float dt)
    {
        if (!_runtime.IsActive)
        {
            return;
        }

        if (_runtime.Snapshot.Outcome == FrontlineMatchOutcome.InProgress)
        {
            if (!_runtime.AdvanceFixedTick())
            {
                return;
            }

            int tick = _runtime.Snapshot.CommittedTick;
            Span<float> coreHealth = stackalloc float[2];
            Span<byte> coreFound = stackalloc byte[2];

            foreach (ref Chunk chunk in World.Query(in CoreQuery))
            {
                ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
                ReadOnlySpan<AttributeBuffer> attributes = chunk.GetSpan<AttributeBuffer>();
                foreach (int index in chunk)
                {
                    int side = participants[index].SideIndex;
                    if ((uint)side >= 2u || coreFound[side] != 0)
                    {
                        throw new InvalidOperationException("RTS Frontline requires exactly one command core per configured side.");
                    }

                    coreFound[side] = 1;
                    coreHealth[side] = attributes[index].GetCurrent(_healthAttributeId);
                }
            }

            if (coreFound[0] == 0 || coreFound[1] == 0)
            {
                throw new InvalidOperationException("RTS Frontline match cannot resolve both command cores.");
            }

            bool sideOneDestroyed = coreHealth[0] <= 0f;
            bool sideTwoDestroyed = coreHealth[1] <= 0f;
            if (sideOneDestroyed || sideTwoDestroyed)
            {
                FrontlineMatchOutcome outcome = sideOneDestroyed && sideTwoDestroyed
                    ? FrontlineMatchOutcome.Draw
                    : sideOneDestroyed
                        ? FrontlineMatchOutcome.SideTwoVictory
                        : FrontlineMatchOutcome.SideOneVictory;
                int winningSideIndex = sideOneDestroyed && sideTwoDestroyed
                    ? -1
                    : sideOneDestroyed ? 1 : 0;
                _runtime.CommitOutcome(new FrontlineMatchResolutionSnapshot(
                    tick,
                    FrontlineMatchResolutionReason.CoreDestroyed,
                    outcome,
                    winningSideIndex,
                    coreHealth[0],
                    coreHealth[1]));
            }
            else if (_runtime.IsDisconnectedPastGrace(0) || _runtime.IsDisconnectedPastGrace(1))
            {
                bool oneDisconnectedPastGrace = _runtime.IsDisconnectedPastGrace(0);
                bool twoDisconnectedPastGrace = _runtime.IsDisconnectedPastGrace(1);
                FrontlineMatchOutcome outcome = oneDisconnectedPastGrace && twoDisconnectedPastGrace
                    ? FrontlineMatchOutcome.Draw
                    : oneDisconnectedPastGrace
                        ? FrontlineMatchOutcome.SideTwoVictory
                        : FrontlineMatchOutcome.SideOneVictory;
                int winningSideIndex = oneDisconnectedPastGrace && twoDisconnectedPastGrace
                    ? -1
                    : oneDisconnectedPastGrace ? 1 : 0;
                _runtime.CommitOutcome(new FrontlineMatchResolutionSnapshot(
                    tick,
                    FrontlineMatchResolutionReason.Disconnect,
                    outcome,
                    winningSideIndex,
                    coreHealth[0],
                    coreHealth[1]));
            }
            else if (_runtime.HasDurationCoreHealthSnapshot || tick >= _runtime.Config.MatchDurationTicks)
            {
                _runtime.CaptureDurationCoreHealth(coreHealth[0], coreHealth[1]);
                if (!_runtime.HasParticipantAwaitingReconnect)
                {
                    _runtime.CommitDurationOutcome();
                }
            }
        }
        foreach (ref Chunk chunk in World.Query(in DeathQuery))
        {
            Span<FrontlineDeathState> deaths = chunk.GetSpan<FrontlineDeathState>();
            ReadOnlySpan<AttributeBuffer> attributes = chunk.GetSpan<AttributeBuffer>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (deaths[index].DestroyQueued != 0 || attributes[index].GetCurrent(_healthAttributeId) > 0f)
                {
                    continue;
                }

                Entity entity = Unsafe.Add(ref first, index);
                if (World.Has<OrderBuffer>(entity) && !_orders.TryCancelAll(entity))
                {
                    continue;
                }

                _commandBuffer.Add(entity, new PresentationDestroyPending());
                deaths[index].DestroyQueued = 1;
            }
        }

        if (_commandBuffer.Size > 0)
        {
            _commandBuffer.Playback(World);
        }
    }
}

internal sealed class FrontlinePresentationSystem : ISystem<float>
{
    private static readonly QueryDescription ReplicatedMatchStateQuery = new QueryDescription()
        .WithAll<FrontlineMatchStateEntity, FrontlineMatchStateProjection, ReplicationSchemaRef, ReplicationMirrorIdentity>();
    private static readonly QueryDescription OpeningCameraTargetQuery = new QueryDescription()
        .WithAll<FrontlineCore, PlayerOwner, WorldPositionCm, VisualTransform, ReplicationMirrorIdentity, PresentationStableId>();

    private static readonly Vector4 PanelFill = new(0.035f, 0.055f, 0.07f, 0.88f);
    private static readonly Vector4 PanelBorder = new(0.23f, 0.58f, 0.62f, 0.95f);
    private static readonly Vector4 Title = new(0.93f, 0.96f, 0.94f, 1f);
    private static readonly Vector4 Text = new(0.75f, 0.85f, 0.82f, 1f);
    private static readonly Vector4 Accent = new(0.96f, 0.77f, 0.32f, 1f);

    private readonly FrontlineRuntime _runtime;
    private readonly GameEngine _engine;

    private int RequireLocalPlayerId()
    {
        ClientLocalSeatRegistry seats = ClientLocalSeatAccess.RequireRegistry(_engine);
        return seats.TryGetSoleSeat(out ClientLocalSeat seat) ? seat.PossessedPlayerId : 0;
    }
    private readonly World _world;
    private readonly bool _isReplicatedClient;
    private readonly int _matchStateSchemaId;
    private readonly ScreenOverlayBuffer? _overlay;
    private FrontlineMatchPhase _cachedPhase = (FrontlineMatchPhase)byte.MaxValue;
    private int _cachedCountdownSeconds = -1;
    private byte _cachedLobbyState = byte.MaxValue;
    private string _roomStatusText = string.Empty;
    private string _sideStatusText = string.Empty;
    private FrontlineMatchOutcome _cachedOutcome = (FrontlineMatchOutcome)byte.MaxValue;
    private string _outcomeText = string.Empty;
    private IReplicatedClientRuntimeStatus? _clientStatus;
    private IReplicatedClientCommandPort? _clientCommands;
    private NetworkRuntimeStateObserver? _networkObserver;
    private string _connectionStatusText = string.Empty;
    private string _opponentStatusText = string.Empty;
    private string _commandStatusText = string.Empty;
    private int _dynamicDirtySerial = 1;
    private bool _hasPublishedDynamicPayload;
    private string _publishedRoomStatusText = string.Empty;
    private string _publishedSideStatusText = string.Empty;
    private string _publishedConnectionStatusText = string.Empty;
    private string _publishedOpponentStatusText = string.Empty;
    private string _publishedCommandStatusText = string.Empty;
    public FrontlinePresentationSystem(GameEngine engine, FrontlineRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _world = engine.World;
        _isReplicatedClient = engine.GetService(CoreServiceKeys.NetworkProcessRole) == NetworkProcessRole.ReplicatedClient;
        _matchStateSchemaId = runtime.Config.Replication.MatchStateSchemaId;
        _overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        HandleReadyInput();
        if (!_runtime.IsActive)
        {
            return;
        }
        FocusOpeningCameraOnce();
        if (_overlay == null)
        {
            return;
        }

        FrontlineHudConfig hud = _runtime.Config.Hud;
        FrontlineMatchSnapshot snapshot = default;
        bool isSynchronizingBattlefield = false;
        if (_isReplicatedClient && !TryResolvePresentationSnapshot(out snapshot))
        {
            if (!TryResolveRoomLobbySnapshot(out snapshot, out isSynchronizingBattlefield))
            {
                snapshot = CreateConnectingSnapshot();
            }
        }
        else if (!_isReplicatedClient)
        {
            snapshot = _runtime.Snapshot;
        }
        RefreshLobbyText(in snapshot, hud);
        string visibleRoomStatusText = isSynchronizingBattlefield
            ? hud.SynchronizingBattlefieldText
            : _roomStatusText;
        RefreshNetworkStatus(in snapshot, hud);
        RefreshCommandStatus(hud);
        RefreshDynamicDirtySerial(visibleRoomStatusText);
        int dynamicDirtySerial = _dynamicDirtySerial;
        FrontlineHudLayoutConfig layout = hud.Layout;
        int statusX = layout.X + layout.Padding;
        int statusY = layout.Y + layout.Padding - 2;
        int instructionX = layout.X + layout.InstructionColumnX;
        int instructionY = layout.Y + layout.Padding;
        _overlay.AddRect(
            layout.X,
            layout.Y,
            layout.Width,
            layout.Height,
            PanelFill,
            PanelBorder,
            stableId: 71400,
            dirtySerial: 1);
        _overlay.AddText(statusX, statusY, hud.Title, layout.TitleFontSize, Title, stableId: 71401, dirtySerial: 1);
        statusY += layout.TitleFontSize + 6;
        _overlay.AddText(
            statusX,
            statusY,
            visibleRoomStatusText,
            layout.StatusFontSize,
            Accent,
            stableId: 71402,
            dirtySerial: dynamicDirtySerial);
        statusY += layout.LineHeight;
        _overlay.AddText(instructionX, instructionY, hud.Objective, layout.StatusFontSize, Accent, stableId: 71404, dirtySerial: 1);
        instructionY += layout.TitleFontSize + 6;
        _overlay.AddText(instructionX, instructionY, hud.GatherHint, layout.BodyFontSize, Text, stableId: 71405, dirtySerial: 1);
        instructionY += layout.LineHeight;
        _overlay.AddText(instructionX, instructionY, hud.TrainHint, layout.BodyFontSize, Text, stableId: 71406, dirtySerial: 1);
        instructionY += layout.LineHeight;
        _overlay.AddText(instructionX, instructionY, hud.AttackHint, layout.BodyFontSize, Text, stableId: 71407, dirtySerial: 1);
        if (snapshot.Phase is FrontlineMatchPhase.WaitingForPlayers or FrontlineMatchPhase.Countdown)
        {
            _overlay.AddText(statusX, statusY, hud.ReadyHint, layout.BodyFontSize, Accent, stableId: 71408, dirtySerial: 1);
            statusY += layout.LineHeight;
            if (_sideStatusText.Length > 0)
            {
                _overlay.AddText(statusX, statusY, _sideStatusText, layout.BodyFontSize, Text, stableId: 71403, dirtySerial: dynamicDirtySerial);
                statusY += layout.LineHeight;
            }
            if (_connectionStatusText.Length > 0)
            {
                _overlay.AddText(statusX, statusY, _connectionStatusText, layout.BodyFontSize, Accent, stableId: 71412, dirtySerial: dynamicDirtySerial);
                statusY += layout.LineHeight;
            }
            if (_opponentStatusText.Length > 0)
            {
                _overlay.AddText(statusX, statusY, _opponentStatusText, layout.BodyFontSize, Accent, stableId: 71413, dirtySerial: dynamicDirtySerial);
            }
        }
        else
        {
            if (_commandStatusText.Length > 0)
            {
                _overlay.AddText(statusX, statusY, _commandStatusText, layout.BodyFontSize, Accent, stableId: 71414, dirtySerial: dynamicDirtySerial);
                statusY += layout.LineHeight;
            }
            if (_connectionStatusText.Length > 0)
            {
                _overlay.AddText(statusX, statusY, _connectionStatusText, layout.BodyFontSize, Accent, stableId: 71415, dirtySerial: dynamicDirtySerial);
                statusY += layout.LineHeight;
            }
            if (_opponentStatusText.Length > 0)
            {
                _overlay.AddText(statusX, statusY, _opponentStatusText, layout.BodyFontSize, Accent, stableId: 71416, dirtySerial: dynamicDirtySerial);
            }
        }

        FrontlineMatchOutcome outcome = snapshot.Outcome;
        if (outcome != FrontlineMatchOutcome.InProgress)
        {
            if (_cachedOutcome != outcome)
            {
                _cachedOutcome = outcome;
                _outcomeText = outcome switch
                {
                    FrontlineMatchOutcome.SideOneVictory => hud.SideOneVictoryText,
                    FrontlineMatchOutcome.SideTwoVictory => hud.SideTwoVictoryText,
                    _ => hud.DrawText,
                };
            }

            int outcomeY = layout.Y + layout.Height + layout.OutcomeGap;
            _overlay.AddRect(
                layout.X,
                outcomeY,
                layout.OutcomeWidth,
                layout.OutcomeHeight,
                PanelFill,
                Accent,
                stableId: 71410,
                dirtySerial: (int)outcome);
            _overlay.AddText(
                statusX,
                outcomeY + layout.Padding,
                _outcomeText,
                layout.TitleFontSize,
                Accent,
                stableId: 71411,
                dirtySerial: (int)outcome);
        }
    }

    private void FocusOpeningCameraOnce()
    {
        MapSession? session = _engine.CurrentMapSession;
        FrontlineOpeningViewSnapshot openingView = _runtime.OpeningView;
        if (!_isReplicatedClient || openingView.IsReady || session == null ||
            !string.Equals(session.MapId.Value, _runtime.Config.MapId, StringComparison.Ordinal))
        {
            return;
        }

        int localPlayerId = RequireLocalPlayerId();
        if (localPlayerId <= 0)
        {
            return;
        }
        if (localPlayerId != _runtime.Config.Sides[0].PlayerId &&
            localPlayerId != _runtime.Config.Sides[1].PlayerId)
        {
            throw new InvalidOperationException(
                $"RTS Frontline opening camera cannot resolve player {localPlayerId} to a configured side.");
        }

        int ownedCoreCount = 0;
        Entity ownedCore = Entity.Null;
        foreach (ref Chunk chunk in _world.Query(in OpeningCameraTargetQuery))
        {
            ReadOnlySpan<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (owners[index].PlayerId != localPlayerId)
                {
                    continue;
                }

                ownedCoreCount++;
                ownedCore = Unsafe.Add(ref first, index);
            }
        }
        if (ownedCoreCount == 0)
        {
            return;
        }
        if (ownedCoreCount != 1)
        {
            throw new InvalidOperationException(
                $"RTS Frontline opening camera found {ownedCoreCount} owned core mirrors for player {localPlayerId}; expected one.");
        }

        CameraCullingDebugState culling = _engine.GetService(CoreServiceKeys.CameraCullingDebugState)
            ?? throw new InvalidOperationException(
                "RTS Frontline opening camera requires camera-culling diagnostics.");
        if (!openingView.HasFocusTarget)
        {
            CameraPoseRequest request = _engine.GetService(CoreServiceKeys.CameraPoseRequest)
                ?? throw new InvalidOperationException(
                    "RTS Frontline opening camera requires the RTS command-source focus request to run first.");
            if (!request.TargetCm.HasValue ||
                !float.IsFinite(request.TargetCm.Value.X) ||
                !float.IsFinite(request.TargetCm.Value.Y))
            {
                throw new InvalidOperationException(
                    "RTS Frontline opening camera requires a finite RTS command-source focus target.");
            }

            CameraConfig? defaultCamera = session.MapConfig.DefaultCamera;
            if (defaultCamera?.TargetXCm is not float defaultTargetXCm ||
                defaultCamera.TargetYCm is not float defaultTargetYCm)
            {
                throw new InvalidOperationException(
                    "RTS Frontline opening camera requires a configured default camera target.");
            }
            Vector2 defaultTargetCm = new(defaultTargetXCm, defaultTargetYCm);
            if (TargetsMatch(request.TargetCm.Value, defaultTargetCm))
            {
                throw new InvalidOperationException(
                    "RTS Frontline command-source focus target must differ from the map's default camera target.");
            }

            _runtime.CaptureOpeningFocusTarget(request.TargetCm.Value, culling.VisibilityRevision);
            return;
        }

        Vector2 focusTargetCm = openingView.FocusTargetCm;
        if (!TargetsMatch(ClientLocalSeatAccess.RequireSolePresentCamera(_engine).State.TargetCm, focusTargetCm) ||
            !TargetsMatch(culling.CameraTargetCm, focusTargetCm) ||
            culling.VisibilityRevision <= openingView.CapturedVisibilityRevision ||
            culling.VisibleEntityCount <= 0 ||
            !_world.TryGet(ownedCore, out CullState coreCull) ||
            !coreCull.IsVisible ||
            !_world.TryGet(ownedCore, out PresentationOwnerHasPresenterPayload payload) ||
            payload.Count <= 0)
        {
            return;
        }

        _runtime.MarkOpeningViewReady(culling.VisibilityRevision);
    }

    private static bool TargetsMatch(Vector2 actual, Vector2 expected) =>
        Vector2.DistanceSquared(actual, expected) <= 1f;

    internal FrontlineMatchSnapshot ResolvePresentationSnapshot()
    {
        if (!_isReplicatedClient)
        {
            return _runtime.Snapshot;
        }

        if (!TryResolvePresentationSnapshot(out FrontlineMatchSnapshot snapshot))
        {
            throw new InvalidOperationException(
                "RTS Frontline replicated client requires exactly one live match-state mirror; found 0.");
        }

        return snapshot;
    }

    private bool TryResolvePresentationSnapshot(out FrontlineMatchSnapshot snapshot)
    {
        int count = 0;
        FrontlineMatchStateProjection projection = default;
        foreach (ref Chunk chunk in _world.Query(in ReplicatedMatchStateQuery))
        {
            ReadOnlySpan<FrontlineMatchStateProjection> projections = chunk.GetSpan<FrontlineMatchStateProjection>();
            ReadOnlySpan<ReplicationSchemaRef> schemas = chunk.GetSpan<ReplicationSchemaRef>();
            foreach (int index in chunk)
            {
                if (schemas[index].SchemaId != _matchStateSchemaId)
                {
                    throw new InvalidOperationException(
                        $"RTS Frontline client match-state mirror uses schema {schemas[index].SchemaId}; expected {_matchStateSchemaId}.");
                }
                projection = projections[index];
                count++;
            }
        }

        if (count != 1)
        {
            if (count == 0)
            {
                snapshot = default;
                return false;
            }

            throw new InvalidOperationException(
                $"RTS Frontline replicated client requires exactly one live match-state mirror; found {count}.");
        }
        snapshot = projection.ToSnapshot();
        return true;
    }

    private bool TryResolveRoomLobbySnapshot(
        out FrontlineMatchSnapshot snapshot,
        out bool isSynchronizingBattlefield)
    {
        NetworkRuntimeStateObserver observer = _engine.GetService(CoreServiceKeys.NetworkRuntimeStateObserver)
            ?? throw new InvalidOperationException("RTS Frontline replicated client requires the Core network room observer.");
        if (!observer.HasRoomSnapshot)
        {
            snapshot = default;
            isSynchronizingBattlefield = false;
            return false;
        }

        NetworkRoomSnapshotHeader header = observer.LastRoomSnapshot;
        Span<NetworkRoomSeatSnapshot> seats = stackalloc NetworkRoomSeatSnapshot[2];
        if (!observer.TryCopyRoomSeats(seats, out int seatCount) || seatCount != seats.Length)
        {
            throw new InvalidOperationException("RTS Frontline client room snapshot does not contain exactly two seats.");
        }

        isSynchronizingBattlefield = header.Phase == NetworkRoomPhase.Started;
        snapshot = new FrontlineMatchSnapshot(
            checked((int)header.CommittedTick),
            header.Phase switch
            {
                NetworkRoomPhase.Countdown => FrontlineMatchPhase.Countdown,
                NetworkRoomPhase.Started => FrontlineMatchPhase.InProgress,
                _ => FrontlineMatchPhase.WaitingForPlayers,
            },
            checked((int)header.CountdownRemainingTicks),
            FrontlineMatchOutcome.InProgress,
            WinningSideIndex: -1,
            seats[0].ReadyState == NetworkRoomReadyState.Ready,
            seats[1].ReadyState == NetworkRoomReadyState.Ready,
            seats[0].ConnectionState == NetworkRoomSeatConnectionState.Connected,
            seats[1].ConnectionState == NetworkRoomSeatConnectionState.Connected);
        return true;
    }

    private static FrontlineMatchSnapshot CreateConnectingSnapshot() => new(
        CommittedTick: 0,
        FrontlineMatchPhase.WaitingForPlayers,
        CountdownRemainingTicks: 0,
        FrontlineMatchOutcome.InProgress,
        WinningSideIndex: -1,
        SideOneReady: false,
        SideTwoReady: false,
        SideOneConnected: false,
        SideTwoConnected: false);

    private void HandleReadyInput()
    {
        if (!_isReplicatedClient || !_runtime.IsActive)
        {
            return;
        }

        IInputActionReader input = _engine.GetService(CoreServiceKeys.AuthoritativeInput)
            ?? throw new InvalidOperationException("RTS Frontline Ready control requires authoritative input.");
        if (!input.PressedThisFrame(_runtime.Config.ReadyActionId))
        {
            return;
        }

        NetworkRuntimeStateObserver observer = _engine.GetService(CoreServiceKeys.NetworkRuntimeStateObserver)
            ?? throw new InvalidOperationException("RTS Frontline Ready control requires the Core network room observer.");
        int localPlayerId = RequireLocalPlayerId();
        if (!observer.HasRoomSnapshot || localPlayerId <= 0 || observer.LastRoomSnapshot.Phase == NetworkRoomPhase.Started)
        {
            return;
        }

        Span<NetworkRoomSeatSnapshot> seats = stackalloc NetworkRoomSeatSnapshot[2];
        if (!observer.TryCopyRoomSeats(seats, out int seatCount) || seatCount != seats.Length)
        {
            throw new InvalidOperationException("RTS Frontline Ready control could not read the complete room snapshot.");
        }

        int localSeat = -1;
        for (int i = 0; i < seats.Length; i++)
        {
            if (seats[i].PlayerId.Value == localPlayerId)
            {
                localSeat = i;
                break;
            }
        }

        if (localSeat < 0 || seats[localSeat].ConnectionState != NetworkRoomSeatConnectionState.Connected)
        {
            throw new InvalidOperationException("RTS Frontline Ready control could not resolve the connected local room seat.");
        }

        IReplicatedClientRoomControlPort roomControl = _engine.GetService(CoreServiceKeys.ReplicatedClientRoomControlPort)
            ?? throw new InvalidOperationException("RTS Frontline Ready control requires the replicated-client room port.");
        bool ready = seats[localSeat].ReadyState == NetworkRoomReadyState.Ready;
        if (!roomControl.TrySetRoomReady(!ready))
        {
            throw new InvalidOperationException("RTS Frontline Ready intent was rejected by the connected room port.");
        }
    }

    private void RefreshLobbyText(in FrontlineMatchSnapshot snapshot, FrontlineHudConfig hud)
    {
        int countdownSeconds = snapshot.Phase == FrontlineMatchPhase.Countdown
            ? Math.Max(1, (snapshot.CountdownRemainingTicks + _runtime.Config.SimulationTickRateHz - 1) / _runtime.Config.SimulationTickRateHz)
            : 0;
        byte lobbyState = (byte)(
            (snapshot.SideOneReady ? 1 : 0) |
            (snapshot.SideTwoReady ? 2 : 0) |
            (snapshot.SideOneConnected ? 4 : 0) |
            (snapshot.SideTwoConnected ? 8 : 0));
        if (_cachedPhase == snapshot.Phase && _cachedCountdownSeconds == countdownSeconds && _cachedLobbyState == lobbyState)
        {
            return;
        }

        _cachedPhase = snapshot.Phase;
        _cachedCountdownSeconds = countdownSeconds;
        _cachedLobbyState = lobbyState;
        _roomStatusText = snapshot.Phase switch
        {
            FrontlineMatchPhase.WaitingForPlayers => hud.WaitingText,
            FrontlineMatchPhase.Countdown => $"{hud.CountdownText} {countdownSeconds}",
            FrontlineMatchPhase.InProgress => hud.BattleStartedText,
            _ => string.Empty,
        };
        _sideStatusText = snapshot.Phase is FrontlineMatchPhase.WaitingForPlayers or FrontlineMatchPhase.Countdown
            ? $"{_runtime.Config.Sides[0].DisplayName}: {ResolveLobbyState(snapshot.SideOneConnected, snapshot.SideOneReady, hud)}    " +
              $"{_runtime.Config.Sides[1].DisplayName}: {ResolveLobbyState(snapshot.SideTwoConnected, snapshot.SideTwoReady, hud)}"
            : string.Empty;
    }

    private static string ResolveLobbyState(bool connected, bool ready, FrontlineHudConfig hud) =>
        !connected ? hud.DisconnectedText : ready ? hud.ReadyText : hud.NotReadyText;

    private void RefreshDynamicDirtySerial(string visibleRoomStatusText)
    {
        if (_hasPublishedDynamicPayload &&
            _publishedRoomStatusText == visibleRoomStatusText &&
            _publishedSideStatusText == _sideStatusText &&
            _publishedConnectionStatusText == _connectionStatusText &&
            _publishedOpponentStatusText == _opponentStatusText &&
            _publishedCommandStatusText == _commandStatusText)
        {
            return;
        }

        if (_hasPublishedDynamicPayload)
        {
            _dynamicDirtySerial = _dynamicDirtySerial == int.MaxValue
                ? 1
                : _dynamicDirtySerial + 1;
        }

        _hasPublishedDynamicPayload = true;
        _publishedRoomStatusText = visibleRoomStatusText;
        _publishedSideStatusText = _sideStatusText;
        _publishedConnectionStatusText = _connectionStatusText;
        _publishedOpponentStatusText = _opponentStatusText;
        _publishedCommandStatusText = _commandStatusText;
    }

    private void RefreshNetworkStatus(in FrontlineMatchSnapshot snapshot, FrontlineHudConfig hud)
    {
        if (!_isReplicatedClient)
        {
            _connectionStatusText = string.Empty;
            _opponentStatusText = string.Empty;
            return;
        }

        EnsureClientFeedbackServices();
        IReplicatedClientRuntimeStatus status = _clientStatus!;
        if (status.IsFaulted ||
            status.ConnectionState == ReplicatedClientConnectionState.RecoveryRejected ||
            status.ConnectionState == ReplicatedClientConnectionState.Rejected ||
            (status.HasEstablishedSession &&
             status.ConnectionState != ReplicatedClientConnectionState.Connected &&
             status.ReconnectWindowRemainingSeconds <= 0f))
        {
            _connectionStatusText = hud.ServiceInterruptedText;
        }
        else if (status.ConnectionState != ReplicatedClientConnectionState.Connected || status.IsAwaitingFullSnapshot)
        {
            _connectionStatusText = status.HasEstablishedSession
                ? $"{hud.ReconnectingText} {Math.Max(0, (int)MathF.Ceiling(status.ReconnectWindowRemainingSeconds))}s"
                : hud.ConnectingText;
        }
        else
        {
            _connectionStatusText = status.RoundTripTimeMilliseconds >= hud.DelayedRoundTripThresholdMilliseconds
                ? hud.DelayedConnectionText
                : hud.SmoothConnectionText;
        }

        int localPlayerId = RequireLocalPlayerId();
        int localSide = localPlayerId == _runtime.Config.Sides[0].PlayerId
            ? 0
            : localPlayerId == _runtime.Config.Sides[1].PlayerId
                ? 1
                : -1;
        if (localSide < 0)
        {
            if (!status.HasEstablishedSession)
            {
                _opponentStatusText = string.Empty;
                return;
            }

            throw new InvalidOperationException("RTS Frontline HUD cannot resolve the local player's configured side.");
        }

        bool opponentConnected = localSide == 0 ? snapshot.SideTwoConnected : snapshot.SideOneConnected;
        _opponentStatusText = snapshot.Phase == FrontlineMatchPhase.InProgress && !opponentConnected
            ? hud.OpponentOfflineText
            : string.Empty;
    }

    private void RefreshCommandStatus(FrontlineHudConfig hud)
    {
        if (!_isReplicatedClient)
        {
            _commandStatusText = string.Empty;
            return;
        }

        EnsureClientFeedbackServices();
        IReplicatedClientCommandPort commands = _clientCommands!;
        if (commands.SubmissionRevision == 0)
        {
            _commandStatusText = string.Empty;
            return;
        }

        if (commands.LastSubmitResult != ReplicatedClientCommandSubmitResult.Submitted)
        {
            _commandStatusText = hud.ResolveSubmitRejection(commands.LastSubmitResult);
            return;
        }

        NetworkRuntimeStateObserver observer = _networkObserver!;
        if (!observer.TryGetClientAdmission(
                commands.LastSubmittedBatchSequence,
                out NetworkCommandAdmissionOutcome admission))
        {
            _commandStatusText = hud.CommandSendingText;
            return;
        }

        _commandStatusText = admission.Stage switch
        {
            NetworkCommandAdmissionStage.NetworkIntake when admission.Result == NetworkCommandAdmissionCode.NetworkScheduled =>
                hud.CommandSendingText,
            NetworkCommandAdmissionStage.GlobalIntake when admission.Result == NetworkCommandAdmissionCode.Queued =>
                hud.CommandAcceptedText,
            NetworkCommandAdmissionStage.EntityIntake when admission.Result == NetworkCommandAdmissionCode.Activated =>
                hud.CommandStartedText,
            NetworkCommandAdmissionStage.EntityIntake when admission.Result == NetworkCommandAdmissionCode.Queued =>
                hud.CommandQueuedText,
            NetworkCommandAdmissionStage.EntityIntake when admission.Result == NetworkCommandAdmissionCode.Pending =>
                hud.CommandPendingText,
            NetworkCommandAdmissionStage.Terminal when admission.Result == NetworkCommandAdmissionCode.TerminalCompleted =>
                hud.CommandCompletedText,
            _ => hud.ResolveAdmissionRejection(admission.Result),
        };
    }

    private void EnsureClientFeedbackServices()
    {
        if (_clientStatus != null && _clientCommands != null && _networkObserver != null)
        {
            return;
        }

        INetworkRuntimePort runtimePort = _engine.GetService(CoreServiceKeys.NetworkRuntimePort)
            ?? throw new InvalidOperationException("RTS Frontline client HUD requires the Core network runtime port.");
        _clientStatus = runtimePort as IReplicatedClientRuntimeStatus
            ?? throw new InvalidOperationException("RTS Frontline client HUD requires platform-neutral connection status.");

        _ = _clientStatus.ConnectionState;
        _clientCommands = _engine.GetService(CoreServiceKeys.ReplicatedClientCommandPort)
            ?? throw new InvalidOperationException("RTS Frontline client HUD requires the Core command feedback port.");
        _networkObserver = _engine.GetService(CoreServiceKeys.NetworkRuntimeStateObserver)
            ?? throw new InvalidOperationException("RTS Frontline client HUD requires the Core network state observer.");
    }
}

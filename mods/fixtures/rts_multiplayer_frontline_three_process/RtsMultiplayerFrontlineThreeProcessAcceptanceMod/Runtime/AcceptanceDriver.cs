using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.ActionLoops;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Diagnostics;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using RtsMultiplayerFrontlineMod.Runtime;

namespace RtsMultiplayerFrontlineThreeProcessAcceptanceMod.Runtime;

internal sealed class AcceptanceDriver : ISystem<float>
{
    private const string FrontlineRuntimeContextKey = "rts.multiplayer.frontline.runtime";

    private static readonly QueryDescription ClientCoreQuery = new QueryDescription()
        .WithAll<FrontlineCore, FrontlineParticipant, PlayerOwner, WorldPositionCm, VisualTransform, AttributeBuffer, ReplicationMirrorIdentity, PresentationStableId>();
    private static readonly QueryDescription ClientHarvesterQuery = new QueryDescription()
        .WithAll<FrontlineHarvester, FrontlineParticipant, PlayerOwner, WorldPositionCm, VisualTransform, ReplicationMirrorIdentity>();
    private static readonly QueryDescription ClientInfantryQuery = new QueryDescription()
        .WithAll<FrontlineInfantry, FrontlineParticipant, PlayerOwner, WorldPositionCm, VisualTransform, AttributeBuffer, ReplicationMirrorIdentity, PresentationStableId>();
    private static readonly QueryDescription ClientCrystalQuery = new QueryDescription()
        .WithAll<FrontlineCrystalNode, WorldPositionCm, VisualTransform, ReplicationMirrorIdentity>();
    private static readonly QueryDescription ClientMatchQuery = new QueryDescription()
        .WithAll<FrontlineMatchStateEntity, FrontlineMatchStateProjection, ReplicationMirrorIdentity>();
    private static readonly QueryDescription ServerCoreQuery = new QueryDescription()
        .WithAll<FrontlineCore, FrontlineParticipant, WorldPositionCm, AttributeBuffer>();
    private static readonly QueryDescription ServerInfantryQuery = new QueryDescription()
        .WithAll<FrontlineInfantry, FrontlineParticipant>();
    private static readonly QueryDescription PresentationFrameQuery = new QueryDescription()
        .WithAll<PresentationFrameState, PresentationFrameStateTag>();

    private readonly GameEngine _engine;
    private readonly World _world;
    private readonly AcceptancePlan _plan;
    private readonly FrontlineConfig _frontline;
    private readonly AcceptanceProgress _progress;
    private readonly AcceptanceEvidence _evidence;
    private readonly string _evidencePath;
    private readonly NetworkProcessRole _role;
    private readonly FrontlineRuntime _frontlineRuntime;
    private readonly NetworkRuntimeStateObserver _observer;
    private readonly INetworkFaultInjectionMetricsPort _faultInjectionMetrics;
    private readonly int _crystalAttributeId;
    private readonly int _healthAttributeId;
    private readonly long _startedTimestamp = Stopwatch.GetTimestamp();
    private readonly Entity[] _entityScratch = new Entity[32];
    private readonly Entity[] _selectionTargets = new Entity[32];
    private readonly Entity[] _commandActors = new Entity[32];
    private readonly Entity[] _preservedCommandActors = new Entity[32];
    private readonly List<AcceptanceAdmissionTransitionEvidence> _pendingAdmissionHistory = new();
    private readonly List<AcceptanceAdmissionTransitionEvidence> _preservedAdmissionHistory = new();
    private readonly NetworkCommandAdmissionOutcome[] _admissionProgressScratch;

    private PlayerInputHandler? _input;
    private InteractionActionBindings? _bindings;
    private AuthoritativeGroundPointerOverride? _groundOverride;
    private IReplicatedClientCommandPort? _commandPort;
    private IReplicatedClientRuntimeStatus? _clientStatus;
    private EntityCollectionStore? _collections;
    private IScreenProjector? _projector;
    private PresentationFrameReceiptBuffer? _presentationReceipts;
    private InputOrderMappingSystem? _inputOrderMapping;
    private int _infantryBodyTemplateId;
    private GestureState _gesture;
    private ClientStage _clientStage = ClientStage.Connecting;
    private int _substep;
    private int _localPlayerId;
    private int _localSideIndex = -1;
    private int _selectionTargetCount;
    private int _selectionTargetIndex;
    private int _commandActorCount;
    private int _preservedCommandActorCount;
    private float _gatherCrystalsBeforeCommand;
    private string _pendingCommandAction = string.Empty;
    private ulong _pendingCommandSequence;
    private string _preservedCommandAction = string.Empty;
    private ulong _preservedCommandSequence;
    private long _stageStartedTimestamp;
    private long _meetingReachedTimestamp;
    private long _lastEvidenceCheckpointTimestamp;
    private bool _serverInitializedGameplay;
    private bool _battlePointsDerived;
    private bool _hasLosingCoreLastPosition;
    private bool _terminal;
    private int _completionWorldReadyFrameId = -1;
    private ServerCoreTeardownStage _serverCoreTeardownStage;
    private long _serverCoreTeardownStartedTimestamp;
    private Entity _trackedHarvester = Entity.Null;
    private Entity _attackTarget = Entity.Null;
    private WorldCmInt2 _meetingPoint;
    private WorldCmInt2 _siegePoint;
    private WorldCmInt2 _losingCoreLastPosition;

    public AcceptanceDriver(
        GameEngine engine,
        AcceptancePlan plan,
        FrontlineConfig frontline,
        AcceptanceProgress progress)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _world = engine.World;
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _frontline = frontline ?? throw new ArgumentNullException(nameof(frontline));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _role = engine.GetService(CoreServiceKeys.NetworkProcessRole);
        if (_role is not NetworkProcessRole.ReplicatedClient and not NetworkProcessRole.AuthoritativeServer)
        {
            throw new InvalidOperationException("Three-process acceptance requires a network client or authoritative server role.");
        }

        _observer = engine.GetService(CoreServiceKeys.NetworkRuntimeStateObserver)
            ?? throw new InvalidOperationException("Three-process acceptance requires the network runtime observer.");
        _faultInjectionMetrics = engine.GetService(CoreServiceKeys.NetworkFaultInjectionMetrics)
            ?? throw new InvalidOperationException("Three-process acceptance requires network fault injection metrics.");
        _admissionProgressScratch = new NetworkCommandAdmissionOutcome[
            _observer.ClientAdmissionProgressCapacityPerBatch];
        if (!engine.GlobalContext.TryGetValue(FrontlineRuntimeContextKey, out object? runtimeValue) ||
            runtimeValue is not FrontlineRuntime frontlineRuntime)
        {
            throw new InvalidOperationException("Three-process acceptance requires the installed Frontline runtime.");
        }
        _frontlineRuntime = frontlineRuntime;
        _crystalAttributeId = AttributeRegistry.GetId(frontline.CrystalAttribute);
        _healthAttributeId = AttributeRegistry.GetId(frontline.HealthAttribute);
        if (_crystalAttributeId == AttributeRegistry.InvalidId || _healthAttributeId == AttributeRegistry.InvalidId)
        {
            throw new InvalidOperationException("Frontline acceptance attributes were not registered before the driver installed.");
        }

        (string? planFingerprint, string contentFingerprint) = AcceptanceContentIdentity.Resolve(engine);
        _evidence = new AcceptanceEvidence
        {
            Role = _role == NetworkProcessRole.AuthoritativeServer ? "authoritativeServer" : "replicatedClient",
            PlanFingerprint = planFingerprint,
            ContentFingerprint = contentFingerprint,
        };
        _evidencePath = AcceptanceEvidenceWriter.ResolvePath(plan);
        _stageStartedTimestamp = _startedTimestamp;
        StartStep(_role == NetworkProcessRole.AuthoritativeServer ? "AuthoritativeMatch" : ClientStage.Connecting.ToString());

    }

    public void Initialize()
    {
    }

    public void Update(in float dt)
    {
        if (_terminal)
        {
            return;
        }

        try
        {
            if (ElapsedSeconds(_startedTimestamp) > _plan.OverallTimeoutSeconds)
            {
                throw new TimeoutException(
                    $"Three-process acceptance exceeded {_plan.OverallTimeoutSeconds} seconds in role {_evidence.Role}.");
            }

            if (_observer.FaultCount != 0)
            {
                throw new InvalidOperationException(
                    $"Network runtime reported {_observer.FaultCount} fault(s); last={_observer.LastFault.Code}.");
            }

            if (_role == NetworkProcessRole.AuthoritativeServer)
            {
                UpdateServer();
            }
            else
            {
                UpdateClient();
            }

            if (!_terminal)
            {
                WriteRunningCheckpointIfDue();
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
            throw;
        }
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }

    private void BindClientServices()
    {
        if (_inputOrderMapping != null)
        {
            return;
        }

        PlayerInputHandler input = _engine.GetService(CoreServiceKeys.InputHandler)
            ?? throw new InvalidOperationException("Acceptance client requires PlayerInputHandler.");
        InteractionActionBindings bindings = _engine.GetService(CoreServiceKeys.InteractionActionBindings)
            ?? throw new InvalidOperationException("Acceptance client requires interaction action bindings.");
        AuthoritativeGroundPointerOverride groundOverride = _engine.GetService(CoreServiceKeys.AuthoritativeGroundPointerOverride)
            ?? throw new InvalidOperationException("Acceptance client requires authoritative ground pointer override.");
        IReplicatedClientCommandPort commandPort = _engine.GetService(CoreServiceKeys.ReplicatedClientCommandPort)
            ?? throw new InvalidOperationException("Acceptance client requires the platform-neutral replicated command port.");
        INetworkRuntimePort runtimePort = _engine.GetService(CoreServiceKeys.NetworkRuntimePort)
            ?? throw new InvalidOperationException("Acceptance client requires the network runtime port.");
        IReplicatedClientRuntimeStatus clientStatus = runtimePort as IReplicatedClientRuntimeStatus
            ?? throw new InvalidOperationException("Acceptance client runtime does not expose connection status.");
        EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("Acceptance client requires the entity collection store.");
        IScreenProjector projector = _engine.GetService(CoreServiceKeys.ScreenProjector)
            ?? throw new InvalidOperationException("Acceptance client requires the platform-neutral screen projector.");
        PresentationFrameReceiptBuffer presentationReceipts =
            _engine.GetService(CoreServiceKeys.PresentationFrameReceiptBuffer)
            ?? throw new InvalidOperationException(
                "Acceptance client requires presentation frame receipts for framebuffer readiness.");
        PerformerDefinitionRegistry performerDefinitions =
            _engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
            ?? throw new InvalidOperationException(
                "Acceptance client requires performer definitions for framebuffer readiness.");
        int infantryBodyTemplateId = performerDefinitions.GetId("rts.frontline.infantry.body");
        if (infantryBodyTemplateId <= 0)
        {
            throw new InvalidOperationException(
                "Acceptance client cannot resolve the frontline infantry body performer definition.");
        }
        InputOrderMappingSystem inputOrderMapping = _engine.GetService(CoreServiceKeys.ActiveInputOrderMapping)
            ?? throw new InvalidOperationException("Acceptance client requires the active input-order mapping system.");

        _input = input;
        _bindings = bindings;
        _groundOverride = groundOverride;
        _commandPort = commandPort;
        _clientStatus = clientStatus;
        _collections = collections;
        _projector = projector;
        _presentationReceipts = presentationReceipts;
        _inputOrderMapping = inputOrderMapping;
        _infantryBodyTemplateId = infantryBodyTemplateId;

        RequireInputAction(bindings.ConfirmActionId);
        RequireInputAction(bindings.CommandActionId);
        RequireInputAction(bindings.PointerPositionActionId);
        RequireInputAction(_frontline.ReadyActionId);
        RequireInputAction("SkillQ");
        RequireInputAction(CommandSourceModifierActionIds.Additive);
    }

    private void UpdateClient()
    {
        BindClientServices();
        TrackClientObservations();
        TrackInFlightAdmissionProgress();
        EnforceClientStageTimeout();
        switch (_clientStage)
        {
            case ClientStage.Connecting:
                UpdateConnecting();
                break;
            case ClientStage.Ready:
                UpdateReady();
                break;
            case ClientStage.Gathering:
                UpdateGathering();
                break;
            case ClientStage.Training:
                UpdateTraining();
                break;
            case ClientStage.Advancing:
                UpdateAdvancing();
                break;
            case ClientStage.Engaging:
                UpdateEngaging();
                break;
            case ClientStage.WaitingForOutcome:
                UpdateWaitingForOutcome();
                break;
            default:
                throw new InvalidOperationException($"Unsupported acceptance client stage {_clientStage}.");
        }

    }

    private void UpdateConnecting()
    {
        SessionHandshakeResponse handshake = _observer.LastClientHandshake;
        if (!handshake.Accepted || handshake.SessionEpoch.IsEmpty ||
            !_clientStatus!.HasEstablishedSession ||
            _clientStatus.ConnectionState != ReplicatedClientConnectionState.Connected ||
            _clientStatus.IsAwaitingFullSnapshot ||
            !_observer.HasRoomSnapshot)
        {
            return;
        }

        if (!string.Equals(handshake.ContentFingerprint.ToHexString(), _evidence.ContentFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Client handshake content fingerprint differs from locally canonicalized content.");
        }

        int localPlayerId = _engine.GetService(CoreServiceKeys.LocalPlayerId);
        if (localPlayerId <= 0 ||
            !_engine.TryGetService(CoreServiceKeys.LocalPlayerEntity, out Entity localPlayer) ||
            !_world.IsAlive(localPlayer))
        {
            return;
        }
        if (localPlayerId != handshake.PlayerId.Value)
        {
            throw new InvalidOperationException(
                $"Local player {localPlayerId} differs from handshake player {handshake.PlayerId.Value}.");
        }

        int sideIndex = _frontline.ResolveSideIndexForPlayer(localPlayerId);
        if (!TryResolveOwnCore(localPlayerId, out Entity core) ||
            CountOwned<ClientHarvesterMarker>(localPlayerId) != _plan.Expected.InitialHarvesterCount ||
            CountOwned<ClientInfantryMarker>(localPlayerId) != _plan.Expected.InitialInfantryCount ||
            CountClientCrystals() != 2 ||
            !TryGetClientMatchState(out _))
        {
            return;
        }

        float crystals = ReadAttribute(core, _crystalAttributeId);
        if (!Approximately(crystals, _plan.Expected.InitialCrystals))
        {
            throw new InvalidOperationException(
                $"Player {localPlayerId} started with {crystals} crystals; expected {_plan.Expected.InitialCrystals}.");
        }
        int visibleEnemyInfantry = CountVisibleEnemyInfantry(sideIndex);
        int visibleEnemyCores = CountVisibleEnemyCores(sideIndex);
        if (visibleEnemyInfantry != 0 || visibleEnemyCores != 0)
        {
            throw new InvalidOperationException(
                $"Initial fog projection leaked enemy infantry={visibleEnemyInfantry}, cores={visibleEnemyCores}.");
        }

        if (!BothSeatsConnected())
        {
            return;
        }

        FrontlineOpeningViewSnapshot openingView = _frontlineRuntime.OpeningView;
        if (!openingView.HasFocusTarget || !openingView.IsReady)
        {
            return;
        }
        CameraCullingDebugState culling = _engine.GetService(CoreServiceKeys.CameraCullingDebugState)
            ?? throw new InvalidOperationException(
                "Acceptance client requires camera-culling diagnostics for opening-view readiness.");
        if (!CameraTargetMatches(_engine.GameSession.Camera.State.TargetCm, openingView.FocusTargetCm) ||
            !CameraTargetMatches(culling.CameraTargetCm, openingView.FocusTargetCm) ||
            culling.VisibilityRevision < openingView.ReadyVisibilityRevision ||
            culling.VisibleEntityCount <= 0)
        {
            return;
        }

        if (!TryResolveOwnHarvester(localPlayerId, out Entity openingHarvester) ||
            !TryResolveNearestCrystal(openingHarvester, out Entity openingCrystal) ||
            !AreOpeningEntitiesVisible(localPlayerId, core, openingCrystal) ||
            !TryProjectEntityClick(openingCrystal, out _))
        {
            return;
        }

        CaptureSeats(requireBothConnected: true);
        _localPlayerId = localPlayerId;
        _localSideIndex = sideIndex;
        _evidence.SessionEpoch = handshake.SessionEpoch.Value;
        _evidence.PlayerId = localPlayerId;
        _evidence.SeatSlot = handshake.Seat.Slot;
        _evidence.SideIndex = sideIndex;
        _evidence.Gameplay.InitialCrystals = (int)MathF.Round(crystals);
        _evidence.Gameplay.InitialInfantryCount = _plan.Expected.InitialInfantryCount;
        _evidence.Gameplay.InitialVisibleEnemyInfantryCount = visibleEnemyInfantry;
        _evidence.Gameplay.InitialVisibleEnemyCoreCount = visibleEnemyCores;
        CompleteStep("Full snapshot, local player binding, and a clickable opening crystal field are ready.");
        Transition(ClientStage.Ready, AcceptanceProgressStage.Ready);
    }

    private void UpdateReady()
    {
        if (_substep == 0)
        {
            BeginButtonGesture(_frontline.ReadyActionId, expectsCommand: false, "Ready");
            _substep = 1;
            return;
        }
        if (_substep == 1)
        {
            if (!AdvanceGesture())
            {
                return;
            }
            _substep = 2;
        }

        if (!BothSeatsReady() || !TryGetClientMatchState(out FrontlineMatchStateProjection match) ||
            match.Phase != FrontlineMatchPhase.InProgress)
        {
            return;
        }

        CompleteStep("Both commanders readied through input and the match reached InProgress.");
        Transition(ClientStage.Gathering, AcceptanceProgressStage.Gathering);
    }

    private void UpdateGathering()
    {
        if (_substep == 0)
        {
            if (!TryResolveOwnHarvester(_localPlayerId, out _trackedHarvester))
            {
                return;
            }
            _evidence.Gameplay.HarvesterHandle = FormatHandle(_trackedHarvester);
            _evidence.Gameplay.HarvesterStartPosition = CapturePosition(_trackedHarvester);
            ref readonly ResourceTransportProfile transport = ref _world.Get<ResourceTransportProfile>(_trackedHarvester);
            float gatherDelta = _plan.Expected.HarvestedCrystals - _plan.Expected.InitialCrystals;
            float configuredTrips = gatherDelta / transport.CargoAmount;
            if (gatherDelta <= 0f || !Approximately(configuredTrips, MathF.Round(configuredTrips)))
            {
                throw new InvalidOperationException(
                    $"Gathering target {_plan.Expected.HarvestedCrystals} is not reachable from " +
                    $"{_plan.Expected.InitialCrystals} in configured cargo increments of {transport.CargoAmount}.");
            }
            _gatherCrystalsBeforeCommand = _plan.Expected.InitialCrystals;
            BeginEntitySelection(_trackedHarvester, additive: false);
            _substep = 1;
            return;
        }
        if (_substep == 1)
        {
            if (!AdvanceGesture())
            {
                return;
            }
            RequireSelectedExactly(_trackedHarvester);
            BeginGatherCommand();
            _substep = 2;
            return;
        }
        if (_substep == 2)
        {
            if (!AdvanceGesture())
            {
                return;
            }
            _substep = 3;
        }
        if (_substep == 3)
        {
            if (!TryCompletePendingCommand())
            {
                return;
            }
            _substep = 4;
        }

        if (!TryResolveOwnCore(_localPlayerId, out Entity core))
        {
            return;
        }
        float crystals = ReadAttribute(core, _crystalAttributeId);
        ref readonly ResourceTransportProfile profile = ref _world.Get<ResourceTransportProfile>(_trackedHarvester);
        float expectedAfterCargo = _gatherCrystalsBeforeCommand + profile.CargoAmount;
        if (crystals < expectedAfterCargo)
        {
            return;
        }
        if (!Approximately(crystals, expectedAfterCargo))
        {
            throw new InvalidOperationException(
                $"Gathering changed crystals from {_gatherCrystalsBeforeCommand} to {crystals}; " +
                $"expected one configured cargo of {profile.CargoAmount}.");
        }
        if (crystals < _plan.Expected.HarvestedCrystals)
        {
            _gatherCrystalsBeforeCommand = crystals;
            BeginGatherCommand();
            _substep = 2;
            return;
        }
        if (!Approximately(crystals, _plan.Expected.HarvestedCrystals))
        {
            throw new InvalidOperationException(
                $"Gathering produced {crystals} crystals; expected exactly {_plan.Expected.HarvestedCrystals}.");
        }

        AcceptancePositionEvidence end = CapturePosition(_trackedHarvester);
        AcceptancePositionEvidence start = _evidence.Gameplay.HarvesterStartPosition!;
        if (DistanceCm(start, end) < _plan.Battle.MinimumObservedMoveCm)
        {
            throw new InvalidOperationException("Gather command completed without the required harvester movement.");
        }
        _evidence.Gameplay.HarvestedCrystals = (int)MathF.Round(crystals);
        _evidence.Gameplay.HarvesterEndPosition = end;
        CompleteStep("A selected harvester completed the configured cargo runs and returned the required crystals to the core.");
        Transition(ClientStage.Training, AcceptanceProgressStage.Training);
    }

    private void UpdateTraining()
    {
        if (!TryResolveOwnCore(_localPlayerId, out Entity core))
        {
            return;
        }
        int observedInfantryCount = CountOwned<ClientInfantryMarker>(_localPlayerId);
        int firstTrainedInfantryCount = checked(_plan.Expected.InitialInfantryCount + 1);
        int secondTrainedInfantryCount = _plan.Expected.TrainedInfantryCount;
        if (_evidence.Gameplay.FirstTrainedInfantryObservedCommittedTick < 0 &&
            observedInfantryCount >= firstTrainedInfantryCount)
        {
            if (observedInfantryCount != firstTrainedInfantryCount)
            {
                throw new InvalidOperationException(
                    $"Client first observed trained infantry count {observedInfantryCount}; " +
                    $"expected exactly {firstTrainedInfantryCount} before the second training completed.");
            }
            _evidence.Gameplay.FirstTrainedInfantryObservedCommittedTick =
                RequireClientAuthoritativeSnapshotTick("first trained infantry");
            _evidence.Gameplay.FirstTrainedInfantryObservedCount = observedInfantryCount;
        }
        if (_evidence.Gameplay.SecondTrainedInfantryObservedCommittedTick < 0 &&
            observedInfantryCount >= secondTrainedInfantryCount)
        {
            if (observedInfantryCount != secondTrainedInfantryCount)
            {
                throw new InvalidOperationException(
                    $"Client first observed completed training count {observedInfantryCount}; " +
                    $"expected exactly {secondTrainedInfantryCount}.");
            }
            _evidence.Gameplay.SecondTrainedInfantryObservedCommittedTick =
                RequireClientAuthoritativeSnapshotTick("second trained infantry");
            _evidence.Gameplay.SecondTrainedInfantryObservedCount = observedInfantryCount;
        }
        if (_substep == 0)
        {
            BeginEntitySelection(core, additive: false);
            _substep = 1;
            return;
        }
        if (_substep == 1)
        {
            if (!AdvanceGesture())
            {
                return;
            }
            RequireSelectedExactly(core);
            BeginButtonGesture("SkillQ", expectsCommand: true, "TrainInfantry");
            _substep = 2;
            return;
        }
        if (_substep == 2)
        {
            if (!AdvanceGesture())
            {
                return;
            }
            RequireTargetlessTrainingCommittedWithoutAiming();
            _substep = 3;
        }
        if (_substep == 3)
        {
            if (!TryPreserveActivatedPendingCommand())
            {
                return;
            }
            BeginButtonGesture(
                "SkillQ",
                expectsCommand: true,
                "QueueTrainInfantry",
                queueModifier: true);
            _substep = 4;
            return;
        }
        if (_substep == 4)
        {
            if (!AdvanceGesture())
            {
                return;
            }
            RequireTargetlessTrainingCommittedWithoutAiming();
            _substep = 5;
        }
        if (_substep == 5)
        {
            if (!TryCompletePreservedCommand())
            {
                return;
            }
            ValidateTrainingAdmission(requireEntityQueue: false);
            _substep = 6;
        }
        if (_substep == 6)
        {
            if (!TryCompletePendingCommand())
            {
                return;
            }
            _substep = 7;
        }
        if (_substep == 7)
        {
            if (_evidence.Gameplay.SecondTrainedInfantryObservedCommittedTick <= 0)
            {
                return;
            }
            ValidateTrainingAdmission(requireEntityQueue: true);
            _substep = 8;
        }

        float crystals = ReadAttribute(core, _crystalAttributeId);
        int infantryCount = observedInfantryCount;
        if (infantryCount < _plan.Expected.TrainedInfantryCount)
        {
            return;
        }
        if (infantryCount != _plan.Expected.TrainedInfantryCount ||
            !Approximately(crystals, _plan.Expected.PostTrainingCrystals))
        {
            throw new InvalidOperationException(
                $"Training ended with crystals={crystals}, infantry={infantryCount}; expected " +
                $"{_plan.Expected.PostTrainingCrystals} and {_plan.Expected.TrainedInfantryCount}.");
        }
        int producedInfantry = infantryCount - _plan.Expected.InitialInfantryCount;
        float spentCrystals = _plan.Expected.HarvestedCrystals - crystals;
        float configuredSpend = producedInfantry * _frontline.TrainCostCrystals;
        if (producedInfantry != 2 || !Approximately(spentCrystals, configuredSpend))
        {
            throw new InvalidOperationException(
                $"Queued training produced {producedInfantry} infantry and spent {spentCrystals} crystals; " +
                $"expected two infantry at {_frontline.TrainCostCrystals} crystals each.");
        }

        _evidence.Gameplay.PostTrainingCrystals = (int)MathF.Round(crystals);
        _evidence.Gameplay.TrainedInfantryCount = infantryCount;
        CompleteStep("The selected core completed one immediate and one queued training order with exact replicated costs and units.");
        Transition(ClientStage.Advancing, AcceptanceProgressStage.Training);
    }

    private void UpdateAdvancing()
    {
        if (_substep == 0)
        {
            int requested = _localSideIndex == _plan.Expected.WinningSideIndex
                ? _plan.Expected.TrainedInfantryCount
                : _plan.Expected.LoserAttackers;
            if (!PrepareOwnedInfantrySelection(requested))
            {
                return;
            }
            _substep = 1;
        }
        if (_substep == 1)
        {
            if (!AdvancePreparedSelection())
            {
                return;
            }
            _evidence.Gameplay.SelectedInfantryHandles = CaptureSelectedHandles();
            _evidence.Gameplay.MoveStartPositions = CaptureSelectedPositions();
            DeriveBattlePoints();
            RequestCameraFocus(_meetingPoint);
            _substep = 2;
            return;
        }
        if (_substep == 2)
        {
            if (!TryBeginFocusedGroundCommand("MoveToMeeting", _meetingPoint))
            {
                return;
            }
            _substep = 3;
            return;
        }
        if (_substep == 3)
        {
            if (!AdvanceGesture())
            {
                return;
            }
            _substep = 4;
        }
        if (_substep == 4)
        {
            if (!TryCompletePendingCommand())
            {
                return;
            }
            _substep = 5;
        }
        if (!HaveAllSelectedActorsMoved(_plan.Battle.MinimumObservedMoveCm) ||
            !AreSelectedActorsNear(_meetingPoint, _plan.Battle.ArrivalToleranceCm) ||
            !AreSelectedActorsVisibleWithPerformerPayload() ||
            !AreMovedActorsOnscreenInPresentationReceipts())
        {
            return;
        }
        if (_progress.Stage == AcceptanceProgressStage.Training)
        {
            _progress.TransitionTo(AcceptanceProgressStage.Advancing, "Selected infantry are visibly advancing.");
            return;
        }
        if (_progress.Stage != AcceptanceProgressStage.Advancing)
        {
            throw new InvalidOperationException(
                $"Advancing player evidence requires progress stage Advancing; observed {_progress.Stage}.");
        }
        _evidence.Gameplay.MoveEndPositions = CaptureSelectedPositions();
        _meetingReachedTimestamp = Stopwatch.GetTimestamp();
        CompleteStep("Selected infantry moved to a meeting point derived from the two public crystal fields.");
        Transition(ClientStage.Engaging, AcceptanceProgressStage.Advancing);
    }

    private void UpdateEngaging()
    {
        if (_localSideIndex == _plan.Expected.WinningSideIndex)
        {
            UpdateWinningClientEngagement();
        }
        else
        {
            UpdateLosingClientEngagement();
        }
    }

    private void UpdateLosingClientEngagement()
    {
        if (_substep == 0)
        {
            if (!TryResolveVisibleEnemyInfantry(out _attackTarget))
            {
                return;
            }
            _evidence.Gameplay.EnemyInfantryEnteredVision = true;
            _evidence.Gameplay.AttackTargetHandle = FormatHandle(_attackTarget);
            _evidence.Gameplay.AttackTargetPositionBefore = CapturePosition(_attackTarget);
            _evidence.Gameplay.AttackTargetHealthBefore = ReadAttribute(_attackTarget, _healthAttributeId);
            BeginEntityCommand("AttackEnemyInfantry", _attackTarget);
            _substep = 1;
            return;
        }
        if (_substep == 1)
        {
            if (!AdvanceGesture())
            {
                return;
            }
            _substep = 2;
        }
        if (_substep == 2)
        {
            if (!TryCompletePendingCommand())
            {
                return;
            }
            _substep = 3;
        }

        if (!_world.IsAlive(_attackTarget))
        {
            return;
        }
        float health = ReadAttribute(_attackTarget, _healthAttributeId);
        if (health >= _evidence.Gameplay.AttackTargetHealthBefore)
        {
            return;
        }
        _evidence.Gameplay.AttackTargetHealthAfter = health;

        CompleteStep("The southern commander attacked a visible opposing infantry unit and reduced its health.");
        Transition(ClientStage.WaitingForOutcome, AcceptanceProgressStage.Engaging);
    }

    private void UpdateWinningClientEngagement()
    {
        if (_substep == 0)
        {
            if (!TryResolveVisibleEnemyInfantry(out _) ||
                ElapsedSeconds(_meetingReachedTimestamp) < _plan.Battle.WinnerHoldAtMeetingSeconds)
            {
                return;
            }

            if (!TryRequireExpectedWinnerSurvivorSelection())
            {
                return;
            }
            _evidence.Gameplay.EnemyInfantryEnteredVision = true;
            _evidence.Gameplay.SelectedInfantryHandles = CaptureSelectedHandles();
            RequestCameraFocus(_siegePoint);
            _substep = 1;
            return;
        }
        if (_substep == 1)
        {
            if (!TryRequireExpectedWinnerSurvivorSelection())
            {
                return;
            }
            if (!TryBeginFocusedGroundCommand("MoveToSiege", _siegePoint))
            {
                return;
            }
            _substep = 2;
            return;
        }
        if (_substep == 2)
        {
            if (!AdvanceGesture())
            {
                return;
            }
            _substep = 3;
        }
        if (_substep == 3)
        {
            if (!TryCompletePendingCommand())
            {
                return;
            }
            _substep = 4;
        }
        if (_substep == 4)
        {
            if (!AreSelectedActorsNear(_siegePoint, _plan.Battle.ArrivalToleranceCm))
            {
                return;
            }
            int liveCount = CollectOwnedInfantry(_localPlayerId, _selectionTargets);
            if (liveCount < _plan.Expected.WinnerMinimumAttackers)
            {
                throw new InvalidOperationException(
                    $"Winning side retained {liveCount} attackers; expected at least {_plan.Expected.WinnerMinimumAttackers}.");
            }
            SortClientEntitiesByHandle(_selectionTargets, liveCount);
            RequireSelectedSet(_selectionTargets.AsSpan(0, liveCount));
            _evidence.Gameplay.SelectedInfantryHandles = CaptureSelectedHandles();
            _substep = 5;
        }
        if (_substep == 5)
        {
            if (!TryResolveVisibleEnemyCore(out _attackTarget))
            {
                return;
            }
            _evidence.Gameplay.EnemyCoreEnteredVision = true;
            _evidence.Gameplay.AttackTargetHandle = FormatHandle(_attackTarget);
            _evidence.Gameplay.AttackTargetPositionBefore = CapturePosition(_attackTarget);
            _evidence.Gameplay.AttackTargetHealthBefore = ReadAttribute(_attackTarget, _healthAttributeId);
            _evidence.Gameplay.SelectedInfantryHandles = CaptureSelectedHandles();
            BeginEntityCommand("AttackEnemyCore", _attackTarget);
            _substep = 6;
            return;
        }
        if (_substep == 6)
        {
            if (!AdvanceGesture())
            {
                return;
            }
            _substep = 7;
        }
        if (_substep == 7)
        {
            if (!TryCompletePendingCommand())
            {
                return;
            }
            _substep = 8;
        }

        if (!_world.IsAlive(_attackTarget))
        {
            if (!TryGetClientMatchState(out FrontlineMatchStateProjection completed) ||
                completed.Phase != FrontlineMatchPhase.Completed)
            {
                throw new InvalidOperationException(
                    "The attacked enemy core disappeared before the replicated match state reached Completed.");
            }
            _evidence.Gameplay.AttackTargetHealthAfter = 0f;
            CompleteStep("The northern commander destroyed the revealed enemy core.");
            Transition(ClientStage.WaitingForOutcome, AcceptanceProgressStage.Engaging);
            return;
        }
        float health = ReadAttribute(_attackTarget, _healthAttributeId);
        if (health >= _evidence.Gameplay.AttackTargetHealthBefore)
        {
            return;
        }
        _evidence.Gameplay.AttackTargetHealthAfter = health;

        CompleteStep("The northern commander revealed the enemy core, attacked it, and reduced its health.");
        Transition(ClientStage.WaitingForOutcome, AcceptanceProgressStage.Engaging);
    }

    private bool TryRequireExpectedWinnerSurvivorSelection()
    {
        int expectedSurvivors = _plan.Expected.TrainedInfantryCount -
            _plan.Expected.WinnerCasualtiesBeforeSiege;
        int liveCount = CollectOwnedInfantry(_localPlayerId, _selectionTargets);
        if (liveCount > expectedSurvivors)
        {
            return false;
        }
        if (liveCount < expectedSurvivors)
        {
            throw new InvalidOperationException(
                $"Winning side retained {liveCount} infantry before siege selection; expected exactly {expectedSurvivors} after replicated casualties.");
        }

        SortClientEntitiesByHandle(_selectionTargets, liveCount);
        RequireSelectedSet(_selectionTargets.AsSpan(0, liveCount));
        return true;
    }

    private void UpdateWaitingForOutcome()
    {
        if (!TryGetClientMatchState(out FrontlineMatchStateProjection match) ||
            match.Phase != FrontlineMatchPhase.Completed)
        {
            return;
        }
        if (match.WinningSideIndex != _plan.Expected.WinningSideIndex ||
            match.Outcome != OutcomeForWinningSide(_plan.Expected.WinningSideIndex))
        {
            throw new InvalidOperationException(
                $"Client observed outcome={match.Outcome}, winner={match.WinningSideIndex}; expected side {_plan.Expected.WinningSideIndex}.");
        }

        if (!_hasLosingCoreLastPosition || _evidence.Gameplay.DefeatedCoreLastPosition == null)
        {
            throw new InvalidOperationException(
                "Client reached the final replicated result without ever observing the defeated core position.");
        }

        int losingSide = 1 - _plan.Expected.WinningSideIndex;
        if (_substep == 0)
        {
            RequestCameraFocus(_losingCoreLastPosition);
            _substep = 1;
            return;
        }
        if (_substep == 1)
        {
            Vector2 cameraTarget = _engine.GameSession.Camera.State.TargetCm;
            var roundedCameraTarget = new WorldCmInt2(
                checked((int)MathF.Round(cameraTarget.X)),
                checked((int)MathF.Round(cameraTarget.Y)));
            long cameraToleranceSquared =
                (long)_plan.Battle.CompletionCameraToleranceCm * _plan.Battle.CompletionCameraToleranceCm;
            if (DistanceSquared(roundedCameraTarget, _losingCoreLastPosition) > cameraToleranceSquared)
            {
                return;
            }

            int losingCoreCount = CountClientCores(losingSide);
            if (losingCoreCount != 0)
            {
                return;
            }
            AcceptancePositionEvidence[] witnesses = CaptureInfantryNear(
                _plan.Expected.WinningSideIndex,
                _losingCoreLastPosition,
                _plan.Battle.CompletionWitnessRadiusCm);
            if (witnesses.Length < _plan.Battle.MinimumCompletionWinnerInfantry)
            {
                return;
            }

            _evidence.Gameplay.CompletedCameraTarget = new AcceptanceWorldPointEvidence
            {
                XCm = roundedCameraTarget.X,
                YCm = roundedCameraTarget.Y,
            };
            _evidence.Gameplay.CompletedLosingCoreCount = losingCoreCount;
            _evidence.Gameplay.CompletedWinnerInfantryNearDefeatedCoreCount = witnesses.Length;
            _evidence.Gameplay.CompletedWinnerInfantryNearDefeatedCorePositions = witnesses;
            _completionWorldReadyFrameId = GetPresentationFrameId();
            _substep = 2;
            return;
        }
        if (GetPresentationFrameId() <= _completionWorldReadyFrameId)
        {
            return;
        }

        Vector2 finalCameraTarget = _engine.GameSession.Camera.State.TargetCm;
        var roundedFinalCameraTarget = new WorldCmInt2(
            checked((int)MathF.Round(finalCameraTarget.X)),
            checked((int)MathF.Round(finalCameraTarget.Y)));
        long finalCameraToleranceSquared =
            (long)_plan.Battle.CompletionCameraToleranceCm * _plan.Battle.CompletionCameraToleranceCm;
        if (DistanceSquared(roundedFinalCameraTarget, _losingCoreLastPosition) > finalCameraToleranceSquared)
        {
            throw new InvalidOperationException("Completion camera moved away from the defeated core before capture.");
        }
        int finalLosingCoreCount = CountClientCores(losingSide);
        if (finalLosingCoreCount != 0)
        {
            throw new InvalidOperationException("The defeated core mirror reappeared before completion capture.");
        }
        AcceptancePositionEvidence[] finalWitnesses = CaptureInfantryNear(
            _plan.Expected.WinningSideIndex,
            _losingCoreLastPosition,
            _plan.Battle.CompletionWitnessRadiusCm);
        if (finalWitnesses.Length < _plan.Battle.MinimumCompletionWinnerInfantry)
        {
            throw new InvalidOperationException("Winning infantry left the defeated core area before completion capture.");
        }

        _evidence.Gameplay.MatchPhase = match.Phase.ToString();
        _evidence.Gameplay.Outcome = match.Outcome.ToString();
        _evidence.Gameplay.OutcomeSource = "replicated-match-state";
        _evidence.Gameplay.WinningSideIndex = match.WinningSideIndex;
        _evidence.Gameplay.CommittedTick = match.CommittedTick;
        _evidence.Gameplay.CommittedTickSource = "replicated-match-state";
        _evidence.Gameplay.CompletedCameraTarget = new AcceptanceWorldPointEvidence
        {
            XCm = roundedFinalCameraTarget.X,
            YCm = roundedFinalCameraTarget.Y,
        };
        _evidence.Gameplay.CompletedLosingCoreCount = finalLosingCoreCount;
        _evidence.Gameplay.CompletedWinnerInfantryNearDefeatedCoreCount = finalWitnesses.Length;
        _evidence.Gameplay.CompletedWinnerInfantryNearDefeatedCorePositions = finalWitnesses;
        _evidence.Gameplay.CompletedPresentationFrameId = GetPresentationFrameId();
        CompleteStep("Both clients framed the defeated core position after its mirror disappeared, with winning infantry still present.");
        Pass();
    }

    private void UpdateServer()
    {
        if (!_observer.HasRoomSnapshot)
        {
            return;
        }

        NetworkRoomSnapshotHeader room = _observer.LastRoomSnapshot;
        _evidence.SessionEpoch = room.SessionEpoch.Value;
        CaptureSeats(requireBothConnected: false);
        Span<float> coreHealth = stackalloc float[2];
        Span<int> coreCount = stackalloc int[2];
        Span<int> pendingCoreDestroyCount = stackalloc int[2];
        Span<int> infantryCount = stackalloc int[2];
        foreach (ref Chunk chunk in _world.Query(in ServerCoreQuery))
        {
            ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            ReadOnlySpan<AttributeBuffer> attributes = chunk.GetSpan<AttributeBuffer>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                int side = participants[index].SideIndex;
                if ((uint)side >= 2u)
                {
                    throw new InvalidOperationException($"Server observed undeclared core side {side}.");
                }
                coreCount[side]++;
                coreHealth[side] = attributes[index].GetCurrent(_healthAttributeId);
                if (_world.Has<PresentationDestroyPending>(Unsafe.Add(ref first, index)))
                {
                    pendingCoreDestroyCount[side]++;
                }
            }
        }
        foreach (ref Chunk chunk in _world.Query(in ServerInfantryQuery))
        {
            ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            foreach (int index in chunk)
            {
                int side = participants[index].SideIndex;
                if ((uint)side < 2u)
                {
                    infantryCount[side]++;
                }
            }
        }
        FrontlineMatchSnapshot match = _frontlineRuntime.Snapshot;
        if (coreCount[0] == 0 && coreCount[1] == 0 && match.Phase != FrontlineMatchPhase.Completed)
        {
            return;
        }
        if (match.Phase != FrontlineMatchPhase.Completed && (coreCount[0] != 1 || coreCount[1] != 1))
        {
            throw new InvalidOperationException(
                $"Server requires one live core per side before completion; observed {coreCount[0]} and {coreCount[1]}.");
        }

        if (!_serverInitializedGameplay)
        {
            if (match.Phase == FrontlineMatchPhase.Completed)
            {
                throw new InvalidOperationException(
                    "Server acceptance reached completion before observing the authoritative opening state.");
            }
            if (!Approximately(ReadServerCoreCrystals(0), _plan.Expected.InitialCrystals) ||
                !Approximately(ReadServerCoreCrystals(1), _plan.Expected.InitialCrystals) ||
                infantryCount[0] != _plan.Expected.InitialInfantryCount ||
                infantryCount[1] != _plan.Expected.InitialInfantryCount)
            {
                throw new InvalidOperationException("Server authoritative opening economy or infantry count differs from the acceptance plan.");
            }
            _serverInitializedGameplay = true;
        }

        for (int side = 0; side < 2; side++)
        {
            int firstTrainedInfantryCount = checked(_plan.Expected.InitialInfantryCount + 1);
            if (_evidence.Gameplay.FirstTrainedInfantrySpawnCommittedTickBySide[side] < 0 &&
                infantryCount[side] >= firstTrainedInfantryCount)
            {
                if (infantryCount[side] != firstTrainedInfantryCount)
                {
                    throw new InvalidOperationException(
                        $"Server first observed side {side} trained infantry count {infantryCount[side]}; " +
                        $"expected exactly {firstTrainedInfantryCount}.");
                }
                _evidence.Gameplay.FirstTrainedInfantrySpawnCommittedTickBySide[side] =
                    RequireServerAuthoritativeExecutingTick(side, "first trained infantry");
            }

            int secondTrainedInfantryCount = _plan.Expected.TrainedInfantryCount;
            if (_evidence.Gameplay.SecondTrainedInfantrySpawnCommittedTickBySide[side] < 0 &&
                infantryCount[side] >= secondTrainedInfantryCount)
            {
                if (infantryCount[side] != secondTrainedInfantryCount)
                {
                    throw new InvalidOperationException(
                        $"Server first observed side {side} completed training count {infantryCount[side]}; " +
                        $"expected exactly {secondTrainedInfantryCount}.");
                }
                _evidence.Gameplay.SecondTrainedInfantrySpawnCommittedTickBySide[side] =
                    RequireServerAuthoritativeExecutingTick(side, "second trained infantry");
            }
        }

        _evidence.Gameplay.ObservedInfantryCountBySide[0] = infantryCount[0];
        _evidence.Gameplay.ObservedInfantryCountBySide[1] = infantryCount[1];

        if (match.Phase != FrontlineMatchPhase.Completed)
        {
            _evidence.Gameplay.ObservedCoreHealthBySide[0] = coreHealth[0];
            _evidence.Gameplay.ObservedCoreHealthBySide[1] = coreHealth[1];
            return;
        }
        if (room.Phase != NetworkRoomPhase.Started)
        {
            throw new InvalidOperationException(
                $"Authoritative Frontline match completed while the network room phase was {room.Phase}.");
        }
        CaptureSeats(requireBothConnected: true);

        if (_evidence.Gameplay.FirstTrainedInfantrySpawnCommittedTickBySide[0] <= 0 ||
            _evidence.Gameplay.FirstTrainedInfantrySpawnCommittedTickBySide[1] <= 0 ||
            _evidence.Gameplay.SecondTrainedInfantrySpawnCommittedTickBySide[0] <= 0 ||
            _evidence.Gameplay.SecondTrainedInfantrySpawnCommittedTickBySide[1] <= 0)
        {
            throw new InvalidOperationException(
                "Authoritative completion lacks the first or second trained infantry spawn committed tick for both sides.");
        }

        if (match.WinningSideIndex != _plan.Expected.WinningSideIndex ||
            match.Outcome != OutcomeForWinningSide(_plan.Expected.WinningSideIndex))
        {
            throw new InvalidOperationException(
                $"Authoritative Frontline outcome={match.Outcome}, winner={match.WinningSideIndex}; " +
                $"expected side {_plan.Expected.WinningSideIndex}.");
        }

        FrontlineMatchResolutionSnapshot resolution = _frontlineRuntime.Resolution;
        int losingSide = _plan.Expected.WinningSideIndex == 0 ? 1 : 0;
        int winningSide = _plan.Expected.WinningSideIndex;
        if (resolution.Reason != FrontlineMatchResolutionReason.CoreDestroyed ||
            resolution.CommittedTick != match.CommittedTick ||
            resolution.Outcome != match.Outcome ||
            resolution.WinningSideIndex != match.WinningSideIndex)
        {
            throw new InvalidOperationException(
                "Authoritative Frontline completion did not preserve one atomic core-destruction resolution.");
        }
        float winningHealth = winningSide == 0
            ? resolution.SideOneCoreHealth
            : resolution.SideTwoCoreHealth;
        float losingHealth = losingSide == 0
            ? resolution.SideOneCoreHealth
            : resolution.SideTwoCoreHealth;
        if (winningHealth <= 0f || losingHealth > 0f ||
            coreCount[winningSide] != 1 ||
            !Approximately(coreHealth[winningSide], winningHealth))
        {
            throw new InvalidOperationException(
                $"Authoritative Frontline resolution has invalid final core health: winner={winningHealth}, loser={losingHealth}.");
        }

        if (coreCount[losingSide] == 1)
        {
            if (coreHealth[losingSide] > 0f ||
                pendingCoreDestroyCount[winningSide] != 0 ||
                pendingCoreDestroyCount[losingSide] > 1)
            {
                throw new InvalidOperationException(
                    $"Completed core-destruction match has invalid teardown state: health={coreHealth[winningSide]}/{coreHealth[losingSide]}, " +
                    $"pending={pendingCoreDestroyCount[winningSide]}/{pendingCoreDestroyCount[losingSide]}.");
            }

            ServerCoreTeardownStage observedStage = pendingCoreDestroyCount[losingSide] == 0
                ? ServerCoreTeardownStage.OutcomeCommitted
                : ServerCoreTeardownStage.DestroyPending;
            if (observedStage < _serverCoreTeardownStage)
            {
                throw new InvalidOperationException(
                    $"Authoritative losing-core teardown regressed from {_serverCoreTeardownStage} to {observedStage}.");
            }
            if (_serverCoreTeardownStartedTimestamp == 0)
            {
                _serverCoreTeardownStartedTimestamp = Stopwatch.GetTimestamp();
            }
            _serverCoreTeardownStage = observedStage;
            if (ElapsedSeconds(_serverCoreTeardownStartedTimestamp) > _plan.StageTimeoutSeconds.Attack)
            {
                throw new TimeoutException(
                    $"Authoritative losing core remained in {_serverCoreTeardownStage} longer than " +
                    $"{_plan.StageTimeoutSeconds.Attack} seconds after the core-destruction outcome.");
            }
            return;
        }

        if (coreCount[losingSide] != 0 || pendingCoreDestroyCount[winningSide] != 0)
        {
            throw new InvalidOperationException(
                $"Completed core-destruction match requires winner/loser core counts 1/0; observed " +
                $"{coreCount[winningSide]}/{coreCount[losingSide]} with pending-destroy counts " +
                $"{pendingCoreDestroyCount[winningSide]}/{pendingCoreDestroyCount[losingSide]}.");
        }
        _serverCoreTeardownStage = ServerCoreTeardownStage.Destroyed;

        _evidence.Gameplay.ObservedCoreHealthBySide[0] = resolution.SideOneCoreHealth;
        _evidence.Gameplay.ObservedCoreHealthBySide[1] = resolution.SideTwoCoreHealth;

        _evidence.Gameplay.MatchPhase = match.Phase.ToString();
        _evidence.Gameplay.Outcome = match.Outcome.ToString();
        _evidence.Gameplay.OutcomeSource = "authoritative-frontline-runtime-snapshot";
        _evidence.Gameplay.WinningSideIndex = match.WinningSideIndex;
        _evidence.Gameplay.CommittedTick = match.CommittedTick;
        _evidence.Gameplay.CommittedTickSource = "authoritative-frontline-runtime-snapshot";
        CompleteStep("The authoritative world reached the configured winning core destruction with both seats connected.");
        Pass();
    }

    private void EnforceClientStageTimeout()
    {
        int timeout = _clientStage switch
        {
            ClientStage.Connecting => _plan.StageTimeoutSeconds.Connect,
            ClientStage.Ready => _plan.StageTimeoutSeconds.Ready,
            ClientStage.Gathering => _plan.StageTimeoutSeconds.Gather,
            ClientStage.Training => _plan.StageTimeoutSeconds.Train,
            ClientStage.Advancing => _plan.StageTimeoutSeconds.Move,
            ClientStage.Engaging or ClientStage.WaitingForOutcome => _plan.StageTimeoutSeconds.Attack,
            _ => throw new InvalidOperationException($"No timeout is configured for stage {_clientStage}."),
        };
        if (ElapsedSeconds(_stageStartedTimestamp) > timeout)
        {
            throw new TimeoutException(
                $"Client player {_localPlayerId} timed out after {timeout} seconds in stage {_clientStage}, substep {_substep}.");
        }
    }

    private void Transition(ClientStage next, AcceptanceProgressStage progressStage)
    {
        _clientStage = next;
        _progress.TransitionTo(progressStage, next.ToString());
        _substep = 0;
        _stageStartedTimestamp = Stopwatch.GetTimestamp();
        StartStep(next.ToString());
    }

    private void StartStep(string name)
    {
        _evidence.Steps.Add(new AcceptanceStepEvidence
        {
            Name = name,
            StartedInputRevision = _input?.UpdateRevision ?? 0,
            StartedCommittedTick = TryGetCommittedTick(),
        });
    }

    private void CompleteStep(string detail)
    {
        AcceptanceStepEvidence step = _evidence.Steps[^1];
        step.Status = "passed";
        step.CompletedAtUtc = DateTime.UtcNow.ToString("O");
        step.CompletedInputRevision = _input?.UpdateRevision ?? 0;
        step.CompletedCommittedTick = TryGetCommittedTick();
        step.Detail = detail;
    }

    private int TryGetCommittedTick()
    {
        if (_role == NetworkProcessRole.ReplicatedClient && TryGetClientMatchState(out FrontlineMatchStateProjection projection))
        {
            return projection.CommittedTick;
        }
        return _role == NetworkProcessRole.AuthoritativeServer
            ? _frontlineRuntime.Snapshot.CommittedTick
            : 0;
    }

    private void BeginEntitySelection(Entity entity, bool additive)
    {
        if (!_world.IsAlive(entity))
        {
            throw new InvalidOperationException("Cannot select a dead replicated entity.");
        }
        BeginPointerGesture(
            _bindings!.ConfirmActionId,
            ProjectEntity(entity),
            worldCm: null,
            additive,
            expectsCommand: false,
            actionName: "Select");
    }

    private void BeginEntityCommand(string actionName, Entity target)
    {
        BeginPointerGesture(
            _bindings!.CommandActionId,
            ProjectEntity(target),
            GetWorldPosition(target),
            additive: false,
            expectsCommand: true,
            actionName);
    }

    private void BeginGatherCommand()
    {
        RequireSelectedExactly(_trackedHarvester);
        if (!TryResolveNearestCrystal(_trackedHarvester, out Entity crystal))
        {
            throw new InvalidOperationException("The selected harvester has no replicated crystal field to gather from.");
        }
        BeginEntityCommand("Gather", crystal);
    }

    private void RequireTargetlessTrainingCommittedWithoutAiming()
    {
        if (_pendingCommandSequence == 0)
        {
            throw new InvalidOperationException("The training input did not commit a network command batch.");
        }
        if (_inputOrderMapping!.IsAiming)
        {
            throw new InvalidOperationException("Targetless infantry training incorrectly entered an aiming interaction.");
        }
    }

    private void ValidateTrainingAdmission(bool requireEntityQueue)
    {
        if (_evidence.Commands.Count == 0)
        {
            throw new InvalidOperationException("Training completed without command admission evidence.");
        }

        AcceptanceCommandEvidence command = _evidence.Commands[^1];
        string expectedAction = requireEntityQueue ? "QueueTrainInfantry" : "TrainInfantry";
        if (!string.Equals(command.Action, expectedAction, StringComparison.Ordinal) ||
            !string.Equals(command.AdmissionStage, NetworkCommandAdmissionStage.Terminal.ToString(), StringComparison.Ordinal) ||
            !string.Equals(command.AdmissionResult, NetworkCommandAdmissionCode.TerminalCompleted.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Training admission ended as {command.Action}/{command.AdmissionStage}/{command.AdmissionResult}; " +
                $"expected {expectedAction}/Terminal/TerminalCompleted.");
        }

        int queuedIndex = -1;
        int activatedIndex = -1;
        for (int i = 0; i < command.AdmissionHistory.Length; i++)
        {
            AcceptanceAdmissionTransitionEvidence transition = command.AdmissionHistory[i];
            if (!string.Equals(transition.Stage, NetworkCommandAdmissionStage.EntityIntake.ToString(), StringComparison.Ordinal))
            {
                continue;
            }
            if (queuedIndex < 0 &&
                string.Equals(transition.Result, NetworkCommandAdmissionCode.Queued.ToString(), StringComparison.Ordinal))
            {
                queuedIndex = i;
            }
            if (activatedIndex < 0 &&
                string.Equals(transition.Result, NetworkCommandAdmissionCode.Activated.ToString(), StringComparison.Ordinal))
            {
                activatedIndex = i;
            }
        }

        if (activatedIndex < 0)
        {
            throw new InvalidOperationException($"Training command {expectedAction} never exposed EntityIntake:Activated.");
        }
        if (requireEntityQueue)
        {
            if (queuedIndex < 0 || queuedIndex >= activatedIndex)
            {
                throw new InvalidOperationException(
                    "Queued infantry training did not expose EntityIntake:Queued before EntityIntake:Activated.");
            }
            AcceptanceAdmissionTransitionEvidence activated = command.AdmissionHistory[activatedIndex];
            if (_evidence.Gameplay.SecondTrainedInfantryObservedCommittedTick <= 0 ||
                activated.AuthoritativeCommittedTick <= 0 ||
                activated.AuthoritativeCommittedTick > _evidence.Gameplay.SecondTrainedInfantryObservedCommittedTick)
            {
                throw new InvalidOperationException(
                    "Queued infantry training lacks a causal authoritative activation tick before the second trained infantry observation.");
            }
        }
        else if (queuedIndex >= 0)
        {
            throw new InvalidOperationException("The first infantry training command unexpectedly entered the entity queue.");
        }
    }

    private int RequireClientAuthoritativeSnapshotTick(string observation)
    {
        int committedTick = checked((int)_clientStatus!.LastCommittedTick);
        if (committedTick <= 0)
        {
            throw new InvalidOperationException(
                $"Client observed the {observation} without a positive authoritative snapshot tick.");
        }
        return committedTick;
    }

    private int RequireServerAuthoritativeExecutingTick(int side, string observation)
    {
        if (!_engine.GameSession.SimulationTicks.IsExecuting)
        {
            throw new InvalidOperationException(
                $"Server observed side {side} {observation} outside an authoritative simulation tick.");
        }
        int authoritativeTick = _engine.GameSession.SimulationTicks.ExecutingTick;
        if (authoritativeTick <= 0)
        {
            throw new InvalidOperationException(
                $"Server observed side {side} {observation} without a positive authoritative simulation tick.");
        }
        return authoritativeTick;
    }

    private void BeginGroundCommand(string actionName, WorldCmInt2 target)
    {
        Vector2 screen = _projector!.WorldToScreen(new Vector3(target.X * 0.01f, 0f, target.Y * 0.01f));
        RequireFiniteScreenPoint(screen, actionName);
        BeginGroundCommand(actionName, target, screen);
    }

    private void RequestCameraFocus(WorldCmInt2 target)
    {
        _engine.SetService(
            CoreServiceKeys.CameraPoseRequest,
            new CameraPoseRequest { TargetCm = new Vector2(target.X, target.Y) });
    }

    private bool TryBeginFocusedGroundCommand(string actionName, WorldCmInt2 target)
    {
        Vector2 screen = _projector!.WorldToScreen(new Vector3(target.X * 0.01f, 0f, target.Y * 0.01f));
        if (!float.IsFinite(screen.X) || !float.IsFinite(screen.Y))
        {
            return false;
        }

        BeginGroundCommand(actionName, target, screen);
        return true;
    }

    private void BeginGroundCommand(string actionName, WorldCmInt2 target, Vector2 screen)
    {
        BeginPointerGesture(
            _bindings!.CommandActionId,
            screen,
            target,
            additive: false,
            expectsCommand: true,
            actionName);
    }

    private void BeginButtonGesture(
        string actionId,
        bool expectsCommand,
        string actionName,
        WorldCmInt2? worldCm = null,
        bool queueModifier = false)
    {
        BeginPointerGesture(actionId, null, worldCm, additive: queueModifier, expectsCommand, actionName);
    }

    private void BeginPointerGesture(
        string actionId,
        Vector2? screen,
        WorldCmInt2? worldCm,
        bool additive,
        bool expectsCommand,
        string actionName)
    {
        if (_gesture.Phase != GesturePhase.None)
        {
            throw new InvalidOperationException("Acceptance attempted to overlap two input gestures.");
        }
        if (expectsCommand)
        {
            if (_pendingCommandSequence != 0 || _pendingAdmissionHistory.Count != 0)
            {
                throw new InvalidOperationException("Acceptance attempted to start a command while prior admission evidence was pending.");
            }
            CaptureCommandActors();
            _pendingCommandAction = actionName;
        }
        if (worldCm.HasValue)
        {
            if (_groundOverride!.HasOverride)
            {
                throw new InvalidOperationException("A prior authoritative ground pointer override was not consumed.");
            }
            WorldCmInt2 point = worldCm.Value;
            _groundOverride.Set(_bindings!.CommandActionId, new Vector2(point.X, point.Y));
        }
        if (screen.HasValue)
        {
            RequireFiniteScreenPoint(screen.Value, actionName);
            _input!.InjectAction(_bindings!.PointerPositionActionId, new Vector3(screen.Value.X, screen.Value.Y, 0f));
        }
        if (additive)
        {
            _input!.InjectButtonPress(CommandSourceModifierActionIds.Additive);
        }

        ulong submissionRevision = expectsCommand ? _commandPort!.SubmissionRevision : 0;
        _input!.InjectButtonPress(actionId);
        _gesture = new GestureState(
            GesturePhase.PressQueued,
            actionId,
            screen,
            additive,
            expectsCommand,
            submissionRevision,
            _input.UpdateRevision);
    }

    private bool AdvanceGesture()
    {
        if (_gesture.Phase == GesturePhase.None)
        {
            throw new InvalidOperationException("Acceptance has no input gesture to advance.");
        }
        if (_input!.UpdateRevision <= _gesture.QueuedAtInputRevision)
        {
            return false;
        }

        if (_gesture.Phase == GesturePhase.PressQueued)
        {
            if (_gesture.ExpectsCommand)
            {
                if (_commandPort!.SubmissionRevision != _gesture.SubmissionRevisionBefore + 1 ||
                    _commandPort.LastSubmitResult != ReplicatedClientCommandSubmitResult.Submitted ||
                    _commandPort.LastSubmittedBatchSequence == 0)
                {
                    throw new InvalidOperationException(
                        $"Input action '{_pendingCommandAction}' did not produce exactly one submitted network command batch; " +
                        $"revision={_commandPort.SubmissionRevision}, result={_commandPort.LastSubmitResult}, " +
                        $"sequence={_commandPort.LastSubmittedBatchSequence}.");
                }
                _pendingCommandSequence = _commandPort.LastSubmittedBatchSequence;
            }

            if (_gesture.Screen.HasValue)
            {
                Vector2 screen = _gesture.Screen.Value;
                _input.InjectAction(_bindings!.PointerPositionActionId, new Vector3(screen.X, screen.Y, 0f));
            }
            _input.InjectButtonRelease(_gesture.ActionId);
            if (_gesture.Additive)
            {
                _input.InjectButtonRelease(CommandSourceModifierActionIds.Additive);
            }
            _gesture = _gesture with
            {
                Phase = GesturePhase.ReleaseQueued,
                QueuedAtInputRevision = _input.UpdateRevision,
            };
            return false;
        }

        _gesture = default;
        return true;
    }

    private bool TryCompletePendingCommand()
    {
        if (_pendingCommandSequence == 0)
        {
            throw new InvalidOperationException("Acceptance has no pending command sequence.");
        }

        if (!TryCompleteCommand(
                _pendingCommandSequence,
                _pendingCommandAction,
                _commandActorCount,
                _commandActors,
                _pendingAdmissionHistory))
        {
            return false;
        }

        ClearPendingCommand();
        return true;
    }

    private bool TryPreserveActivatedPendingCommand()
    {
        if (_pendingCommandSequence == 0)
        {
            throw new InvalidOperationException("Acceptance has no pending command sequence to preserve.");
        }
        if (_preservedCommandSequence != 0 || _preservedAdmissionHistory.Count != 0)
        {
            throw new InvalidOperationException("Acceptance already has a preserved in-flight command.");
        }
        if (!_observer.TryGetClientAdmission(_pendingCommandSequence, out NetworkCommandAdmissionOutcome summary))
        {
            return false;
        }

        TrackAdmissionProgress(_pendingCommandSequence, _pendingAdmissionHistory);
        ValidateAdmissionSummary(
            in summary,
            _pendingCommandSequence,
            _pendingCommandAction,
            _commandActorCount);
        if (summary.Stage == NetworkCommandAdmissionStage.Terminal)
        {
            throw new InvalidOperationException(
                $"Command {_pendingCommandAction} sequence {_pendingCommandSequence} completed before the overlapping queued command was submitted.");
        }
        if (summary.Stage != NetworkCommandAdmissionStage.EntityIntake ||
            summary.Result != NetworkCommandAdmissionCode.Activated)
        {
            return false;
        }

        _preservedCommandSequence = _pendingCommandSequence;
        _preservedCommandAction = _pendingCommandAction;
        _preservedCommandActorCount = _commandActorCount;
        Array.Copy(_commandActors, _preservedCommandActors, _commandActorCount);
        _preservedAdmissionHistory.AddRange(_pendingAdmissionHistory);
        ClearPendingCommand();
        return true;
    }

    private bool TryCompletePreservedCommand()
    {
        if (_preservedCommandSequence == 0)
        {
            throw new InvalidOperationException("Acceptance has no preserved in-flight command.");
        }

        if (!TryCompleteCommand(
                _preservedCommandSequence,
                _preservedCommandAction,
                _preservedCommandActorCount,
                _preservedCommandActors,
                _preservedAdmissionHistory))
        {
            return false;
        }

        _preservedCommandSequence = 0;
        _preservedCommandAction = string.Empty;
        _preservedCommandActorCount = 0;
        _preservedAdmissionHistory.Clear();
        return true;
    }

    private bool TryCompleteCommand(
        ulong sequence,
        string action,
        int actorCount,
        Entity[] actors,
        List<AcceptanceAdmissionTransitionEvidence> admissionHistory)
    {
        if (!_observer.TryGetClientAdmission(sequence, out NetworkCommandAdmissionOutcome summary))
        {
            return false;
        }

        TrackAdmissionProgress(sequence, admissionHistory);
        ValidateAdmissionSummary(in summary, sequence, action, actorCount);
        var actorAdmissions = new AcceptanceActorAdmissionEvidence[summary.ActorCount];
        for (int i = 0; i < summary.ActorCount; i++)
        {
            if (!_observer.TryGetClientActorAdmission(sequence, checked((ushort)i), out NetworkCommandAdmissionOutcome actor))
            {
                return false;
            }
            if (actor.Stage != NetworkCommandAdmissionStage.EntityIntake)
            {
                return false;
            }
            if (IsAdmissionRejection(actor.Result))
            {
                throw new InvalidOperationException(
                    $"Command {action} sequence {sequence} actor {i} was rejected: {actor.Result}.");
            }
            if (actor.Result != NetworkCommandAdmissionCode.Activated)
            {
                return false;
            }
            actorAdmissions[i] = new AcceptanceActorAdmissionEvidence
            {
                BatchIndex = i,
                Stage = actor.Stage.ToString(),
                Result = actor.Result.ToString(),
            };
        }
        if (summary.Stage != NetworkCommandAdmissionStage.Terminal ||
            summary.Result != NetworkCommandAdmissionCode.TerminalCompleted)
        {
            return false;
        }
        bool observedNetworkIntake = false;
        bool observedEntityActivation = false;
        bool observedTerminalCompletion = false;
        for (int i = 0; i < admissionHistory.Count; i++)
        {
            AcceptanceAdmissionTransitionEvidence transition = admissionHistory[i];
            observedNetworkIntake |=
                transition.Stage == NetworkCommandAdmissionStage.NetworkIntake.ToString() &&
                transition.Result == NetworkCommandAdmissionCode.NetworkScheduled.ToString();
            observedEntityActivation |=
                transition.Stage == NetworkCommandAdmissionStage.EntityIntake.ToString() &&
                transition.Result == NetworkCommandAdmissionCode.Activated.ToString();
            observedTerminalCompletion |=
                transition.Stage == NetworkCommandAdmissionStage.Terminal.ToString() &&
                transition.Result == NetworkCommandAdmissionCode.TerminalCompleted.ToString();
        }
        if (!observedNetworkIntake || !observedEntityActivation || !observedTerminalCompletion)
        {
            throw new InvalidOperationException(
                $"Command {action} sequence {sequence} did not expose its full " +
                "NetworkIntake-to-Terminal admission history.");
        }

        var handles = new string[actorCount];
        for (int i = 0; i < actorCount; i++)
        {
            handles[i] = FormatHandle(actors[i]);
        }
        _evidence.Commands.Add(new AcceptanceCommandEvidence
        {
            Action = action,
            ClientBatchSequence = sequence,
            ActorCount = summary.ActorCount,
            AdmissionStage = summary.Stage.ToString(),
            AdmissionResult = summary.Result.ToString(),
            ActorHandles = handles,
            AdmissionHistory = admissionHistory.ToArray(),
            ActorAdmissions = actorAdmissions,
        });
        return true;
    }

    private void ValidateAdmissionSummary(
        in NetworkCommandAdmissionOutcome summary,
        ulong sequence,
        string action,
        int actorCount)
    {
        if (summary.PlayerId != _localPlayerId || summary.SeatSlot != _evidence.SeatSlot)
        {
            throw new InvalidOperationException(
                $"Admission sequence {sequence} belongs to player {summary.PlayerId}/seat {summary.SeatSlot}, " +
                $"not {_localPlayerId}/{_evidence.SeatSlot}.");
        }
        if (summary.ActorCount != actorCount || summary.ActorCount <= 0)
        {
            throw new InvalidOperationException(
                $"Admission sequence {sequence} actorCount={summary.ActorCount}; selected actorCount={actorCount}.");
        }
        if (IsAdmissionRejection(summary.Result))
        {
            throw new InvalidOperationException(
                $"Command {action} sequence {sequence} was rejected at {summary.Stage}: {summary.Result}.");
        }
    }

    private void ClearPendingCommand()
    {
        _pendingCommandSequence = 0;
        _pendingCommandAction = string.Empty;
        _commandActorCount = 0;
        _pendingAdmissionHistory.Clear();
    }

    private void TrackInFlightAdmissionProgress()
    {
        if (_pendingCommandSequence != 0)
        {
            TrackAdmissionProgress(_pendingCommandSequence, _pendingAdmissionHistory);
        }
        if (_preservedCommandSequence != 0)
        {
            TrackAdmissionProgress(_preservedCommandSequence, _preservedAdmissionHistory);
        }
    }

    private void TrackAdmissionProgress(
        ulong sequence,
        List<AcceptanceAdmissionTransitionEvidence> admissionHistory)
    {
        if (sequence == 0 ||
            !_observer.TryCopyClientAdmissionProgress(
                sequence,
                _admissionProgressScratch,
                out int progressCount))
        {
            return;
        }

        if (admissionHistory.Count > progressCount)
        {
            throw new InvalidOperationException(
                $"Admission history for sequence {sequence} regressed from " +
                $"{admissionHistory.Count} to {progressCount} stages.");
        }

        for (int i = admissionHistory.Count; i < progressCount; i++)
        {
            NetworkCommandAdmissionOutcome outcome = _admissionProgressScratch[i];
            admissionHistory.Add(new AcceptanceAdmissionTransitionEvidence
            {
                Stage = outcome.Stage.ToString(),
                Result = outcome.Result.ToString(),
                AdmissionBatchIndex = outcome.AdmissionBatchIndex,
                ObservedInputRevision = _input!.UpdateRevision,
                ObservedCommittedTick = TryGetCommittedTick(),
                AuthoritativeCommittedTick = outcome.CommittedTick,
            });
        }
    }

    private void CaptureCommandActors()
    {
        Entity owner = RequireLocalPlayerEntity();
        _commandActorCount = _collections!.CopyEntities(owner, EntityCollectionKeys.CommandSource, _commandActors);
        if (_commandActorCount <= 0)
        {
            throw new InvalidOperationException("Player input command has no formally selected command-source actors.");
        }
        for (int i = 0; i < _commandActorCount; i++)
        {
            if (!_world.IsAlive(_commandActors[i]) || !_world.Has<ReplicationMirrorIdentity>(_commandActors[i]))
            {
                throw new InvalidOperationException("Player input command selection contains a dead or non-replicated actor.");
            }
        }
    }

    private bool PrepareOwnedInfantrySelection(int count)
    {
        int available = CollectOwnedInfantry(_localPlayerId, _selectionTargets);
        if (available < count)
        {
            return false;
        }
        SortClientEntitiesByHandle(_selectionTargets, available);
        _selectionTargetCount = count;
        _selectionTargetIndex = 0;
        BeginEntitySelection(_selectionTargets[0], additive: false);
        return true;
    }

    private bool AdvancePreparedSelection()
    {
        if (!AdvanceGesture())
        {
            return false;
        }

        _selectionTargetIndex++;
        if (_selectionTargetIndex < _selectionTargetCount)
        {
            BeginEntitySelection(_selectionTargets[_selectionTargetIndex], additive: true);
            return false;
        }

        RequireSelectedSet(_selectionTargets.AsSpan(0, _selectionTargetCount));
        return true;
    }

    private string[] CaptureSelectedHandles()
    {
        Entity owner = RequireLocalPlayerEntity();
        int count = _collections!.CopyEntities(owner, EntityCollectionKeys.CommandSource, _entityScratch);
        var handles = new string[count];
        for (int i = 0; i < count; i++)
        {
            handles[i] = FormatHandle(_entityScratch[i]);
        }
        return handles;
    }

    private AcceptancePositionEvidence[] CaptureSelectedPositions()
    {
        Entity owner = RequireLocalPlayerEntity();
        int count = _collections!.CopyEntities(owner, EntityCollectionKeys.CommandSource, _entityScratch);
        var result = new AcceptancePositionEvidence[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = CapturePosition(_entityScratch[i]);
        }
        return result;
    }

    private void RequireSelectedExactly(Entity expected)
    {
        Span<Entity> one = stackalloc Entity[1];
        one[0] = expected;
        RequireSelectedSet(one);
    }

    private void RequireSelectedSet(ReadOnlySpan<Entity> expected)
    {
        Entity owner = RequireLocalPlayerEntity();
        int count = _collections!.CopyEntities(owner, EntityCollectionKeys.CommandSource, _entityScratch);
        if (count != expected.Length)
        {
            throw new InvalidOperationException(
                $"Formal command-source selection contains {count} entities; expected {expected.Length}.");
        }
        for (int i = 0; i < expected.Length; i++)
        {
            bool found = false;
            for (int j = 0; j < count; j++)
            {
                found |= _entityScratch[j] == expected[i];
            }
            if (!found)
            {
                throw new InvalidOperationException("Formal command-source selection does not contain the clicked entity set.");
            }
        }
    }

    private Entity RequireLocalPlayerEntity()
    {
        if (!_engine.TryGetService(CoreServiceKeys.LocalPlayerEntity, out Entity owner) ||
            !_world.IsAlive(owner))
        {
            throw new InvalidOperationException("Acceptance client lost its local player entity binding.");
        }
        return owner;
    }

    private void DeriveBattlePoints()
    {
        if (!TryResolveOwnCore(_localPlayerId, out Entity core))
        {
            throw new InvalidOperationException("Cannot derive battle direction without the local core.");
        }
        int count = CollectClientCrystals(_entityScratch);
        if (count != 2)
        {
            throw new InvalidOperationException($"Battle direction requires exactly two public crystal fields; observed {count}.");
        }
        WorldCmInt2 corePosition = GetWorldPosition(core);
        WorldCmInt2 first = GetWorldPosition(_entityScratch[0]);
        WorldCmInt2 second = GetWorldPosition(_entityScratch[1]);
        WorldCmInt2 near = DistanceSquared(corePosition, first) <= DistanceSquared(corePosition, second) ? first : second;
        WorldCmInt2 far = near.Equals(first) ? second : first;
        double dx = far.X - (double)near.X;
        double dy = far.Y - (double)near.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 1d)
        {
            throw new InvalidOperationException("Public crystal fields do not define a battle direction.");
        }
        double ux = dx / length;
        double uy = dy / length;
        int midpointX = checked((int)Math.Round((first.X + (long)second.X) * 0.5d));
        int midpointY = checked((int)Math.Round((first.Y + (long)second.Y) * 0.5d));
        int sideSign = _localSideIndex == 0 ? -1 : 1;
        _meetingPoint = new WorldCmInt2(
            checked((int)Math.Round(midpointX + (sideSign * ux * _plan.Battle.MeetingOffsetCm))),
            checked((int)Math.Round(midpointY + (sideSign * uy * _plan.Battle.MeetingOffsetCm))));
        _siegePoint = new WorldCmInt2(
            checked((int)Math.Round(far.X + (ux * _plan.Battle.SiegeBeyondFarResourceCm))),
            checked((int)Math.Round(far.Y + (uy * _plan.Battle.SiegeBeyondFarResourceCm))));
        _evidence.Gameplay.MeetingPoint = new AcceptanceWorldPointEvidence
        {
            XCm = _meetingPoint.X,
            YCm = _meetingPoint.Y,
        };
        _evidence.Gameplay.SiegePoint = new AcceptanceWorldPointEvidence
        {
            XCm = _siegePoint.X,
            YCm = _siegePoint.Y,
        };
        _battlePointsDerived = true;
    }

    private bool AreSelectedActorsNear(WorldCmInt2 destination, int toleranceCm)
    {
        Entity owner = RequireLocalPlayerEntity();
        int count = _collections!.CopyEntities(owner, EntityCollectionKeys.CommandSource, _entityScratch);
        if (count <= 0)
        {
            return false;
        }
        long toleranceSquared = (long)toleranceCm * toleranceCm;
        int liveCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (!_world.IsAlive(_entityScratch[i]))
            {
                continue;
            }
            liveCount++;
            if (DistanceSquared(GetWorldPosition(_entityScratch[i]), destination) > toleranceSquared)
            {
                return false;
            }
        }
        return liveCount > 0;
    }

    private bool AreSelectedActorsVisibleWithPerformerPayload()
    {
        Entity owner = RequireLocalPlayerEntity();
        int count = _collections!.CopyEntities(owner, EntityCollectionKeys.CommandSource, _entityScratch);
        if (count <= 0)
        {
            return false;
        }
        for (int i = 0; i < count; i++)
        {
            if (!HasVisiblePerformerPayload(_entityScratch[i]))
            {
                return false;
            }
        }
        return true;
    }

    private bool AreMovedActorsOnscreenInPresentationReceipts()
    {
        AcceptancePositionEvidence[] starts = _evidence.Gameplay.MoveStartPositions;
        if (starts.Length == 0)
        {
            return false;
        }
        for (int i = 0; i < starts.Length; i++)
        {
            int stableId = starts[i].PresentationStableId;
            if (stableId <= 0)
            {
                throw new InvalidOperationException(
                    "Acceptance movement evidence contains a non-positive presentation stable id.");
            }
            if (!_presentationReceipts!.HasOnscreenInstance(
                    stableId,
                    _infantryBodyTemplateId))
            {
                return false;
            }
        }
        return true;
    }

    private bool HaveAllSelectedActorsMoved(int minimumCm)
    {
        AcceptancePositionEvidence[] starts = _evidence.Gameplay.MoveStartPositions;
        if (starts.Length == 0)
        {
            return false;
        }
        long minimumSquared = (long)minimumCm * minimumCm;
        for (int i = 0; i < starts.Length; i++)
        {
            if (!TryResolveHandle(starts[i].Handle, out Entity entity))
            {
                return false;
            }
            WorldCmInt2 current = GetWorldPosition(entity);
            long dx = current.X - (long)starts[i].XCm;
            long dy = current.Y - (long)starts[i].YCm;
            if ((dx * dx) + (dy * dy) < minimumSquared)
            {
                return false;
            }
        }
        return true;
    }

    private bool TryResolveHandle(string formatted, out Entity entity)
    {
        entity = Entity.Null;
        int separator = formatted.IndexOf(':');
        if (separator <= 0 ||
            !int.TryParse(formatted.AsSpan(0, separator), out int slot) ||
            !uint.TryParse(formatted.AsSpan(separator + 1), out uint generation))
        {
            return false;
        }
        foreach (ref Chunk chunk in _world.Query(in ClientInfantryQuery))
        {
            ReadOnlySpan<ReplicationMirrorIdentity> identities = chunk.GetSpan<ReplicationMirrorIdentity>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (identities[index].Handle.Slot == slot && identities[index].Handle.Generation == generation)
                {
                    entity = Unsafe.Add(ref first, index);
                    return true;
                }
            }
        }
        return false;
    }

    private void TrackClientObservations()
    {
        Span<int> infantryCounts = stackalloc int[2];
        Span<bool> infantryObserved = stackalloc bool[2];
        foreach (ref Chunk chunk in _world.Query(in ClientCoreQuery))
        {
            ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            ReadOnlySpan<AttributeBuffer> attributes = chunk.GetSpan<AttributeBuffer>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                int side = participants[index].SideIndex;
                if ((uint)side < 2u)
                {
                    _evidence.Gameplay.ObservedCoreHealthBySide[side] = attributes[index].GetCurrent(_healthAttributeId);
                    if (side == 1 - _plan.Expected.WinningSideIndex)
                    {
                        Entity core = Unsafe.Add(ref first, index);
                        _losingCoreLastPosition = GetWorldPosition(core);
                        _hasLosingCoreLastPosition = true;
                        _evidence.Gameplay.DefeatedCoreLastPosition = CapturePosition(core);
                    }
                }
            }
        }
        foreach (ref Chunk chunk in _world.Query(in ClientInfantryQuery))
        {
            ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            foreach (int index in chunk)
            {
                int side = participants[index].SideIndex;
                if ((uint)side < 2u)
                {
                    infantryCounts[side]++;
                    infantryObserved[side] = true;
                }
            }
        }
        for (int side = 0; side < 2; side++)
        {
            if (infantryObserved[side] || side == _localSideIndex)
            {
                _evidence.Gameplay.ObservedInfantryCountBySide[side] = infantryCounts[side];
            }
        }
    }

    private bool TryResolveOwnCore(int playerId, out Entity core)
    {
        core = Entity.Null;
        foreach (ref Chunk chunk in _world.Query(in ClientCoreQuery))
        {
            ReadOnlySpan<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (owners[index].PlayerId == playerId)
                {
                    if (core != Entity.Null)
                    {
                        throw new InvalidOperationException($"Player {playerId} has more than one replicated core.");
                    }
                    core = Unsafe.Add(ref first, index);
                }
            }
        }
        return core != Entity.Null;
    }

    private bool TryResolveVisibleEnemyCore(out Entity core)
    {
        core = Entity.Null;
        foreach (ref Chunk chunk in _world.Query(in ClientCoreQuery))
        {
            ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (participants[index].SideIndex != _localSideIndex)
                {
                    core = Unsafe.Add(ref first, index);
                    return true;
                }
            }
        }
        return false;
    }

    private bool TryResolveOwnHarvester(int playerId, out Entity harvester)
    {
        harvester = Entity.Null;
        int bestSlot = int.MaxValue;
        foreach (ref Chunk chunk in _world.Query(in ClientHarvesterQuery))
        {
            ReadOnlySpan<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
            ReadOnlySpan<ReplicationMirrorIdentity> identities = chunk.GetSpan<ReplicationMirrorIdentity>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (owners[index].PlayerId == playerId && identities[index].Handle.Slot < bestSlot)
                {
                    bestSlot = identities[index].Handle.Slot;
                    harvester = Unsafe.Add(ref first, index);
                }
            }
        }
        return harvester != Entity.Null;
    }

    private bool TryResolveNearestCrystal(Entity actor, out Entity crystal)
    {
        crystal = Entity.Null;
        WorldCmInt2 source = GetWorldPosition(actor);
        long bestDistance = long.MaxValue;
        foreach (ref Chunk chunk in _world.Query(in ClientCrystalQuery))
        {
            ReadOnlySpan<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                WorldCmInt2 position = positions[index].ToWorldCmInt2();
                long distance = DistanceSquared(source, position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    crystal = Unsafe.Add(ref first, index);
                }
            }
        }
        return crystal != Entity.Null;
    }

    private bool AreOpeningEntitiesVisible(int playerId, Entity core, Entity openingCrystal)
    {
        if (!HasVisiblePerformerPayload(core) || !HasVisiblePerformerPayload(openingCrystal))
        {
            return false;
        }

        int visibleHarvesterCount = 0;
        foreach (ref Chunk chunk in _world.Query(in ClientHarvesterQuery))
        {
            ReadOnlySpan<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (owners[index].PlayerId == playerId &&
                    HasVisiblePerformerPayload(Unsafe.Add(ref first, index)))
                {
                    visibleHarvesterCount++;
                }
            }
        }
        if (visibleHarvesterCount != _plan.Expected.InitialHarvesterCount)
        {
            return false;
        }

        int visibleInfantryCount = 0;
        foreach (ref Chunk chunk in _world.Query(in ClientInfantryQuery))
        {
            ReadOnlySpan<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (owners[index].PlayerId == playerId &&
                    HasVisiblePerformerPayload(Unsafe.Add(ref first, index)))
                {
                    visibleInfantryCount++;
                }
            }
        }
        return visibleInfantryCount == _plan.Expected.InitialInfantryCount;
    }

    private bool HasVisiblePerformerPayload(Entity entity)
    {
        return _world.IsAlive(entity) &&
            _world.TryGet(entity, out CullState cull) &&
            cull.IsVisible &&
            _world.TryGet(entity, out PresentationOwnerHasPerformerPayload payload) &&
            payload.Count > 0;
    }

    private static bool CameraTargetMatches(Vector2 actual, Vector2 expected) =>
        Vector2.DistanceSquared(actual, expected) <= 1f;

    private bool TryResolveVisibleEnemyInfantry(out Entity infantry)
    {
        infantry = Entity.Null;
        int bestSlot = int.MaxValue;
        foreach (ref Chunk chunk in _world.Query(in ClientInfantryQuery))
        {
            ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            ReadOnlySpan<ReplicationMirrorIdentity> identities = chunk.GetSpan<ReplicationMirrorIdentity>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (participants[index].SideIndex != _localSideIndex && identities[index].Handle.Slot < bestSlot)
                {
                    bestSlot = identities[index].Handle.Slot;
                    infantry = Unsafe.Add(ref first, index);
                }
            }
        }
        return infantry != Entity.Null;
    }

    private int CountVisibleEnemyInfantry(int localSideIndex)
    {
        int count = 0;
        foreach (ref Chunk chunk in _world.Query(in ClientInfantryQuery))
        {
            ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            foreach (int index in chunk)
            {
                count += participants[index].SideIndex != localSideIndex ? 1 : 0;
            }
        }
        return count;
    }

    private int CountVisibleEnemyCores(int localSideIndex)
    {
        int count = 0;
        foreach (ref Chunk chunk in _world.Query(in ClientCoreQuery))
        {
            ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            foreach (int index in chunk)
            {
                count += participants[index].SideIndex != localSideIndex ? 1 : 0;
            }
        }
        return count;
    }

    private int CollectOwnedInfantry(int playerId, Span<Entity> destination)
    {
        int count = 0;
        foreach (ref Chunk chunk in _world.Query(in ClientInfantryQuery))
        {
            ReadOnlySpan<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (owners[index].PlayerId != playerId)
                {
                    continue;
                }
                if (count >= destination.Length)
                {
                    throw new InvalidOperationException("Acceptance infantry scratch capacity was exceeded.");
                }
                destination[count++] = Unsafe.Add(ref first, index);
            }
        }
        return count;
    }

    private int CountClientCrystals() => CollectClientCrystals(_entityScratch);

    private int CollectClientCrystals(Span<Entity> destination)
    {
        int count = 0;
        foreach (ref Chunk chunk in _world.Query(in ClientCrystalQuery))
        {
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (count >= destination.Length)
                {
                    throw new InvalidOperationException("Acceptance crystal scratch capacity was exceeded.");
                }
                destination[count++] = Unsafe.Add(ref first, index);
            }
        }
        return count;
    }

    private int CountOwned<TMarker>(int playerId) where TMarker : struct
    {
        return typeof(TMarker) == typeof(ClientInfantryMarker)
            ? CollectOwnedInfantry(playerId, _entityScratch)
            : CountOwnedHarvesters(playerId);
    }

    private int CountOwnedHarvesters(int playerId)
    {
        int count = 0;
        foreach (ref Chunk chunk in _world.Query(in ClientHarvesterQuery))
        {
            ReadOnlySpan<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
            foreach (int index in chunk)
            {
                count += owners[index].PlayerId == playerId ? 1 : 0;
            }
        }
        return count;
    }

    private int CountClientCores(int sideIndex)
    {
        int count = 0;
        foreach (ref Chunk chunk in _world.Query(in ClientCoreQuery))
        {
            ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            foreach (int index in chunk)
            {
                count += participants[index].SideIndex == sideIndex ? 1 : 0;
            }
        }
        return count;
    }

    private AcceptancePositionEvidence[] CaptureInfantryNear(int sideIndex, WorldCmInt2 point, int radiusCm)
    {
        long radiusSquared = (long)radiusCm * radiusCm;
        var witnesses = new List<AcceptancePositionEvidence>();
        foreach (ref Chunk chunk in _world.Query(in ClientInfantryQuery))
        {
            ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            ReadOnlySpan<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (participants[index].SideIndex == sideIndex &&
                    DistanceSquared(positions[index].ToWorldCmInt2(), point) <= radiusSquared)
                {
                    witnesses.Add(CapturePosition(Unsafe.Add(ref first, index)));
                }
            }
        }
        return witnesses.ToArray();
    }

    private bool TryGetClientMatchState(out FrontlineMatchStateProjection projection)
    {
        projection = default;
        int count = 0;
        foreach (ref Chunk chunk in _world.Query(in ClientMatchQuery))
        {
            ReadOnlySpan<FrontlineMatchStateProjection> projections = chunk.GetSpan<FrontlineMatchStateProjection>();
            foreach (int index in chunk)
            {
                projection = projections[index];
                count++;
            }
        }
        if (count > 1)
        {
            throw new InvalidOperationException($"Client observed {count} replicated match-state entities; expected one.");
        }
        return count == 1;
    }

    private float ReadServerCoreCrystals(int sideIndex)
    {
        foreach (ref Chunk chunk in _world.Query(in ServerCoreQuery))
        {
            ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            ReadOnlySpan<AttributeBuffer> attributes = chunk.GetSpan<AttributeBuffer>();
            foreach (int index in chunk)
            {
                if (participants[index].SideIndex == sideIndex)
                {
                    return attributes[index].GetCurrent(_crystalAttributeId);
                }
            }
        }
        throw new InvalidOperationException($"Server could not resolve core crystals for side {sideIndex}.");
    }

    private void CaptureSeats(bool requireBothConnected)
    {
        Span<NetworkRoomSeatSnapshot> seats = stackalloc NetworkRoomSeatSnapshot[2];
        if (!_observer.TryCopyRoomSeats(seats, out int count) || count != 2)
        {
            throw new InvalidOperationException($"Network room evidence requires exactly two seats; observed {count}.");
        }
        _evidence.Seats.Clear();
        for (int i = 0; i < seats.Length; i++)
        {
            bool empty = seats[i].ConnectionState == NetworkRoomSeatConnectionState.Empty;
            int expectedPlayerId = empty ? 0 : i + 1;
            if (seats[i].PlayerId.Value != expectedPlayerId)
            {
                throw new InvalidOperationException($"Room seat {i} is bound to unexpected player {seats[i].PlayerId.Value}.");
            }
            if (requireBothConnected && seats[i].ConnectionState != NetworkRoomSeatConnectionState.Connected)
            {
                throw new InvalidOperationException($"Room seat {i} is not connected: {seats[i].ConnectionState}.");
            }
            _evidence.Seats.Add(new AcceptanceSeatEvidence
            {
                SeatSlot = i,
                PlayerId = seats[i].PlayerId.Value,
                ConnectionState = seats[i].ConnectionState.ToString(),
                ReadyState = seats[i].ReadyState.ToString(),
            });
        }
    }

    private bool BothSeatsReady()
    {
        if (!_observer.HasRoomSnapshot)
        {
            return false;
        }
        Span<NetworkRoomSeatSnapshot> seats = stackalloc NetworkRoomSeatSnapshot[2];
        return _observer.TryCopyRoomSeats(seats, out int count) && count == 2 &&
            seats[0].ReadyState == NetworkRoomReadyState.Ready &&
            seats[1].ReadyState == NetworkRoomReadyState.Ready;
    }

    private bool BothSeatsConnected()
    {
        if (!_observer.HasRoomSnapshot)
        {
            return false;
        }
        Span<NetworkRoomSeatSnapshot> seats = stackalloc NetworkRoomSeatSnapshot[2];
        return _observer.TryCopyRoomSeats(seats, out int count) && count == 2 &&
            seats[0].ConnectionState == NetworkRoomSeatConnectionState.Connected &&
            seats[1].ConnectionState == NetworkRoomSeatConnectionState.Connected;
    }

    private AcceptancePositionEvidence CapturePosition(Entity entity)
    {
        WorldCmInt2 position = GetWorldPosition(entity);
        if (!_world.TryGet(entity, out PresentationStableId stableId) || stableId.Value <= 0)
        {
            throw new InvalidOperationException("Acceptance entity has no positive presentation stable id.");
        }
        return new AcceptancePositionEvidence
        {
            Handle = FormatHandle(entity),
            PresentationStableId = stableId.Value,
            XCm = position.X,
            YCm = position.Y,
        };
    }

    private int GetPresentationFrameId()
    {
        int count = 0;
        int frameId = -1;
        foreach (ref Chunk chunk in _world.Query(in PresentationFrameQuery))
        {
            ReadOnlySpan<PresentationFrameState> states = chunk.GetSpan<PresentationFrameState>();
            foreach (int index in chunk)
            {
                frameId = states[index].FrameId;
                count++;
            }
        }
        if (count != 1 || frameId < 0)
        {
            throw new InvalidOperationException(
                $"Acceptance requires one initialized presentation frame state; observed count={count}, frameId={frameId}.");
        }
        return frameId;
    }

    private WorldCmInt2 GetWorldPosition(Entity entity)
    {
        if (!_world.IsAlive(entity) || !_world.TryGet(entity, out WorldPositionCm position))
        {
            throw new InvalidOperationException("Acceptance entity has no live world position.");
        }
        return position.ToWorldCmInt2();
    }

    private Vector2 ProjectEntity(Entity entity)
    {
        if (!_world.TryGet(entity, out VisualTransform visual))
        {
            throw new InvalidOperationException("Acceptance entity has no visual transform for a player click.");
        }

        if (!TryProjectEntityClick(entity, out Vector2 screen))
        {
            throw new InvalidOperationException(
                "Acceptance entity has no exact player-click hit within its projected bounds.");
        }

        return screen;
    }

    private bool TryProjectEntityClick(Entity entity, out Vector2 screen)
    {
        screen = default;
        if (!SpatialBoundsUtility.TryProjectScreenBounds(
                _world,
                entity,
                _projector!,
                out ScreenRect bounds))
        {
            return false;
        }

        Entity owner = RequireLocalPlayerEntity();
        CommandSourceAcquisitionConfig acquisition =
            _engine.GetService(CoreServiceKeys.CommandSourceAcquisitionConfig)
            ?? throw new InvalidOperationException(
                "Acceptance entity click requires command-source acquisition configuration.");
        const float InnerEdgeFraction = 0.125f;
        float centerX = (bounds.MinX + bounds.MaxX) * 0.5f;
        float centerY = (bounds.MinY + bounds.MaxY) * 0.5f;
        float innerMinX = bounds.MinX + ((bounds.MaxX - bounds.MinX) * InnerEdgeFraction);
        float innerMaxX = bounds.MaxX - ((bounds.MaxX - bounds.MinX) * InnerEdgeFraction);
        float innerMinY = bounds.MinY + ((bounds.MaxY - bounds.MinY) * InnerEdgeFraction);
        float innerMaxY = bounds.MaxY - ((bounds.MaxY - bounds.MinY) * InnerEdgeFraction);
        Span<Vector2> candidates = stackalloc Vector2[]
        {
            new(centerX, centerY),
            new(innerMinX, centerY),
            new(innerMaxX, centerY),
            new(centerX, innerMinY),
            new(centerX, innerMaxY),
            new(innerMinX, innerMinY),
            new(innerMaxX, innerMinY),
            new(innerMinX, innerMaxY),
            new(innerMaxX, innerMaxY),
        };
        for (int i = 0; i < candidates.Length; i++)
        {
            Vector2 candidate = candidates[i];
            RequireFiniteScreenPoint(candidate, "entity click");
            Entity hit = CommandSourcePointerHitResolver.FindNearestInspectableEntity(
                _world,
                _engine.GlobalContext,
                owner,
                candidate,
                acquisition.ClickPickRadiusPixels);
            if (hit == entity)
            {
                screen = candidate;
                return true;
            }
        }
        return false;
    }

    private float ReadAttribute(Entity entity, int attributeId)
    {
        if (!_world.IsAlive(entity) || !_world.TryGet(entity, out AttributeBuffer attributes) ||
            !attributes.HasAttribute(attributeId))
        {
            throw new InvalidOperationException($"Acceptance entity lacks required attribute {attributeId}.");
        }
        return attributes.GetCurrent(attributeId);
    }

    private string FormatHandle(Entity entity)
    {
        if (!_world.IsAlive(entity) || !_world.TryGet(entity, out ReplicationMirrorIdentity identity) ||
            !identity.Handle.IsValid)
        {
            throw new InvalidOperationException("Acceptance client evidence requires a valid replicated entity handle.");
        }
        return $"{identity.Handle.Slot}:{identity.Handle.Generation}";
    }

    private void SortClientEntitiesByHandle(Span<Entity> entities, int count)
    {
        for (int i = 1; i < count; i++)
        {
            Entity value = entities[i];
            int slot = _world.Get<ReplicationMirrorIdentity>(value).Handle.Slot;
            int insertion = i;
            while (insertion > 0 && _world.Get<ReplicationMirrorIdentity>(entities[insertion - 1]).Handle.Slot > slot)
            {
                entities[insertion] = entities[insertion - 1];
                insertion--;
            }
            entities[insertion] = value;
        }
    }

    private void RequireInputAction(string actionId)
    {
        if (!_input!.HasAction(actionId))
        {
            throw new InvalidOperationException($"Acceptance requires configured input action '{actionId}'.");
        }
    }

    private void Pass()
    {
        RefreshEvidenceSnapshot();
        _evidence.Status = "passed";
        _evidence.CompletedAtUtc = DateTime.UtcNow.ToString("O");
        _progress.TransitionTo(AcceptanceProgressStage.Completed, _clientStage.ToString());
        AcceptanceEvidenceWriter.WriteAtomic(_evidence, _evidencePath);
        _terminal = true;
    }

    private void Fail(Exception exception)
    {
        _terminal = true;
        RefreshEvidenceSnapshot();
        _evidence.Status = "failed";
        _evidence.Failure = exception.ToString();
        _evidence.CompletedAtUtc = DateTime.UtcNow.ToString("O");
        if (_evidence.Steps.Count > 0 && _evidence.Steps[^1].Status == "running")
        {
            _evidence.Steps[^1].Status = "failed";
            _evidence.Steps[^1].CompletedAtUtc = DateTime.UtcNow.ToString("O");
            _evidence.Steps[^1].Detail = exception.Message;
        }
        _progress.TransitionTo(AcceptanceProgressStage.Failed, exception.Message);
        AcceptanceEvidenceWriter.WriteAtomic(_evidence, _evidencePath);
    }

    private void WriteRunningCheckpointIfDue()
    {
        if (_lastEvidenceCheckpointTimestamp != 0 &&
            ElapsedSeconds(_lastEvidenceCheckpointTimestamp) < _plan.EvidenceCheckpointSeconds)
        {
            return;
        }

        RefreshEvidenceSnapshot();
        AcceptanceEvidenceWriter.WriteAtomic(_evidence, _evidencePath);
        _lastEvidenceCheckpointTimestamp = Stopwatch.GetTimestamp();
    }

    private void RefreshEvidenceSnapshot()
    {
        CaptureRuntimeCheckpoint();
        _evidence.FaultCount = _observer.FaultCount;
        NetworkFaultInjectionObservationSnapshot observation = _faultInjectionMetrics.Capture();
        if (observation.Role != _role)
        {
            throw new InvalidOperationException(
                $"Fault injection metrics role '{observation.Role}' does not match acceptance role '{_role}'.");
        }
        _evidence.NetworkFaultInjection.Capture(in observation);
    }

    private void CaptureRuntimeCheckpoint()
    {
        AcceptanceRuntimeCheckpoint checkpoint = _evidence.Runtime;
        checkpoint.CapturedAtUtc = DateTime.UtcNow.ToString("O");
        checkpoint.PendingCommandAction = _pendingCommandAction;
        checkpoint.PendingCommandSequence = _pendingCommandSequence;
        checkpoint.HasBattlePoints = _battlePointsDerived;
        checkpoint.MeetingPoint = _battlePointsDerived
            ? new AcceptanceWorldPointCheckpoint { XCm = _meetingPoint.X, YCm = _meetingPoint.Y }
            : null;
        checkpoint.SiegePoint = _battlePointsDerived
            ? new AcceptanceWorldPointCheckpoint { XCm = _siegePoint.X, YCm = _siegePoint.Y }
            : null;
        checkpoint.DefeatedCorePoint = _hasLosingCoreLastPosition
            ? new AcceptanceWorldPointCheckpoint
            {
                XCm = _losingCoreLastPosition.X,
                YCm = _losingCoreLastPosition.Y,
            }
            : null;

        if (_role == NetworkProcessRole.AuthoritativeServer)
        {
            FrontlineMatchSnapshot match = _frontlineRuntime.Snapshot;
            checkpoint.Stage = "AuthoritativeMatch";
            checkpoint.Substep = 0;
            checkpoint.SelectedActors = Array.Empty<AcceptanceSelectedActorCheckpoint>();
            checkpoint.VisibleEnemyCoreCount = -1;
            checkpoint.CommittedTick = match.CommittedTick;
            checkpoint.MatchPhase = match.Phase.ToString();
            checkpoint.Outcome = match.Outcome.ToString();
            return;
        }

        checkpoint.Stage = _clientStage.ToString();
        checkpoint.Substep = _substep;
        checkpoint.SelectedActors = CaptureSelectedActorCheckpoint();
        checkpoint.VisibleEnemyCoreCount = _localSideIndex >= 0
            ? CountVisibleEnemyCores(_localSideIndex)
            : -1;
        if (TryGetClientMatchState(out FrontlineMatchStateProjection projection))
        {
            checkpoint.CommittedTick = projection.CommittedTick;
            checkpoint.MatchPhase = projection.Phase.ToString();
            checkpoint.Outcome = projection.Outcome.ToString();
        }
        else
        {
            checkpoint.CommittedTick = 0;
            checkpoint.MatchPhase = string.Empty;
            checkpoint.Outcome = string.Empty;
        }
    }

    private AcceptanceSelectedActorCheckpoint[] CaptureSelectedActorCheckpoint()
    {
        if (_collections == null)
        {
            return Array.Empty<AcceptanceSelectedActorCheckpoint>();
        }

        if (!_engine.TryGetService(CoreServiceKeys.LocalPlayerEntity, out Entity owner) ||
            !_world.IsAlive(owner))
        {
            return Array.Empty<AcceptanceSelectedActorCheckpoint>();
        }

        int count = _collections.CopyEntities(owner, EntityCollectionKeys.CommandSource, _entityScratch);
        var selected = new AcceptanceSelectedActorCheckpoint[count];
        for (int i = 0; i < count; i++)
        {
            Entity entity = _entityScratch[i];
            bool isAlive = _world.IsAlive(entity);
            ReplicationMirrorIdentity identity = default;
            WorldPositionCm position = default;
            bool hasIdentity = isAlive && _world.TryGet(entity, out identity) && identity.Handle.IsValid;
            bool hasPosition = isAlive && _world.TryGet(entity, out position);
            WorldCmInt2 worldCm = hasPosition ? position.ToWorldCmInt2() : default;
            selected[i] = new AcceptanceSelectedActorCheckpoint
            {
                Handle = hasIdentity ? $"{identity.Handle.Slot}:{identity.Handle.Generation}" : string.Empty,
                IsAlive = isAlive,
                HasReplicationIdentity = hasIdentity,
                XCm = hasPosition ? worldCm.X : null,
                YCm = hasPosition ? worldCm.Y : null,
            };
        }
        return selected;
    }

    private static bool IsAdmissionRejection(NetworkCommandAdmissionCode code) =>
        !NetworkCommandAdmissionCodeSemantics.IsAcceptedProgress(code);

    private static FrontlineMatchOutcome OutcomeForWinningSide(int sideIndex) => sideIndex switch
    {
        0 => FrontlineMatchOutcome.SideOneVictory,
        1 => FrontlineMatchOutcome.SideTwoVictory,
        _ => throw new ArgumentOutOfRangeException(nameof(sideIndex)),
    };

    private static bool Approximately(float actual, float expected) => MathF.Abs(actual - expected) <= 0.01f;

    private static double ElapsedSeconds(long startedTimestamp) =>
        (Stopwatch.GetTimestamp() - startedTimestamp) / (double)Stopwatch.Frequency;

    private static long DistanceSquared(WorldCmInt2 left, WorldCmInt2 right)
    {
        long dx = left.X - (long)right.X;
        long dy = left.Y - (long)right.Y;
        return (dx * dx) + (dy * dy);
    }

    private static double DistanceCm(AcceptancePositionEvidence left, AcceptancePositionEvidence right)
    {
        long dx = left.XCm - (long)right.XCm;
        long dy = left.YCm - (long)right.YCm;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static void RequireFiniteScreenPoint(Vector2 screen, string action)
    {
        if (!float.IsFinite(screen.X) || !float.IsFinite(screen.Y))
        {
            throw new InvalidOperationException($"Acceptance could not project a finite screen point for {action}.");
        }
    }

    private enum ClientStage : byte
    {
        Connecting = 0,
        Ready = 1,
        Gathering = 2,
        Training = 3,
        Advancing = 4,
        Engaging = 5,
        WaitingForOutcome = 6,
    }

    private enum GesturePhase : byte
    {
        None = 0,
        PressQueued = 1,
        ReleaseQueued = 2,
    }

    private enum ServerCoreTeardownStage : byte
    {
        None = 0,
        OutcomeCommitted = 1,
        DestroyPending = 2,
        Destroyed = 3,
    }

    private readonly record struct GestureState(
        GesturePhase Phase,
        string ActionId,
        Vector2? Screen,
        bool Additive,
        bool ExpectsCommand,
        ulong SubmissionRevisionBefore,
        long QueuedAtInputRevision);

    private readonly struct ClientHarvesterMarker
    {
    }

    private readonly struct ClientInfantryMarker
    {
    }
}

internal static class FrontlineConfigAcceptanceExtensions
{
    public static int ResolveSideIndexForPlayer(this FrontlineConfig config, int playerId)
    {
        for (int i = 0; i < config.Sides.Length; i++)
        {
            if (config.Sides[i].PlayerId == playerId)
            {
                return i;
            }
        }
        throw new InvalidOperationException($"Frontline player {playerId} is not declared by the match config.");
    }
}

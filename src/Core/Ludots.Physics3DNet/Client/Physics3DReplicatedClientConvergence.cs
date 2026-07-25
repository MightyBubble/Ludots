using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Session;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet.Bridge;
using Ludots.Core.Physics3DNet.Input;

namespace Ludots.Core.Physics3DNet.Client;

public interface IPhysics3DClientInputSource
{
    bool TrySampleMovement(uint targetTick, out Vector2 movement);
}

public interface IPhysics3DLocalPredictionDriver
{
    bool Supports(Physics3DNetLocalDrivenKind kind);

    bool TryStep(
        Entity entity,
        Physics3DBodyId body,
        Physics3DNetLocalDrivenKind kind,
        uint targetTick,
        in Physics3DFixedInputFrame input,
        out Physics3DBodyState predictedState);
}

/// <summary>
/// Replicated-client owner of fixed-input sampling, local correction/replay, and remote interpolation.
/// All storage is fixed at construction. Remote bodies are held kinematic and are never extrapolated.
/// </summary>
public sealed class Physics3DReplicatedClientConvergence : IFixedInputPayloadSource, ISystem<float>
{
    private enum ValidationRole : byte
    {
        None = 0,
        Local = 1,
        Remote = 2,
    }

    private const int EmptyMapKey = -1;
    private const int TombstoneMapKey = -2;

    private readonly World _world;
    private readonly IPhysics3DWorld _physics;
    private readonly Physics3DNetConfig _config;
    private readonly IPhysics3DClientInputSource _input;
    private readonly IPhysics3DLocalPredictionDriver _driver;
    private readonly Physics3DNetLocalPredictionHistory _localHistory;
    private readonly Physics3DNetRemoteInterpolationBuffer _remoteInterpolation;
    private readonly Physics3DNetPredictedPose[] _replayPoseScratch;
    private readonly byte[] _replayInputScratch;
    private readonly int _globalEntityCapacity;
    private readonly int[] _mapKeys;
    private readonly int[] _mapValues;
    private readonly int _mapMask;
    private readonly int[] _freeRemoteLanes;
    private readonly bool[] _remoteActive;
    private readonly NetworkEntityHandle[] _remoteHandles;
    private readonly NetworkEntityHandle[] _remoteBufferHandles;
    private readonly uint[] _remoteBufferGenerations;
    private readonly Entity[] _remoteEntities;
    private readonly Physics3DBodyId[] _remoteBodies;
    private readonly int[] _validationMapKeys;
    private readonly int[] _validationMapValues;
    private readonly int _validationMapMask;
    private readonly ValidationRole[] _validationRoles;
    private readonly NetworkEntityHandle[] _validationHandles;
    private readonly bool[] _validationHasUpsert;
    private readonly NetworkEntityHandle[] _validationReleaseHandles;
    private readonly Entity[] _validationReleaseEntities;

    private SessionSeatBinding _clientSeat;
    private ulong _sessionEpoch;
    private bool _sessionActive;
    private bool _localActive;
    private NetworkEntityHandle _localHandle;
    private Entity _localEntity;
    private Physics3DBodyId _localBody;
    private Physics3DNetLocalDrivenKind _localKind;
    private int _freeRemoteCount;
    private int _remoteCount;
    private uint _latestCommittedTick;
    private float _presentationTicksSinceCommit;
    private int _validationCount;
    private int _validationReleaseCount;
    private bool _validationOpen;
    private bool _validationCommitPrepared;
    private bool _validationContextValid;

    public Physics3DReplicatedClientConvergence(
        World world,
        IPhysics3DWorld physics,
        Physics3DNetConfig config,
        int globalEntityCapacity,
        int activeMirrorCapacity,
        IPhysics3DClientInputSource input,
        IPhysics3DLocalPredictionDriver driver)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        config.Validate();
        if (globalEntityCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(globalEntityCapacity));
        }

        if (activeMirrorCapacity <= 0 || activeMirrorCapacity > globalEntityCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(activeMirrorCapacity));
        }

        _globalEntityCapacity = globalEntityCapacity;
        _localHistory = new Physics3DNetLocalPredictionHistory(config);
        _remoteInterpolation = new Physics3DNetRemoteInterpolationBuffer(config, activeMirrorCapacity);
        _replayPoseScratch = new Physics3DNetPredictedPose[config.LocalPredictionHistoryTicks];
        _replayInputScratch = new byte[checked(
            config.LocalPredictionHistoryTicks * Physics3DFixedInputFrameCodec.PayloadBytes)];

        int mapCapacity = 1;
        int requiredMapCapacity = checked(activeMirrorCapacity * 2);
        while (mapCapacity < requiredMapCapacity)
        {
            mapCapacity = checked(mapCapacity << 1);
        }

        _mapKeys = new int[mapCapacity];
        Array.Fill(_mapKeys, EmptyMapKey);
        _mapValues = new int[mapCapacity];
        _mapMask = mapCapacity - 1;
        _freeRemoteLanes = new int[activeMirrorCapacity];
        _remoteActive = new bool[activeMirrorCapacity];
        _remoteHandles = new NetworkEntityHandle[activeMirrorCapacity];
        _remoteBufferHandles = new NetworkEntityHandle[activeMirrorCapacity];
        _remoteBufferGenerations = new uint[activeMirrorCapacity];
        _remoteEntities = new Entity[activeMirrorCapacity];
        _remoteBodies = new Physics3DBodyId[activeMirrorCapacity];
        int validationCapacity = checked(activeMirrorCapacity * 2);
        int validationMapCapacity = checked(mapCapacity * 2);
        _validationMapKeys = new int[validationMapCapacity];
        Array.Fill(_validationMapKeys, EmptyMapKey);
        _validationMapValues = new int[validationMapCapacity];
        _validationMapMask = validationMapCapacity - 1;
        _validationRoles = new ValidationRole[validationCapacity];
        _validationHandles = new NetworkEntityHandle[validationCapacity];
        _validationHasUpsert = new bool[validationCapacity];
        _validationReleaseHandles = new NetworkEntityHandle[activeMirrorCapacity];
        _validationReleaseEntities = new Entity[activeMirrorCapacity];
        for (int lane = 0; lane < activeMirrorCapacity; lane++)
        {
            _freeRemoteLanes[activeMirrorCapacity - lane - 1] = lane;
        }

        _freeRemoteCount = activeMirrorCapacity;
    }

    public int GlobalEntityCapacity => _globalEntityCapacity;
    public int ActiveMirrorCapacity => _remoteActive.Length;
    public int RemoteCount => _remoteCount;
    public bool HasLocalDrivenBody => _localActive;
    public NetworkEntityHandle LocalDrivenHandle => _localHandle;
    public Physics3DNetLocalDrivenKind LocalDrivenKind => _localKind;
    public uint LatestCommittedTick => _latestCommittedTick;
    public float LastRenderTick { get; private set; }
    public bool IsSessionActive => _sessionActive;
    public SessionSeatBinding ClientSeat => _clientSeat;
    public ulong SessionEpoch => _sessionEpoch;
    public Physics3DNetLocalPredictionHistory LocalHistory => _localHistory;

    public FixedInputPayloadSampleStatus TrySample(uint targetTick, Span<byte> destination)
    {
        if (!_sessionActive || !_localActive ||
            targetTick == 0 ||
            destination.Length != Physics3DFixedInputFrameCodec.PayloadBytes ||
            !_input.TrySampleMovement(targetTick, out Vector2 movement) ||
            !Physics3DFixedInputFrameCodec.TryEncode(movement, destination))
        {
            return FixedInputPayloadSampleStatus.Failed;
        }

        return FixedInputPayloadSampleStatus.Sampled;
    }

    public FixedInputPayloadCommitStatus TryCommit(uint targetTick, ReadOnlySpan<byte> sentPayload)
    {
        if (!_sessionActive || !_localActive ||
            targetTick == 0 ||
            sentPayload.Length != Physics3DFixedInputFrameCodec.PayloadBytes ||
            !Physics3DFixedInputFrameCodec.TryDecode(sentPayload, out Physics3DFixedInputFrame input) ||
            !_world.IsAlive(_localEntity) ||
            !_physics.ContainsBody(_localBody) ||
            !_driver.Supports(_localKind))
        {
            return FixedInputPayloadCommitStatus.Failed;
        }

        _localHistory.EnsureCanRecord(targetTick, sentPayload);
        Physics3DBodyState previous = _physics.GetBodyState(_localBody);
        try
        {
            if (!_driver.TryStep(
                    _localEntity,
                    _localBody,
                    _localKind,
                    targetTick,
                    in input,
                    out Physics3DBodyState predicted))
            {
                _physics.SetBodyState(_localBody, in previous);
                return FixedInputPayloadCommitStatus.Failed;
            }

            Physics3DNetPredictedPose pose = ToPredictedPose(targetTick, in predicted);
            _localHistory.Record(in pose, sentPayload);
            SetEntityPose(_localEntity, in predicted);
            return FixedInputPayloadCommitStatus.Committed;
        }
        catch
        {
            _physics.SetBodyState(_localBody, in previous);
            throw;
        }
    }

    public void OnBatchValidationBeginning(in ReplicationApplyContext context)
    {
        ClearValidationPlan();
        _validationOpen = true;
        SessionSeatBinding seat = context.ClientSeat;
        _validationContextValid = seat.IsValid &&
            context.SessionEpoch != 0 &&
            context.CommittedTick != 0 &&
            (!_sessionActive || (_clientSeat == seat && _sessionEpoch == context.SessionEpoch)) &&
            (_latestCommittedTick == 0 || context.CommittedTick >= _latestCommittedTick);
    }

    public bool CanCommitBatchValidation()
    {
        if (!_validationOpen || !_validationContextValid)
        {
            return false;
        }

        int localCount = _localActive ? 1 : 0;
        int remoteCount = _remoteCount;
        for (int index = 0; index < _validationCount; index++)
        {
            int slot = _validationHandles[index].Slot;
            ValidationRole original = GetCommittedRole(slot);
            if (original == ValidationRole.Local)
            {
                localCount--;
            }
            else if (original == ValidationRole.Remote)
            {
                remoteCount--;
            }

            if (_validationRoles[index] == ValidationRole.Local)
            {
                localCount++;
            }
            else if (_validationRoles[index] == ValidationRole.Remote)
            {
                remoteCount++;
            }
        }

        return localCount is >= 0 and <= 1 &&
            remoteCount >= 0 &&
            remoteCount <= _remoteActive.Length;
    }

    public void OnBatchCommitBeginning()
    {
        if (!CanCommitBatchValidation())
        {
            throw new InvalidOperationException("Physics3D client convergence cannot commit an invalid replication batch.");
        }

        _validationCommitPrepared = true;
        if (_localActive &&
            TryFindValidation(_localHandle.Slot, out int localPlan) &&
            (_validationRoles[localPlan] != ValidationRole.Local ||
             _validationHandles[localPlan] != _localHandle))
        {
            ClearLocal();
        }

        for (int lane = 0; lane < _remoteActive.Length; lane++)
        {
            if (!_remoteActive[lane] ||
                !TryFindValidation(_remoteHandles[lane].Slot, out int plan) ||
                (_validationRoles[plan] == ValidationRole.Remote &&
                 _validationHandles[plan] == _remoteHandles[lane]))
            {
                continue;
            }

            NetworkEntityHandle handle = _remoteHandles[lane];
            Entity entity = _remoteEntities[lane];
            ReleaseRemoteLane(lane, in handle, entity);
        }
    }

    public void OnBatchEnded(bool committed)
    {
        ClearValidationPlan();
    }

    public bool CanAccept(
        Entity entity,
        in NetworkEntityHandle handle,
        bool locallyControlled,
        Physics3DNetLocalDrivenKind localKind)
    {
        if (entity == Entity.Null || !handle.IsValid ||
            (uint)handle.Slot >= (uint)_globalEntityCapacity)
        {
            return false;
        }

        if (_validationOpen)
        {
            bool committedIdentityMatches =
                (_localActive && _localHandle == handle && _localEntity == entity) ||
                (TryFindRemoteLane(handle.Slot, out int committedLane) &&
                 _remoteActive[committedLane] &&
                 _remoteHandles[committedLane] == handle &&
                 _remoteEntities[committedLane] == entity);
            if (!committedIdentityMatches ||
                (locallyControlled && !_driver.Supports(localKind)))
            {
                return false;
            }

            return TryPlanValidationRole(
                in handle,
                locallyControlled ? ValidationRole.Local : ValidationRole.Remote,
                hasUpsert: true);
        }

        if (locallyControlled)
        {
            return _driver.Supports(localKind) &&
                (!_localActive || (_localHandle == handle && _localEntity == entity));
        }

        if (TryFindRemoteLane(handle.Slot, out int lane))
        {
            return _remoteActive[lane] &&
                _remoteHandles[lane] == handle &&
                _remoteEntities[lane] == entity;
        }

        return _freeRemoteCount > 0;
    }

    public bool CanAcceptCreate(
        in NetworkEntityHandle handle,
        bool locallyControlled,
        Physics3DNetLocalDrivenKind localKind)
    {
        if (!handle.IsValid || (uint)handle.Slot >= (uint)_globalEntityCapacity)
        {
            return false;
        }

        if (_validationOpen)
        {
            return (!locallyControlled || _driver.Supports(localKind)) &&
                TryPlanValidationRole(
                    in handle,
                    locallyControlled ? ValidationRole.Local : ValidationRole.Remote,
                    hasUpsert: true);
        }

        if (locallyControlled)
        {
            return !_localActive && _driver.Supports(localKind);
        }

        return !TryFindRemoteLane(handle.Slot, out _) && _freeRemoteCount > 0;
    }

    public bool CanRelease(Entity entity, in NetworkEntityHandle handle)
    {
        if (_localActive && _localHandle == handle && _localEntity == entity)
        {
            if (_validationOpen)
            {
                return TryRecordValidationRelease(entity, in handle) &&
                    TryPlanValidationRole(in handle, ValidationRole.None, hasUpsert: false);
            }

            return true;
        }

        bool matchesRemote = TryFindRemoteLane(handle.Slot, out int lane) &&
            _remoteActive[lane] &&
            _remoteHandles[lane] == handle &&
            _remoteEntities[lane] == entity;
        if (!matchesRemote)
        {
            return false;
        }

        return !_validationOpen ||
            (TryRecordValidationRelease(entity, in handle) &&
             TryPlanValidationRole(in handle, ValidationRole.None, hasUpsert: false));
    }

    public void ApplyAuthoritative(
        Entity entity,
        Physics3DBodyId body,
        in NetworkEntityHandle handle,
        bool locallyControlled,
        Physics3DNetLocalDrivenKind localKind,
        in Physics3DBodyState authoritative,
        in ReplicationApplyContext context)
    {
        SessionSeatBinding contextSeat = context.ClientSeat;
        ValidateAuthoritativeAcceptance(
            entity,
            body,
            in handle,
            locallyControlled,
            localKind,
            in context);
        EnsureSession(in contextSeat, context.SessionEpoch);
        if (locallyControlled)
        {
            if (TryFindRemoteLane(handle.Slot, out int remoteLane))
            {
                ReleaseRemoteLane(remoteLane, in handle, entity);
            }

            BindOrValidateLocal(entity, body, in handle, localKind);
            CorrectLocal(in authoritative, context.CommittedTick);
            ObserveCommittedTick(context.CommittedTick);
            return;
        }

        if (_localActive && _localHandle == handle && _localEntity == entity)
        {
            ClearLocal();
        }

        int lane = TrackOrValidateRemote(entity, body, in handle);
        NetworkEntityHandle bufferHandle = _remoteBufferHandles[lane];
        _remoteInterpolation.Push(
            in bufferHandle,
            new Physics3DNetRemoteSample(
                context.CommittedTick,
                authoritative.PositionCm,
                authoritative.Orientation,
                authoritative.LinearVelocityCmPerSecond,
                authoritative.AngularVelocityRadiansPerSecond));
        Physics3DBodyState frozen = FreezeRemote(in authoritative);
        _physics.SetBodyState(body, in frozen);
        if (_remoteInterpolation.GetSampleCount(in bufferHandle) == 1)
        {
            SetEntityPose(entity, in frozen);
        }

        ObserveCommittedTick(context.CommittedTick);
    }

    public void Release(
        Entity entity,
        in NetworkEntityHandle handle,
        in ReplicationApplyContext context)
    {
        SessionSeatBinding contextSeat = context.ClientSeat;
        RequireSession(in contextSeat, context.SessionEpoch);
        if (_localActive && _localHandle == handle && _localEntity == entity)
        {
            ClearLocal();
            return;
        }

        if (!TryFindRemoteLane(handle.Slot, out int lane))
        {
            if (_validationCommitPrepared && WasValidatedRelease(entity, in handle))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Physics3D client convergence has no remote body {handle.Slot}:{handle.Generation} to release.");
        }

        ReleaseRemoteLane(lane, in handle, entity);
    }

    public void Teardown(in SessionSeatBinding seat, ulong sessionEpoch)
    {
        if (!_sessionActive)
        {
            return;
        }

        RequireSession(in seat, sessionEpoch);
        ClearValidationPlan();
        ClearLocal();
        _remoteInterpolation.Reset();
        Array.Fill(_mapKeys, EmptyMapKey);
        Array.Clear(_mapValues);
        Array.Clear(_remoteActive);
        Array.Clear(_remoteHandles);
        Array.Clear(_remoteBufferHandles);
        Array.Clear(_remoteEntities);
        Array.Clear(_remoteBodies);
        for (int lane = 0; lane < _remoteActive.Length; lane++)
        {
            _freeRemoteLanes[_remoteActive.Length - lane - 1] = lane;
        }

        _freeRemoteCount = _remoteActive.Length;
        _remoteCount = 0;
        _clientSeat = default;
        _sessionEpoch = 0;
        _sessionActive = false;
        _latestCommittedTick = 0;
        _presentationTicksSinceCommit = 0f;
        LastRenderTick = 0f;
    }

    public void AbortSession()
    {
        if (_sessionActive)
        {
            SessionSeatBinding seat = _clientSeat;
            ulong epoch = _sessionEpoch;
            Teardown(in seat, epoch);
            return;
        }

        ClearValidationPlan();
    }

    public bool TrySampleRemote(
        in NetworkEntityHandle handle,
        float renderTick,
        out Physics3DNetInterpolationSample sample)
    {
        sample = default;
        if (!float.IsFinite(renderTick) || renderTick < 0f ||
            !TryFindRemoteLane(handle.Slot, out int lane) ||
            !_remoteActive[lane] || _remoteHandles[lane] != handle)
        {
            return false;
        }

        NetworkEntityHandle bufferHandle = _remoteBufferHandles[lane];
        sample = _remoteInterpolation.Sample(in bufferHandle, renderTick);
        return true;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float deltaTime) { }
    public void AfterUpdate(in float deltaTime) { }
    public void Dispose()
    {
        AbortSession();
    }

    public void Update(in float renderDeltaTime)
    {
        if (!float.IsFinite(renderDeltaTime) || renderDeltaTime < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(renderDeltaTime));
        }

        if (!_sessionActive || _latestCommittedTick == 0)
        {
            return;
        }

        int interval = _config.SnapshotIntervalTicks;
        _presentationTicksSinceCommit = MathF.Min(
            interval,
            _presentationTicksSinceCommit + (renderDeltaTime * _config.AuthoritativeHz));
        float renderTick = MathF.Max(
            0f,
            _latestCommittedTick - interval + _presentationTicksSinceCommit);
        LastRenderTick = MathF.Min(renderTick, _latestCommittedTick);

        for (int lane = 0; lane < _remoteActive.Length; lane++)
        {
            if (!_remoteActive[lane])
            {
                continue;
            }

            Entity entity = _remoteEntities[lane];
            Physics3DBodyId body = _remoteBodies[lane];
            if (!_world.IsAlive(entity) || !_physics.ContainsBody(body))
            {
                throw new InvalidOperationException("Tracked Physics3D remote body disappeared outside replication teardown.");
            }

            NetworkEntityHandle bufferHandle = _remoteBufferHandles[lane];
            Physics3DNetInterpolationSample sample = _remoteInterpolation.Sample(in bufferHandle, LastRenderTick);
            var frozen = new Physics3DBodyState
            {
                PositionCm = sample.PositionCm,
                Orientation = sample.Orientation,
                LinearVelocityCmPerSecond = Vector3.Zero,
                AngularVelocityRadiansPerSecond = Vector3.Zero,
                Awake = true,
            };
            _physics.SetBodyState(body, in frozen);
            SetEntityPose(entity, in frozen);
        }
    }

    private void CorrectLocal(in Physics3DBodyState authoritative, uint committedTick)
    {
        if (!_localHistory.IsBound)
        {
            throw new InvalidOperationException("Local convergence body has no prediction history binding.");
        }

        Physics3DNetCorrectionReplayRange replay = _localHistory.PrepareCorrectionReplay(
            _localHandle.Slot,
            _localHandle.Generation,
            committedTick,
            _replayPoseScratch,
            _replayInputScratch);
        Physics3DBodyState previous = _physics.GetBodyState(_localBody);
        try
        {
            _physics.SetBodyState(_localBody, in authoritative);
            Physics3DBodyState final = authoritative;
            for (int index = 0; index < replay.FrameCount; index++)
            {
                ReadOnlySpan<byte> payload = _replayInputScratch.AsSpan(
                    index * Physics3DFixedInputFrameCodec.PayloadBytes,
                    Physics3DFixedInputFrameCodec.PayloadBytes);
                if (!Physics3DFixedInputFrameCodec.TryDecode(payload, out Physics3DFixedInputFrame input))
                {
                    throw new InvalidOperationException("Validated local prediction history contains an invalid payload.");
                }

                uint replayTick = checked((uint)_replayPoseScratch[index].Tick);
                if (!_driver.TryStep(
                        _localEntity,
                        _localBody,
                        _localKind,
                        replayTick,
                        in input,
                        out final))
                {
                    throw new InvalidOperationException($"Local prediction driver failed replay tick {replayTick}.");
                }

                _replayPoseScratch[index] = ToPredictedPose(replayTick, in final);
            }

            _localHistory.Confirm(_localHandle.Slot, _localHandle.Generation, committedTick);
            _localHistory.DiscardConfirmed();
            for (int index = 0; index < replay.FrameCount; index++)
            {
                ReadOnlySpan<byte> payload = _replayInputScratch.AsSpan(
                    index * Physics3DFixedInputFrameCodec.PayloadBytes,
                    Physics3DFixedInputFrameCodec.PayloadBytes);
                _localHistory.ReplaceExisting(in _replayPoseScratch[index], payload);
            }

            SetEntityPose(_localEntity, in final);
        }
        catch
        {
            _physics.SetBodyState(_localBody, in previous);
            throw;
        }
    }

    private void ValidateAuthoritativeAcceptance(
        Entity entity,
        Physics3DBodyId body,
        in NetworkEntityHandle handle,
        bool locallyControlled,
        Physics3DNetLocalDrivenKind localKind,
        in ReplicationApplyContext context)
    {
        SessionSeatBinding seat = context.ClientSeat;
        if (!seat.IsValid ||
            context.SessionEpoch == 0 ||
            context.CommittedTick == 0 ||
            entity == Entity.Null ||
            !_world.IsAlive(entity) ||
            !handle.IsValid ||
            (uint)handle.Slot >= (uint)_globalEntityCapacity ||
            !_physics.ContainsBody(body) ||
            (_sessionActive && (_clientSeat != seat || _sessionEpoch != context.SessionEpoch)) ||
            (_latestCommittedTick != 0 && context.CommittedTick < _latestCommittedTick))
        {
            throw new InvalidOperationException("Physics3D client convergence rejected authoritative body state.");
        }

        if (locallyControlled)
        {
            if (!_driver.Supports(localKind) ||
                (_localActive &&
                 (_localHandle != handle || _localEntity != entity || _localKind != localKind)))
            {
                throw new InvalidOperationException("Physics3D client convergence rejected local ownership state.");
            }

            if (TryFindRemoteLane(handle.Slot, out int localizingLane) &&
                (!_remoteActive[localizingLane] ||
                 _remoteHandles[localizingLane] != handle ||
                 _remoteEntities[localizingLane] != entity))
            {
                throw new InvalidOperationException("Physics3D client convergence rejected localizing identity.");
            }

            if (_localActive && _localHistory.IsBound)
            {
                _localHistory.PrepareCorrectionReplay(
                    handle.Slot,
                    handle.Generation,
                    context.CommittedTick,
                    _replayPoseScratch,
                    _replayInputScratch);
            }

            return;
        }

        if (_localActive &&
            _localHandle.Slot == handle.Slot &&
            (_localHandle != handle || _localEntity != entity))
        {
            throw new InvalidOperationException("Physics3D client convergence rejected remote ownership identity.");
        }

        if (TryFindRemoteLane(handle.Slot, out int remoteLane))
        {
            if (!_remoteActive[remoteLane] ||
                _remoteHandles[remoteLane] != handle ||
                _remoteEntities[remoteLane] != entity)
            {
                throw new InvalidOperationException("Physics3D client convergence rejected remote interpolation identity.");
            }

            NetworkEntityHandle bufferHandle = _remoteBufferHandles[remoteLane];
            Physics3DNetInterpolationSample newest = _remoteInterpolation.Sample(in bufferHandle, float.MaxValue);
            if (newest.UpperTick > 0 && context.CommittedTick <= (uint)newest.UpperTick)
            {
                throw new Physics3DNetTemporalOrderException(
                    handle.Slot,
                    newest.UpperTick,
                    context.CommittedTick);
            }

            return;
        }

        if (_freeRemoteCount == 0)
        {
            throw new Physics3DNetCapacityExceededException(
                "remote interpolation entities",
                _remoteActive.Length,
                context.CommittedTick);
        }
    }

    private void BindOrValidateLocal(
        Entity entity,
        Physics3DBodyId body,
        in NetworkEntityHandle handle,
        Physics3DNetLocalDrivenKind kind)
    {
        if (!_driver.Supports(kind))
        {
            throw new InvalidOperationException($"Local prediction driver does not support {kind}.");
        }

        if (_localActive)
        {
            if (_localHandle != handle || _localEntity != entity || _localKind != kind)
            {
                throw new InvalidOperationException("A replicated client may predict only one explicitly owned Character/Vehicle.");
            }

            _localBody = body;
            return;
        }

        _localHistory.BindLocalDriven(handle.Slot, handle.Generation, kind);
        _localActive = true;
        _localHandle = handle;
        _localEntity = entity;
        _localBody = body;
        _localKind = kind;
    }

    private void ClearLocal()
    {
        _localHistory.Reset();
        _localActive = false;
        _localHandle = default;
        _localEntity = Entity.Null;
        _localBody = default;
        _localKind = default;
    }

    private int TrackOrValidateRemote(
        Entity entity,
        Physics3DBodyId body,
        in NetworkEntityHandle handle)
    {
        if (TryFindRemoteLane(handle.Slot, out int existing))
        {
            if (!_remoteActive[existing] ||
                _remoteHandles[existing] != handle ||
                _remoteEntities[existing] != entity)
            {
                throw new InvalidOperationException(
                    $"Remote interpolation identity mismatch for {handle.Slot}:{handle.Generation}.");
            }

            _remoteBodies[existing] = body;
            return existing;
        }

        if (_freeRemoteCount == 0)
        {
            throw new Physics3DNetCapacityExceededException(
                "remote interpolation entities",
                _remoteActive.Length,
                _latestCommittedTick);
        }

        int lane = _freeRemoteLanes[--_freeRemoteCount];
        if (_remoteBufferGenerations[lane] == uint.MaxValue)
        {
            throw new InvalidOperationException($"Remote interpolation lane {lane} generation overflowed.");
        }

        uint bufferGeneration = ++_remoteBufferGenerations[lane];
        var bufferHandle = new NetworkEntityHandle(lane, bufferGeneration);
        if (!TryAddRemoteMap(handle.Slot, lane))
        {
            _freeRemoteLanes[_freeRemoteCount++] = lane;
            throw new InvalidOperationException("Remote interpolation sparse map rejected a validated slot.");
        }

        _remoteInterpolation.Track(in bufferHandle);
        _remoteActive[lane] = true;
        _remoteHandles[lane] = handle;
        _remoteBufferHandles[lane] = bufferHandle;
        _remoteEntities[lane] = entity;
        _remoteBodies[lane] = body;
        _remoteCount++;
        return lane;
    }

    private void ReleaseRemoteLane(int lane, in NetworkEntityHandle handle, Entity entity)
    {
        if (!_remoteActive[lane] ||
            _remoteHandles[lane] != handle ||
            _remoteEntities[lane] != entity)
        {
            throw new InvalidOperationException(
                $"Remote interpolation release mismatch for {handle.Slot}:{handle.Generation}.");
        }

        NetworkEntityHandle bufferHandle = _remoteBufferHandles[lane];
        _remoteInterpolation.Untrack(in bufferHandle);
        if (!TryRemoveRemoteMap(handle.Slot, out int mappedLane) || mappedLane != lane)
        {
            throw new InvalidOperationException("Remote interpolation sparse map lost its tracked lane.");
        }

        _remoteActive[lane] = false;
        _remoteHandles[lane] = default;
        _remoteBufferHandles[lane] = default;
        _remoteEntities[lane] = Entity.Null;
        _remoteBodies[lane] = default;
        _freeRemoteLanes[_freeRemoteCount++] = lane;
        _remoteCount--;
    }

    private void EnsureSession(in SessionSeatBinding seat, ulong sessionEpoch)
    {
        if (!seat.IsValid || sessionEpoch == 0)
        {
            throw new InvalidOperationException("Physics3D client convergence requires an accepted seat and epoch.");
        }

        if (!_sessionActive)
        {
            _clientSeat = seat;
            _sessionEpoch = sessionEpoch;
            _sessionActive = true;
            return;
        }

        RequireSession(in seat, sessionEpoch);
    }

    private void RequireSession(in SessionSeatBinding seat, ulong sessionEpoch)
    {
        if (!_sessionActive || _clientSeat != seat || _sessionEpoch != sessionEpoch)
        {
            throw new InvalidOperationException(
                "Physics3D client convergence rejected stale session or seat-generation state.");
        }
    }

    private void ObserveCommittedTick(uint committedTick)
    {
        if (_latestCommittedTick != 0 && committedTick < _latestCommittedTick)
        {
            throw new InvalidOperationException(
                $"Physics3D client committed tick regressed from {_latestCommittedTick} to {committedTick}.");
        }

        if (committedTick > _latestCommittedTick)
        {
            _latestCommittedTick = committedTick;
            _presentationTicksSinceCommit = 0f;
        }
    }

    private void SetEntityPose(Entity entity, in Physics3DBodyState state)
    {
        Physics3DPoseCm current = _world.Get<Physics3DPoseCm>(entity);
        var previous = new PreviousPhysics3DPoseCm
        {
            Position = current.Position,
            Orientation = current.Orientation,
        };
        var next = new Physics3DPoseCm
        {
            Position = state.PositionCm,
            Orientation = state.Orientation,
            LinearVelocity = state.LinearVelocityCmPerSecond,
            AngularVelocity = state.AngularVelocityRadiansPerSecond,
        };
        _world.Set(entity, previous);
        _world.Set(entity, next);
    }

    private static Physics3DBodyState FreezeRemote(in Physics3DBodyState authoritative) => new()
    {
        PositionCm = authoritative.PositionCm,
        Orientation = authoritative.Orientation,
        LinearVelocityCmPerSecond = Vector3.Zero,
        AngularVelocityRadiansPerSecond = Vector3.Zero,
        Awake = true,
    };

    private static Physics3DNetPredictedPose ToPredictedPose(uint tick, in Physics3DBodyState state) => new(
        tick,
        state.PositionCm,
        state.Orientation,
        state.LinearVelocityCmPerSecond,
        state.AngularVelocityRadiansPerSecond);

    private ValidationRole GetCommittedRole(int globalSlot)
    {
        if (_localActive && _localHandle.Slot == globalSlot)
        {
            return ValidationRole.Local;
        }

        return TryFindRemoteLane(globalSlot, out int lane) && _remoteActive[lane]
            ? ValidationRole.Remote
            : ValidationRole.None;
    }

    private bool TryPlanValidationRole(
        in NetworkEntityHandle handle,
        ValidationRole role,
        bool hasUpsert)
    {
        if (!_validationOpen)
        {
            return false;
        }

        if (TryFindValidation(handle.Slot, out int existing))
        {
            if (!hasUpsert && _validationHasUpsert[existing])
            {
                return true;
            }

            _validationRoles[existing] = role;
            _validationHandles[existing] = handle;
            _validationHasUpsert[existing] |= hasUpsert;
            return true;
        }

        if (_validationCount == _validationRoles.Length)
        {
            return false;
        }

        int index = Hash(handle.Slot) & _validationMapMask;
        int tombstone = -1;
        for (int probe = 0; probe < _validationMapKeys.Length; probe++)
        {
            int key = _validationMapKeys[index];
            if (key == TombstoneMapKey && tombstone < 0)
            {
                tombstone = index;
            }
            else if (key == EmptyMapKey)
            {
                int target = tombstone >= 0 ? tombstone : index;
                int plan = _validationCount++;
                _validationMapKeys[target] = handle.Slot;
                _validationMapValues[target] = plan;
                _validationRoles[plan] = role;
                _validationHandles[plan] = handle;
                _validationHasUpsert[plan] = hasUpsert;
                return true;
            }

            index = (index + 1) & _validationMapMask;
        }

        return false;
    }

    private bool TryFindValidation(int globalSlot, out int plan)
    {
        int index = Hash(globalSlot) & _validationMapMask;
        for (int probe = 0; probe < _validationMapKeys.Length; probe++)
        {
            int key = _validationMapKeys[index];
            if (key == EmptyMapKey)
            {
                plan = -1;
                return false;
            }

            if (key == globalSlot)
            {
                plan = _validationMapValues[index];
                return true;
            }

            index = (index + 1) & _validationMapMask;
        }

        plan = -1;
        return false;
    }

    private bool TryRecordValidationRelease(Entity entity, in NetworkEntityHandle handle)
    {
        for (int index = 0; index < _validationReleaseCount; index++)
        {
            if (_validationReleaseHandles[index] == handle)
            {
                return _validationReleaseEntities[index] == entity;
            }
        }

        if (_validationReleaseCount == _validationReleaseHandles.Length)
        {
            return false;
        }

        _validationReleaseHandles[_validationReleaseCount] = handle;
        _validationReleaseEntities[_validationReleaseCount] = entity;
        _validationReleaseCount++;
        return true;
    }

    private bool WasValidatedRelease(Entity entity, in NetworkEntityHandle handle)
    {
        for (int index = 0; index < _validationReleaseCount; index++)
        {
            if (_validationReleaseHandles[index] == handle &&
                _validationReleaseEntities[index] == entity)
            {
                return true;
            }
        }

        return false;
    }

    private void ClearValidationPlan()
    {
        Array.Fill(_validationMapKeys, EmptyMapKey);
        Array.Clear(_validationMapValues);
        Array.Clear(_validationRoles);
        Array.Clear(_validationHandles);
        Array.Clear(_validationHasUpsert);
        Array.Clear(_validationReleaseHandles);
        Array.Clear(_validationReleaseEntities);
        _validationCount = 0;
        _validationReleaseCount = 0;
        _validationOpen = false;
        _validationCommitPrepared = false;
        _validationContextValid = false;
    }

    private bool TryFindRemoteLane(int globalSlot, out int lane)
    {
        int index = Hash(globalSlot) & _mapMask;
        for (int probe = 0; probe < _mapKeys.Length; probe++)
        {
            int key = _mapKeys[index];
            if (key == EmptyMapKey)
            {
                lane = -1;
                return false;
            }

            if (key == globalSlot)
            {
                lane = _mapValues[index];
                return true;
            }

            index = (index + 1) & _mapMask;
        }

        lane = -1;
        return false;
    }

    private bool TryAddRemoteMap(int globalSlot, int lane)
    {
        int index = Hash(globalSlot) & _mapMask;
        int tombstone = -1;
        for (int probe = 0; probe < _mapKeys.Length; probe++)
        {
            int key = _mapKeys[index];
            if (key == globalSlot)
            {
                return false;
            }

            if (key == TombstoneMapKey && tombstone < 0)
            {
                tombstone = index;
            }
            else if (key == EmptyMapKey)
            {
                int target = tombstone >= 0 ? tombstone : index;
                _mapKeys[target] = globalSlot;
                _mapValues[target] = lane;
                return true;
            }

            index = (index + 1) & _mapMask;
        }

        if (tombstone >= 0)
        {
            _mapKeys[tombstone] = globalSlot;
            _mapValues[tombstone] = lane;
            return true;
        }

        return false;
    }

    private bool TryRemoveRemoteMap(int globalSlot, out int lane)
    {
        int index = Hash(globalSlot) & _mapMask;
        for (int probe = 0; probe < _mapKeys.Length; probe++)
        {
            int key = _mapKeys[index];
            if (key == EmptyMapKey)
            {
                lane = -1;
                return false;
            }

            if (key == globalSlot)
            {
                lane = _mapValues[index];
                _mapKeys[index] = TombstoneMapKey;
                _mapValues[index] = 0;
                return true;
            }

            index = (index + 1) & _mapMask;
        }

        lane = -1;
        return false;
    }

    private static int Hash(int value)
    {
        uint x = unchecked((uint)value);
        x ^= x >> 16;
        x *= 0x7feb352dU;
        x ^= x >> 15;
        x *= 0x846ca68bU;
        x ^= x >> 16;
        return unchecked((int)x);
    }
}

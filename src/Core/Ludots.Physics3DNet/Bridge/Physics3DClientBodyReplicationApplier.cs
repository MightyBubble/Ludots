using Arch.Core;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet.Client;

namespace Ludots.Core.Physics3DNet.Bridge;

public sealed class Physics3DClientBodyReplicationApplier
    : IClientReplicationSchemaApplier,
      IClientReplicationBatchValidationParticipant
{
    private readonly IPhysics3DWorld _physics;
    private readonly int _schemaId;
    private readonly Physics3DReplicationQuantizationConfig _quantization;
    private readonly Physics3DNetworkPlayerBodyConfig _bodyConfig;
    private readonly Physics3DReplicatedClientConvergence _convergence;
    private readonly Physics3DShapeId _shape;

    public Physics3DClientBodyReplicationApplier(
        IPhysics3DWorld physics,
        int schemaId,
        Physics3DReplicationQuantizationConfig quantization,
        Physics3DNetworkPlayerBodyConfig bodyConfig,
        Physics3DReplicatedClientConvergence convergence)
    {
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        if (schemaId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaId));
        }

        _quantization = quantization ?? throw new ArgumentNullException(nameof(quantization));
        _bodyConfig = bodyConfig ?? throw new ArgumentNullException(nameof(bodyConfig));
        _convergence = convergence ?? throw new ArgumentNullException(nameof(convergence));
        _quantization.Validate();
        _bodyConfig.Validate();
        _schemaId = schemaId;
        _shape = physics.RegisterCapsuleShape(_bodyConfig.RadiusCm, _bodyConfig.CylinderLengthCm);
    }

    public void OnBatchValidationBeginning(in ReplicationApplyContext context) =>
        _convergence.OnBatchValidationBeginning(in context);

    public bool CanCommitBatchValidation() => _convergence.CanCommitBatchValidation();

    public void OnBatchCommitBeginning() => _convergence.OnBatchCommitBeginning();

    public void OnBatchEnded(bool committed) => _convergence.OnBatchEnded(committed);

    public bool CanCreate(World world, in ReplicatedEntityState state, in ReplicationApplyContext context)
    {
        bool local = TryResolveLocalKind(state.Ownership, in context, out Physics3DNetLocalDrivenKind localKind);
        NetworkEntityHandle handle = state.Entity;
        return world != null &&
            state.SchemaId == _schemaId &&
            IsSupportedOwnership(state.Ownership) &&
            TryDecodeDynamic(state.Values, out _) &&
            _convergence.CanAcceptCreate(in handle, local, localKind);
    }

    public bool CanApply(
        World world,
        Entity entity,
        in ReplicatedEntityState state,
        in ReplicationApplyContext context)
    {
        if (!TryValidateApplyState(
                world,
                entity,
                in state,
                in context,
                out Physics3DBodyCm body,
                out _,
                out bool local,
                out Physics3DNetLocalDrivenKind localKind))
        {
            return false;
        }

        NetworkEntityHandle handle = state.Entity;
        Physics3DBodyKind clientKind = local ? Physics3DBodyKind.Dynamic : Physics3DBodyKind.Kinematic;
        return (body.Kind == clientKind ||
                _physics.ActiveMobileBodyCount < _physics.MobileBodyCapacity) &&
            _convergence.CanAccept(entity, in handle, local, localKind);
    }

    public bool CanRelease(
        World world,
        Entity entity,
        ReplicationMirrorLeaveKind leaveKind,
        in ReplicationApplyContext context)
    {
        if (world == null ||
            !world.IsAlive(entity) ||
            !world.TryGet(entity, out Physics3DBodyCm body) ||
            !_physics.ContainsBody(body.Id) ||
            !world.TryGet(entity, out Physics3DNetworkClientMirror mirror) ||
            mirror.SessionEpoch != context.SessionEpoch ||
            !world.TryGet(entity, out ReplicationMirrorIdentity identity))
        {
            return false;
        }

        NetworkEntityHandle handle = identity.Handle;
        return _convergence.CanRelease(entity, in handle);
    }

    public Entity Create(
        World world,
        in ReplicationMirrorIdentity identity,
        in ReplicationMirrorState state,
        in ReplicationApplyContext context)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (state.SchemaId != _schemaId || !TryDecodeDynamic(state.Values, out Physics3DBodyState decoded))
        {
            throw new InvalidOperationException($"Physics3D schema {_schemaId} rejected replicated create payload.");
        }

        var schema = new ReplicationSchemaRef(_schemaId);
        bool local = TryResolveLocalKind(state.Ownership, in context, out Physics3DNetLocalDrivenKind localKind);
        Physics3DBodyKind clientKind = local ? Physics3DBodyKind.Dynamic : Physics3DBodyKind.Kinematic;
        var body = new Physics3DBodyCm { Kind = clientKind };
        var pose = ToPose(in decoded);
        var previous = new PreviousPhysics3DPoseCm
        {
            Position = decoded.PositionCm,
            Orientation = decoded.Orientation,
        };
        var replicated = new Physics3DNetworkReplicatedBody
        {
            Handle = identity.Handle,
            AuthoritativeKind = Physics3DBodyKind.Dynamic,
            Ownership = state.Ownership,
        };
        var mirror = new Physics3DNetworkClientMirror
        {
            AuthoritativeKind = Physics3DBodyKind.Dynamic,
            IsLocallyControlled = local,
            SessionEpoch = context.SessionEpoch,
            LastCommittedTick = context.CommittedTick,
        };
        Entity entity = world.Create(
            in identity,
            in state,
            in schema,
            in body,
            in pose,
            in previous,
            in replicated,
            in mirror);

        Physics3DBodyId createdBody = default;
        bool bodyCreated = false;
        try
        {
            Physics3DBodyDescription description = CreateDescription(entity, clientKind, in decoded);
            createdBody = _physics.CreateBody(in description);
            bodyCreated = true;
            body.Id = createdBody;
            world.Set(entity, body);
            NetworkEntityHandle handle = identity.Handle;
            _convergence.ApplyAuthoritative(
                entity,
                body.Id,
                in handle,
                local,
                localKind,
                in decoded,
                in context);
            return entity;
        }
        catch
        {
            if (bodyCreated && _physics.ContainsBody(createdBody))
            {
                _physics.DestroyBody(createdBody);
            }

            if (world.IsAlive(entity))
            {
                world.Destroy(entity);
            }

            throw;
        }
    }

    public void Apply(
        World world,
        Entity entity,
        in ReplicatedEntityState state,
        in ReplicationApplyContext context)
    {
        if (!TryValidateApplyState(
                world,
                entity,
                in state,
                in context,
                out Physics3DBodyCm body,
                out Physics3DBodyState decoded,
                out bool local,
                out Physics3DNetLocalDrivenKind localKind))
        {
            throw new InvalidOperationException($"Physics3D schema {_schemaId} rejected replicated update payload.");
        }

        Physics3DNetworkClientMirror mirror = world.Get<Physics3DNetworkClientMirror>(entity);
        mirror.LastCommittedTick = context.CommittedTick;
        Physics3DNetworkReplicatedBody replicated = world.Get<Physics3DNetworkReplicatedBody>(entity);
        replicated.Ownership = state.Ownership;
        Physics3DBodyKind clientKind = local ? Physics3DBodyKind.Dynamic : Physics3DBodyKind.Kinematic;
        mirror.IsLocallyControlled = local;

        if (body.Kind != clientKind)
        {
            Physics3DBodyDescription description = CreateDescription(entity, clientKind, in decoded);
            Physics3DBodyId oldBody = body.Id;
            Physics3DBodyId replacement = _physics.CreateBody(in description);
            try
            {
                _physics.DestroyBody(oldBody);
            }
            catch
            {
                _physics.DestroyBody(replacement);
                throw;
            }

            body.Id = replacement;
            body.Kind = clientKind;
            world.Set(entity, body);
        }
        NetworkEntityHandle handle = state.Entity;
        _convergence.ApplyAuthoritative(
            entity,
            body.Id,
            in handle,
            local,
            localKind,
            in decoded,
            in context);
        world.Set(entity, replicated);
        world.Set(entity, mirror);
    }

    public void Release(
        World world,
        Entity entity,
        ReplicationMirrorLeaveKind leaveKind,
        in ReplicationApplyContext context)
    {
        if (!TryValidateReleaseState(world, entity, in context, out Physics3DBodyCm body, out NetworkEntityHandle handle))
        {
            throw new InvalidOperationException($"Physics3D schema {_schemaId} rejected replicated release.");
        }

        _convergence.Release(entity, in handle, in context);
        _physics.DestroyBody(body.Id);
        world.Remove<Physics3DBodyCm>(entity);
        world.Remove<Physics3DPoseCm>(entity);
        world.Remove<PreviousPhysics3DPoseCm>(entity);
        world.Remove<Physics3DNetworkReplicatedBody>(entity);
        world.Remove<Physics3DNetworkClientMirror>(entity);
        world.Remove<ReplicationSchemaRef>(entity);
    }

    private bool TryDecodeDynamic(ReplicationStateVector values, out Physics3DBodyState decoded)
    {
        return Physics3DReplicationStateCodec.TryDecode(
                in values,
                _quantization,
                out decoded,
                out Physics3DBodyKind authoritativeKind) &&
            authoritativeKind == Physics3DBodyKind.Dynamic;
    }

    private bool TryValidateApplyState(
        World world,
        Entity entity,
        in ReplicatedEntityState state,
        in ReplicationApplyContext context,
        out Physics3DBodyCm body,
        out Physics3DBodyState decoded,
        out bool local,
        out Physics3DNetLocalDrivenKind localKind)
    {
        body = default;
        decoded = default;
        local = false;
        localKind = default;
        if (world == null ||
            !world.IsAlive(entity) ||
            state.SchemaId != _schemaId ||
            !IsSupportedOwnership(state.Ownership) ||
            !TryDecodeDynamic(state.Values, out decoded) ||
            !world.TryGet(entity, out ReplicationSchemaRef schema) ||
            schema.SchemaId != _schemaId ||
            !world.TryGet(entity, out ReplicationMirrorIdentity identity) ||
            identity.Handle != state.Entity ||
            !world.TryGet(entity, out body) ||
            !_physics.ContainsBody(body.Id) ||
            _physics.GetBodyKind(body.Id) != body.Kind ||
            !world.TryGet(entity, out Physics3DNetworkClientMirror mirror) ||
            mirror.SessionEpoch != context.SessionEpoch)
        {
            return false;
        }

        local = TryResolveLocalKind(state.Ownership, in context, out localKind);
        return true;
    }

    private bool TryValidateReleaseState(
        World world,
        Entity entity,
        in ReplicationApplyContext context,
        out Physics3DBodyCm body,
        out NetworkEntityHandle handle)
    {
        body = default;
        handle = default;
        if (world == null ||
            !world.IsAlive(entity) ||
            !world.TryGet(entity, out body) ||
            !_physics.ContainsBody(body.Id) ||
            !world.TryGet(entity, out Physics3DNetworkClientMirror mirror) ||
            mirror.SessionEpoch != context.SessionEpoch ||
            !world.TryGet(entity, out ReplicationMirrorIdentity identity))
        {
            return false;
        }

        handle = identity.Handle;
        return true;
    }

    private Physics3DBodyDescription CreateDescription(
        Entity entity,
        Physics3DBodyKind clientKind,
        in Physics3DBodyState state) => new(
        entity,
        clientKind,
        _shape,
        state.PositionCm,
        state.Orientation,
        state.LinearVelocityCmPerSecond,
        state.AngularVelocityRadiansPerSecond,
        mass: clientKind == Physics3DBodyKind.Dynamic ? _bodyConfig.Mass : 0f,
        _bodyConfig.CollisionLayer,
        _bodyConfig.Material,
        _bodyConfig.ContinuousDetection);

    private static bool IsSupportedOwnership(ReplicationControlOwnership ownership) =>
        !ownership.IsOwned ||
        ownership.ControlKind is Physics3DNetworkControlKinds.PlayerBody or Physics3DNetworkControlKinds.Vehicle;

    private static bool TryResolveLocalKind(
        ReplicationControlOwnership ownership,
        in ReplicationApplyContext context,
        out Physics3DNetLocalDrivenKind kind)
    {
        if (ownership.Matches(context.ClientSeat, Physics3DNetworkControlKinds.PlayerBody))
        {
            kind = Physics3DNetLocalDrivenKind.Character;
            return true;
        }

        if (ownership.Matches(context.ClientSeat, Physics3DNetworkControlKinds.Vehicle))
        {
            kind = Physics3DNetLocalDrivenKind.Vehicle;
            return true;
        }

        kind = default;
        return false;
    }

    private static Physics3DPoseCm ToPose(in Physics3DBodyState state) => new()
    {
        Position = state.PositionCm,
        Orientation = state.Orientation,
        LinearVelocity = state.LinearVelocityCmPerSecond,
        AngularVelocity = state.AngularVelocityRadiansPerSecond,
    };
}

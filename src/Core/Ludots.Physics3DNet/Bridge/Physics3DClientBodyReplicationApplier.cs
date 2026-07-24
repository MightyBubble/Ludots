using Arch.Core;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet.Bridge;

public sealed class Physics3DClientBodyReplicationApplier : IClientReplicationSchemaApplier
{
    private readonly IPhysics3DWorld _physics;
    private readonly int _schemaId;
    private readonly Physics3DReplicationQuantizationConfig _quantization;
    private readonly Physics3DNetworkPlayerBodyConfig _bodyConfig;
    private readonly Physics3DShapeId _shape;

    public Physics3DClientBodyReplicationApplier(
        IPhysics3DWorld physics,
        int schemaId,
        Physics3DReplicationQuantizationConfig quantization,
        Physics3DNetworkPlayerBodyConfig bodyConfig)
    {
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        if (schemaId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaId));
        }

        _quantization = quantization ?? throw new ArgumentNullException(nameof(quantization));
        _bodyConfig = bodyConfig ?? throw new ArgumentNullException(nameof(bodyConfig));
        _quantization.Validate();
        _bodyConfig.Validate();
        _schemaId = schemaId;
        _shape = physics.RegisterCapsuleShape(_bodyConfig.RadiusCm, _bodyConfig.CylinderLengthCm);
    }

    public bool CanCreate(World world, in ReplicatedEntityState state, in ReplicationApplyContext context) =>
        world != null &&
        state.SchemaId == _schemaId &&
        TryDecodeDynamic(state.Values, out _);

    public bool CanApply(
        World world,
        Entity entity,
        in ReplicatedEntityState state,
        in ReplicationApplyContext context)
    {
        return world != null &&
            world.IsAlive(entity) &&
            state.SchemaId == _schemaId &&
            TryDecodeDynamic(state.Values, out _) &&
            world.TryGet(entity, out ReplicationSchemaRef schema) &&
            schema.SchemaId == _schemaId &&
            world.TryGet(entity, out ReplicationMirrorIdentity identity) &&
            identity.Handle == state.Entity &&
            world.TryGet(entity, out Physics3DBodyCm body) &&
            body.Kind == Physics3DBodyKind.Kinematic &&
            _physics.ContainsBody(body.Id) &&
            world.TryGet(entity, out Physics3DNetworkClientMirror mirror) &&
            mirror.SessionEpoch == context.SessionEpoch;
    }

    public bool CanRelease(
        World world,
        Entity entity,
        ReplicationMirrorLeaveKind leaveKind,
        in ReplicationApplyContext context)
    {
        return world != null &&
            world.IsAlive(entity) &&
            world.TryGet(entity, out Physics3DBodyCm body) &&
            _physics.ContainsBody(body.Id) &&
            world.TryGet(entity, out Physics3DNetworkClientMirror mirror) &&
            mirror.SessionEpoch == context.SessionEpoch;
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
        var body = new Physics3DBodyCm { Kind = Physics3DBodyKind.Kinematic };
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
        };
        var mirror = new Physics3DNetworkClientMirror
        {
            AuthoritativeKind = Physics3DBodyKind.Dynamic,
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

        try
        {
            Physics3DBodyDescription description = CreateDescription(entity, in decoded);
            body.Id = _physics.CreateBody(in description);
            world.Set(entity, body);
            return entity;
        }
        catch
        {
            world.Destroy(entity);
            throw;
        }
    }

    public void Apply(
        World world,
        Entity entity,
        in ReplicatedEntityState state,
        in ReplicationApplyContext context)
    {
        if (!CanApply(world, entity, in state, in context) ||
            !TryDecodeDynamic(state.Values, out Physics3DBodyState decoded))
        {
            throw new InvalidOperationException($"Physics3D schema {_schemaId} rejected replicated update payload.");
        }

        Physics3DBodyCm body = world.Get<Physics3DBodyCm>(entity);
        Physics3DPoseCm current = world.Get<Physics3DPoseCm>(entity);
        var previous = new PreviousPhysics3DPoseCm
        {
            Position = current.Position,
            Orientation = current.Orientation,
        };
        Physics3DPoseCm next = ToPose(in decoded);
        Physics3DNetworkClientMirror mirror = world.Get<Physics3DNetworkClientMirror>(entity);
        mirror.LastCommittedTick = context.CommittedTick;

        _physics.SetBodyState(body.Id, in decoded);
        world.Set(entity, previous);
        world.Set(entity, next);
        world.Set(entity, mirror);
    }

    public void Release(
        World world,
        Entity entity,
        ReplicationMirrorLeaveKind leaveKind,
        in ReplicationApplyContext context)
    {
        if (!CanRelease(world, entity, leaveKind, in context))
        {
            throw new InvalidOperationException($"Physics3D schema {_schemaId} rejected replicated release.");
        }

        Physics3DBodyCm body = world.Get<Physics3DBodyCm>(entity);
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

    private Physics3DBodyDescription CreateDescription(Entity entity, in Physics3DBodyState state) => new(
        entity,
        Physics3DBodyKind.Kinematic,
        _shape,
        state.PositionCm,
        state.Orientation,
        state.LinearVelocityCmPerSecond,
        state.AngularVelocityRadiansPerSecond,
        mass: 0f,
        _bodyConfig.CollisionLayer,
        _bodyConfig.Material,
        _bodyConfig.ContinuousDetection);

    private static Physics3DPoseCm ToPose(in Physics3DBodyState state) => new()
    {
        Position = state.PositionCm,
        Orientation = state.Orientation,
        LinearVelocity = state.LinearVelocityCmPerSecond,
        AngularVelocity = state.AngularVelocityRadiansPerSecond,
    };
}

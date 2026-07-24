using Arch.Core;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet.Bridge;

public sealed class Physics3DHeadlessReplicationApplier : IClientReplicationSchemaApplier
{
    private readonly int _schemaId;
    private readonly Physics3DReplicationQuantizationConfig _quantization;

    public Physics3DHeadlessReplicationApplier(
        int schemaId,
        Physics3DReplicationQuantizationConfig quantization)
    {
        if (schemaId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaId));
        }

        _quantization = quantization ?? throw new ArgumentNullException(nameof(quantization));
        _quantization.Validate();
        _schemaId = schemaId;
    }

    public bool CanCreate(World world, in ReplicatedEntityState state, in ReplicationApplyContext context) =>
        world != null &&
        context.SessionEpoch != 0 &&
        state.Entity.IsValid &&
        state.SchemaId == _schemaId &&
        TryDecode(state.Values, out _, out _);

    public bool CanApply(
        World world,
        Entity entity,
        in ReplicatedEntityState state,
        in ReplicationApplyContext context)
    {
        return world != null &&
            world.IsAlive(entity) &&
            state.Entity.IsValid &&
            state.SchemaId == _schemaId &&
            TryDecode(state.Values, out _, out _) &&
            world.TryGet(entity, out ReplicationSchemaRef schema) &&
            schema.SchemaId == _schemaId &&
            world.TryGet(entity, out ReplicationMirrorIdentity identity) &&
            identity.Handle == state.Entity &&
            world.TryGet(entity, out Physics3DHeadlessClientMirror mirror) &&
            mirror.Handle == state.Entity &&
            IsFormalBodyKind(mirror.AuthoritativeKind) &&
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
            world.TryGet(entity, out ReplicationSchemaRef schema) &&
            schema.SchemaId == _schemaId &&
            world.TryGet(entity, out ReplicationMirrorIdentity identity) &&
            identity.Handle.IsValid &&
            world.TryGet(entity, out Physics3DHeadlessClientMirror mirror) &&
            mirror.Handle == identity.Handle &&
            IsFormalBodyKind(mirror.AuthoritativeKind) &&
            mirror.SessionEpoch == context.SessionEpoch;
    }

    public Entity Create(
        World world,
        in ReplicationMirrorIdentity identity,
        in ReplicationMirrorState state,
        in ReplicationApplyContext context)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!identity.Handle.IsValid ||
            state.SchemaId != _schemaId ||
            !TryDecode(state.Values, out Physics3DBodyState decoded, out Physics3DBodyKind kind))
        {
            throw new InvalidOperationException($"Physics3D headless schema {_schemaId} rejected replicated create payload.");
        }

        var schema = new ReplicationSchemaRef(_schemaId);
        var mirror = new Physics3DHeadlessClientMirror
        {
            Handle = identity.Handle,
            State = decoded,
            AuthoritativeKind = kind,
            SessionEpoch = context.SessionEpoch,
            LastCommittedTick = context.CommittedTick,
        };
        return world.Create(in identity, in state, in schema, in mirror);
    }

    public void Apply(
        World world,
        Entity entity,
        in ReplicatedEntityState state,
        in ReplicationApplyContext context)
    {
        if (!CanApply(world, entity, in state, in context) ||
            !TryDecode(state.Values, out Physics3DBodyState decoded, out Physics3DBodyKind kind))
        {
            throw new InvalidOperationException($"Physics3D headless schema {_schemaId} rejected replicated update payload.");
        }

        Physics3DHeadlessClientMirror mirror = world.Get<Physics3DHeadlessClientMirror>(entity);
        mirror.State = decoded;
        mirror.AuthoritativeKind = kind;
        mirror.LastCommittedTick = context.CommittedTick;
        world.Set(entity, in mirror);
    }

    public void Release(
        World world,
        Entity entity,
        ReplicationMirrorLeaveKind leaveKind,
        in ReplicationApplyContext context)
    {
        if (!CanRelease(world, entity, leaveKind, in context))
        {
            throw new InvalidOperationException($"Physics3D headless schema {_schemaId} rejected replicated release.");
        }

        world.Remove<Physics3DHeadlessClientMirror>(entity);
        world.Remove<ReplicationSchemaRef>(entity);
    }

    private bool TryDecode(
        ReplicationStateVector values,
        out Physics3DBodyState decoded,
        out Physics3DBodyKind authoritativeKind)
    {
        return Physics3DReplicationStateCodec.TryDecode(
                in values,
                _quantization,
                out decoded,
                out authoritativeKind) &&
            IsFormalBodyKind(authoritativeKind);
    }

    private static bool IsFormalBodyKind(Physics3DBodyKind kind) =>
        kind is Physics3DBodyKind.Static or Physics3DBodyKind.Kinematic or Physics3DBodyKind.Dynamic;
}

using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet.Bridge;

public sealed class Physics3DBodyReplicationProjector : IReplicationSchemaProjector
{
    private readonly IPhysics3DWorld _physics;
    private readonly int _schemaId;
    private readonly Physics3DReplicationQuantizationConfig _quantization;

    public Physics3DBodyReplicationProjector(
        IPhysics3DWorld physics,
        int schemaId,
        Physics3DReplicationQuantizationConfig quantization)
    {
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        if (schemaId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaId));
        }

        _quantization = quantization ?? throw new ArgumentNullException(nameof(quantization));
        _quantization.Validate();
        _schemaId = schemaId;
    }

    public bool TryProject(
        World world,
        Entity entity,
        in KnowledgeDisclosureRecord disclosure,
        out ReplicationProjectedState state)
    {
        state = default;
        if (world == null ||
            disclosure.Presence != KnowledgePresence.LiveVisible ||
            !world.IsAlive(entity) ||
            !world.TryGet(entity, out ReplicationSchemaRef schema) ||
            schema.SchemaId != _schemaId ||
            !world.TryGet(entity, out Physics3DBodyCm body) ||
            !world.TryGet(entity, out Physics3DNetworkReplicatedBody replicated) ||
            body.Kind != replicated.AuthoritativeKind ||
            !_physics.ContainsBody(body.Id) ||
            _physics.GetBodyKind(body.Id) != body.Kind)
        {
            return false;
        }

        Physics3DBodyState bodyState = _physics.GetBodyState(body.Id);
        if (world.TryGet(entity, out Physics3DNetworkPlayer player) &&
            (player.SeatSlot < 0 ||
             player.SeatGeneration == 0 ||
             player.PlayerId <= 0 ||
             !replicated.Ownership.IsOwned ||
             replicated.Ownership.SeatSlot != player.SeatSlot ||
             replicated.Ownership.SeatGeneration != player.SeatGeneration ||
             replicated.Ownership.ControlKind != Physics3DNetworkControlKinds.PlayerBody))
        {
            return false;
        }

        if (!Physics3DReplicationStateCodec.TryEncode(
                in bodyState,
                body.Kind,
                _quantization,
                out ReplicationStateVector values))
        {
            return false;
        }

        state = new ReplicationProjectedState(
            Physics3DReplicationStateCodec.ComputeRevision(in values, disclosure.Revision),
            in values,
            replicated.Ownership);
        return true;
    }
}

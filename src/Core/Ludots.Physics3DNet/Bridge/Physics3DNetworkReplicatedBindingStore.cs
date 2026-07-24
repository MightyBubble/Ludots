using Arch.Core;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet.Bridge;

public enum Physics3DNetworkReplicatedBindingFailure : byte
{
    None = 0,
    InvalidBody = 1,
    BodySlotOutOfRange = 2,
    DuplicateBinding = 3,
    MissingBinding = 4,
    GenerationMismatch = 5,
    EntityMismatch = 6,
    InvalidHandle = 7,
    InvalidSchema = 8,
}

/// <summary>
/// Authoritative fixed-capacity SoA map from Physics3D body slot to network handle/entity/schema.
/// Written only on the owning thread by player lifecycle and body registry; AOI workers may read
/// concurrently while the owner guarantees no structural mutation.
/// </summary>
public sealed class Physics3DNetworkReplicatedBindingStore
{
    private readonly bool[] _active;
    private readonly int[] _bodyGenerations;
    private readonly Entity[] _entities;
    private readonly int[] _networkSlots;
    private readonly uint[] _networkGenerations;
    private readonly int[] _schemaIds;
    private readonly Physics3DBodyKind[] _kinds;

    public Physics3DNetworkReplicatedBindingStore(int bodySlotCapacity)
    {
        if (bodySlotCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bodySlotCapacity));
        }

        BodySlotCapacity = bodySlotCapacity;
        _active = new bool[bodySlotCapacity];
        _bodyGenerations = new int[bodySlotCapacity];
        _entities = new Entity[bodySlotCapacity];
        _networkSlots = new int[bodySlotCapacity];
        _networkGenerations = new uint[bodySlotCapacity];
        _schemaIds = new int[bodySlotCapacity];
        _kinds = new Physics3DBodyKind[bodySlotCapacity];
    }

    public int BodySlotCapacity { get; }
    public int Count { get; private set; }
    public Physics3DNetworkReplicatedBindingFailure LastFailure { get; private set; }

    public bool TryBind(
        Physics3DBodyId body,
        Entity entity,
        NetworkEntityHandle handle,
        int schemaId,
        Physics3DBodyKind kind)
    {
        LastFailure = Physics3DNetworkReplicatedBindingFailure.None;
        if (!body.IsValid)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.InvalidBody;
            return false;
        }

        if ((uint)body.Slot >= (uint)_active.Length)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.BodySlotOutOfRange;
            return false;
        }

        if (entity == Entity.Null)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.EntityMismatch;
            return false;
        }

        if (!handle.IsValid)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.InvalidHandle;
            return false;
        }

        if (schemaId <= 0)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.InvalidSchema;
            return false;
        }

        int slot = body.Slot;
        if (_active[slot])
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.DuplicateBinding;
            return false;
        }

        _active[slot] = true;
        _bodyGenerations[slot] = body.Generation;
        _entities[slot] = entity;
        _networkSlots[slot] = handle.Slot;
        _networkGenerations[slot] = handle.Generation;
        _schemaIds[slot] = schemaId;
        _kinds[slot] = kind;
        Count++;
        return true;
    }

    public bool TryUnbind(Physics3DBodyId body, Entity entity, NetworkEntityHandle handle)
    {
        LastFailure = Physics3DNetworkReplicatedBindingFailure.None;
        if (!body.IsValid)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.InvalidBody;
            return false;
        }

        if ((uint)body.Slot >= (uint)_active.Length)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.BodySlotOutOfRange;
            return false;
        }

        int slot = body.Slot;
        if (!_active[slot])
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.MissingBinding;
            return false;
        }

        if (_bodyGenerations[slot] != body.Generation)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.GenerationMismatch;
            return false;
        }

        if (_entities[slot] != entity)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.EntityMismatch;
            return false;
        }

        var boundHandle = new NetworkEntityHandle(_networkSlots[slot], _networkGenerations[slot]);
        if (boundHandle != handle)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.InvalidHandle;
            return false;
        }

        _active[slot] = false;
        _bodyGenerations[slot] = 0;
        _entities[slot] = Entity.Null;
        _networkSlots[slot] = 0;
        _networkGenerations[slot] = 0;
        _schemaIds[slot] = 0;
        _kinds[slot] = default;
        Count--;
        return true;
    }

    public bool TryGet(
        Physics3DBodyId body,
        out Entity entity,
        out NetworkEntityHandle handle,
        out int schemaId,
        out Physics3DBodyKind kind)
    {
        entity = Entity.Null;
        handle = default;
        schemaId = 0;
        kind = default;
        if (!body.IsValid || (uint)body.Slot >= (uint)_active.Length)
        {
            return false;
        }

        int slot = body.Slot;
        if (!_active[slot] || _bodyGenerations[slot] != body.Generation)
        {
            return false;
        }

        entity = _entities[slot];
        handle = new NetworkEntityHandle(_networkSlots[slot], _networkGenerations[slot]);
        schemaId = _schemaIds[slot];
        kind = _kinds[slot];
        return true;
    }
}

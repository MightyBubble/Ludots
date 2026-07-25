using System.Runtime.CompilerServices;
using System.Threading;
using Arch.Core;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet.Bridge;

public enum Physics3DNetworkReplicatedBindingFailure : byte
{
    None = 0,
    InvalidBody = 1,
    InvalidEntity = 2,
    InvalidNetworkHandle = 3,
    InvalidSchema = 4,
    BodyAlreadyBound = 5,
    NetworkSlotAlreadyBound = 6,
    BindingUnavailable = 7,
    BindingMismatch = 8,
}

public readonly struct Physics3DNetworkReplicatedBinding
{
    public Physics3DNetworkReplicatedBinding(
        Entity entity,
        NetworkEntityHandle networkHandle,
        int schemaId,
        Physics3DBodyKind bodyKind)
    {
        Entity = entity;
        NetworkHandle = networkHandle;
        SchemaId = schemaId;
        BodyKind = bodyKind;
    }

    public Entity Entity { get; }
    public NetworkEntityHandle NetworkHandle { get; }
    public int SchemaId { get; }
    public Physics3DBodyKind BodyKind { get; }
}

/// <summary>
/// Single fixed-capacity SoA truth for Physics3D body-to-network replication bindings.
/// Player and ordinary-body lifecycles are the only writers; AOI consumes a frozen read publication.
/// </summary>
public sealed class Physics3DNetworkReplicatedBindingStore
{
    private readonly byte[] _activeBodySlots;
    private readonly int[] _bodyGenerations;
    private readonly Entity[] _entities;
    private readonly int[] _networkSlots;
    private readonly uint[] _networkGenerations;
    private readonly int[] _schemaIds;
    private readonly Physics3DBodyKind[] _bodyKinds;
    private readonly int[] _networkSlotBodySlots;
    private readonly int[] _networkSlotBodyGenerations;
    private readonly int _ownerThreadId;
    private int _readPublicationDepth;

    public Physics3DNetworkReplicatedBindingStore(int bodySlotCapacity, int networkSlotCapacity)
    {
        if (bodySlotCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bodySlotCapacity));
        }

        if (networkSlotCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(networkSlotCapacity));
        }

        _activeBodySlots = new byte[bodySlotCapacity];
        _bodyGenerations = new int[bodySlotCapacity];
        _entities = new Entity[bodySlotCapacity];
        _networkSlots = new int[bodySlotCapacity];
        _networkGenerations = new uint[bodySlotCapacity];
        _schemaIds = new int[bodySlotCapacity];
        _bodyKinds = new Physics3DBodyKind[bodySlotCapacity];
        _networkSlotBodySlots = new int[networkSlotCapacity];
        _networkSlotBodyGenerations = new int[networkSlotCapacity];
        Array.Fill(_networkSlotBodySlots, -1);
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public int BodySlotCapacity => _activeBodySlots.Length;
    public int NetworkSlotCapacity => _networkSlotBodySlots.Length;
    public int Count { get; private set; }
    public Physics3DNetworkReplicatedBindingFailure LastFailure { get; private set; }

    public Physics3DNetworkReplicatedBindingReadPublication CreateReadPublication()
    {
        EnsureOwnerThread();
        return new Physics3DNetworkReplicatedBindingReadPublication(this);
    }

    public bool TryBind(
        Physics3DBodyId body,
        Entity entity,
        NetworkEntityHandle networkHandle,
        int schemaId,
        Physics3DBodyKind bodyKind)
    {
        EnsureOwnerThread();
        EnsureMutationAllowed();
        LastFailure = Physics3DNetworkReplicatedBindingFailure.None;
        if (!body.IsValid || (uint)body.Slot >= (uint)_activeBodySlots.Length)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.InvalidBody;
            return false;
        }

        if (entity == Entity.Null)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.InvalidEntity;
            return false;
        }

        if (!networkHandle.IsValid || (uint)networkHandle.Slot >= (uint)_networkSlotBodySlots.Length)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.InvalidNetworkHandle;
            return false;
        }

        if (schemaId <= 0)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.InvalidSchema;
            return false;
        }

        if (_activeBodySlots[body.Slot] != 0)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.BodyAlreadyBound;
            return false;
        }

        if (_networkSlotBodySlots[networkHandle.Slot] >= 0)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.NetworkSlotAlreadyBound;
            return false;
        }

        _bodyGenerations[body.Slot] = body.Generation;
        _entities[body.Slot] = entity;
        _networkSlots[body.Slot] = networkHandle.Slot;
        _networkGenerations[body.Slot] = networkHandle.Generation;
        _schemaIds[body.Slot] = schemaId;
        _bodyKinds[body.Slot] = bodyKind;
        _networkSlotBodySlots[networkHandle.Slot] = body.Slot;
        _networkSlotBodyGenerations[networkHandle.Slot] = body.Generation;
        Volatile.Write(ref _activeBodySlots[body.Slot], (byte)1);
        Count++;
        return true;
    }

    public bool TryUnbind(
        Physics3DBodyId body,
        Entity entity,
        NetworkEntityHandle networkHandle)
    {
        EnsureOwnerThread();
        EnsureMutationAllowed();
        LastFailure = Physics3DNetworkReplicatedBindingFailure.None;
        if (!TryResolveCore(body, out Physics3DNetworkReplicatedBinding binding))
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.BindingUnavailable;
            return false;
        }

        if (binding.Entity != entity || binding.NetworkHandle != networkHandle)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.BindingMismatch;
            return false;
        }

        int bodySlot = body.Slot;
        int networkSlot = networkHandle.Slot;
        if (_networkSlotBodySlots[networkSlot] != bodySlot ||
            _networkSlotBodyGenerations[networkSlot] != body.Generation)
        {
            LastFailure = Physics3DNetworkReplicatedBindingFailure.BindingMismatch;
            return false;
        }

        Volatile.Write(ref _activeBodySlots[bodySlot], (byte)0);
        _bodyGenerations[bodySlot] = 0;
        _entities[bodySlot] = Entity.Null;
        _networkSlots[bodySlot] = 0;
        _networkGenerations[bodySlot] = 0;
        _schemaIds[bodySlot] = 0;
        _bodyKinds[bodySlot] = default;
        _networkSlotBodySlots[networkSlot] = -1;
        _networkSlotBodyGenerations[networkSlot] = 0;
        Count--;
        return true;
    }

    public bool TryResolve(
        Physics3DBodyId body,
        out Physics3DNetworkReplicatedBinding binding)
    {
        EnsureOwnerThread();
        return TryResolveCore(body, out binding);
    }

    public bool TryResolve(
        NetworkEntityHandle networkHandle,
        out Physics3DBodyId body,
        out Physics3DNetworkReplicatedBinding binding)
    {
        EnsureOwnerThread();
        body = default;
        binding = default;
        if (!networkHandle.IsValid ||
            (uint)networkHandle.Slot >= (uint)_networkSlotBodySlots.Length)
        {
            return false;
        }

        int bodySlot = _networkSlotBodySlots[networkHandle.Slot];
        if (bodySlot < 0)
        {
            return false;
        }

        body = new Physics3DBodyId(bodySlot, _networkSlotBodyGenerations[networkHandle.Slot]);
        return TryResolveCore(body, out binding) && binding.NetworkHandle == networkHandle;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryResolvePublished(
        Physics3DBodyId body,
        out Physics3DNetworkReplicatedBinding binding)
    {
        if (Volatile.Read(ref _readPublicationDepth) <= 0)
        {
            throw new InvalidOperationException("Physics3D network binding read publication is not active.");
        }

        return TryResolveCore(body, out binding);
    }

    internal void EnterReadPublication()
    {
        EnsureOwnerThread();
        _readPublicationDepth++;
    }

    internal void ExitReadPublication()
    {
        EnsureOwnerThread();
        if (_readPublicationDepth <= 0)
        {
            throw new InvalidOperationException("Physics3D network binding read publication is not active.");
        }

        _readPublicationDepth--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryResolveCore(
        Physics3DBodyId body,
        out Physics3DNetworkReplicatedBinding binding)
    {
        if (!body.IsValid ||
            (uint)body.Slot >= (uint)_activeBodySlots.Length ||
            Volatile.Read(ref _activeBodySlots[body.Slot]) == 0 ||
            _bodyGenerations[body.Slot] != body.Generation)
        {
            binding = default;
            return false;
        }

        binding = new Physics3DNetworkReplicatedBinding(
            _entities[body.Slot],
            new NetworkEntityHandle(_networkSlots[body.Slot], _networkGenerations[body.Slot]),
            _schemaIds[body.Slot],
            _bodyKinds[body.Slot]);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "Physics3D network binding mutation and owner reads must remain on the authoritative fixed-frame thread.");
        }
    }

    private void EnsureMutationAllowed()
    {
        if (_readPublicationDepth != 0)
        {
            throw new InvalidOperationException(
                "Physics3D network bindings cannot mutate during an AOI read publication.");
        }
    }
}

public sealed class Physics3DNetworkReplicatedBindingReadPublication
{
    private readonly Physics3DNetworkReplicatedBindingStore _store;
    private int _active;

    internal Physics3DNetworkReplicatedBindingReadPublication(
        Physics3DNetworkReplicatedBindingStore store)
    {
        _store = store;
    }

    public void Enter()
    {
        if (Volatile.Read(ref _active) != 0)
        {
            throw new InvalidOperationException("Physics3D network binding read publication is already active.");
        }

        _store.EnterReadPublication();
        Volatile.Write(ref _active, 1);
    }

    public void Exit()
    {
        if (Volatile.Read(ref _active) == 0)
        {
            throw new InvalidOperationException("Physics3D network binding read publication is not active.");
        }

        Volatile.Write(ref _active, 0);
        _store.ExitReadPublication();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryResolve(
        Physics3DBodyId body,
        out Physics3DNetworkReplicatedBinding binding)
    {
        if (Volatile.Read(ref _active) == 0)
        {
            throw new InvalidOperationException("Physics3D network binding read publication is not active.");
        }

        return _store.TryResolvePublished(body, out binding);
    }
}

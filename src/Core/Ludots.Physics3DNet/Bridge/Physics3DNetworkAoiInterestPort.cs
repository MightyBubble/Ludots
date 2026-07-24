using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet.Bridge;

public enum Physics3DNetworkAoiFailure : byte
{
    None = 0,
    UnknownSeat = 1,
    ViewerPoseUnavailable = 2,
    InvalidNetworkHandle = 3,
    EntityTableMismatch = 4,
    DuplicateNetworkSlot = 5,
    DestinationCapacityExceeded = 6,
}

public sealed class Physics3DNetworkAoiInterestPort : IAuthoritativeReplicationInterestPort
{
    private static readonly QueryDescription ReplicatedBodyQuery = new QueryDescription()
        .WithAll<Physics3DNetworkReplicatedBody, Physics3DPoseCm>();

    private readonly World _world;
    private readonly NetworkEntityTable _networkEntities;
    private readonly Physics3DNetworkPlayerLifecycle _players;
    private readonly float _radiusSquared;
    private readonly int[] _selectionStamps;
    private readonly uint[] _selectionGenerations;
    private readonly int[] _selectedSlots;
    private int _queryStamp;

    public Physics3DNetworkAoiInterestPort(
        World world,
        NetworkEntityTable networkEntities,
        Physics3DNetworkPlayerLifecycle players,
        Physics3DNetworkAoiConfig config)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _networkEntities = networkEntities ?? throw new ArgumentNullException(nameof(networkEntities));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        if (config.GlobalEntityCapacity != networkEntities.Capacity)
        {
            throw new ArgumentException("Physics3D AOI global capacity must match the Core network entity table.", nameof(config));
        }

        _radiusSquared = config.RadiusCm * config.RadiusCm;
        _selectionStamps = new int[config.GlobalEntityCapacity];
        _selectionGenerations = new uint[config.GlobalEntityCapacity];
        _selectedSlots = new int[config.GlobalEntityCapacity];
    }

    public Physics3DNetworkAoiFailure LastFailure { get; private set; }

    public bool TryCopyInterest(
        in SessionSeatBinding seat,
        Span<NetworkEntityHandle> destination,
        out int count)
    {
        LastFailure = Physics3DNetworkAoiFailure.None;
        count = 0;
        if (!_players.TryGetExistingController(in seat, out Entity viewer))
        {
            LastFailure = Physics3DNetworkAoiFailure.UnknownSeat;
            return false;
        }

        if (!_world.TryGet(viewer, out Physics3DPoseCm viewerPose))
        {
            LastFailure = Physics3DNetworkAoiFailure.ViewerPoseUnavailable;
            return false;
        }

        int stamp = NextQueryStamp();
        int selectedCount = 0;
        foreach (ref Chunk chunk in _world.Query(in ReplicatedBodyQuery))
        {
            chunk.GetSpan<Physics3DNetworkReplicatedBody, Physics3DPoseCm>(
                out Span<Physics3DNetworkReplicatedBody> replicated,
                out Span<Physics3DPoseCm> poses);
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (Vector3.DistanceSquared(viewerPose.Position, poses[index].Position) > _radiusSquared)
                {
                    continue;
                }

                NetworkEntityHandle handle = replicated[index].Handle;
                if (!handle.IsValid || (uint)handle.Slot >= (uint)_selectionStamps.Length)
                {
                    LastFailure = Physics3DNetworkAoiFailure.InvalidNetworkHandle;
                    return false;
                }

                Entity entity = Unsafe.Add(ref first, index);
                if (!_networkEntities.TryResolve(handle, out Entity mapped) || mapped != entity)
                {
                    LastFailure = Physics3DNetworkAoiFailure.EntityTableMismatch;
                    return false;
                }

                int slot = handle.Slot;
                if (_selectionStamps[slot] == stamp)
                {
                    LastFailure = Physics3DNetworkAoiFailure.DuplicateNetworkSlot;
                    return false;
                }

                _selectionStamps[slot] = stamp;
                _selectionGenerations[slot] = handle.Generation;
                _selectedSlots[selectedCount++] = slot;
            }
        }

        count = selectedCount;
        if (destination.Length < selectedCount)
        {
            LastFailure = Physics3DNetworkAoiFailure.DestinationCapacityExceeded;
            return false;
        }

        Span<int> selectedSlots = _selectedSlots.AsSpan(0, selectedCount);
        selectedSlots.Sort();
        for (int index = 0; index < selectedSlots.Length; index++)
        {
            int slot = selectedSlots[index];
            destination[index] = new NetworkEntityHandle(slot, _selectionGenerations[slot]);
        }

        return true;
    }

    private int NextQueryStamp()
    {
        if (_queryStamp == int.MaxValue)
        {
            Array.Clear(_selectionStamps);
            _queryStamp = 0;
        }

        return ++_queryStamp;
    }
}

using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using BepuPhysics;
using BepuPhysics.Collidables;
using Ludots.Core.Layers;

namespace Ludots.Core.Physics3D;

internal sealed class Physics3DBodyStore
{
    private const byte FreeSlot = 0;
    private const byte MobileSlot = 1;
    private const byte StaticSlot = 2;

    private readonly int[] _freeSlots;
    private readonly int[] _generations;
    private readonly byte[] _slotKinds;
    private readonly int[] _slotToBepuHandle;
    private readonly int[] _bepuBodyHandleToSlot;
    private readonly int[] _bepuStaticHandleToSlot;
    private readonly Entity[] _entities;
    private readonly LayerMask[] _layers;
    private readonly Physics3DMaterial[] _materials;
    private readonly Physics3DBodyKind[] _bodyKinds;
    private readonly Physics3DBodyContactPolicy[] _contactPolicies;
    private readonly Physics3DCollisionSubgroup[] _collisionSubgroups;
    private readonly byte[] _collisionMetadataTracked;
    private readonly int[] _surfaceVelocitySlots;
    private readonly int[] _slotToSurfaceVelocityIndex;
    private int _freeSlotCount;
    private int _customCollisionFilterCount;
    private int _nonSolidContactPolicyCount;
    private int _surfaceVelocityBodyCount;

    public Physics3DBodyStore(int mobileCapacity, int staticCapacity)
    {
        MobileCapacity = mobileCapacity;
        StaticCapacity = staticCapacity;
        int totalCapacity = checked(mobileCapacity + staticCapacity);
        _freeSlots = new int[totalCapacity];
        _generations = new int[totalCapacity];
        _slotKinds = new byte[totalCapacity];
        _slotToBepuHandle = new int[totalCapacity];
        _bepuBodyHandleToSlot = new int[mobileCapacity];
        _bepuStaticHandleToSlot = new int[staticCapacity];
        _entities = new Entity[totalCapacity];
        _layers = new LayerMask[totalCapacity];
        _materials = new Physics3DMaterial[totalCapacity];
        _bodyKinds = new Physics3DBodyKind[totalCapacity];
        _contactPolicies = new Physics3DBodyContactPolicy[totalCapacity];
        _collisionSubgroups = new Physics3DCollisionSubgroup[totalCapacity];
        _collisionMetadataTracked = new byte[totalCapacity];
        _surfaceVelocitySlots = new int[totalCapacity];
        _slotToSurfaceVelocityIndex = new int[totalCapacity];

        Array.Fill(_slotToBepuHandle, -1);
        Array.Fill(_bepuBodyHandleToSlot, -1);
        Array.Fill(_bepuStaticHandleToSlot, -1);
        Array.Fill(_slotToSurfaceVelocityIndex, -1);
        for (int i = 0; i < totalCapacity; i++)
        {
            _freeSlots[i] = totalCapacity - 1 - i;
        }

        _freeSlotCount = totalCapacity;
    }

    public int MobileCapacity { get; }
    public int StaticCapacity { get; }
    public int TotalCapacity => _slotKinds.Length;
    public int ActiveBodyCount { get; private set; }
    public int ActiveMobileBodyCount { get; private set; }
    public int ActiveStaticBodyCount { get; private set; }
    public bool HasCustomCollisionFilters => _customCollisionFilterCount > 0;
    public bool HasNonSolidContactPolicies => _nonSolidContactPolicyCount > 0;
    public int SurfaceVelocityBodyCount => _surfaceVelocityBodyCount;

    public int AllocateSlot(Physics3DBodyKind kind)
    {
        if (kind == Physics3DBodyKind.Static)
        {
            if (ActiveStaticBodyCount >= StaticCapacity)
            {
                throw new Physics3DCapacityExceededException("static bodies", StaticCapacity);
            }
        }
        else if (kind is Physics3DBodyKind.Dynamic or Physics3DBodyKind.Kinematic)
        {
            if (ActiveMobileBodyCount >= MobileCapacity)
            {
                throw new Physics3DCapacityExceededException("mobile bodies", MobileCapacity);
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Physics3D body kind.");
        }

        if (_freeSlotCount == 0)
        {
            throw new Physics3DCapacityExceededException("body slots", TotalCapacity);
        }

        int slot = _freeSlots[--_freeSlotCount];
        int generation = unchecked(_generations[slot] + 1);
        if (generation <= 0)
        {
            generation = 1;
        }

        _generations[slot] = generation;
        _slotKinds[slot] = kind == Physics3DBodyKind.Static ? StaticSlot : MobileSlot;
        _bodyKinds[slot] = kind;
        ActiveBodyCount++;
        if (kind == Physics3DBodyKind.Static)
        {
            ActiveStaticBodyCount++;
        }
        else
        {
            ActiveMobileBodyCount++;
        }

        return slot;
    }

    public void BindMobile(int slot, BodyHandle handle, in Physics3DBodyDescription description)
    {
        if ((uint)handle.Value >= (uint)_bepuBodyHandleToSlot.Length)
        {
            throw new Physics3DCapacityExceededException("Bepu body handles", _bepuBodyHandleToSlot.Length);
        }

        BindCommon(slot, handle.Value, in description);
        _bepuBodyHandleToSlot[handle.Value] = slot;
    }

    public void BindStatic(int slot, StaticHandle handle, in Physics3DBodyDescription description)
    {
        if ((uint)handle.Value >= (uint)_bepuStaticHandleToSlot.Length)
        {
            throw new Physics3DCapacityExceededException("Bepu static handles", _bepuStaticHandleToSlot.Length);
        }

        BindCommon(slot, handle.Value, in description);
        _bepuStaticHandleToSlot[handle.Value] = slot;
    }

    public Physics3DBodyId GetId(int slot) => new(slot, _generations[slot]);

    public bool Contains(Physics3DBodyId id)
        => (uint)id.Slot < (uint)_slotKinds.Length &&
           id.Generation > 0 &&
           _slotKinds[id.Slot] != FreeSlot &&
           _generations[id.Slot] == id.Generation;

    public int RequireSlot(Physics3DBodyId id)
    {
        if (!Contains(id))
        {
            throw new InvalidOperationException($"Physics3D body id '{id}' is stale or unknown.");
        }

        return id.Slot;
    }

    public int RequireMobileSlot(BodyHandle handle)
    {
        if ((uint)handle.Value >= (uint)_bepuBodyHandleToSlot.Length)
        {
            throw new InvalidOperationException($"Physics3D body handle '{handle.Value}' is outside the configured range.");
        }

        int slot = _bepuBodyHandleToSlot[handle.Value];
        if (slot < 0)
        {
            throw new InvalidOperationException($"Physics3D body handle '{handle.Value}' is not bound.");
        }

        return slot;
    }

    public void Release(Physics3DBodyId id)
    {
        int slot = RequireSlot(id);
        int bepuHandle = _slotToBepuHandle[slot];
        if (_slotKinds[slot] == StaticSlot)
        {
            _bepuStaticHandleToSlot[bepuHandle] = -1;
            ActiveStaticBodyCount--;
        }
        else
        {
            _bepuBodyHandleToSlot[bepuHandle] = -1;
            ActiveMobileBodyCount--;
        }

        UntrackCollisionMetadata(slot);
        _slotKinds[slot] = FreeSlot;
        _slotToBepuHandle[slot] = -1;
        _entities[slot] = Entity.Null;
        _layers[slot] = default;
        _materials[slot] = default;
        _bodyKinds[slot] = default;
        _contactPolicies[slot] = default;
        _collisionSubgroups[slot] = default;
        _freeSlots[_freeSlotCount++] = slot;
        ActiveBodyCount--;
    }

    public void RollbackSlot(int slot)
    {
        if ((uint)slot >= (uint)_slotKinds.Length || _slotKinds[slot] == FreeSlot)
        {
            throw new InvalidOperationException("Cannot roll back a free Physics3D body slot.");
        }

        Physics3DBodyKind kind = _bodyKinds[slot];
        UntrackCollisionMetadata(slot);
        _slotKinds[slot] = FreeSlot;
        _bodyKinds[slot] = default;
        _contactPolicies[slot] = default;
        _collisionSubgroups[slot] = default;
        _freeSlots[_freeSlotCount++] = slot;
        ActiveBodyCount--;
        if (kind == Physics3DBodyKind.Static)
        {
            ActiveStaticBodyCount--;
        }
        else
        {
            ActiveMobileBodyCount--;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int RequireSlot(CollidableReference collidable)
    {
        int handle = collidable.RawHandleValue;
        int slot;
        if (collidable.Mobility == CollidableMobility.Static)
        {
            if ((uint)handle >= (uint)_bepuStaticHandleToSlot.Length || (slot = _bepuStaticHandleToSlot[handle]) < 0)
            {
                throw new InvalidOperationException($"Physics3D static handle '{handle}' is not bound.");
            }
        }
        else if ((uint)handle >= (uint)_bepuBodyHandleToSlot.Length || (slot = _bepuBodyHandleToSlot[handle]) < 0)
        {
            throw new InvalidOperationException($"Physics3D body handle '{handle}' is not bound.");
        }

        return slot;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowCollision(CollidableReference a, CollidableReference b)
        => AllowCollision(RequireSlot(a), RequireSlot(b));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowCollision(int slotA, int slotB)
        => LayerMask.TestBidirectional(in _layers[slotA], in _layers[slotB]) &&
           Physics3DCollisionSubgroup.AllowCollision(
               in _collisionSubgroups[slotA],
               in _collisionSubgroups[slotB]);

    public int GetBepuHandle(int slot) => _slotToBepuHandle[slot];
    public Physics3DBodyKind GetBodyKind(int slot) => _bodyKinds[slot];
    public Entity GetEntity(int slot) => _entities[slot];
    public ref readonly LayerMask GetLayer(int slot) => ref _layers[slot];
    public ref readonly Physics3DMaterial GetMaterial(int slot) => ref _materials[slot];
    public ref readonly Physics3DBodyContactPolicy GetContactPolicy(int slot) => ref _contactPolicies[slot];
    public int GetSurfaceVelocitySlot(int denseIndex) => _surfaceVelocitySlots[denseIndex];
    public bool IsSensor(int slot) => _contactPolicies[slot].Kind == Physics3DBodyContactPolicyKind.Sensor;
    public ref readonly Physics3DCollisionSubgroup GetCollisionSubgroup(int slot) => ref _collisionSubgroups[slot];
    public bool IsActiveSlot(int slot) => _slotKinds[slot] != FreeSlot;

    public bool IsAwake(int slot, Simulation simulation)
    {
        if (!IsActiveSlot(slot) || _slotKinds[slot] == StaticSlot)
        {
            return false;
        }

        return simulation.Bodies.GetBodyReference(new BodyHandle(_slotToBepuHandle[slot])).Awake;
    }

    private void BindCommon(int slot, int bepuHandle, in Physics3DBodyDescription description)
    {
        if ((uint)slot >= (uint)_slotKinds.Length || _slotKinds[slot] == FreeSlot)
        {
            throw new InvalidOperationException("Cannot bind a free Physics3D body slot.");
        }

        _slotToBepuHandle[slot] = bepuHandle;
        _entities[slot] = description.Entity;
        _layers[slot] = description.CollisionLayer;
        _materials[slot] = description.Material;
        _bodyKinds[slot] = description.Kind;
        _contactPolicies[slot] = description.ContactPolicy;
        _collisionSubgroups[slot] = description.CollisionSubgroup;
        TrackCollisionMetadata(slot);
    }

    private void TrackCollisionMetadata(int slot)
    {
        if (_collisionMetadataTracked[slot] != 0)
        {
            throw new InvalidOperationException($"Physics3D collision metadata for slot {slot} is already tracked.");
        }

        if (!IsDefaultCollisionFilter(in _layers[slot], in _collisionSubgroups[slot]))
        {
            _customCollisionFilterCount++;
        }

        if (_contactPolicies[slot].Kind != Physics3DBodyContactPolicyKind.Solid)
        {
            _nonSolidContactPolicyCount++;
        }

        if (_contactPolicies[slot].Kind == Physics3DBodyContactPolicyKind.SurfaceVelocity)
        {
            int denseIndex = _surfaceVelocityBodyCount++;
            _surfaceVelocitySlots[denseIndex] = slot;
            _slotToSurfaceVelocityIndex[slot] = denseIndex;
        }

        _collisionMetadataTracked[slot] = 1;
    }

    private void UntrackCollisionMetadata(int slot)
    {
        if (_collisionMetadataTracked[slot] == 0)
        {
            return;
        }

        if (!IsDefaultCollisionFilter(in _layers[slot], in _collisionSubgroups[slot]))
        {
            _customCollisionFilterCount--;
        }

        if (_contactPolicies[slot].Kind != Physics3DBodyContactPolicyKind.Solid)
        {
            _nonSolidContactPolicyCount--;
        }

        if (_contactPolicies[slot].Kind == Physics3DBodyContactPolicyKind.SurfaceVelocity)
        {
            int denseIndex = _slotToSurfaceVelocityIndex[slot];
            if (denseIndex < 0)
            {
                throw new InvalidOperationException($"Physics3D surface-velocity slot {slot} is not tracked.");
            }

            int lastDenseIndex = --_surfaceVelocityBodyCount;
            int movedSlot = _surfaceVelocitySlots[lastDenseIndex];
            _surfaceVelocitySlots[denseIndex] = movedSlot;
            _slotToSurfaceVelocityIndex[movedSlot] = denseIndex;
            _surfaceVelocitySlots[lastDenseIndex] = 0;
            _slotToSurfaceVelocityIndex[slot] = -1;
        }

        _collisionMetadataTracked[slot] = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDefaultCollisionFilter(in LayerMask layer, in Physics3DCollisionSubgroup subgroup)
        => layer.Category == uint.MaxValue &&
           layer.Mask == uint.MaxValue &&
           subgroup.AssemblyId == 0;
}

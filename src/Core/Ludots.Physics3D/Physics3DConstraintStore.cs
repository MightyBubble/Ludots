using System;
using BepuPhysics;

namespace Ludots.Core.Physics3D;

internal sealed class Physics3DConstraintStore
{
    private const byte Free = 0;
    private const byte Pending = 1;
    private const byte Bound = 2;

    private readonly int[] _freeSlots;
    private readonly int[] _generations;
    private readonly byte[] _states;
    private readonly int[] _bepuHandles;
    private readonly int[] _bodySlotsA;
    private readonly int[] _bodySlotsB;
    private readonly int[] _nextA;
    private readonly int[] _nextB;
    private readonly int[] _previousA;
    private readonly int[] _previousB;
    private readonly int[] _headByBodySlot;
    private int _freeSlotCount;

    public Physics3DConstraintStore(int capacity, int bodyCapacity)
    {
        Capacity = capacity;
        _freeSlots = new int[capacity];
        _generations = new int[capacity];
        _states = new byte[capacity];
        _bepuHandles = new int[capacity];
        _bodySlotsA = new int[capacity];
        _bodySlotsB = new int[capacity];
        _nextA = new int[capacity];
        _nextB = new int[capacity];
        _previousA = new int[capacity];
        _previousB = new int[capacity];
        _headByBodySlot = new int[bodyCapacity];
        Array.Fill(_bepuHandles, -1);
        Array.Fill(_bodySlotsA, -1);
        Array.Fill(_bodySlotsB, -1);
        Array.Fill(_nextA, -1);
        Array.Fill(_nextB, -1);
        Array.Fill(_previousA, -1);
        Array.Fill(_previousB, -1);
        Array.Fill(_headByBodySlot, -1);
        for (int i = 0; i < capacity; i++)
        {
            _freeSlots[i] = capacity - 1 - i;
        }

        _freeSlotCount = capacity;
    }

    public int Capacity { get; }
    public int Count { get; private set; }

    public int AllocateSlot()
    {
        if (_freeSlotCount == 0)
        {
            throw new Physics3DCapacityExceededException("constraints", Capacity);
        }

        int slot = _freeSlots[--_freeSlotCount];
        int generation = unchecked(_generations[slot] + 1);
        if (generation <= 0)
        {
            generation = 1;
        }

        _generations[slot] = generation;
        _states[slot] = Pending;
        Count++;
        return slot;
    }

    public void Bind(int slot, ConstraintHandle handle, int bodySlotA, int bodySlotB)
    {
        if ((uint)slot >= (uint)Capacity || _states[slot] != Pending)
        {
            throw new InvalidOperationException("Physics3D constraint slot is not pending.");
        }

        _bepuHandles[slot] = handle.Value;
        _bodySlotsA[slot] = bodySlotA;
        _bodySlotsB[slot] = bodySlotB;
        Link(slot, bodySlotA, endpointA: true);
        Link(slot, bodySlotB, endpointA: false);
        _states[slot] = Bound;
    }

    public void Rollback(int slot)
    {
        if ((uint)slot >= (uint)Capacity || _states[slot] != Pending)
        {
            throw new InvalidOperationException("Physics3D constraint slot is not pending.");
        }

        ReleaseSlot(slot);
    }

    public bool Contains(Physics3DConstraintId id)
        => (uint)id.Slot < (uint)Capacity &&
           id.Generation > 0 &&
           _states[id.Slot] == Bound &&
           _generations[id.Slot] == id.Generation;

    public int RequireSlot(Physics3DConstraintId id)
    {
        if (!Contains(id))
        {
            throw new InvalidOperationException($"Physics3D constraint id '{id}' is stale or unknown.");
        }

        return id.Slot;
    }

    public Physics3DConstraintId GetId(int slot) => new(slot, _generations[slot]);
    public ConstraintHandle GetBepuHandle(int slot) => new(_bepuHandles[slot]);

    public void Remove(Physics3DConstraintId id, Simulation simulation)
    {
        int slot = RequireSlot(id);
        ConstraintHandle handle = GetBepuHandle(slot);
        if (!simulation.Solver.ConstraintExists(handle))
        {
            throw new InvalidOperationException($"Bepu constraint '{handle.Value}' is missing for '{id}'.");
        }

        simulation.Solver.Remove(handle);
        Unlink(slot, _bodySlotsA[slot], endpointA: true);
        Unlink(slot, _bodySlotsB[slot], endpointA: false);
        ReleaseSlot(slot);
    }

    public void RemoveAllForBody(int bodySlot, Simulation simulation)
    {
        int constraintSlot = _headByBodySlot[bodySlot];
        while (constraintSlot >= 0)
        {
            int next = GetNext(constraintSlot, bodySlot);
            Remove(GetId(constraintSlot), simulation);
            constraintSlot = next;
        }
    }

    private void Link(int constraintSlot, int bodySlot, bool endpointA)
    {
        int oldHead = _headByBodySlot[bodySlot];
        SetPrevious(constraintSlot, endpointA, -1);
        SetNext(constraintSlot, endpointA, oldHead);
        if (oldHead >= 0)
        {
            SetPreviousForBody(oldHead, bodySlot, constraintSlot);
        }

        _headByBodySlot[bodySlot] = constraintSlot;
    }

    private void Unlink(int constraintSlot, int bodySlot, bool endpointA)
    {
        int previous = GetPrevious(constraintSlot, endpointA);
        int next = GetNext(constraintSlot, endpointA);
        if (previous < 0)
        {
            _headByBodySlot[bodySlot] = next;
        }
        else
        {
            SetNextForBody(previous, bodySlot, next);
        }

        if (next >= 0)
        {
            SetPreviousForBody(next, bodySlot, previous);
        }
    }

    private int GetNext(int constraintSlot, int bodySlot)
    {
        if (_bodySlotsA[constraintSlot] == bodySlot)
        {
            return _nextA[constraintSlot];
        }

        if (_bodySlotsB[constraintSlot] == bodySlot)
        {
            return _nextB[constraintSlot];
        }

        throw new InvalidOperationException("Constraint adjacency does not contain the requested body slot.");
    }

    private int GetNext(int constraintSlot, bool endpointA) => endpointA ? _nextA[constraintSlot] : _nextB[constraintSlot];
    private int GetPrevious(int constraintSlot, bool endpointA) => endpointA ? _previousA[constraintSlot] : _previousB[constraintSlot];
    private void SetNext(int constraintSlot, bool endpointA, int value) => (endpointA ? ref _nextA[constraintSlot] : ref _nextB[constraintSlot]) = value;
    private void SetPrevious(int constraintSlot, bool endpointA, int value) => (endpointA ? ref _previousA[constraintSlot] : ref _previousB[constraintSlot]) = value;

    private void SetNextForBody(int constraintSlot, int bodySlot, int value)
    {
        if (_bodySlotsA[constraintSlot] == bodySlot)
        {
            _nextA[constraintSlot] = value;
        }
        else if (_bodySlotsB[constraintSlot] == bodySlot)
        {
            _nextB[constraintSlot] = value;
        }
        else
        {
            throw new InvalidOperationException("Constraint adjacency does not contain the requested body slot.");
        }
    }

    private void SetPreviousForBody(int constraintSlot, int bodySlot, int value)
    {
        if (_bodySlotsA[constraintSlot] == bodySlot)
        {
            _previousA[constraintSlot] = value;
        }
        else if (_bodySlotsB[constraintSlot] == bodySlot)
        {
            _previousB[constraintSlot] = value;
        }
        else
        {
            throw new InvalidOperationException("Constraint adjacency does not contain the requested body slot.");
        }
    }

    private void ReleaseSlot(int slot)
    {
        _states[slot] = Free;
        _bepuHandles[slot] = -1;
        _bodySlotsA[slot] = -1;
        _bodySlotsB[slot] = -1;
        _nextA[slot] = -1;
        _nextB[slot] = -1;
        _previousA[slot] = -1;
        _previousB[slot] = -1;
        _freeSlots[_freeSlotCount++] = slot;
        Count--;
    }
}

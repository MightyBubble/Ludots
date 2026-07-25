using System;
using System.Numerics;
using BepuPhysics;

namespace Ludots.Core.Physics3D;

internal enum Physics3DActuationKind : byte
{
    Force = 1,
    Acceleration = 2,
    Torque = 3,
    LinearImpulse = 4,
    AngularImpulse = 5,
    ImpulseAtWorldPoint = 6
}

internal sealed class Physics3DActuationCommandBuffer
{
    private readonly Physics3DBodyId[] _bodyIds;
    private readonly Physics3DActuationKind[] _kinds;
    private readonly Vector3[] _values;
    private readonly Vector3[] _worldPointsCm;
    private readonly int[] _sortedCommandIndices;
    private readonly int[] _commandCountsByBodySlot;
    private readonly int[] _nextCommandIndexByBodySlot;
    private readonly int[] _touchedBodySlots;
    private readonly Vector3[] _mergedLinearImpulses;
    private readonly Vector3[] _mergedAngularImpulses;

    public Physics3DActuationCommandBuffer(int commandCapacity, int bodySlotCapacity)
    {
        if (commandCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(commandCapacity));
        }

        if (bodySlotCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bodySlotCapacity));
        }

        _bodyIds = new Physics3DBodyId[commandCapacity];
        _kinds = new Physics3DActuationKind[commandCapacity];
        _values = new Vector3[commandCapacity];
        _worldPointsCm = new Vector3[commandCapacity];
        _sortedCommandIndices = new int[commandCapacity];
        _commandCountsByBodySlot = new int[bodySlotCapacity];
        _nextCommandIndexByBodySlot = new int[bodySlotCapacity];
        _touchedBodySlots = new int[bodySlotCapacity];
        _mergedLinearImpulses = new Vector3[bodySlotCapacity];
        _mergedAngularImpulses = new Vector3[bodySlotCapacity];
    }

    public int Capacity => _bodyIds.Length;
    public int Count { get; private set; }

    public void Enqueue(
        Physics3DBodyId body,
        Physics3DActuationKind kind,
        Vector3 value,
        Vector3 worldPointCm = default)
    {
        if (Count == Capacity)
        {
            throw new Physics3DCapacityExceededException("actuation commands", Capacity);
        }

        int index = Count++;
        _bodyIds[index] = body;
        _kinds[index] = kind;
        _values[index] = value;
        _worldPointsCm[index] = worldPointCm;
    }

    public void Clear()
    {
        Count = 0;
    }

    public void Replay(Physics3DBodyStore bodies, Simulation simulation, float deltaSeconds)
    {
        if (Count == 0)
        {
            return;
        }

        SortCommandsByBodySlot();
        ValidateCommands(bodies);
        int touchedBodyCount = MergeCommands(bodies, simulation, deltaSeconds);
        ValidateMergedImpulses(touchedBodyCount);

        try
        {
            for (int index = 0; index < touchedBodyCount; index++)
            {
                int slot = _touchedBodySlots[index];
                BodyReference bodyReference = simulation.Bodies.GetBodyReference(
                    new BodyHandle(bodies.GetBepuHandle(slot)));
                Physics3DWorld.SetAwake(bodyReference, true);
                bodyReference.ApplyLinearImpulse(_mergedLinearImpulses[slot]);
                bodyReference.ApplyAngularImpulse(_mergedAngularImpulses[slot]);
            }
        }
        finally
        {
            Count = 0;
        }
    }

    private void SortCommandsByBodySlot()
    {
        for (int commandIndex = 0; commandIndex < Count; commandIndex++)
        {
            int slot = _bodyIds[commandIndex].Slot;
            if ((uint)slot >= (uint)_commandCountsByBodySlot.Length)
            {
                throw new InvalidOperationException(
                    $"Physics3D actuation command body id '{_bodyIds[commandIndex]}' is outside the configured body slot range.");
            }
        }

        int touchedBodyCount = 0;
        for (int commandIndex = 0; commandIndex < Count; commandIndex++)
        {
            int slot = _bodyIds[commandIndex].Slot;
            if (_commandCountsByBodySlot[slot] == 0)
            {
                _touchedBodySlots[touchedBodyCount++] = slot;
            }

            _commandCountsByBodySlot[slot]++;
        }

        Array.Sort(_touchedBodySlots, 0, touchedBodyCount);
        int nextCommandIndex = 0;
        for (int index = 0; index < touchedBodyCount; index++)
        {
            int slot = _touchedBodySlots[index];
            _nextCommandIndexByBodySlot[slot] = nextCommandIndex;
            nextCommandIndex += _commandCountsByBodySlot[slot];
        }

        for (int commandIndex = 0; commandIndex < Count; commandIndex++)
        {
            int slot = _bodyIds[commandIndex].Slot;
            _sortedCommandIndices[_nextCommandIndexByBodySlot[slot]++] = commandIndex;
        }

        for (int index = 0; index < touchedBodyCount; index++)
        {
            int slot = _touchedBodySlots[index];
            _commandCountsByBodySlot[slot] = 0;
        }
    }

    private void ValidateCommands(Physics3DBodyStore bodies)
    {
        for (int sortedIndex = 0; sortedIndex < Count; sortedIndex++)
        {
            int commandIndex = _sortedCommandIndices[sortedIndex];
            int slot = bodies.RequireSlot(_bodyIds[commandIndex]);
            if (bodies.GetBodyKind(slot) != Physics3DBodyKind.Dynamic)
            {
                throw new InvalidOperationException(
                    $"Physics3D actuation commands require a dynamic body; '{_bodyIds[commandIndex]}' is '{bodies.GetBodyKind(slot)}'.");
            }
        }
    }

    private int MergeCommands(Physics3DBodyStore bodies, Simulation simulation, float deltaSeconds)
    {
        int touchedBodyCount = 0;
        int previousSlot = -1;
        for (int sortedIndex = 0; sortedIndex < Count; sortedIndex++)
        {
            int commandIndex = _sortedCommandIndices[sortedIndex];
            int slot = _bodyIds[commandIndex].Slot;
            if (slot != previousSlot)
            {
                _touchedBodySlots[touchedBodyCount++] = slot;
                _mergedLinearImpulses[slot] = Vector3.Zero;
                _mergedAngularImpulses[slot] = Vector3.Zero;
                previousSlot = slot;
            }

            BodyReference bodyReference = simulation.Bodies.GetBodyReference(
                new BodyHandle(bodies.GetBepuHandle(slot)));
            Vector3 value = _values[commandIndex];
            switch (_kinds[commandIndex])
            {
                case Physics3DActuationKind.Force:
                    _mergedLinearImpulses[slot] += value * deltaSeconds;
                    break;
                case Physics3DActuationKind.Acceleration:
                    float inverseMass = bodyReference.LocalInertia.InverseMass;
                    if (!float.IsFinite(inverseMass) || inverseMass <= 0f)
                    {
                        throw new InvalidOperationException(
                            $"Dynamic Physics3D body '{_bodyIds[commandIndex]}' has invalid inverse mass '{inverseMass}'.");
                    }

                    _mergedLinearImpulses[slot] += value * (deltaSeconds / inverseMass);
                    break;
                case Physics3DActuationKind.Torque:
                    _mergedAngularImpulses[slot] += value * deltaSeconds;
                    break;
                case Physics3DActuationKind.LinearImpulse:
                    _mergedLinearImpulses[slot] += value;
                    break;
                case Physics3DActuationKind.AngularImpulse:
                    _mergedAngularImpulses[slot] += value;
                    break;
                case Physics3DActuationKind.ImpulseAtWorldPoint:
                    _mergedLinearImpulses[slot] += value;
                    _mergedAngularImpulses[slot] += Vector3.Cross(
                        _worldPointsCm[commandIndex] - bodyReference.Pose.Position,
                        value);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown Physics3D actuation kind '{_kinds[commandIndex]}'.");
            }
        }

        return touchedBodyCount;
    }

    private void ValidateMergedImpulses(int touchedBodyCount)
    {
        for (int index = 0; index < touchedBodyCount; index++)
        {
            int slot = _touchedBodySlots[index];
            Physics3DValidation.RequireFinite(_mergedLinearImpulses[slot], "mergedLinearImpulse");
            Physics3DValidation.RequireFinite(_mergedAngularImpulses[slot], "mergedAngularImpulse");
        }
    }
}

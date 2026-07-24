using System;
using BepuPhysics;

namespace Ludots.Core.Physics3D;

internal sealed class Physics3DContactCollector
{
    private readonly ulong[] _workerKeys;
    private readonly int[] _workerCounts;
    private readonly ulong[] _candidateKeys;
    private ulong[] _persistentKeys;
    private ulong[] _nextPersistentKeys;
    private Physics3DContactPair[] _pairs;
    private Physics3DContactPair[] _nextPairs;
    private readonly Physics3DContactEvent[] _events;
    private readonly int _workerCapacity;
    private readonly int _pairCapacity;
    private bool _overflowed;

    public Physics3DContactCollector(int workerCount, int workerCapacity)
    {
        if (workerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workerCount));
        }

        if (workerCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workerCapacity));
        }

        _pairCapacity = checked(workerCount * workerCapacity);
        _workerKeys = new ulong[_pairCapacity];
        _workerCounts = new int[workerCount];
        _candidateKeys = new ulong[_pairCapacity];
        _persistentKeys = new ulong[_pairCapacity];
        _nextPersistentKeys = new ulong[_pairCapacity];
        _pairs = new Physics3DContactPair[_pairCapacity];
        _nextPairs = new Physics3DContactPair[_pairCapacity];
        _events = new Physics3DContactEvent[checked(_pairCapacity * 3)];
        _workerCapacity = workerCapacity;
    }

    public int Count { get; private set; }
    public int EventCount { get; private set; }

    public void BeginStep()
    {
        Array.Clear(_workerCounts);
        EventCount = 0;
        _overflowed = false;
    }

    public void Record(int workerIndex, int slotA, int slotB)
    {
        if ((uint)workerIndex >= (uint)_workerCounts.Length)
        {
            throw new InvalidOperationException($"Physics3D contact callback reported worker index '{workerIndex}' outside configured range.");
        }

        int localIndex = _workerCounts[workerIndex];
        if (localIndex >= _workerCapacity)
        {
            _overflowed = true;
            return;
        }

        _workerCounts[workerIndex] = localIndex + 1;
        _workerKeys[workerIndex * _workerCapacity + localIndex] = CreateKey(slotA, slotB);
    }

    public void CompleteStep(Physics3DBodyStore bodies, Simulation simulation, long stepIndex)
    {
        if (_overflowed)
        {
            throw new Physics3DCapacityExceededException("contact pairs per worker", _workerCapacity);
        }

        int candidateCount = MergeAndDeduplicateWorkerKeys();
        int previousIndex = 0;
        int candidateIndex = 0;
        int nextCount = 0;
        while (previousIndex < Count || candidateIndex < candidateCount)
        {
            ulong previousKey = previousIndex < Count ? _persistentKeys[previousIndex] : ulong.MaxValue;
            ulong candidateKey = candidateIndex < candidateCount ? _candidateKeys[candidateIndex] : ulong.MaxValue;
            if (previousKey == candidateKey)
            {
                Physics3DContactPair pair = CreatePair(candidateKey, bodies, stepIndex);
                AddPersistent(candidateKey, pair, ref nextCount);
                AddEvent(pair, Physics3DContactEventKind.Stay, stepIndex);
                previousIndex++;
                candidateIndex++;
            }
            else if (candidateKey < previousKey)
            {
                Physics3DContactPair pair = CreatePair(candidateKey, bodies, stepIndex);
                AddPersistent(candidateKey, pair, ref nextCount);
                AddEvent(pair, Physics3DContactEventKind.Begin, stepIndex);
                candidateIndex++;
            }
            else
            {
                Physics3DContactPair previousPair = _pairs[previousIndex];
                if (IsSleepingPair(previousKey, bodies, simulation))
                {
                    Physics3DContactPair pair = new(
                        previousPair.BodyA,
                        previousPair.EntityA,
                        previousPair.BodyB,
                        previousPair.EntityB,
                        stepIndex);
                    AddPersistent(previousKey, pair, ref nextCount);
                    AddEvent(pair, Physics3DContactEventKind.Stay, stepIndex);
                }
                else
                {
                    AddEvent(previousPair, Physics3DContactEventKind.End, stepIndex);
                }

                previousIndex++;
            }
        }

        (_persistentKeys, _nextPersistentKeys) = (_nextPersistentKeys, _persistentKeys);
        (_pairs, _nextPairs) = (_nextPairs, _pairs);
        Count = nextCount;
    }

    public void RemoveBody(int bodySlot, long stepIndex)
    {
        int outputIndex = 0;
        for (int index = 0; index < Count; index++)
        {
            ulong key = _persistentKeys[index];
            if (GetLowSlot(key) == bodySlot || GetHighSlot(key) == bodySlot)
            {
                AddEvent(_pairs[index], Physics3DContactEventKind.End, stepIndex);
                continue;
            }

            if (outputIndex != index)
            {
                _persistentKeys[outputIndex] = key;
                _pairs[outputIndex] = _pairs[index];
            }

            outputIndex++;
        }

        Count = outputIndex;
    }

    public int CopyPairsTo(Span<Physics3DContactPair> destination)
    {
        if (destination.Length < Count)
        {
            throw new Physics3DCapacityExceededException("contact pair destination", destination.Length);
        }

        _pairs.AsSpan(0, Count).CopyTo(destination);
        return Count;
    }

    public int CopyEventsTo(Span<Physics3DContactEvent> destination)
    {
        if (destination.Length < EventCount)
        {
            throw new Physics3DCapacityExceededException("contact event destination", destination.Length);
        }

        _events.AsSpan(0, EventCount).CopyTo(destination);
        return EventCount;
    }

    private int MergeAndDeduplicateWorkerKeys()
    {
        int total = 0;
        for (int workerIndex = 0; workerIndex < _workerCounts.Length; workerIndex++)
        {
            int count = _workerCounts[workerIndex];
            _workerKeys.AsSpan(workerIndex * _workerCapacity, count).CopyTo(_candidateKeys.AsSpan(total));
            total += count;
        }

        if (total == 0)
        {
            return 0;
        }

        Span<ulong> keys = _candidateKeys.AsSpan(0, total);
        keys.Sort();
        int uniqueCount = 1;
        ulong previous = keys[0];
        for (int index = 1; index < keys.Length; index++)
        {
            ulong key = keys[index];
            if (key == previous)
            {
                continue;
            }

            previous = key;
            keys[uniqueCount++] = key;
        }

        return uniqueCount;
    }

    private void AddPersistent(ulong key, Physics3DContactPair pair, ref int count)
    {
        if (count >= _pairCapacity)
        {
            throw new Physics3DCapacityExceededException("persistent contact pairs", _pairCapacity);
        }

        _nextPersistentKeys[count] = key;
        _nextPairs[count] = pair;
        count++;
    }

    private void AddEvent(Physics3DContactPair pair, Physics3DContactEventKind kind, long stepIndex)
    {
        if (EventCount >= _events.Length)
        {
            throw new Physics3DCapacityExceededException("contact events", _events.Length);
        }

        var contactEvent = new Physics3DContactEvent(
            pair.BodyA,
            pair.EntityA,
            pair.BodyB,
            pair.EntityB,
            kind,
            stepIndex);
        ulong key = CreateKey(pair.BodyA.Slot, pair.BodyB.Slot);
        int insertIndex = EventCount;
        while (insertIndex > 0)
        {
            ref Physics3DContactEvent previous = ref _events[insertIndex - 1];
            ulong previousKey = CreateKey(previous.BodyA.Slot, previous.BodyB.Slot);
            if (previousKey < key || (previousKey == key && previous.Kind <= kind))
            {
                break;
            }

            _events[insertIndex] = previous;
            insertIndex--;
        }

        _events[insertIndex] = contactEvent;
        EventCount++;
    }

    private static Physics3DContactPair CreatePair(ulong key, Physics3DBodyStore bodies, long stepIndex)
    {
        int slotA = GetLowSlot(key);
        int slotB = GetHighSlot(key);
        return new Physics3DContactPair(
            bodies.GetId(slotA),
            bodies.GetEntity(slotA),
            bodies.GetId(slotB),
            bodies.GetEntity(slotB),
            stepIndex);
    }

    private static bool IsSleepingPair(ulong key, Physics3DBodyStore bodies, Simulation simulation)
    {
        int slotA = GetLowSlot(key);
        int slotB = GetHighSlot(key);
        return bodies.IsActiveSlot(slotA) &&
               bodies.IsActiveSlot(slotB) &&
               !bodies.IsAwake(slotA, simulation) &&
               !bodies.IsAwake(slotB, simulation);
    }

    private static ulong CreateKey(int slotA, int slotB)
    {
        uint low = unchecked((uint)Math.Min(slotA, slotB));
        uint high = unchecked((uint)Math.Max(slotA, slotB));
        return ((ulong)low << 32) | high;
    }

    private static int GetLowSlot(ulong key) => unchecked((int)(key >> 32));
    private static int GetHighSlot(ulong key) => unchecked((int)key);
}

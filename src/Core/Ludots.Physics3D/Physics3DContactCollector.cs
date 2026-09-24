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
    private readonly byte[] _pairEventKinds;
    private readonly byte[] _nextPairEventKinds;
    private readonly Physics3DContactPair[] _endedPairs;
    private readonly ulong[] _endedKeys;
    private readonly Physics3DContactEvent[] _events;
    private readonly int _workerCapacity;
    private readonly int _pairCapacity;
    private int _endedCount;
    private long _eventsStepIndex;
    private bool _eventsMaterialized;
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
        _pairEventKinds = new byte[_pairCapacity];
        _nextPairEventKinds = new byte[_pairCapacity];
        _endedPairs = new Physics3DContactPair[_pairCapacity];
        _endedKeys = new ulong[_pairCapacity];
        _events = new Physics3DContactEvent[checked(_pairCapacity * 2)];
        _workerCapacity = workerCapacity;
    }

    public int Count { get; private set; }
    public int EventCount { get; private set; }

    public void BeginStep()
    {
        Array.Clear(_workerCounts);
        EventCount = 0;
        _endedCount = 0;
        _eventsMaterialized = false;
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
        int endedCount = 0;
        // Both inputs are key-sorted. Stay events are deferred until CopyEventsTo so dense piles
        // do not rewrite tens of thousands of Stay records on the Step hot path.
        while (previousIndex < Count || candidateIndex < candidateCount)
        {
            ulong previousKey = previousIndex < Count ? _persistentKeys[previousIndex] : ulong.MaxValue;
            ulong candidateKey = candidateIndex < candidateCount ? _candidateKeys[candidateIndex] : ulong.MaxValue;
            if (previousKey == candidateKey)
            {
                Physics3DContactPair previousPair = _pairs[previousIndex];
                Physics3DContactPair pair = new(
                    previousPair.BodyA,
                    previousPair.EntityA,
                    previousPair.BodyB,
                    previousPair.EntityB,
                    stepIndex,
                    previousPair.ContactKind);
                AddPersistent(candidateKey, pair, Physics3DContactEventKind.Stay, ref nextCount);
                previousIndex++;
                candidateIndex++;
            }
            else if (candidateKey < previousKey)
            {
                Physics3DContactPair pair = CreatePair(candidateKey, bodies, stepIndex);
                AddPersistent(candidateKey, pair, Physics3DContactEventKind.Begin, ref nextCount);
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
                        stepIndex,
                        previousPair.ContactKind);
                    AddPersistent(previousKey, pair, Physics3DContactEventKind.Stay, ref nextCount);
                }
                else
                {
                    if (endedCount >= _pairCapacity)
                    {
                        throw new Physics3DCapacityExceededException("ended contact pairs", _pairCapacity);
                    }

                    _endedKeys[endedCount] = previousKey;
                    _endedPairs[endedCount] = previousPair;
                    endedCount++;
                }

                previousIndex++;
            }
        }

        (_persistentKeys, _nextPersistentKeys) = (_nextPersistentKeys, _persistentKeys);
        (_pairs, _nextPairs) = (_nextPairs, _pairs);
        // Event kinds were written into _nextPairEventKinds alongside _nextPairs.
        _nextPairEventKinds.AsSpan(0, nextCount).CopyTo(_pairEventKinds.AsSpan(0, nextCount));
        Count = nextCount;
        _endedCount = endedCount;
        _eventsStepIndex = stepIndex;
        EventCount = checked(nextCount + endedCount);
        _eventsMaterialized = false;
    }

    public void RemoveBody(int bodySlot, long stepIndex)
    {
        MaterializeEvents();
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
                _pairEventKinds[outputIndex] = _pairEventKinds[index];
            }

            outputIndex++;
        }

        Count = outputIndex;
        SortEvents();
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
        MaterializeEvents();
        if (destination.Length < EventCount)
        {
            throw new Physics3DCapacityExceededException("contact event destination", destination.Length);
        }

        _events.AsSpan(0, EventCount).CopyTo(destination);
        return EventCount;
    }

    public bool ContainsPersistentPair(int slotA, int slotB)
    {
        ulong key = CreateKey(slotA, slotB);
        int low = 0;
        int high = Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            ulong candidate = _persistentKeys[middle];
            if (candidate == key)
            {
                return true;
            }

            if (candidate < key)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return false;
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
        // Reuse the next-persistent buffer as radix scratch; it is overwritten later in CompleteStep.
        RadixSort(keys, _nextPersistentKeys.AsSpan(0, total));
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

    private static void RadixSort(Span<ulong> values, Span<ulong> scratch)
    {
        if (values.Length <= 1)
        {
            return;
        }

        Span<int> counts = stackalloc int[256];
        bool dataInValues = true;
        for (int shift = 0; shift < 64; shift += 8)
        {
            Span<ulong> source = dataInValues ? values : scratch;
            Span<ulong> destination = dataInValues ? scratch : values;
            counts.Clear();
            for (int index = 0; index < source.Length; index++)
            {
                counts[(int)((source[index] >> shift) & 0xFF)]++;
            }

            int sum = 0;
            for (int bucket = 0; bucket < counts.Length; bucket++)
            {
                int count = counts[bucket];
                counts[bucket] = sum;
                sum += count;
            }

            for (int index = 0; index < source.Length; index++)
            {
                ulong value = source[index];
                int bucket = (int)((value >> shift) & 0xFF);
                destination[counts[bucket]++] = value;
            }

            dataInValues = !dataInValues;
        }

        if (!dataInValues)
        {
            scratch.CopyTo(values);
        }
    }

    private void AddPersistent(
        ulong key,
        Physics3DContactPair pair,
        Physics3DContactEventKind eventKind,
        ref int count)
    {
        if (count >= _pairCapacity)
        {
            throw new Physics3DCapacityExceededException("persistent contact pairs", _pairCapacity);
        }

        _nextPersistentKeys[count] = key;
        _nextPairs[count] = pair;
        _nextPairEventKinds[count] = (byte)eventKind;
        count++;
    }

    private void MaterializeEvents()
    {
        if (_eventsMaterialized)
        {
            return;
        }

        int pairIndex = 0;
        int endedIndex = 0;
        int output = 0;
        long stepIndex = _eventsStepIndex;
        while (pairIndex < Count || endedIndex < _endedCount)
        {
            ulong pairKey = pairIndex < Count ? _persistentKeys[pairIndex] : ulong.MaxValue;
            ulong endedKey = endedIndex < _endedCount ? _endedKeys[endedIndex] : ulong.MaxValue;
            if (pairKey <= endedKey && pairIndex < Count)
            {
                Physics3DContactPair pair = _pairs[pairIndex];
                WriteEvent(
                    ref output,
                    pair,
                    (Physics3DContactEventKind)_pairEventKinds[pairIndex],
                    stepIndex);
                pairIndex++;
            }
            else
            {
                WriteEvent(
                    ref output,
                    _endedPairs[endedIndex],
                    Physics3DContactEventKind.End,
                    stepIndex);
                endedIndex++;
            }
        }

        EventCount = output;
        _eventsMaterialized = true;
    }

    private void WriteEvent(
        ref int output,
        in Physics3DContactPair pair,
        Physics3DContactEventKind kind,
        long stepIndex)
    {
        if (output >= _events.Length)
        {
            throw new Physics3DCapacityExceededException("contact events", _events.Length);
        }

        _events[output++] = new Physics3DContactEvent(
            pair.BodyA,
            pair.EntityA,
            pair.BodyB,
            pair.EntityB,
            kind,
            pair.ContactKind,
            stepIndex);
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
            pair.ContactKind,
            stepIndex);
        _events[EventCount++] = contactEvent;
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
            stepIndex,
            bodies.IsSensor(slotA) || bodies.IsSensor(slotB)
                ? Physics3DContactKind.Sensor
                : Physics3DContactKind.Solid);
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

    private void SortEvents()
    {
        Span<Physics3DContactEvent> events = _events.AsSpan(0, EventCount);
        for (int start = events.Length / 2 - 1; start >= 0; start--)
        {
            SiftDown(events, start, events.Length);
        }

        for (int end = events.Length - 1; end > 0; end--)
        {
            (events[0], events[end]) = (events[end], events[0]);
            SiftDown(events, 0, end);
        }
    }

    private static void SiftDown(Span<Physics3DContactEvent> events, int root, int length)
    {
        while (true)
        {
            int child = root * 2 + 1;
            if (child >= length)
            {
                return;
            }

            if (child + 1 < length && Compare(events[child], events[child + 1]) < 0)
            {
                child++;
            }

            if (Compare(events[root], events[child]) >= 0)
            {
                return;
            }

            (events[root], events[child]) = (events[child], events[root]);
            root = child;
        }
    }

    private static int Compare(in Physics3DContactEvent left, in Physics3DContactEvent right)
    {
        ulong leftKey = CreateKey(left.BodyA.Slot, left.BodyB.Slot);
        ulong rightKey = CreateKey(right.BodyA.Slot, right.BodyB.Slot);
        int keyComparison = leftKey.CompareTo(rightKey);
        if (keyComparison != 0)
        {
            return keyComparison;
        }

        int generationAComparison = left.BodyA.Generation.CompareTo(right.BodyA.Generation);
        if (generationAComparison != 0)
        {
            return generationAComparison;
        }

        int generationBComparison = left.BodyB.Generation.CompareTo(right.BodyB.Generation);
        return generationBComparison != 0
            ? generationBComparison
            : left.Kind.CompareTo(right.Kind);
    }
}

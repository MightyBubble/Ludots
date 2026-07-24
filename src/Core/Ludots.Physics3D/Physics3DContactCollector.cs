using System;
using System.Runtime.InteropServices;
using BepuPhysics;

namespace Ludots.Core.Physics3D;

internal sealed class Physics3DContactCollector
{
    private const int RadixBitsPerPass = 16;
    private const int RadixBucketCount = 1 << RadixBitsPerPass;
    private const int RadixPassCount = 64 / RadixBitsPerPass;
    private const ulong RadixMask = RadixBucketCount - 1UL;

    private readonly ulong[] _workerKeys;
    private readonly PaddedWorkerCount[] _workerCounts;
    private readonly ulong[] _candidateKeys;
    private readonly ulong[] _radixScratchKeys;
    private readonly int[] _radixBucketCounts;
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
        _workerCounts = new PaddedWorkerCount[workerCount];
        _candidateKeys = new ulong[_pairCapacity];
        _radixScratchKeys = new ulong[_pairCapacity];
        _radixBucketCounts = new int[RadixBucketCount];
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

        int localIndex = _workerCounts[workerIndex].Value;
        if (localIndex >= _workerCapacity)
        {
            _overflowed = true;
            return;
        }

        _workerCounts[workerIndex].Value = localIndex + 1;
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
        // Both inputs are key-sorted, so this merge also appends events in their final deterministic order.
        while (previousIndex < Count || candidateIndex < candidateCount)
        {
            ulong previousKey = previousIndex < Count ? _persistentKeys[previousIndex] : ulong.MaxValue;
            ulong candidateKey = candidateIndex < candidateCount ? _candidateKeys[candidateIndex] : ulong.MaxValue;
            if (previousKey == candidateKey)
            {
                Physics3DContactPair pair = AdvancePair(_pairs[previousIndex], stepIndex);
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
                    Physics3DContactPair pair = AdvancePair(previousPair, stepIndex);
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
            int count = _workerCounts[workerIndex].Value;
            _workerKeys.AsSpan(workerIndex * _workerCapacity, count).CopyTo(_candidateKeys.AsSpan(total));
            total += count;
        }

        if (total == 0)
        {
            return 0;
        }

        SortCandidateKeys(total);
        Span<ulong> keys = _candidateKeys.AsSpan(0, total);
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

    private void SortCandidateKeys(int count)
    {
        ulong[] source = _candidateKeys;
        ulong[] destination = _radixScratchKeys;
        for (int pass = 0; pass < RadixPassCount; pass++)
        {
            Array.Clear(_radixBucketCounts);
            int shift = pass * RadixBitsPerPass;
            for (int index = 0; index < count; index++)
            {
                int bucket = (int)((source[index] >> shift) & RadixMask);
                _radixBucketCounts[bucket]++;
            }

            int destinationIndex = 0;
            for (int bucket = 0; bucket < _radixBucketCounts.Length; bucket++)
            {
                int bucketCount = _radixBucketCounts[bucket];
                _radixBucketCounts[bucket] = destinationIndex;
                destinationIndex += bucketCount;
            }

            for (int index = 0; index < count; index++)
            {
                ulong key = source[index];
                int bucket = (int)((key >> shift) & RadixMask);
                destination[_radixBucketCounts[bucket]++] = key;
            }

            (source, destination) = (destination, source);
        }
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

    private static Physics3DContactPair AdvancePair(in Physics3DContactPair pair, long stepIndex)
        => new(
            pair.BodyA,
            pair.EntityA,
            pair.BodyB,
            pair.EntityB,
            stepIndex,
            pair.ContactKind);

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

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct PaddedWorkerCount
    {
        [FieldOffset(0)]
        public int Value;
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

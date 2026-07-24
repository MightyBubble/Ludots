using System;
using System.Diagnostics;
using BepuUtilities;
using BepuUtilities.Memory;

namespace Ludots.Core.Physics3D;

internal sealed class Physics3DThreadDispatcher : IThreadDispatcher, IDisposable
{
    private readonly ThreadDispatcher _inner;
    private readonly Action<int> _trackedWorkerBody;
    private readonly long[] _allocatedBytesByWorker;
    private readonly long[] _elapsedTimestampTicksByWorker;
    private Action<int>? _currentWorkerBody;
    private bool _disposed;

    public Physics3DThreadDispatcher(int threadCount)
    {
        if (threadCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threadCount), threadCount, "Thread count must be positive.");
        }

        _inner = new ThreadDispatcher(threadCount);
        _trackedWorkerBody = TrackWorker;
        _allocatedBytesByWorker = new long[threadCount];
        _elapsedTimestampTicksByWorker = new long[threadCount];
    }

    public int ThreadCount => _inner.ThreadCount;

    public long BackgroundWorkerAllocatedBytesCurrentStep
    {
        get
        {
            long total = 0;
            for (int index = 1; index < _allocatedBytesByWorker.Length; index++)
            {
                total += _allocatedBytesByWorker[index];
            }

            return total;
        }
    }

    public long BackgroundWorkerCpuTimestampTicksCurrentStep
    {
        get
        {
            long total = 0;
            for (int index = 1; index < _elapsedTimestampTicksByWorker.Length; index++)
            {
                total += _elapsedTimestampTicksByWorker[index];
            }

            return total;
        }
    }

    public void BeginStepMetrics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Array.Clear(_allocatedBytesByWorker);
        Array.Clear(_elapsedTimestampTicksByWorker);
    }

    public void DispatchWorkers(Action<int> workerBody, int maximumWorkerCount = int.MaxValue)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(workerBody);
        if (_currentWorkerBody is not null)
        {
            throw new InvalidOperationException("Physics3D thread dispatcher is not reentrant.");
        }

        _currentWorkerBody = workerBody;
        try
        {
            _inner.DispatchWorkers(_trackedWorkerBody, maximumWorkerCount);
        }
        finally
        {
            _currentWorkerBody = null;
        }
    }

    public BufferPool GetThreadMemoryPool(int workerIndex) => _inner.GetThreadMemoryPool(workerIndex);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inner.Dispose();
    }

    private void TrackWorker(int workerIndex)
    {
        Action<int> workerBody = _currentWorkerBody
            ?? throw new InvalidOperationException("Physics3D dispatcher has no active worker body.");
        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        long timestamp = Stopwatch.GetTimestamp();
        workerBody(workerIndex);
        _elapsedTimestampTicksByWorker[workerIndex] += Stopwatch.GetTimestamp() - timestamp;
        _allocatedBytesByWorker[workerIndex] += GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
    }
}

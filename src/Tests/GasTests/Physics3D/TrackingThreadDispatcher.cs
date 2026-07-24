using System;
using System.Threading;
using BepuUtilities;
using BepuUtilities.Memory;

namespace Ludots.Tests.Physics3D;

internal sealed class TrackingThreadDispatcher : IThreadDispatcher, IDisposable
{
    private readonly Worker[] _workers;
    private readonly AutoResetEvent _finished = new(false);
    private readonly BufferPool[] _bufferPools;
    private readonly long[] _allocatedBytesByWorker;
    private volatile Action<int>? _workerBody;
    private int _remainingWorkerCounter;
    private volatile bool _disposed;

    public TrackingThreadDispatcher(int threadCount, int threadPoolBlockAllocationSize = 16_384)
    {
        if (threadCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threadCount));
        }

        ThreadCount = threadCount;
        _workers = new Worker[threadCount - 1];
        for (int index = 0; index < _workers.Length; index++)
        {
            var signal = new AutoResetEvent(false);
            var thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = $"Physics3D allocation worker {index + 1}"
            };
            _workers[index] = new Worker(thread, signal);
            thread.Start(new WorkerState(signal, index + 1));
        }

        _bufferPools = new BufferPool[threadCount];
        for (int index = 0; index < _bufferPools.Length; index++)
        {
            _bufferPools[index] = new BufferPool(threadPoolBlockAllocationSize);
        }

        _allocatedBytesByWorker = new long[threadCount];
    }

    public int ThreadCount { get; }
    public bool IsDisposed => _disposed;

    public long BackgroundWorkerAllocatedBytes
    {
        get
        {
            long total = 0;
            for (int index = 1; index < _allocatedBytesByWorker.Length; index++)
            {
                total += Volatile.Read(ref _allocatedBytesByWorker[index]);
            }

            return total;
        }
    }

    public void DispatchWorkers(Action<int> workerBody, int maximumWorkerCount = int.MaxValue)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(workerBody);
        if (maximumWorkerCount <= 0)
        {
            return;
        }

        if (maximumWorkerCount == 1 || ThreadCount == 1)
        {
            TrackWorker(workerBody, 0);
            return;
        }

        if (_workerBody != null)
        {
            throw new InvalidOperationException("TrackingThreadDispatcher is not reentrant.");
        }

        _workerBody = workerBody;
        int workersToSignal = Math.Min(maximumWorkerCount - 1, _workers.Length);
        _remainingWorkerCounter = workersToSignal;
        for (int index = 0; index < workersToSignal; index++)
        {
            _workers[index].Signal.Set();
        }

        DispatchThread(0);
        _finished.WaitOne();
        _workerBody = null;
    }

    public BufferPool GetThreadMemoryPool(int workerIndex)
    {
        if ((uint)workerIndex >= (uint)_bufferPools.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(workerIndex));
        }

        return _bufferPools[workerIndex];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (int index = 0; index < _workers.Length; index++)
        {
            _workers[index].Signal.Set();
        }

        for (int index = 0; index < _workers.Length; index++)
        {
            _workers[index].Thread.Join();
            _workers[index].Signal.Dispose();
        }

        for (int index = 0; index < _bufferPools.Length; index++)
        {
            _bufferPools[index].Clear();
        }

        _finished.Dispose();
    }

    private void WorkerLoop(object? state)
    {
        WorkerState worker = state as WorkerState
            ?? throw new InvalidOperationException("Physics3D allocation worker state is invalid.");
        while (true)
        {
            worker.Signal.WaitOne();
            if (_disposed)
            {
                return;
            }

            DispatchThread(worker.Index);
        }
    }

    private void DispatchThread(int workerIndex)
    {
        Action<int> workerBody = _workerBody
            ?? throw new InvalidOperationException("Physics3D allocation worker has no dispatched work.");
        TrackWorker(workerBody, workerIndex);
        if (Interlocked.Decrement(ref _remainingWorkerCounter) == -1)
        {
            _finished.Set();
        }
    }

    private void TrackWorker(Action<int> workerBody, int workerIndex)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        workerBody(workerIndex);
        _allocatedBytesByWorker[workerIndex] += GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private readonly record struct Worker(Thread Thread, AutoResetEvent Signal);

    private sealed record WorkerState(AutoResetEvent Signal, int Index);
}

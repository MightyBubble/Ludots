using System;
using System.Diagnostics;
using System.Threading;
using BepuUtilities;
using BepuUtilities.Memory;

namespace Ludots.Core.Physics3D;

internal sealed class Physics3DThreadDispatcher : IThreadDispatcher, IDisposable
{
    private readonly Worker[] _workers;
    private readonly AutoResetEvent _finished = new(false);
    private readonly BufferPool[] _bufferPools;
    private readonly long[] _allocatedBytesByWorker;
    private readonly long[] _dispatchElapsedTimestampTicksByWorker;
    private volatile Action<int>? _currentWorkerBody;
    private int _remainingWorkerCounter;
    private volatile bool _disposed;

    public Physics3DThreadDispatcher(
        int threadCount,
        int threadMemoryPoolBlockAllocationSize,
        int memoryPoolExpectedPooledResourceCount)
    {
        if (threadCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threadCount), threadCount, "Thread count must be positive.");
        }

        if (threadMemoryPoolBlockAllocationSize <= 0 ||
            (threadMemoryPoolBlockAllocationSize & (threadMemoryPoolBlockAllocationSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threadMemoryPoolBlockAllocationSize),
                threadMemoryPoolBlockAllocationSize,
                "Thread memory pool block allocation size must be a positive power of two.");
        }

        if (memoryPoolExpectedPooledResourceCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(memoryPoolExpectedPooledResourceCount),
                memoryPoolExpectedPooledResourceCount,
                "Memory pool expected resource count must be positive.");
        }

        ThreadCount = threadCount;
        _bufferPools = new BufferPool[threadCount];
        for (int index = 0; index < _bufferPools.Length; index++)
        {
            _bufferPools[index] = new BufferPool(
                threadMemoryPoolBlockAllocationSize,
                memoryPoolExpectedPooledResourceCount);
        }

        _allocatedBytesByWorker = new long[threadCount];
        _dispatchElapsedTimestampTicksByWorker = new long[threadCount];
        _workers = new Worker[threadCount - 1];
        for (int index = 0; index < _workers.Length; index++)
        {
            var signal = new AutoResetEvent(false);
            var thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = $"Physics3D worker {index + 1}"
            };
            _workers[index] = new Worker(thread, signal);
            thread.Start(new WorkerState(signal, index + 1));
        }
    }

    public int ThreadCount { get; }

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

    public long BackgroundWorkerDispatchElapsedTimestampTicksCurrentStep
    {
        get
        {
            long total = 0;
            for (int index = 1; index < _dispatchElapsedTimestampTicksByWorker.Length; index++)
            {
                total += _dispatchElapsedTimestampTicksByWorker[index];
            }

            return total;
        }
    }

    public void BeginStepMetrics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Array.Clear(_allocatedBytesByWorker);
        Array.Clear(_dispatchElapsedTimestampTicksByWorker);
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

        if (_currentWorkerBody is not null)
        {
            throw new InvalidOperationException("Physics3D thread dispatcher is not reentrant.");
        }

        _currentWorkerBody = workerBody;
        try
        {
            int workersToSignal = Math.Min(maximumWorkerCount - 1, _workers.Length);
            _remainingWorkerCounter = workersToSignal;
            for (int index = 0; index < workersToSignal; index++)
            {
                _workers[index].Signal.Set();
            }

            DispatchThread(0);
            _finished.WaitOne();
        }
        finally
        {
            _currentWorkerBody = null;
        }
    }

    public BufferPool GetThreadMemoryPool(int workerIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)workerIndex >= (uint)_bufferPools.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(workerIndex), workerIndex, "Worker index is outside the configured range.");
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
            ?? throw new InvalidOperationException("Physics3D worker state is invalid.");
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
        Action<int> workerBody = _currentWorkerBody
            ?? throw new InvalidOperationException("Physics3D dispatcher has no active worker body.");
        TrackWorker(workerBody, workerIndex);
        if (Interlocked.Decrement(ref _remainingWorkerCounter) == -1)
        {
            _finished.Set();
        }
    }

    private void TrackWorker(Action<int> workerBody, int workerIndex)
    {
        if (workerIndex == 0)
        {
            workerBody(workerIndex);
            return;
        }

        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        long timestamp = Stopwatch.GetTimestamp();
        workerBody(workerIndex);
        _dispatchElapsedTimestampTicksByWorker[workerIndex] += Stopwatch.GetTimestamp() - timestamp;
        _allocatedBytesByWorker[workerIndex] += GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
    }

    private readonly struct Worker
    {
        public Worker(Thread thread, AutoResetEvent signal)
        {
            Thread = thread;
            Signal = signal;
        }

        public Thread Thread { get; }
        public AutoResetEvent Signal { get; }
    }

    private sealed class WorkerState
    {
        public WorkerState(AutoResetEvent signal, int index)
        {
            Signal = signal;
            Index = index;
        }

        public AutoResetEvent Signal { get; }
        public int Index { get; }
    }
}

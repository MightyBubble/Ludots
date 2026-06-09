using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Ludots.Core.Navigation.Pathing;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PathServiceRouterCacheTests
    {
        [Test]
        public void PathServiceRouter_RepeatedSameStartGoal_ReusesCachedPathAndReturnsFreshHandles()
        {
            var store = new PathStore(maxPaths: 32, maxPointsPerPath: 16);
            var nodeGraph = new CountingPathService(store, PathDomain.NodeGraph);
            var navMesh = new CountingPathService(store, PathDomain.NavMesh);
            var router = new PathServiceRouter(nodeGraph, navMesh, store, cacheCapacity: 8);
            var request = CreateRequest(1);

            Assert.That(router.TrySolve(in request, out PathResult first), Is.True);
            Assert.That(first.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(router.TrySolve(in request, out PathResult second), Is.True);
            Assert.That(second.Status, Is.EqualTo(PathStatus.Found));

            Assert.That(navMesh.SolveCount, Is.EqualTo(1));
            Assert.That(router.CacheDiagnostics.Hits, Is.EqualTo(1));
            Assert.That(router.CacheDiagnostics.Misses, Is.EqualTo(1));
            Assert.That(first.Handle.Index, Is.Not.EqualTo(second.Handle.Index));
            AssertSamePath(router, first.Handle, second.Handle);
            ReleaseIfAlive(store, first.Handle);
            ReleaseIfAlive(store, second.Handle);
        }

        [Test]
        public void PathServiceRouter_ConcurrentRepeatedStartGoal_IsSerializedAndCacheBacked()
        {
            const int queryCount = 32;
            var store = new PathStore(maxPaths: 128, maxPointsPerPath: 16);
            var nodeGraph = new CountingPathService(store, PathDomain.NodeGraph);
            var navMesh = new CountingPathService(store, PathDomain.NavMesh);
            var router = new PathServiceRouter(nodeGraph, navMesh, store, cacheCapacity: 16);
            var request = CreateRequest(2);
            using var gate = new ManualResetEventSlim(false);
            var tasks = new Task<int>[queryCount];

            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    gate.Wait();
                    Assert.That(router.TrySolve(in request, out PathResult result), Is.True);
                    Assert.That(result.Status, Is.EqualTo(PathStatus.Found));
                    int points = CopyPointCount(router, result.Handle);
                    ReleaseIfAlive(store, result.Handle);
                    return points;
                });
            }

            gate.Set();
            Task.WaitAll(tasks);

            Assert.That(tasks, Has.All.Matches<Task<int>>(task => task.Result == 3));
            Assert.That(navMesh.SolveCount, Is.EqualTo(1));
            Assert.That(router.CacheDiagnostics.Hits, Is.EqualTo(queryCount - 1));
            Assert.That(router.CacheDiagnostics.Misses, Is.EqualTo(1));
        }

        [Test]
        public void Benchmark_PathServiceRouter_RepeatedStartGoal_WarmCache()
        {
            const int measuredQueries = 4096;
            var store = new PathStore(maxPaths: 512, maxPointsPerPath: 16);
            var nodeGraph = new CountingPathService(store, PathDomain.NodeGraph);
            var navMesh = new CountingPathService(store, PathDomain.NavMesh);
            var router = new PathServiceRouter(nodeGraph, navMesh, store, cacheCapacity: 32);
            var request = CreateRequest(3);

            Assert.That(router.TrySolve(in request, out PathResult warmup), Is.True);
            ReleaseIfAlive(store, warmup.Handle);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            _ = GC.GetAllocatedBytesForCurrentThread();

            long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
            long startTicks = Stopwatch.GetTimestamp();
            for (int i = 0; i < measuredQueries; i++)
            {
                Assert.That(router.TrySolve(in request, out PathResult result), Is.True);
                Assert.That(result.Status, Is.EqualTo(PathStatus.Found));
                ReleaseIfAlive(store, result.Handle);
            }

            double elapsedMs = (Stopwatch.GetTimestamp() - startTicks) * 1000d / Stopwatch.Frequency;
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
            PathQueryCacheDiagnostics cache = router.CacheDiagnostics;
            double avgUs = elapsedMs * 1000d / measuredQueries;
            Console.WriteLine($"[Benchmark] PathServiceRouter repeated warm start-goal: queries={measuredQueries} avg={avgUs:F3}us allocBytes={allocatedBytes} hits={cache.Hits} misses={cache.Misses} stores={cache.Stores} underlyingSolves={navMesh.SolveCount}");

            Assert.That(navMesh.SolveCount, Is.EqualTo(1));
            Assert.That(cache.Hits, Is.GreaterThanOrEqualTo(measuredQueries));
            Assert.That(cache.Misses, Is.EqualTo(1));
        }

        private static PathRequest CreateRequest(int id)
        {
            return new PathRequest(
                requestId: id,
                actor: default,
                domain: PathDomain.NavMesh,
                agentTypeId: "Infantry",
                start: PathEndpoint.FromWorldCm(1_000, 2_000),
                goal: PathEndpoint.FromWorldCm(9_000, 10_000),
                budget: new PathBudget(maxExpanded: 0, maxPoints: 16));
        }

        private static void AssertSamePath(IPathService service, PathHandle first, PathHandle second)
        {
            Span<int> firstX = stackalloc int[16];
            Span<int> firstY = stackalloc int[16];
            Span<int> secondX = stackalloc int[16];
            Span<int> secondY = stackalloc int[16];
            Assert.That(service.TryCopyPath(in first, firstX, firstY, out int firstCount), Is.True);
            Assert.That(service.TryCopyPath(in second, secondX, secondY, out int secondCount), Is.True);
            Assert.That(secondCount, Is.EqualTo(firstCount));
            for (int i = 0; i < firstCount; i++)
            {
                Assert.That(secondX[i], Is.EqualTo(firstX[i]));
                Assert.That(secondY[i], Is.EqualTo(firstY[i]));
            }
        }

        private static int CopyPointCount(IPathService service, PathHandle handle)
        {
            Span<int> xs = stackalloc int[16];
            Span<int> ys = stackalloc int[16];
            Assert.That(service.TryCopyPath(in handle, xs, ys, out int count), Is.True);
            return count;
        }

        private static void ReleaseIfAlive(PathStore store, PathHandle handle)
        {
            if (store.IsAlive(in handle))
            {
                store.Release(in handle);
            }
        }

        private sealed class CountingPathService : IPathService
        {
            private readonly PathStore _store;
            private readonly PathDomain _domain;
            private int _solveCount;

            public CountingPathService(PathStore store, PathDomain domain)
            {
                _store = store;
                _domain = domain;
            }

            public int SolveCount => Volatile.Read(ref _solveCount);

            public bool TrySolve(in PathRequest request, out PathResult result)
            {
                Interlocked.Increment(ref _solveCount);
                if (request.Domain != _domain)
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, 0, errorCode: 2);
                    return false;
                }

                if (!_store.TryAllocate(3, out PathHandle handle))
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, 0, errorCode: 4);
                    return false;
                }

                Span<int> xs = stackalloc[]
                {
                    request.Start.Xcm,
                    (request.Start.Xcm + request.Goal.Xcm) / 2,
                    request.Goal.Xcm
                };
                Span<int> ys = stackalloc[]
                {
                    request.Start.Ycm,
                    (request.Start.Ycm + request.Goal.Ycm) / 2,
                    request.Goal.Ycm
                };
                Assert.That(_store.TryWrite(in handle, xs, ys, 3), Is.True);
                result = new PathResult(request.RequestId, request.Actor, PathStatus.Found, handle, expanded: 3, errorCode: 0);
                return true;
            }

            public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
            {
                return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
            }
        }
    }
}

using System;
using Arch.Core;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Spatial.Eqs;
using Ludots.Core.Spatial.Eqs.Generators;
using Ludots.Core.Spatial.Eqs.Tests;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Spatial
{
    [TestFixture]
    public sealed class PathReachableTestTests
    {
        [Test]
        public void Score_FiltersUnreachableCandidates_AndUsesActorSource()
        {
            using var world = World.Create();
            Entity actor = world.Create();
            var store = new PathStore(maxPaths: 1, maxPointsPerPath: 2);
            var pathService = new ReachabilityPathService(store, reachableGoalXcm: 10);
            var query = new EqsQuery(
                new RingGenerator(radiusCm: 10, count: 2),
                new PathReachableTest("tw.agent"));
            var context = new EqsContext(
                new WorldCmInt2(0, 0),
                world,
                pathService: pathService,
                pathStore: store,
                sourceWorldCm: new WorldCmInt2(-204, -297),
                sourceEntity: actor);
            Span<EqsItem> items = stackalloc EqsItem[2];

            int count = query.Run(context, items);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(items[0].Position, Is.EqualTo(new WorldCmInt2(10, 0)));
            Assert.That(items[0].Filtered, Is.False);
            Assert.That(items[1].Position, Is.EqualTo(new WorldCmInt2(-10, 0)));
            Assert.That(items[1].Filtered, Is.True);
            Assert.That(pathService.LastRequest.Start.Xcm, Is.EqualTo(-204));
            Assert.That(pathService.LastRequest.Start.Ycm, Is.EqualTo(-297));
            Assert.That(pathService.LastRequest.Actor, Is.EqualTo(actor));
            Assert.That(store.TryAllocate(2, out PathHandle handle), Is.True);
            store.Release(in handle);
        }

        private sealed class ReachabilityPathService : IPathService
        {
            private readonly PathStore _store;
            private readonly int _reachableGoalXcm;
            public PathRequest LastRequest;

            public ReachabilityPathService(PathStore store, int reachableGoalXcm)
            {
                _store = store;
                _reachableGoalXcm = reachableGoalXcm;
            }

            public bool TrySolve(in PathRequest request, out PathResult result)
            {
                LastRequest = request;
                if (request.AgentTypeId != "tw.agent" || request.Goal.Xcm != _reachableGoalXcm)
                {
                    result = new PathResult(
                        request.RequestId,
                        request.Actor,
                        PathStatus.NoPath,
                        default,
                        expanded: 0,
                        errorCode: 1);
                    return true;
                }

                Assert.That(_store.TryAllocate(2, out PathHandle handle), Is.True);
                result = new PathResult(
                    request.RequestId,
                    request.Actor,
                    PathStatus.Found,
                    handle,
                    expanded: 0,
                    errorCode: 0,
                    resolvedDomain: PathDomain.Auto);
                return true;
            }

            public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
            {
                return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
            }
        }
    }
}

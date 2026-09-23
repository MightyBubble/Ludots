using System;
using Ludots.Core.Navigation.Pathing;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Spatial.Eqs.Tests
{
    /// <summary>
    /// Filters candidates that cannot be reached from the source point with the
    /// configured pathing agent type.
    /// </summary>
    public sealed class PathReachableTest : IEqsTest
    {
        private readonly string _agentTypeId;

        public PathReachableTest(string agentTypeId)
        {
            if (string.IsNullOrWhiteSpace(agentTypeId))
            {
                throw new ArgumentException("PathReachable requires an agent type id.", nameof(agentTypeId));
            }

            _agentTypeId = agentTypeId;
        }

        public void Score(in EqsContext ctx, ref EqsItem item)
        {
            if (item.Filtered)
            {
                return;
            }

            if (!ctx.SourceWorldCm.HasValue)
            {
                throw new InvalidOperationException(
                    "PathReachableTest requires EqsContext.SourceWorldCm.");
            }

            if (ctx.PathService == null || ctx.PathStore == null)
            {
                // No path service means the map declares no navigable domain (navmesh or node graph),
                // so nothing can be reached from anywhere. Filter the candidate rather than throw:
                // an outright failure here would turn a legitimate "this map has no navigation"
                // configuration into a crash inside the order kernel.
                item.Filtered = true;
                return;
            }

            WorldCmInt2 source = ctx.SourceWorldCm.Value;
            var request = new PathRequest(
                requestId: 0,
                actor: ctx.SourceEntity,
                domain: PathDomain.Auto,
                agentTypeId: _agentTypeId,
                start: PathEndpoint.FromWorldCm(source.X, source.Y),
                goal: PathEndpoint.FromWorldCm(item.Position.X, item.Position.Y),
                budget: new PathBudget(0, 0));

            bool solved = ctx.PathService.TrySolve(in request, out PathResult result) &&
                result.Status == PathStatus.Found &&
                result.Handle.IsValid;

            if (result.Handle.IsValid && ctx.PathStore.IsAlive(in result.Handle))
            {
                ctx.PathStore.Release(in result.Handle);
            }

            if (!solved)
            {
                item.Filtered = true;
            }
        }
    }
}

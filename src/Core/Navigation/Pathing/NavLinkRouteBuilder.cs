using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.Pathing
{
    /// <summary>
    /// 把"跨层移动"拆成 面腿 → Link 段 → 面腿… 的路线构建器。
    ///
    /// 与 <see cref="GraphQuery.GraphHybridRouteBuilder"/> 的分工：那个解决"多个 waypoint 之间怎么连"，
    /// 本类解决"两个 layer 之间怎么换"。Link 段必须由执行层消费（穿越动作），
    /// 因此这里产出的是带段身份的点序列，而不是一条扁平的折线。
    ///
    /// 不生成跨层直线：起点与目标不在同一 layer 时，只有经合法且允许该 profile 的 Link 才可能连通。
    /// </summary>
    public sealed class NavLinkRouteBuilder
    {
        private readonly IPathService _pathService;
        private readonly PathStore _pathStore;
        private readonly NavLinkRegistry _links;
        private readonly NavLayerResolver _layers;
        private readonly int[] _legXcm;
        private readonly int[] _legYcm;
        private int _nextRequestId = 1;

        public NavLinkRouteBuilder(
            IPathService pathService,
            PathStore pathStore,
            NavLinkRegistry links,
            NavLayerResolver layers)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _pathStore = pathStore ?? throw new ArgumentNullException(nameof(pathStore));
            _links = links ?? throw new ArgumentNullException(nameof(links));
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));

            int capacity = pathStore.MaxPointsPerPath;
            _legXcm = new int[capacity];
            _legYcm = new int[capacity];
        }

        /// <summary>
        /// 构建从 <paramref name="start"/> 到 <paramref name="goal"/> 的路线。
        ///
        /// 同层：单条面腿。跨层：必须找到一条起于 start 层的 Link，
        /// 面腿走到 Link 起点 → Link 段跨越 → 从 Link 终点继续面腿。
        /// 找不到可用 Link 时返回 false 并给出原因，**不回退为直线**。
        /// </summary>
        public bool TryBuild(
            Entity actor,
            string agentTypeId,
            string mapId,
            int profileIndex,
            int startLayer,
            in PathEndpoint start,
            int goalLayer,
            in PathEndpoint goal,
            Span<int> outXcm,
            Span<int> outYcm,
            Span<PathSegment> outSegments,
            out int pointCount,
            out int segmentCount,
            out string failure)
        {
            pointCount = 0;
            segmentCount = 0;
            failure = string.Empty;

            if (startLayer == goalLayer)
            {
                return TryAppendSurfaceLeg(
                    actor,
                    agentTypeId,
                    start,
                    goal,
                    outXcm,
                    outYcm,
                    outSegments,
                    ref pointCount,
                    ref segmentCount,
                    out failure);
            }

            if (!_links.TryGetGraph(mapId, out NavLinkGraph graph))
            {
                failure = $"Map '{mapId}' declares no cross-surface links, so layer {_layers.GetName(startLayer)} cannot reach layer {_layers.GetName(goalLayer)}.";
                return false;
            }

            if (!graph.TrySelectUsableLink(startLayer, profileIndex, out NavLink link))
            {
                failure = $"No usable link leaves layer {_layers.GetName(startLayer)} for this profile; the target layer cannot be reached.";
                return false;
            }

            if (link.ToLayer != goalLayer && !link.Bidirectional)
            {
                failure = $"Link '{link.LinkId}' is one-way and does not lead toward layer {_layers.GetName(goalLayer)}.";
                return false;
            }

            return TryAppendLinkRoute(
                actor,
                agentTypeId,
                in link,
                in start,
                in goal,
                outXcm,
                outYcm,
                outSegments,
                ref pointCount,
                ref segmentCount,
                out failure);
        }

        private bool TryAppendLinkRoute(
            Entity actor,
            string agentTypeId,
            in NavLink link,
            in PathEndpoint start,
            in PathEndpoint goal,
            Span<int> outXcm,
            Span<int> outYcm,
            Span<PathSegment> outSegments,
            ref int pointCount,
            ref int segmentCount,
            out string failure)
        {
            var linkStart = PathEndpoint.FromWorldCm(link.FromXCm, link.FromYCm);
            var linkEnd = PathEndpoint.FromWorldCm(link.ToXCm, link.ToYCm);

            if (!TryAppendSurfaceLeg(
                    actor, agentTypeId, in start, in linkStart,
                    outXcm, outYcm, outSegments, ref pointCount, ref segmentCount, out failure))
            {
                return false;
            }

            if (!AppendLinkSegment(in link, linkStart, linkEnd, outXcm, outYcm, outSegments, ref pointCount, ref segmentCount, out failure))
            {
                return false;
            }

            return TryAppendSurfaceLeg(
                actor, agentTypeId, linkEnd, in goal,
                outXcm, outYcm, outSegments, ref pointCount, ref segmentCount, out failure);
        }

        private static bool AppendLinkSegment(
            in NavLink link,
            in PathEndpoint from,
            in PathEndpoint to,
            Span<int> outXcm,
            Span<int> outYcm,
            Span<PathSegment> outSegments,
            ref int pointCount,
            ref int segmentCount,
            out string failure)
        {
            failure = string.Empty;
            if (segmentCount >= outSegments.Length)
            {
                failure = "Route exceeded segment capacity.";
                return false;
            }

            int startIndex = pointCount;
            if (pointCount + 2 > outXcm.Length)
            {
                failure = "Route exceeded point capacity while emitting a link segment.";
                return false;
            }

            outXcm[pointCount] = from.Xcm;
            outYcm[pointCount] = from.Ycm;
            pointCount++;
            outXcm[pointCount] = to.Xcm;
            outYcm[pointCount] = to.Ycm;
            pointCount++;

            outSegments[segmentCount] = PathSegment.Link(startIndex, pointCount - startIndex, link.Id);
            segmentCount++;
            return true;
        }

        private bool TryAppendSurfaceLeg(
            Entity actor,
            string agentTypeId,
            in PathEndpoint start,
            in PathEndpoint goal,
            Span<int> outXcm,
            Span<int> outYcm,
            Span<PathSegment> outSegments,
            ref int pointCount,
            ref int segmentCount,
            out string failure)
        {
            failure = string.Empty;
            if (segmentCount >= outSegments.Length)
            {
                failure = "Route exceeded segment capacity.";
                return false;
            }

            var request = new PathRequest(
                _nextRequestId++,
                actor,
                PathDomain.Auto,
                agentTypeId,
                start,
                goal,
                new PathBudget(maxExpanded: 0, maxPoints: Math.Min(outXcm.Length, _pathStore.MaxPointsPerPath)));

            if (!_pathService.TrySolve(in request, out PathResult result) ||
                result.Status != PathStatus.Found ||
                !result.Handle.IsValid)
            {
                failure = DescribeFailure(result.Status, result.ErrorCode);
                return false;
            }

            try
            {
                if (!_pathService.TryCopyPath(in result.Handle, _legXcm, _legYcm, out int legCount) || legCount <= 0)
                {
                    failure = "Path copy failed.";
                    return false;
                }

                // 衔接上一段：与上一段末点重复时跳过首个点。
                int skip = pointCount == 0 ? 0 : 1;
                int emit = legCount - skip;
                if (emit <= 0)
                {
                    return true;
                }

                if (pointCount + emit > outXcm.Length)
                {
                    failure = "Route exceeded point capacity.";
                    return false;
                }

                int segmentStart = pointCount - (skip == 1 ? 1 : 0);
                for (int i = skip; i < legCount; i++)
                {
                    outXcm[pointCount] = _legXcm[i];
                    outYcm[pointCount] = _legYcm[i];
                    pointCount++;
                }

                outSegments[segmentCount] = PathSegment.Surface(segmentStart, pointCount - segmentStart);
                segmentCount++;
                return true;
            }
            finally
            {
                if (_pathStore.IsAlive(result.Handle))
                {
                    _pathStore.Release(result.Handle);
                }
            }
        }

        private static string DescribeFailure(PathStatus status, int errorCode)
        {
            return status switch
            {
                PathStatus.NoPath => "No path was found near the requested destination.",
                PathStatus.NotReady => "Nav tiles are still streaming.",
                PathStatus.BudgetExceeded => "Path solve hit its path budget.",
                PathStatus.InvalidRequest => $"The path service rejected the request (error {errorCode}).",
                PathStatus.Error => $"The path service returned an error (error {errorCode}).",
                _ => $"The route could not be resolved (error {errorCode})."
            };
        }
    }
}

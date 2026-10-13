using System;

namespace Ludots.Core.Navigation.Pathing
{
    /// <summary>
    /// 路径段的种类。NavMesh / NodeGraph 腿是连续可行走面，Link 段是它们之间的离散连接。
    ///
    /// 执行层必须能区分"沿面行走"与"执行一次跨越动作"，因此段身份是路径结果的正式组成部分，
    /// 不是调试信息。
    /// </summary>
    public enum PathSegmentKind : byte
    {
        None = 0,

        /// <summary>连续可行走面上的一段航点。</summary>
        Surface = 1,

        /// <summary>一次跨表面连接。执行方要按 <see cref="NavLink"/> 的动作合同处理。</summary>
        Link = 2
    }

    /// <summary>
    /// 路径上的一个段。段序列是执行顺序；Link 段的端点即跨越的两侧落点。
    /// </summary>
    public readonly struct PathSegment
    {
        public PathSegment(
            PathSegmentKind kind,
            int startPointIndex,
            int pointCount,
            int linkId)
        {
            Kind = kind;
            StartPointIndex = startPointIndex;
            PointCount = pointCount;
            LinkId = linkId;
        }

        public PathSegmentKind Kind { get; }

        /// <summary>该段在路径点数组中的起始下标。</summary>
        public int StartPointIndex { get; }

        /// <summary>该段占用的路径点数量。</summary>
        public int PointCount { get; }

        /// <summary>Link 段的 link id；Surface 段为 -1。</summary>
        public int LinkId { get; }

        public bool IsLink => Kind == PathSegmentKind.Link;

        public static PathSegment Surface(int startPointIndex, int pointCount)
            => new PathSegment(PathSegmentKind.Surface, startPointIndex, pointCount, linkId: -1);

        public static PathSegment Link(int startPointIndex, int pointCount, int linkId)
            => new PathSegment(PathSegmentKind.Link, startPointIndex, pointCount, linkId);
    }
}

using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation.NavMesh
{
    /// <summary>
    /// 运行时 Link：一条可行走的跨表面连接。
    ///
    /// 与 <see cref="NavBorderPortal"/> 的区别：portal 拼接相邻瓦片的同一表面，
    /// Link 连接两块本来互不可达的可行走面（码头、滩头、桥、山口、跳台）。
    /// </summary>
    public readonly struct NavLink
    {
        public NavLink(
            int id,
            string linkId,
            int fromLayer,
            int fromXCm,
            int fromYCm,
            int toLayer,
            int toXCm,
            int toYCm,
            bool bidirectional,
            float cost,
            string? action,
            NavLinkUsageMask usage)
        {
            Id = id;
            LinkId = linkId;
            FromLayer = fromLayer;
            FromXCm = fromXCm;
            FromYCm = fromYCm;
            ToLayer = toLayer;
            ToXCm = toXCm;
            ToYCm = toYCm;
            Bidirectional = bidirectional;
            Cost = cost;
            Action = action;
            Usage = usage;
        }

        public int Id { get; }

        public string LinkId { get; }

        public int FromLayer { get; }

        public int FromXCm { get; }

        public int FromYCm { get; }

        public int ToLayer { get; }

        public int ToXCm { get; }

        public int ToYCm { get; }

        /// <summary>false 时只允许 From → To 方向通行。</summary>
        public bool Bidirectional { get; }

        public float Cost { get; }

        /// <summary>执行该 Link 所需的动作标识；null 表示无需动作（连续桥面）。</summary>
        public string? Action { get; }

        /// <summary>允许使用该 Link 的 agent profile 掩码。</summary>
        public NavLinkUsageMask Usage { get; }

        /// <summary>跨越方向。Link 是定向边，反向通行由 <see cref="Bidirectional"/> 决定。</summary>
        public bool AllowsProfile(int profileIndex)
        {
            return profileIndex >= 0 && Usage.Allows(profileIndex);
        }

        /// <summary>该 Link 是否为跨层连接（层不同）。同层 Link 表达同层内的离散连接。</summary>
        public bool IsCrossLayer => FromLayer != ToLayer;
    }
}

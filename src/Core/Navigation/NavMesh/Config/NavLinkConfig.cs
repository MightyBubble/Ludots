using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    /// <summary>
    /// 单个 NavMesh Link 的端点。坐标是世界系厘米，必须投影到该 layer 上的有效 NavTile。
    /// </summary>
    public sealed class NavLinkEndpointConfig
    {
        public string Layer { get; set; } = string.Empty;
        public int XCm { get; set; }
        public int YCm { get; set; }
    }

    /// <summary>
    /// 非连续可行走面之间的合法连接（跳台、桥、梯子、码头、滩头换乘）。
    ///
    /// 这不是"查询失败后的直线补救"：Link 是路径图上的正式边，只有作者声明且
    /// profile/layer/方向都匹配时查询才会经过它。
    /// </summary>
    public sealed class NavLinkConfig
    {
        public string Id { get; set; } = string.Empty;
        public NavLinkEndpointConfig From { get; set; } = new NavLinkEndpointConfig();
        public NavLinkEndpointConfig To { get; set; } = new NavLinkEndpointConfig();
        public bool Bidirectional { get; set; }

        /// <summary>允许使用该 Link 的 agent profile id。空数组表示不允许任何 profile。</summary>
        public List<string> AllowedProfiles { get; set; } = new List<string>();

        /// <summary>路径代价权重。0 表示不额外惩罚，但仍会受 layer/profile 过滤。</summary>
        public float Cost { get; set; }

        /// <summary>执行该 Link 所需的动作标识；null 表示无需动作（如桥面）。</summary>
        public string Action { get; set; }
    }
}

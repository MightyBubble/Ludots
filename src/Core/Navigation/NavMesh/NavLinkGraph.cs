using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.NavMesh
{
    /// <summary>
    /// 运行时 Link 图：把 authored links 编译成按 (layer, profile) 可查的定向边集合。
    ///
    /// 可用性（<see cref="SetEnabled"/>）与几何（端点 / 层 / 方向）分离：
    /// 开关只改变查询能否经过该 Link，不改几何，也不触发重烤。
    /// 任何变化都推进 <see cref="Revision"/>，使引用它的路径得以失效。
    /// </summary>
    public sealed class NavLinkGraph
    {
        private readonly NavLink[] _links;
        private readonly Dictionary<int, int[]> _linksByLayer;
        private readonly Dictionary<int, int[]> _linksByFromLayer;
        private readonly byte[] _enabled;
        private readonly int _profileCount;
        private uint _revision;

        public NavLinkGraph(
            IReadOnlyList<NavLinkConfig> links,
            NavMeshProfileRegistry profiles,
            AgentProfileRegistry agentProfiles,
            Func<string, int> resolveLayer)
        {
            if (links == null) throw new ArgumentNullException(nameof(links));
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));
            if (agentProfiles == null) throw new ArgumentNullException(nameof(agentProfiles));
            if (resolveLayer == null) throw new ArgumentNullException(nameof(resolveLayer));

            _profileCount = profiles.Count;
            _links = new NavLink[links.Count];
            _enabled = new byte[links.Count];
            var byLayer = new Dictionary<int, List<int>>();
            var byFromLayer = new Dictionary<int, List<int>>();

            for (int i = 0; i < links.Count; i++)
            {
                NavLinkConfig link = links[i];
                if (link == null)
                {
                    throw new InvalidOperationException($"NavLinkConfig.links[{i}] is null.");
                }

                int fromLayer = resolveLayer(link.From.Layer);
                int toLayer = resolveLayer(link.To.Layer);
                if (fromLayer < 0 || toLayer < 0)
                {
                    throw new InvalidOperationException(
                        $"NavMesh link '{link.Id}' references a layer that is not declared by the compiled config.");
                }

                NavLinkUsageMask usage = BuildUsageMask(link, profiles, agentProfiles, i);
                _links[i] = new NavLink(
                    id: i,
                    linkId: link.Id,
                    fromLayer: fromLayer,
                    fromXCm: link.From.XCm,
                    fromYCm: link.From.YCm,
                    toLayer: toLayer,
                    toXCm: link.To.XCm,
                    toYCm: link.To.YCm,
                    bidirectional: link.Bidirectional,
                    cost: link.Cost,
                    action: link.Action,
                    usage: usage);
                _enabled[i] = 1;

                AddTo(byLayer, fromLayer, i);
                if (toLayer != fromLayer)
                {
                    AddTo(byLayer, toLayer, i);
                }

                AddTo(byFromLayer, fromLayer, i);
            }

            _linksByLayer = Freeze(byLayer);
            _linksByFromLayer = Freeze(byFromLayer);
        }

        public int Count => _links.Length;

        public int ProfileCount => _profileCount;

        /// <summary>几何或可用性变化时推进；引用 Link 的路径据此失效。</summary>
        public uint Revision => _revision;

        public NavLink Get(int linkId)
        {
            if ((uint)linkId >= (uint)_links.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(linkId));
            }

            return _links[linkId];
        }

        public bool IsEnabled(int linkId)
        {
            return (uint)linkId < (uint)_enabled.Length && _enabled[linkId] != 0;
        }

        /// <summary>开关一条 Link。返回是否真的发生了状态变化。</summary>
        public bool SetEnabled(int linkId, bool enabled)
        {
            if ((uint)linkId >= (uint)_links.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(linkId));
            }

            byte next = enabled ? (byte)1 : (byte)0;
            if (_enabled[linkId] == next)
            {
                return false;
            }

            _enabled[linkId] = next;
            AdvanceRevision();
            return true;
        }

        /// <summary>某 layer 上所有与它相连（出任一端）的 Link 下标。</summary>
        public ReadOnlySpan<int> LinksTouchingLayer(int layer)
        {
            return _linksByLayer.TryGetValue(layer, out int[]? indices) ? indices : ReadOnlySpan<int>.Empty;
        }

        /// <summary>某 layer 上可作为跨越起点的 Link 下标。</summary>
        public ReadOnlySpan<int> LinksLeavingLayer(int layer)
        {
            return _linksByFromLayer.TryGetValue(layer, out int[]? indices) ? indices : ReadOnlySpan<int>.Empty;
        }

        /// <inheritdoc cref="LinksLeavingLayer(int)"/>
        public bool TrySelectUsableLink(int layer, int profileIndex, out NavLink selected)
        {
            ReadOnlySpan<int> candidates = _linksByFromLayer.TryGetValue(layer, out int[]? indices)
                ? indices
                : ReadOnlySpan<int>.Empty;

            for (int i = 0; i < candidates.Length; i++)
            {
                int linkId = candidates[i];
                if (_enabled[linkId] == 0)
                {
                    continue;
                }

                NavLink link = _links[linkId];
                if (link.AllowsProfile(profileIndex))
                {
                    selected = link;
                    return true;
                }
            }

            selected = default;
            return false;
        }

        private static NavLinkUsageMask BuildUsageMask(
            NavLinkConfig link,
            NavMeshProfileRegistry profiles,
            AgentProfileRegistry agentProfiles,
            int index)
        {
            if (link.AllowedProfiles == null || link.AllowedProfiles.Count == 0)
            {
                return NavLinkUsageMask.None;
            }

            NavLinkUsageMask mask = NavLinkUsageMask.None;
            for (int i = 0; i < link.AllowedProfiles.Count; i++)
            {
                string profileId = link.AllowedProfiles[i];
                agentProfiles.Require(profileId, $"NavMesh link '{link.Id}'");
                if (!profiles.TryGetIndex(profileId, out int profileIndex))
                {
                    throw new InvalidOperationException(
                        $"NavMesh link '{link.Id}' allows agent profile '{profileId}', but navmesh.json declares no bake profile for it.");
                }

                mask = mask.With(profileIndex);
            }

            return mask;
        }

        private static void AddTo(Dictionary<int, List<int>> map, int key, int value)
        {
            if (!map.TryGetValue(key, out List<int>? list))
            {
                list = new List<int>();
                map[key] = list;
            }

            list.Add(value);
        }

        private static Dictionary<int, int[]> Freeze(Dictionary<int, List<int>> map)
        {
            var frozen = new Dictionary<int, int[]>(map.Count);
            foreach (KeyValuePair<int, List<int>> pair in map)
            {
                frozen[pair.Key] = pair.Value.ToArray();
            }

            return frozen;
        }

        private void AdvanceRevision()
        {
            _revision = _revision == uint.MaxValue ? 1u : _revision + 1u;
        }
    }
}

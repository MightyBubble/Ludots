using System;
using System.Collections.Generic;

namespace Ludots.Core.Map
{
    /// <summary>
    /// 地图跨 mod 片段合并的可观测报告（纯记录型，不阻断装载）：与资产层
    /// ConfigConflictReport 同职责——供审计查询，防线本身仍在装载期 fail-fast。
    /// </summary>
    public sealed class MapMergeReport
    {
        private readonly List<(string MapId, string InstanceId, string Source)> _deletions = new();
        private readonly List<(string MapId, string InstanceId, string Source)> _deletionsNotFound = new();
        private readonly List<(string MapId, int EntityIndex, string Source)> _anonymousNonBaseFragments = new();

        public IReadOnlyList<(string MapId, string InstanceId, string Source)> Deletions => _deletions;
        public IReadOnlyList<(string MapId, string InstanceId, string Source)> DeletionsNotFound => _deletionsNotFound;
        public IReadOnlyList<(string MapId, int EntityIndex, string Source)> AnonymousNonBaseFragments => _anonymousNonBaseFragments;

        public void RecordDeletion(string mapId, string instanceId, string source)
        {
            _deletions.Add((mapId, instanceId, source));
        }

        public void RecordDeletionNotFound(string mapId, string instanceId, string source)
        {
            _deletionsNotFound.Add((mapId, instanceId, source));
        }

        public void RecordAnonymousNonBaseFragment(string mapId, int entityIndex, string source)
        {
            _anonymousNonBaseFragments.Add((mapId, entityIndex, source));
        }

        public void Clear()
        {
            _deletions.Clear();
            _deletionsNotFound.Clear();
            _anonymousNonBaseFragments.Clear();
        }

        public static string DescribeSource(string? sourceUri, string fallback)
        {
            return string.IsNullOrWhiteSpace(sourceUri) ? fallback : sourceUri!;
        }
    }
}

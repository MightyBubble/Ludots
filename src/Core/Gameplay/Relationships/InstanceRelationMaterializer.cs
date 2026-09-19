using System;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Systems;

namespace Ludots.Core.Gameplay.Relationships
{
    /// <summary>
    /// 实例 relations 物化（MapLoaded 前的装载站，与参与者绑定同批）：把合并后的
    /// entity.relations 解析成实体对并写入 RelationshipRuntime。to 支持绝对 instanceId
    /// 与组内可寻址路径（两层命名空间共用 MapLoadEntityIndex）；type/metric 必须已在
    /// 对称（isSymmetric）类型只写 from→to 单向，需要双向边请显式写两条；
    /// Relationships catalog 注册，未知即装载期 fail-fast。变更走 EnsureLink/SetMetric，
    /// 天然进关系变更缓冲，下一拍以 Relation* 事件暴露给 trigger 图——初始边与运行时
    /// 变更同一条事件路径。
    /// </summary>
    public static class InstanceRelationMaterializer
    {
        public static void Materialize(
            MapSession session,
            MapConfig mapConfig,
            MapLoadEntityIndex entityIndex,
            RelationshipRuntime runtime,
            RelationshipTypeRegistry types,
            RelationshipMetricRegistry metricRegistry)
        {
            if (!AnyAuthoredRelation(mapConfig))
            {
                return;
            }

            if (runtime == null)
            {
                throw new InvalidOperationException($"Map '{session.MapId.Value}' authors instance relations but RelationshipRuntime is not installed.");
            }

            if (types == null)
            {
                throw new InvalidOperationException($"Map '{session.MapId.Value}' authors instance relations but RelationshipTypeRegistry is not installed.");
            }

            if (metricRegistry == null)
            {
                throw new InvalidOperationException($"Map '{session.MapId.Value}' authors instance relations but RelationshipMetricRegistry is not installed.");
            }

            string mapId = session.MapId.Value;
            for (int i = 0; i < mapConfig.Entities.Count; i++)
            {
                EntitySpawnData entry = mapConfig.Entities[i];
                if (entry?.Relations is not { Count: > 0 })
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.InstanceId))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' entities[{i}] declares relations but has no InstanceId; relation owners must be addressable.");
                }

                Entity source = entityIndex.GetRequired(mapId, entry.InstanceId, $"entities[{i}].relations");
                for (int r = 0; r < entry.Relations.Count; r++)
                {
                    EntityRelationAuthoring relation = entry.Relations[r];
                    string context = $"Map '{mapId}' entity '{entry.InstanceId}' relations[{r}]";
                    if (relation == null || string.IsNullOrWhiteSpace(relation.To) || string.IsNullOrWhiteSpace(relation.Type))
                    {
                        throw new InvalidOperationException($"{context} requires non-empty 'to' and 'type'.");
                    }

                    bool byInstanceId = entityIndex.TryGet(relation.To, out Entity target);
                    bool byLocalPath = entityIndex.TryGetByLocalPath(relation.To, out Entity pathTarget);
                    if (byInstanceId && byLocalPath && target != pathTarget)
                    {
                        throw new InvalidOperationException(
                            $"{context} target '{relation.To}' is ambiguous: it names both a placed InstanceId and an addressable local path; rename one side.");
                    }

                    if (!byInstanceId && !byLocalPath)
                    {
                        throw new InvalidOperationException(
                            $"{context} references unknown relation target '{relation.To}'; to must be a placed InstanceId or an addressable local path.");
                    }

                    if (!byInstanceId)
                    {
                        target = pathTarget;
                    }

                    if (target == source)
                    {
                        throw new InvalidOperationException(
                            $"{context} targets the relation owner itself; self-edges are not authored through instance relations.");
                    }

                    if (!types.TryGetId(relation.Type, out int typeId))
                    {
                        throw new InvalidOperationException(
                            $"{context} references relationship type '{relation.Type}' not registered in the Relationships catalog.");
                    }

                    if (relation.Delete == true)
                    {
                        // 边墓碑在合并层（MapManager.MergeEntityFragments）已消化——物化只跑在
                        // 合并结果之上，能到这里说明绕过了合并，fail-fast 而非静默建边。
                        throw new InvalidOperationException(
                            $"{context} carries __delete; edge tombstones must be resolved by map fragment merge before materialization.");
                    }

                    runtime.EnsureLink(source, target, typeId);
                    if (relation.Metric != null)
                    {
                        foreach (var kvp in relation.Metric)
                        {
                            if (!metricRegistry.TryGetId(kvp.Key, out int metricId))
                            {
                                throw new InvalidOperationException(
                                    $"{context} references relationship metric '{kvp.Key}' not registered in the Relationships catalog.");
                            }

                            runtime.SetMetric(source, target, typeId, metricId, kvp.Value);
                        }
                    }
                }
            }
        }

        private static bool AnyAuthoredRelation(MapConfig mapConfig)
        {
            for (int i = 0; i < mapConfig.Entities.Count; i++)
            {
                if (mapConfig.Entities[i]?.Relations is { Count: > 0 })
                {
                    return true;
                }
            }

            return false;
        }
    }
}

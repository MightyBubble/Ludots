using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ludots.Core.Config
{
    /// <summary>
    /// 实体组模板的两段式装载合同：<see cref="Validate"/> 在组注册表装载后一次性校验
    /// 组声明（槽位引用、localId 唯一性、组图无环、槽位 pose 限制），<see cref="Expand"/>
    /// 在其后把地图里带 group 的摆放替换为前缀化的普通实体条目。
    /// Expand 假定 Validate 已在同一对注册表上通过。
    /// </summary>
    public static class EntityGroupPlacement
    {
        public static void Validate(DataRegistry<EntityGroupTemplate> groups, DataRegistry<EntityTemplate> templates)
        {
            if (groups == null)
            {
                throw new ArgumentNullException(nameof(groups));
            }

            if (templates == null)
            {
                throw new ArgumentNullException(nameof(templates));
            }

            foreach (EntityGroupTemplate group in groups.GetAll())
            {
                ValidateGroupSlots(group, groups, templates);
            }

            foreach (EntityGroupTemplate group in groups.GetAll())
            {
                DetectGroupCycle(group.Id, groups, new HashSet<string>(StringComparer.Ordinal), "root");
            }
        }

        public static List<EntitySpawnData> Expand(
            MapConfig mapConfig,
            DataRegistry<EntityGroupTemplate> groups,
            DataRegistry<EntityTemplate> templates)
        {
            if (mapConfig == null)
            {
                throw new ArgumentNullException(nameof(mapConfig));
            }

            if (groups == null)
            {
                throw new ArgumentNullException(nameof(groups));
            }

            if (templates == null)
            {
                throw new ArgumentNullException(nameof(templates));
            }

            var expanded = new List<EntitySpawnData>(mapConfig.Entities.Count);
            for (int i = 0; i < mapConfig.Entities.Count; i++)
            {
                EntitySpawnData entry = mapConfig.Entities[i];
                if (entry == null)
                {
                    throw new InvalidOperationException($"Map '{mapConfig.Id}' contains a null entity entry.");
                }

                if (entry.Group == null)
                {
                    expanded.Add(entry);
                    continue;
                }

                string context = $"Map '{mapConfig.Id}' entity '{ResolveEntryContextId(entry, i)}'";
                if (string.IsNullOrWhiteSpace(entry.Group) ||
                    !string.Equals(entry.Group, entry.Group.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{context}: 组摆放的 group id 必须是非空且首尾无空白的字符串。");
                }

                if (!string.IsNullOrWhiteSpace(entry.Template))
                {
                    throw new InvalidOperationException(
                        $"{context}: 组摆放不能同时声明 template '{entry.Template}'。");
                }

                if (string.IsNullOrWhiteSpace(entry.InstanceId) ||
                    !string.Equals(entry.InstanceId, entry.InstanceId.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{context}: 组摆放必须显式声明首尾无空白的 instanceId 作为组命名空间根。");
                }

                if (entry.Overrides != null)
                {
                    throw new InvalidOperationException(
                        $"{context}: 组摆放不能声明 overrides——组实例没有根实体可应用组件覆盖。");
                }

                if (entry.PresenterParamOverrides != null && entry.PresenterParamOverrides.Count != 0)
                {
                    throw new InvalidOperationException(
                        $"{context}: 组摆放不能声明 presenterParamOverrides——组实例没有根实体可应用表现层参数覆盖。");
                }

                if (entry.PositionXCm.HasValue != entry.PositionYCm.HasValue)
                {
                    throw new InvalidOperationException(
                        $"{context}: 组摆放锚点 positionXCm/positionYCm 必须同时声明或同时省略。");
                }

                EntityGroupTemplate group = groups.Get(entry.Group);
                if (group == null)
                {
                    throw new InvalidOperationException($"{context}: 引用未知实体组模板 '{entry.Group}'。");
                }

                ExpandSlots(
                    expanded,
                    group,
                    entry.InstanceId,
                    entry.PositionXCm ?? 0,
                    entry.PositionYCm ?? 0,
                    groups,
                    templates);
            }

            return expanded;
        }

        private static void ValidateGroupSlots(
            EntityGroupTemplate group,
            DataRegistry<EntityGroupTemplate> groups,
            DataRegistry<EntityTemplate> templates)
        {
            if (group.Slots == null || group.Slots.Count == 0)
            {
                return;
            }

            var seenLocalIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < group.Slots.Count; i++)
            {
                EntityGroupSlot slot = group.Slots[i];
                string context = $"Entity group '{group.Id}' slots[{i}]";
                if (slot == null)
                {
                    throw new InvalidOperationException($"{context}: 槽位条目缺失。");
                }

                if (string.IsNullOrWhiteSpace(slot.LocalId) ||
                    !string.Equals(slot.LocalId, slot.LocalId.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{context}: localId 必须是非空且首尾无空白的字符串。");
                }

                if (!seenLocalIds.Add(slot.LocalId))
                {
                    throw new InvalidOperationException($"{context}: localId '{slot.LocalId}' 在同一组内重复。");
                }

                context = $"{context} (localId '{slot.LocalId}')";
                bool hasTemplate = !string.IsNullOrWhiteSpace(slot.Template);
                bool hasGroup = !string.IsNullOrWhiteSpace(slot.Group);
                if (hasTemplate == hasGroup)
                {
                    throw new InvalidOperationException(
                        $"{context}: 必须恰好声明 template 或 group 之一（template='{FormatId(slot.Template)}', group='{FormatId(slot.Group)}'）。");
                }

                if (hasTemplate)
                {
                    if (!string.Equals(slot.Template, slot.Template!.Trim(), StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"{context}: template id '{slot.Template}' 首尾不能有空白。");
                    }

                    if (!templates.Contains(slot.Template!))
                    {
                        throw new InvalidOperationException($"{context}: 引用未知实体模板 '{slot.Template}'。");
                    }
                }
                else
                {
                    if (!string.Equals(slot.Group, slot.Group!.Trim(), StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"{context}: group id '{slot.Group}' 首尾不能有空白。");
                    }

                    if (!groups.Contains(slot.Group))
                    {
                        throw new InvalidOperationException($"{context}: 引用未知实体组 '{slot.Group}'。");
                    }
                }

                EntityTemplateLocalPose? pose = slot.LocalPose;
                if (pose != null)
                {
                    if (pose.InheritParentFacing == true)
                    {
                        throw new InvalidOperationException(
                            $"{context}: localPose.inheritParentFacing 在实体组槽位无效——槽位没有父实体可继承朝向。");
                    }

                    if (pose.OffsetRotation != null)
                    {
                        throw new InvalidOperationException(
                            $"{context}: localPose.offsetRotation 在实体组槽位无效——槽位只支持平面 offsetXCm/offsetYCm/facingDeg。");
                    }
                }

                if (hasGroup && slot.ComponentOverrides != null)
                {
                    throw new InvalidOperationException(
                        $"{context}: 嵌套组槽位不能声明 componentOverrides——跨层覆盖没有承接实体。");
                }
            }
        }

        private static void DetectGroupCycle(
            string groupId,
            DataRegistry<EntityGroupTemplate> groups,
            HashSet<string> visiting,
            string chain)
        {
            if (!visiting.Add(groupId))
            {
                throw new InvalidOperationException($"Entity group 引用图存在环: {chain} -> {groupId}。");
            }

            EntityGroupTemplate group = groups.Get(groupId);
            if (group?.Slots != null)
            {
                for (int i = 0; i < group.Slots.Count; i++)
                {
                    EntityGroupSlot slot = group.Slots[i];
                    if (slot?.Group != null)
                    {
                        DetectGroupCycle(slot.Group, groups, visiting, $"{chain} -> {groupId}[{i}]");
                    }
                }
            }

            visiting.Remove(groupId);
        }

        private static void ExpandSlots(
            List<EntitySpawnData> expanded,
            EntityGroupTemplate group,
            string prefix,
            int anchorXCm,
            int anchorYCm,
            DataRegistry<EntityGroupTemplate> groups,
            DataRegistry<EntityTemplate> templates)
        {
            if (group.Slots == null || group.Slots.Count == 0)
            {
                return;
            }

            for (int i = 0; i < group.Slots.Count; i++)
            {
                EntityGroupSlot slot = group.Slots[i];
                string slotInstanceId = prefix + "." + slot.LocalId;
                int slotXCm = anchorXCm + (slot.LocalPose?.OffsetXCm ?? 0);
                int slotYCm = anchorYCm + (slot.LocalPose?.OffsetYCm ?? 0);

                if (slot.Group != null)
                {
                    ExpandSlots(expanded, groups.Get(slot.Group), slotInstanceId, slotXCm, slotYCm, groups, templates);
                    continue;
                }

                expanded.Add(BuildSlotEntry(slot, slotInstanceId, slotXCm, slotYCm, templates));
            }
        }

        private static EntitySpawnData BuildSlotEntry(
            EntityGroupSlot slot,
            string instanceId,
            int worldXCm,
            int worldYCm,
            DataRegistry<EntityTemplate> templates)
        {
            var overrides = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            if (slot.ComponentOverrides != null)
            {
                EntityTemplate template = templates.Get(slot.Template!);
                foreach (KeyValuePair<string, JsonNode> authored in slot.ComponentOverrides)
                {
                    if (authored.Value == null)
                    {
                        throw new InvalidOperationException(
                            $"Entity group slot '{instanceId}' componentOverrides['{authored.Key}'] 不能为 null。");
                    }

                    overrides[authored.Key] = ComposeComponentOverride(template, authored.Key, authored.Value);
                }
            }

            overrides["WorldPositionCm"] = new JsonObject
            {
                ["Value"] = new JsonObject
                {
                    ["X"] = worldXCm,
                    ["Y"] = worldYCm,
                },
            };

            if (slot.LocalPose?.FacingDeg is int facingDeg)
            {
                overrides["FacingDirection"] = new JsonObject
                {
                    ["AngleRad"] = facingDeg * MathF.PI / 180f,
                };
            }

            return new EntitySpawnData
            {
                InstanceId = instanceId,
                Template = slot.Template!,
                Overrides = overrides,
            };
        }

        private static JsonNode ComposeComponentOverride(
            EntityTemplate template,
            string componentKey,
            JsonNode slotOverride)
        {
            if (slotOverride is JsonObject overrideObject &&
                template.Components != null &&
                template.Components.TryGetValue(componentKey, out JsonNode? baseNode) &&
                baseNode is JsonObject baseObject)
            {
                var merged = (JsonObject)baseObject.DeepClone();
                ConfigPipeline.DeepMerge(merged, (JsonObject)overrideObject.DeepClone());
                return merged;
            }

            return slotOverride.DeepClone();
        }

        private static string ResolveEntryContextId(EntitySpawnData entry, int index)
        {
            return string.IsNullOrWhiteSpace(entry.InstanceId) ? $"#{index}" : entry.InstanceId;
        }

        private static string FormatId(string? id)
        {
            return id ?? "<null>";
        }
    }
}

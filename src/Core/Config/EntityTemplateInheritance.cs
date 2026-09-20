using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ludots.Core.Config
{
    /// <summary>
    /// 模板 extends / uses 的装载期展开器。语义对齐 presenter 层 extends 先例
    /// （<c>PresenterDefinitionConfigLoader.ExpandDefinition</c>）：递归展开、环
    /// fail fast、数组字段追加、标量字段子代非空才覆盖。折叠优先级一条规则：
    /// 声明越靠后优先级越高，模板自身 components 永远最高——extends 父模板打底、
    /// uses 块按列表顺序逐个覆盖、自身最后。运行时（<c>MapLoader.LoadTemplates</c>）
    /// 与离线烘焙（<c>NavObstacleAuthoringCatalog</c>）两个加载终点共用，保证两侧
    /// 看到同一份展开结果。
    /// </summary>
    public static class EntityTemplateInheritance
    {
        public static void ExpandAll(
            IReadOnlyDictionary<string, EntityTemplate> templates,
            ConfigConflictReport report = null)
        {
            var expanding = new HashSet<string>(StringComparer.Ordinal);
            foreach (var template in templates.Values)
            {
                Expand(template, templates, expanding, report);
            }
        }

        private static void Expand(
            EntityTemplate template,
            IReadOnlyDictionary<string, EntityTemplate> templates,
            HashSet<string> expanding,
            ConfigConflictReport report)
        {
            if (template == null)
            {
                return;
            }

            bool hasExtends = !string.IsNullOrWhiteSpace(template.Extends);
            bool hasUses = template.Uses is { Count: > 0 };
            if (!hasExtends && !hasUses)
            {
                return;
            }

            if (!expanding.Add(template.Id))
            {
                throw new InvalidOperationException(
                    $"Entity template inheritance cycle detected at '{template.Id}'.");
            }

            try
            {
                var sources = new List<EntityTemplate>();
                if (hasExtends)
                {
                    string parentKey = template.Extends!.Trim();
                    if (!templates.TryGetValue(parentKey, out EntityTemplate parent))
                    {
                        throw new InvalidOperationException(
                            $"Entity template '{template.Id}' extends unknown template '{parentKey}'.");
                    }

                    Expand(parent, templates, expanding, report);
                    sources.Add(parent);
                }

                if (hasUses)
                {
                    for (int i = 0; i < template.Uses!.Count; i++)
                    {
                        string rawUse = template.Uses[i] ?? string.Empty;
                        string useKey = rawUse.Trim();
                        if (!templates.TryGetValue(useKey, out EntityTemplate block))
                        {
                            throw new InvalidOperationException(
                                $"Entity template '{template.Id}' uses unknown template '{rawUse}'.");
                        }

                        Expand(block, templates, expanding, report);
                        sources.Add(block);
                    }
                }

                if (hasUses)
                {
                    FoldSources(sources, template, report);
                }
                else
                {
                    MergeIntoChild(sources[0], template);
                }

                template.Extends = null;
                template.Uses = null;
            }
            finally
            {
                expanding.Remove(template.Id);
            }
        }

        /// <summary>
        /// uses 折叠。不能把每个块直接 merge 进 template（那会让自身恒胜，块间冲突
        /// 变成先声明者胜）：折叠发生在一串一次性私有克隆上，后一个克隆作为
        /// MergeIntoChild 的子代吸收前一个的结果（后声明者胜），自身最后合并。
        /// 父模板/块是注册表共享对象，全程只读。
        /// </summary>
        private static void FoldSources(
            List<EntityTemplate> sources,
            EntityTemplate template,
            ConfigConflictReport report)
        {
            RecordComponentWriterChains(sources, template, report);

            var accumulator = CloneTemplate(sources[0]);
            for (int i = 1; i < sources.Count; i++)
            {
                var next = CloneTemplate(sources[i]);
                MergeIntoChild(accumulator, next);
                accumulator = next;
            }

            MergeIntoChild(accumulator, template);
        }

        private static void RecordComponentWriterChains(
            List<EntityTemplate> sources,
            EntityTemplate template,
            ConfigConflictReport report)
        {
            if (report == null)
            {
                return;
            }

            var writersByKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                foreach (string component in source.Components.Keys)
                {
                    if (!writersByKey.TryGetValue(component, out var writers))
                    {
                        writers = new List<string>();
                        writersByKey[component] = writers;
                    }

                    writers.Add(source.Id);
                }
            }

            foreach (string component in template.Components.Keys)
            {
                if (!writersByKey.TryGetValue(component, out var writers))
                {
                    writers = new List<string>();
                    writersByKey[component] = writers;
                }

                writers.Add("self");
            }

            foreach (var kvp in writersByKey)
            {
                if (kvp.Value.Count >= 2)
                {
                    report.RecordComponentOverrideChain(
                        template.Id, kvp.Key, string.Join(" -> ", kvp.Value));
                }
            }
        }

        private static EntityTemplate CloneTemplate(EntityTemplate source)
        {
            var clone = new EntityTemplate
            {
                Id = source.Id,
                OnSpawnEffect = source.OnSpawnEffect,
                InitialInteractionContext = source.InitialInteractionContext,
                Components = new Dictionary<string, JsonNode>(source.Components.Count, StringComparer.Ordinal),
            };

            foreach (var kvp in source.Components)
            {
                clone.Components[kvp.Key] = kvp.Value?.DeepClone();
            }

            if (source.TriggerGraphs is { Count: > 0 })
            {
                clone.TriggerGraphs = new List<string>(source.TriggerGraphs);
            }

            if (source.Children is { Count: > 0 })
            {
                clone.Children = new List<EntityTemplateChild>(source.Children.Count);
                for (int i = 0; i < source.Children.Count; i++)
                {
                    clone.Children.Add(CloneChild(source.Children[i]));
                }
            }

            return clone;
        }

        private static void MergeIntoChild(EntityTemplate parent, EntityTemplate child)
        {
            foreach (var kvp in parent.Components)
            {
                if (child.Components.TryGetValue(kvp.Key, out JsonNode childNode))
                {
                    // 子代带 "__replace": true 时整组件替换父代值（变体形状组件不能字段合并），
                    // 标记在装载期消费剥离，物化只见最终值。
                    if (TryConsumeReplaceMarker(childNode, out JsonNode replaced))
                    {
                        child.Components[kvp.Key] = replaced;
                        continue;
                    }

                    var merged = kvp.Value.DeepClone();
                    JsonMerger.Merge(merged, childNode);
                    child.Components[kvp.Key] = merged;
                }
                else
                {
                    child.Components[kvp.Key] = kvp.Value.DeepClone();
                }
            }

            child.OnSpawnEffect ??= parent.OnSpawnEffect;
            child.InitialInteractionContext ??= parent.InitialInteractionContext;
            child.TriggerGraphs = AppendTriggerGraphs(parent.TriggerGraphs, child.TriggerGraphs);
            child.Children = AppendChildren(parent.Children, child.Children);
        }

        /// <summary>
        /// "__replace": true（对象顶层布尔标记，沿用 ConfigMerger __delete 的保留字惯例）
        /// 表示该子组件整块替换父代值；标记在展开时剥离。
        /// </summary>
        private static bool TryConsumeReplaceMarker(JsonNode node, out JsonNode replaced)
        {
            replaced = null;
            if (node is not JsonObject obj ||
                !obj.TryGetPropertyValue("__replace", out JsonNode? marker) ||
                marker is not JsonValue value ||
                !value.TryGetValue<bool>(out bool replace) || !replace)
            {
                return false;
            }

            var clone = (JsonObject)obj.DeepClone();
            clone.Remove("__replace");
            replaced = clone;
            return true;
        }

        /// <summary>
        /// 同一 TriggerGraph 挂两次会让实体域触发反应翻倍，不是合法组合——
        /// 追加时按精确 id 去重（保留首次出现，父代在前）。
        /// </summary>
        private static List<string>? AppendTriggerGraphs(List<string>? parent, List<string>? child)
        {
            if (parent is not { Count: > 0 })
            {
                return child;
            }

            if (child is not { Count: > 0 })
            {
                return new List<string>(parent);
            }

            var merged = new List<string>(parent);
            var seen = new HashSet<string>(parent, StringComparer.Ordinal);
            for (int i = 0; i < child.Count; i++)
            {
                if (seen.Add(child[i]))
                {
                    merged.Add(child[i]);
                }
            }

            return merged;
        }

        private static List<EntityTemplateChild>? AppendChildren(
            List<EntityTemplateChild>? parent,
            List<EntityTemplateChild>? child)
        {
            if (parent is not { Count: > 0 })
            {
                return child;
            }

            if (child is not { Count: > 0 })
            {
                return new List<EntityTemplateChild>(parent);
            }

            var merged = new List<EntityTemplateChild>(parent.Count + child.Count);
            for (int i = 0; i < parent.Count; i++)
            {
                merged.Add(CloneChild(parent[i]));
            }

            merged.AddRange(child);
            return merged;
        }

        private static EntityTemplateChild CloneChild(EntityTemplateChild source)
        {
            var clone = new EntityTemplateChild
            {
                LocalId = source.LocalId,
                Template = source.Template,
                Attach = source.Attach,
            };
            if (source.LocalPose != null)
            {
                clone.LocalPose = new EntityTemplateLocalPose
                {
                    OffsetXCm = source.LocalPose.OffsetXCm,
                    OffsetYCm = source.LocalPose.OffsetYCm,
                    FacingDeg = source.LocalPose.FacingDeg,
                    InheritParentFacing = source.LocalPose.InheritParentFacing,
                    OffsetRotation = source.LocalPose.OffsetRotation,
                };
            }

            if (source.Children is { Count: > 0 })
            {
                clone.Children = new List<EntityTemplateChild>(source.Children.Count);
                for (int i = 0; i < source.Children.Count; i++)
                {
                    clone.Children.Add(CloneChild(source.Children[i]));
                }
            }

            if (source.Overrides != null)
            {
                clone.Overrides = new Dictionary<string, JsonNode>(source.Overrides.Count, StringComparer.Ordinal);
                foreach (var kvp in source.Overrides)
                {
                    clone.Overrides[kvp.Key] = kvp.Value?.DeepClone();
                }
            }

            return clone;
        }
    }
}

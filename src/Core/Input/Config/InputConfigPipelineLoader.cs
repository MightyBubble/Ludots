using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Input.Config
{
    public sealed class InputConfigPipelineLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public InputConfigPipelineLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public InputConfigRoot Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/default_input.json")
        {
            var fragments = _pipeline.CollectFragmentsWithSources(relativePath);

            if (report != null)
            {
                for (int i = 0; i < fragments.Count; i++)
                    report.RecordFragment(relativePath, fragments[i].SourceUri);
            }

            var actions = new Dictionary<string, InputActionDef>(StringComparer.OrdinalIgnoreCase);
            var actionOrder = new List<string>();

            var contexts = new Dictionary<string, InputContextDef>(StringComparer.OrdinalIgnoreCase);
            var contextOrder = new List<string>();

            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i].Node is not JsonObject obj) continue;

                var fragmentConfig = obj.Deserialize<InputConfigRoot>(_options);
                if (fragmentConfig == null) continue;

                MergeActions(fragmentConfig.Actions, actions, actionOrder);
                MergeContexts(fragmentConfig.Contexts, contexts, contextOrder);
            }

            var result = new InputConfigRoot();
            for (int i = 0; i < actionOrder.Count; i++)
            {
                var id = actionOrder[i];
                if (actions.TryGetValue(id, out var def)) result.Actions.Add(def);
            }

            for (int i = 0; i < contextOrder.Count; i++)
            {
                var id = contextOrder[i];
                if (contexts.TryGetValue(id, out var def)) result.Contexts.Add(def);
            }

            return result;
        }

        private static void MergeActions(List<InputActionDef> incoming, Dictionary<string, InputActionDef> byId, List<string> order)
        {
            if (incoming == null) return;
            for (int i = 0; i < incoming.Count; i++)
            {
                var a = incoming[i];
                if (a == null) continue;
                if (string.IsNullOrWhiteSpace(a.Id)) continue;

                if (!byId.ContainsKey(a.Id)) order.Add(a.Id);
                byId[a.Id] = a;
            }
        }

        private static void MergeContexts(List<InputContextDef> incoming, Dictionary<string, InputContextDef> byId, List<string> order)
        {
            if (incoming == null) return;
            for (int i = 0; i < incoming.Count; i++)
            {
                var c = incoming[i];
                if (c == null) continue;
                if (string.IsNullOrWhiteSpace(c.Id)) continue;

                if (!byId.TryGetValue(c.Id, out var existing))
                {
                    byId[c.Id] = c;
                    order.Add(c.Id);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(c.Name)) existing.Name = c.Name;
                existing.Priority = c.Priority;
                existing.Bindings = MergeBindings(existing.Bindings, c.Bindings);
            }
        }

        private static List<InputBindingDef> MergeBindings(List<InputBindingDef> existing, List<InputBindingDef> incoming)
        {
            existing ??= new List<InputBindingDef>();
            if (incoming == null || incoming.Count == 0) return existing;

            var map = new Dictionary<string, InputBindingDef>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            for (int i = 0; i < existing.Count; i++)
            {
                var b = existing[i];
                if (b == null) continue;
                var key = GetBindingKey(b);
                if (!map.ContainsKey(key)) order.Add(key);
                map[key] = b;
            }

            for (int i = 0; i < incoming.Count; i++)
            {
                var b = incoming[i];
                if (b == null) continue;
                var key = GetBindingKey(b);
                if (!map.ContainsKey(key)) order.Add(key);
                map[key] = b;
            }

            var merged = new List<InputBindingDef>(order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                var key = order[i];
                if (map.TryGetValue(key, out var b)) merged.Add(b);
            }
            return merged;
        }

        private static string GetBindingKey(InputBindingDef binding)
        {
            var action = binding.ActionId ?? "";
            var path = binding.Path ?? "";
            var composite = binding.CompositeType ?? "";

            if (string.IsNullOrWhiteSpace(composite))
            {
                return $"{action}|{path}|";
            }

            return $"{action}||{composite}|{GetCompositePartsKey(binding.CompositeParts)}";
        }

        private static string GetCompositePartsKey(List<InputBindingDef> parts)
        {
            if (parts == null || parts.Count == 0) return "";
            var segments = new string[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                segments[i] = p == null ? "" : GetBindingKey(p);
            }
            return string.Join(",", segments);
        }
    }
}


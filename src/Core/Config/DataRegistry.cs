using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Core.Config
{
    public class DataRegistry<T> where T : class, IIdentifiable
    {
        private readonly Dictionary<string, T> _data = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        private readonly ConfigPipeline _pipeline;

        public DataRegistry(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public void Load(string relativePath)
        {
            Console.WriteLine($"[DataRegistry<{typeof(T).Name}>] Loading from {relativePath}...");

            // 1. Collect all arrays
            var arrays = _pipeline.CollectJsonArrays(relativePath);

            // 2. Flatten and Group by ID
            // We use a temporary dictionary to hold the merging JsonObjects
            var mergedNodes = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var array in arrays)
            {
                foreach (var node in array)
                {
                    if (node is not JsonObject obj) continue;

                    // Extract ID (Case Insensitive check)
                    JsonNode idNode = null;
                    if (obj.TryGetPropertyValue("Id", out idNode) || obj.TryGetPropertyValue("id", out idNode))
                    {
                        // Found it
                    }
                    else
                    {
                        // Fallback: iterate properties to find case-insensitive match (slower but robust)
                        foreach (var kvp in obj)
                        {
                            if (string.Equals(kvp.Key, "Id", StringComparison.OrdinalIgnoreCase))
                            {
                                idNode = kvp.Value;
                                break;
                            }
                        }
                    }

                    if (idNode == null)
                    {
                        Console.WriteLine($"[DataRegistry] Warning: item missing Id in {relativePath}");
                        continue;
                    }

                    string id = idNode.ToString();

                    if (mergedNodes.TryGetValue(id, out var existingNode))
                    {
                        // Merge current node INTO existing node
                        // Note: CollectJsonArrays returns fragments in Core->Mod order.
                        // So 'node' is the OVERRIDE, 'existingNode' is the BASE.
                        // Wait, JsonMerger.Merge(target, source) modifies target.
                        // So we should have: target = BASE, source = OVERRIDE.
                        // So: JsonMerger.Merge(existingNode, node);
                        JsonMerger.Merge(existingNode, node);
                    }
                    else
                    {
                        // New entry. We must Clone it because it might be merged into later
                        // and we don't want to modify the original JsonArray if that matters (though we discarded it)
                        mergedNodes[id] = node.DeepClone();
                    }
                }
            }

            // 3. Deserialize final nodes
            int count = 0;
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
            
            foreach (var kvp in mergedNodes)
            {
                try
                {
                    var item = kvp.Value.Deserialize<T>(options);
                    if (item != null)
                    {
                        _data[item.Id] = item;
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DataRegistry] Error deserializing item {kvp.Key}: {ex.Message}");
                }
            }

            Console.WriteLine($"[DataRegistry<{typeof(T).Name}>] Loaded {count} items.");
        }

        public T Get(string id)
        {
            return _data.TryGetValue(id, out var item) ? item : null;
        }

        public IEnumerable<T> GetAll()
        {
            return _data.Values;
        }
        
        public bool Contains(string id)
        {
            return _data.ContainsKey(id);
        }
    }
}

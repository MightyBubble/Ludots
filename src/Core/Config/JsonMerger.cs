using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Ludots.Core.Config
{
    public static class JsonMerger
    {
        /// <summary>
        /// Modifies the target in place.
        /// </summary>
        public static void Merge(JsonNode target, JsonNode source)
        {
            if (target == null || source == null) return;

            if (target is JsonObject targetObj && source is JsonObject sourceObj)
            {
                foreach (var kvp in sourceObj)
                {
                    var propertyName = kvp.Key;
                    var sourceValue = kvp.Value;

                    if (targetObj.ContainsKey(propertyName))
                    {
                        var targetValue = targetObj[propertyName];

                        if (targetValue is JsonObject && sourceValue is JsonObject)
                        {
                            Merge(targetValue, sourceValue);
                        }
                        else
                        {
                            // We need to clone the node because a node can only have one parent
                            targetObj[propertyName] = sourceValue?.DeepClone();
                        }
                    }
                    else
                    {
                        targetObj[propertyName] = sourceValue?.DeepClone();
                    }
                }
            }
            else if (target is JsonArray targetArray && source is JsonArray sourceArray)
            {
                targetArray.Clear();
                foreach (var item in sourceArray) targetArray.Add(item?.DeepClone());
            }
            else
            {
                // Different node kinds (or primitives): a void method cannot replace the caller's root
                // reference, so this is a silent no-op. Merge expects Object/Object or Array/Array.
            }
        }
    }
}

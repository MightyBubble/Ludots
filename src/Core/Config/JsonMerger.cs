using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Ludots.Core.Config
{
    public static class JsonMerger
    {
        /// <summary>
        /// Merges the source JsonNode into the target JsonNode.
        /// Modifies the target in place.
        /// </summary>
        public static void Merge(JsonNode target, JsonNode source)
        {
            if (target == null || source == null) return;

            if (target is JsonObject targetObj && source is JsonObject sourceObj)
            {
                // Merge Objects
                foreach (var kvp in sourceObj)
                {
                    var propertyName = kvp.Key;
                    var sourceValue = kvp.Value;

                    if (targetObj.ContainsKey(propertyName))
                    {
                        var targetValue = targetObj[propertyName];

                        if (targetValue is JsonObject && sourceValue is JsonObject)
                        {
                            // Recursive merge for objects
                            Merge(targetValue, sourceValue);
                        }
                        else
                        {
                            // Overwrite primitive values or arrays (for now)
                            // We need to clone the node because a node can only have one parent
                            targetObj[propertyName] = sourceValue?.DeepClone();
                        }
                    }
                    else
                    {
                        // Add new property
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
                // Target and Source are different types or primitives
                // We cannot "Merge" a Number into an Object.
                // In a void method modifying target, we can't easily replace "target" itself if it's the root reference passed in.
                // But JsonNode is a reference type. 
                // Wait, if I pass a JsonValue, I can't change it to another JsonValue easily in-place if it's the root.
                // But usually we merge properties of Objects.
                
                // If we are here, it implies we are trying to merge primitives at the top level, which doesn't make sense for "Merge".
                // We generally expect Merge(Obj, Obj).
            }
        }
    }
}

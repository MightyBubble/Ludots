using System;
using System.Text.Json.Nodes;

namespace Ludots.Core.Layers
{
    public static class EntityLayerAuthoring
    {
        public static LayerMask ReadLayerMask(JsonNode data, string diagnosticLabel)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException($"{diagnosticLabel} EntityLayer requires an object payload with category and mask arrays.");
            }

            uint category = ReadNamedLayerMask(obj, "category", diagnosticLabel);
            uint mask = ReadNamedLayerMask(obj, "mask", diagnosticLabel);
            return new LayerMask(category, mask);
        }

        private static uint ReadNamedLayerMask(JsonObject obj, string propertyName, string diagnosticLabel)
        {
            if (!obj.TryGetPropertyValue(propertyName, out JsonNode? node) ||
                node is not JsonArray names ||
                names.Count <= 0)
            {
                throw new InvalidOperationException($"{diagnosticLabel} EntityLayer requires non-empty '{propertyName}'.");
            }

            uint mask = 0u;
            for (int i = 0; i < names.Count; i++)
            {
                string? layerName = names[i]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(layerName))
                {
                    throw new InvalidOperationException(
                        $"{diagnosticLabel} EntityLayer.{propertyName}[{i}] must be a non-empty layer name.");
                }

                int index = LayerRegistry.GetIndex(layerName);
                if (index < 0)
                {
                    throw new InvalidOperationException(
                        $"{diagnosticLabel} EntityLayer.{propertyName}[{i}] references unregistered layer '{layerName}'.");
                }

                mask |= 1u << index;
            }

            if (mask == 0u)
            {
                throw new InvalidOperationException($"{diagnosticLabel} EntityLayer.{propertyName} resolved to an empty mask.");
            }

            return mask;
        }
    }
}

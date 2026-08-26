using System;
using System.IO;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Platform.Abstractions;

namespace Ludots.Adapter.Raylib
{
    internal readonly record struct NavWalkabilityOverlayDescriptor(
        string TextureUri,
        WorldAabbCm BoundsCm,
        string? SidecarPath);

    internal static class NavWalkabilityOverlayDescriptorResolver
    {
        public const string MetadataKey = "navWalkabilityOverlay";

        public static NavWalkabilityOverlayDescriptor ResolveOrThrow(
            MapConfig mapConfig,
            IRenderAssetPathResolver assetPaths)
        {
            if (mapConfig == null)
            {
                throw new ArgumentNullException(nameof(mapConfig));
            }

            if (assetPaths == null)
            {
                throw new ArgumentNullException(nameof(assetPaths));
            }

            if (!mapConfig.Metadata.TryGetValue(MetadataKey, out JsonNode? metadataNode) ||
                metadataNode is not JsonObject metadata)
            {
                throw new InvalidOperationException(
                    $"Map '{mapConfig.Id}' must declare Metadata.{MetadataKey} before DrawNavWalkabilityTexture can be enabled.");
            }

            string textureUri = RequireString(metadata, "textureUri", $"map '{mapConfig.Id}' Metadata.{MetadataKey}");
            if (!assetPaths.TryResolveFullPath(textureUri, out string texturePath))
            {
                throw new InvalidOperationException(
                    $"Map '{mapConfig.Id}' nav walkability texture URI cannot be resolved: '{textureUri}'.");
            }

            string sidecarPath = texturePath + ".json";
            JsonObject boundsSource = metadata;
            string boundsSourceLabel = $"map '{mapConfig.Id}' Metadata.{MetadataKey}";
            string? selectedSidecarPath = null;
            if (File.Exists(sidecarPath))
            {
                JsonNode? sidecarNode;
                try
                {
                    sidecarNode = JsonNode.Parse(File.ReadAllText(sidecarPath));
                }
                catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
                {
                    throw new InvalidOperationException(
                        $"Nav walkability sidecar could not be read: '{sidecarPath}'.",
                        ex);
                }

                boundsSource = sidecarNode as JsonObject
                    ?? throw new InvalidOperationException(
                        $"Nav walkability sidecar root must be a JSON object: '{sidecarPath}'.");
                boundsSourceLabel = $"nav walkability sidecar '{sidecarPath}'";
                selectedSidecarPath = sidecarPath;
            }

            WorldAabbCm bounds = ParseBounds(boundsSource["boundsCm"], boundsSourceLabel);
            return new NavWalkabilityOverlayDescriptor(textureUri, bounds, selectedSidecarPath);
        }

        private static WorldAabbCm ParseBounds(JsonNode? node, string sourceLabel)
        {
            if (node is not JsonObject bounds)
            {
                throw new InvalidOperationException($"{sourceLabel} must declare boundsCm as an object.");
            }

            int minX = RequireInt(bounds, "minX", sourceLabel);
            int minZ = RequireInt(bounds, "minZ", sourceLabel);
            int maxX = RequireInt(bounds, "maxX", sourceLabel);
            int maxZ = RequireInt(bounds, "maxZ", sourceLabel);
            long width = (long)maxX - minX;
            long height = (long)maxZ - minZ;
            if (width <= 0 || width > int.MaxValue || height <= 0 || height > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"{sourceLabel} boundsCm must satisfy min < max with Int32-sized extents.");
            }

            return new WorldAabbCm(minX, minZ, (int)width, (int)height);
        }

        private static string RequireString(JsonObject obj, string key, string sourceLabel)
        {
            string value;
            try
            {
                value = obj[key]?.GetValue<string>() ?? string.Empty;
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"{sourceLabel}.{key} must be a string.", ex);
            }

            if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{sourceLabel}.{key} must be a non-empty string without surrounding whitespace.");
            }

            return value;
        }

        private static int RequireInt(JsonObject obj, string key, string sourceLabel)
        {
            JsonNode? node = obj[key];
            if (node == null)
            {
                throw new InvalidOperationException($"{sourceLabel}.boundsCm.{key} is required.");
            }

            try
            {
                return node.GetValue<int>();
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
            {
                throw new InvalidOperationException($"{sourceLabel}.boundsCm.{key} must be an Int32.", ex);
            }
        }
    }
}

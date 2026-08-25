using System;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Instancing;

namespace Ludots.Core.Presentation.Config
{
    /// <summary>
    /// Loads and strictly validates external factorized instanced transform sources
    /// (format <c>ludots.instanced_transform_factorized.v1</c>) through the VFS. Core owns the
    /// authored instance counts and SoA transform data; adapters only consume what this loader
    /// produced. Unknown fields, missing sets, malformed arrays, unsupported formats and
    /// authored-count mismatches fail fast and never yield a partial source.
    /// </summary>
    public sealed class InstancedBatchFactorizedSourceLoader
    {
        public const string SupportedFormat = "ludots.instanced_transform_factorized.v1";

        private readonly IVirtualFileSystem _vfs;

        public InstancedBatchFactorizedSourceLoader(IVirtualFileSystem vfs)
        {
            _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
        }

        public InstancedBatchFactorizedSource Load(in InstancedBatchInstanceSource source, string batchKey, string groupId)
        {
            string context = $"Instanced batch '{batchKey}' group '{groupId}' source";
            if (!string.Equals(source.Format, SupportedFormat, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{context} declares unsupported format '{source.Format}'. Expected exactly '{SupportedFormat}'.");
            }

            JsonObject root = ReadRoot(source.AssetUri, context);
            string fileContext = $"Factorized source asset '{source.AssetUri}'";
            ValidateObjectFields(root, fileContext, "format", "sets");

            string fileFormat = RequireString(root["format"], $"{fileContext} format");
            if (!string.Equals(fileFormat, SupportedFormat, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{fileContext} declares format '{fileFormat}'. Expected exactly '{SupportedFormat}'.");
            }

            if (root["sets"] is not JsonObject sets)
            {
                throw new InvalidOperationException($"{fileContext} sets must be an object.");
            }

            if (!sets.TryGetPropertyValue(source.SetId, out JsonNode? setNode) || setNode is not JsonObject set)
            {
                throw new InvalidOperationException(
                    $"{fileContext} does not declare set '{source.SetId}'.");
            }

            string setContext = $"{fileContext} set '{source.SetId}'";
            ValidateObjectFields(set, setContext, "instanceCount", "positionCm", "rotation", "scale");

            int instanceCount = ParseRequiredInt(set["instanceCount"], $"{setContext} instanceCount");
            if (instanceCount <= 0)
            {
                throw new InvalidOperationException($"{setContext} instanceCount must be positive.");
            }

            if (instanceCount != source.InstanceCount)
            {
                throw new InvalidOperationException(
                    $"{context} authored instanceCount {source.InstanceCount} does not match {setContext} instanceCount {instanceCount}.");
            }

            if (set["positionCm"] is not JsonObject positionCmNode)
            {
                throw new InvalidOperationException($"{setContext} positionCm must be an object.");
            }

            ValidateObjectFields(positionCmNode, $"{setContext} positionCm", "x", "y", "z");
            var positions = new Vector3[instanceCount];
            float[] positionX = ParseComponentArray(positionCmNode["x"], instanceCount, $"{setContext} positionCm.x");
            float[] positionY = ParseComponentArray(positionCmNode["y"], instanceCount, $"{setContext} positionCm.y");
            float[] positionZ = ParseComponentArray(positionCmNode["z"], instanceCount, $"{setContext} positionCm.z");
            for (int i = 0; i < instanceCount; i++)
            {
                positions[i] = new Vector3(positionX[i], positionY[i], positionZ[i]);
            }

            var rotations = new Quaternion[instanceCount];
            for (int i = 0; i < instanceCount; i++)
            {
                rotations[i] = Quaternion.Identity;
            }

            if (set["rotation"] != null)
            {
                if (set["rotation"] is not JsonObject rotationNode)
                {
                    throw new InvalidOperationException($"{setContext} rotation must be an object.");
                }

                ValidateObjectFields(rotationNode, $"{setContext} rotation", "x", "y", "z", "w");
                float[] rotationX = ParseComponentArray(rotationNode["x"], instanceCount, $"{setContext} rotation.x");
                float[] rotationY = ParseComponentArray(rotationNode["y"], instanceCount, $"{setContext} rotation.y");
                float[] rotationZ = ParseComponentArray(rotationNode["z"], instanceCount, $"{setContext} rotation.z");
                float[] rotationW = ParseComponentArray(rotationNode["w"], instanceCount, $"{setContext} rotation.w");
                for (int i = 0; i < instanceCount; i++)
                {
                    rotations[i] = new Quaternion(rotationX[i], rotationY[i], rotationZ[i], rotationW[i]);
                }
            }

            var scales = new Vector3[instanceCount];
            for (int i = 0; i < instanceCount; i++)
            {
                scales[i] = Vector3.One;
            }

            if (set["scale"] != null)
            {
                if (set["scale"] is not JsonObject scaleNode)
                {
                    throw new InvalidOperationException($"{setContext} scale must be an object.");
                }

                ValidateObjectFields(scaleNode, $"{setContext} scale", "x", "y", "z");
                float[] scaleX = ParseComponentArray(scaleNode["x"], instanceCount, $"{setContext} scale.x");
                float[] scaleY = ParseComponentArray(scaleNode["y"], instanceCount, $"{setContext} scale.y");
                float[] scaleZ = ParseComponentArray(scaleNode["z"], instanceCount, $"{setContext} scale.z");
                for (int i = 0; i < instanceCount; i++)
                {
                    scales[i] = new Vector3(scaleX[i], scaleY[i], scaleZ[i]);
                }
            }

            return new InstancedBatchFactorizedSource(
                source.Format,
                source.AssetUri,
                source.SetId,
                instanceCount,
                source.GroundToVisualHeightmap,
                positions,
                rotations,
                scales);
        }

        private JsonObject ReadRoot(string assetUri, string context)
        {
            ValidateAssetUriShape(assetUri, context);
            try
            {
                using Stream stream = _vfs.GetStream(assetUri);
                return JsonNode.Parse(stream) as JsonObject
                    ?? throw new InvalidOperationException($"'{assetUri}' must parse to a JSON object.");
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                throw new InvalidOperationException(
                    $"{context} references unreadable factorized source asset '{assetUri}': {ex.Message}", ex);
            }
        }

        // Shape errors are authoring errors: validate before VFS IO so model/programming
        // guard exceptions are never relabeled as unreadable assets.
        private static void ValidateAssetUriShape(string assetUri, string context)
        {
            string[] parts = assetUri.Split(new[] { ':' }, 2);
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                throw new InvalidOperationException(
                    $"{context} declares malformed factorized source assetUri '{assetUri}'. Expected 'ModId:Path/To/File'.");
            }
        }

        private static float[] ParseComponentArray(JsonNode? node, int expectedCount, string context)
        {
            if (node is not JsonArray arr)
            {
                throw new InvalidOperationException($"{context} must be an array of exactly {expectedCount} finite numbers.");
            }

            if (arr.Count != expectedCount)
            {
                throw new InvalidOperationException(
                    $"{context} must contain exactly {expectedCount} entries, but contains {arr.Count}.");
            }

            var values = new float[expectedCount];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonValue value || !value.TryGetValue<float>(out float parsed) || !float.IsFinite(parsed))
                {
                    throw new InvalidOperationException($"{context}[{i}] must be a finite number.");
                }

                values[i] = parsed;
            }

            return values;
        }

        private static int ParseRequiredInt(JsonNode? node, string context)
        {
            if (node is not JsonValue value || !value.TryGetValue<int>(out int parsed))
            {
                throw new InvalidOperationException($"{context} requires an explicit integer field.");
            }

            return parsed;
        }

        private static string RequireString(JsonNode? node, string context)
        {
            if (node is not JsonValue value || !value.TryGetValue<string>(out string? parsed) ||
                string.IsNullOrWhiteSpace(parsed) ||
                !string.Equals(parsed, parsed.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{context} must be a non-empty string without leading or trailing whitespace.");
            }

            return parsed;
        }

        private static void ValidateObjectFields(JsonObject obj, string context, params string[] allowedNames)
        {
            foreach ((string propertyName, _) in obj)
            {
                bool allowed = false;
                for (int i = 0; i < allowedNames.Length; i++)
                {
                    if (string.Equals(propertyName, allowedNames[i], StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    throw new InvalidOperationException(
                        $"{context} uses unsupported field '{propertyName}'.");
                }
            }
        }
    }
}

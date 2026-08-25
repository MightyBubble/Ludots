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
    /// authored instance counts and factorized component arrays; adapters only consume what this
    /// loader produced. Unknown fields, missing sets, malformed arrays, unsupported formats,
    /// authored-count mismatches and instance counts above <see cref="MaxInstanceCount"/> fail
    /// fast and never yield a partial source.
    /// </summary>
    public sealed class InstancedBatchFactorizedSourceLoader
    {
        public const string SupportedFormat = "ludots.instanced_transform_factorized.v1";

        /// <summary>
        /// Hard upper bound on authored instance counts. Enforced before any per-instance array
        /// allocation so malformed authored counts fail as authoring errors instead of driving
        /// oversized allocations; the documented 50k-scale usage sits far below this bound.
        /// </summary>
        public const int MaxInstanceCount = 1_000_000;

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

            if (instanceCount > MaxInstanceCount)
            {
                throw new InvalidOperationException(
                    $"{setContext} instanceCount {instanceCount} exceeds the documented maximum {MaxInstanceCount}.");
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

            // Every component array length is validated before any per-instance allocation so a
            // malformed authored count can never drive a huge allocation.
            JsonArray positionX = RequireComponentArray(positionCmNode["x"], instanceCount, $"{setContext} positionCm.x");
            JsonArray positionY = RequireComponentArray(positionCmNode["y"], instanceCount, $"{setContext} positionCm.y");
            JsonArray positionZ = RequireComponentArray(positionCmNode["z"], instanceCount, $"{setContext} positionCm.z");

            JsonArray? rotationX = null, rotationY = null, rotationZ = null, rotationW = null;
            if (set["rotation"] != null)
            {
                if (set["rotation"] is not JsonObject rotationNode)
                {
                    throw new InvalidOperationException($"{setContext} rotation must be an object.");
                }

                ValidateObjectFields(rotationNode, $"{setContext} rotation", "x", "y", "z", "w");
                rotationX = RequireComponentArray(rotationNode["x"], instanceCount, $"{setContext} rotation.x");
                rotationY = RequireComponentArray(rotationNode["y"], instanceCount, $"{setContext} rotation.y");
                rotationZ = RequireComponentArray(rotationNode["z"], instanceCount, $"{setContext} rotation.z");
                rotationW = RequireComponentArray(rotationNode["w"], instanceCount, $"{setContext} rotation.w");
            }

            JsonArray? scaleX = null, scaleY = null, scaleZ = null;
            if (set["scale"] != null)
            {
                if (set["scale"] is not JsonObject scaleNode)
                {
                    throw new InvalidOperationException($"{setContext} scale must be an object.");
                }

                ValidateObjectFields(scaleNode, $"{setContext} scale", "x", "y", "z");
                scaleX = RequireComponentArray(scaleNode["x"], instanceCount, $"{setContext} scale.x");
                scaleY = RequireComponentArray(scaleNode["y"], instanceCount, $"{setContext} scale.y");
                scaleZ = RequireComponentArray(scaleNode["z"], instanceCount, $"{setContext} scale.z");
            }

            var positions = new Vector3[instanceCount];
            for (int i = 0; i < instanceCount; i++)
            {
                positions[i] = new Vector3(
                    ReadComponentValue(positionX[i], $"{setContext} positionCm.x[{i}]"),
                    ReadComponentValue(positionY[i], $"{setContext} positionCm.y[{i}]"),
                    ReadComponentValue(positionZ[i], $"{setContext} positionCm.z[{i}]"));
            }

            var rotations = new Quaternion[instanceCount];
            if (rotationX != null)
            {
                for (int i = 0; i < instanceCount; i++)
                {
                    rotations[i] = new Quaternion(
                        ReadComponentValue(rotationX[i], $"{setContext} rotation.x[{i}]"),
                        ReadComponentValue(rotationY[i], $"{setContext} rotation.y[{i}]"),
                        ReadComponentValue(rotationZ[i], $"{setContext} rotation.z[{i}]"),
                        ReadComponentValue(rotationW[i], $"{setContext} rotation.w[{i}]"));
                }
            }
            else
            {
                for (int i = 0; i < instanceCount; i++)
                {
                    rotations[i] = Quaternion.Identity;
                }
            }

            var scales = new Vector3[instanceCount];
            if (scaleX != null)
            {
                for (int i = 0; i < instanceCount; i++)
                {
                    scales[i] = new Vector3(
                        ReadComponentValue(scaleX[i], $"{setContext} scale.x[{i}]"),
                        ReadComponentValue(scaleY[i], $"{setContext} scale.y[{i}]"),
                        ReadComponentValue(scaleZ[i], $"{setContext} scale.z[{i}]"));
                }
            }
            else
            {
                for (int i = 0; i < instanceCount; i++)
                {
                    scales[i] = Vector3.One;
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

        private static JsonArray RequireComponentArray(JsonNode? node, int expectedCount, string context)
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

            return arr;
        }

        private static float ReadComponentValue(JsonNode? node, string context)
        {
            if (node is not JsonValue value || !value.TryGetValue<float>(out float parsed) || !float.IsFinite(parsed))
            {
                throw new InvalidOperationException($"{context} must be a finite number.");
            }

            return parsed;
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

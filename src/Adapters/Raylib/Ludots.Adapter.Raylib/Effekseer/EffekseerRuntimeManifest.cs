using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Adapter.Raylib.Effekseer
{
    internal sealed class EffekseerRuntimeManifest
    {
        internal const string FileName = "runtime-manifest.json";

        private static readonly string[] RequiredFields =
        {
            "runtimeIdentifier",
            "library",
            "bridgeAbiVersion",
            "effekseerVersion",
            "effekseerCommit",
            "effekseerVendoredTreeSha256",
            "effekseerVendoredFileCount",
            "effekseerRuntimeFormatVersion",
            "dynamicInputSlotCount",
            "emitterNodeTypes",
            "sha256",
        };

        private readonly Dictionary<AssetKind, int> _nodeTypes;

        private EffekseerRuntimeManifest(
            string runtimeIdentifier,
            string library,
            int bridgeAbiVersion,
            string effekseerVersion,
            string effekseerCommit,
            string effekseerVendoredTreeSha256,
            int effekseerVendoredFileCount,
            int runtimeFormatVersion,
            int dynamicInputSlotCount,
            Dictionary<AssetKind, int> nodeTypes,
            string sha256)
        {
            RuntimeIdentifier = runtimeIdentifier;
            Library = library;
            BridgeAbiVersion = bridgeAbiVersion;
            EffekseerVersion = effekseerVersion;
            EffekseerCommit = effekseerCommit;
            EffekseerVendoredTreeSha256 = effekseerVendoredTreeSha256;
            EffekseerVendoredFileCount = effekseerVendoredFileCount;
            RuntimeFormatVersion = runtimeFormatVersion;
            DynamicInputSlotCount = dynamicInputSlotCount;
            _nodeTypes = nodeTypes;
            Sha256 = sha256;
        }

        public string RuntimeIdentifier { get; }
        public string Library { get; }
        public int BridgeAbiVersion { get; }
        public string EffekseerVersion { get; }
        public string EffekseerCommit { get; }
        public string EffekseerVendoredTreeSha256 { get; }
        public int EffekseerVendoredFileCount { get; }
        public int RuntimeFormatVersion { get; }
        public int DynamicInputSlotCount { get; }
        public string Sha256 { get; }

        public int ResolveNodeType(AssetKind assetKind)
        {
            if (!_nodeTypes.TryGetValue(assetKind, out int nodeType))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(assetKind),
                    assetKind,
                    "AssetKind is not declared by the Effekseer runtime manifest.");
            }
            return nodeType;
        }

        public static EffekseerRuntimeManifest LoadAndVerify(
            string manifestPath,
            out string resolvedLibraryPath)
        {
            manifestPath = Path.GetFullPath(manifestPath);
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException(
                    $"Effekseer runtime manifest was not found at its required absolute path: {manifestPath}",
                    manifestPath);
            }
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllBytes(manifestPath),
                new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            JsonElement root = document.RootElement;
            RequireExactObject(root, RequiredFields, "Effekseer runtime manifest");

            string runtimeIdentifier = RequireCanonicalString(root, "runtimeIdentifier");
            if (!string.Equals(runtimeIdentifier, RuntimeInformation.RuntimeIdentifier, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Effekseer runtime manifest targets '{runtimeIdentifier}', but the current runtime is '{RuntimeInformation.RuntimeIdentifier}'.");
            }

            string library = RequireCanonicalString(root, "library");
            if (Path.IsPathFullyQualified(library) || ContainsParentTraversal(library))
            {
                throw new InvalidDataException("Effekseer runtime manifest library must be a portable relative path without parent traversal.");
            }
            resolvedLibraryPath = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(manifestPath)!, library.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(resolvedLibraryPath))
            {
                throw new FileNotFoundException(
                    $"Effekseer native bridge was not found at its required absolute path: {resolvedLibraryPath}",
                    resolvedLibraryPath);
            }
            int bridgeAbiVersion = RequirePositiveInt32(root, "bridgeAbiVersion");
            string effekseerVersion = RequireCanonicalString(root, "effekseerVersion");
            string effekseerCommit = RequireLowerHex(root, "effekseerCommit", 40);
            string effekseerVendoredTreeSha256 = RequireLowerHex(root, "effekseerVendoredTreeSha256", 64);
            int effekseerVendoredFileCount = RequirePositiveInt32(root, "effekseerVendoredFileCount");
            int runtimeFormatVersion = RequirePositiveInt32(root, "effekseerRuntimeFormatVersion");
            int dynamicInputSlotCount = RequirePositiveInt32(root, "dynamicInputSlotCount");
            if (dynamicInputSlotCount != MaterialCustomDataBinding.MaxSlots)
            {
                throw new InvalidDataException(
                    $"Effekseer runtime manifest declares {dynamicInputSlotCount} dynamic input slots, but Core requires {MaterialCustomDataBinding.MaxSlots}.");
            }
            Dictionary<AssetKind, int> nodeTypes = ParseNodeTypes(root.GetProperty("emitterNodeTypes"));
            string sha256 = EmitterAssetDescriptor.NormalizeSha256(RequireCanonicalString(root, "sha256"));
            if (!string.Equals(sha256, root.GetProperty("sha256").GetString(), StringComparison.Ordinal))
            {
                throw new InvalidDataException("Effekseer runtime manifest SHA-256 must use lowercase hexadecimal.");
            }

            string actualSha256;
            using (FileStream stream = File.OpenRead(resolvedLibraryPath))
            {
                actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            }
            if (!string.Equals(sha256, actualSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Effekseer native bridge SHA-256 mismatch for '{resolvedLibraryPath}'. Manifest declares {sha256}, actual is {actualSha256}.");
            }

            return new EffekseerRuntimeManifest(
                runtimeIdentifier,
                library,
                bridgeAbiVersion,
                effekseerVersion,
                effekseerCommit,
                effekseerVendoredTreeSha256,
                effekseerVendoredFileCount,
                runtimeFormatVersion,
                dynamicInputSlotCount,
                nodeTypes,
                sha256);
        }

        private static Dictionary<AssetKind, int> ParseNodeTypes(JsonElement node)
        {
            AssetKind[] expectedKinds = Array.FindAll(
                Enum.GetValues<AssetKind>(),
                static kind => kind.IsEmitterKind());
            string[] expectedNames = Array.ConvertAll(expectedKinds, static kind => kind.ToString());
            RequireExactObject(node, expectedNames, "Effekseer emitterNodeTypes");

            var result = new Dictionary<AssetKind, int>(expectedKinds.Length);
            var values = new HashSet<int>();
            for (int i = 0; i < expectedKinds.Length; i++)
            {
                int nodeType = RequirePositiveInt32(node, expectedNames[i]);
                if (!values.Add(nodeType))
                {
                    throw new InvalidDataException($"Effekseer emitter node type {nodeType} is duplicated.");
                }
                result.Add(expectedKinds[i], nodeType);
            }
            return result;
        }

        private static void RequireExactObject(JsonElement node, string[] requiredFields, string label)
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{label} must be a JSON object.");
            }

            var required = new HashSet<string>(requiredFields, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in node.EnumerateObject())
            {
                if (!required.Contains(property.Name))
                {
                    throw new InvalidDataException($"{label} contains unsupported field '{property.Name}'.");
                }
                if (!seen.Add(property.Name))
                {
                    throw new InvalidDataException($"{label} duplicates field '{property.Name}'.");
                }
            }
            foreach (string field in requiredFields)
            {
                if (!seen.Contains(field))
                {
                    throw new InvalidDataException($"{label} is missing required field '{field}'.");
                }
            }
        }

        private static string RequireCanonicalString(JsonElement root, string field)
        {
            JsonElement node = root.GetProperty(field);
            if (node.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"Effekseer runtime manifest field '{field}' must be a string.");
            }
            string value = node.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Effekseer runtime manifest field '{field}' must be a canonical non-empty string.");
            }
            return value;
        }

        private static int RequirePositiveInt32(JsonElement root, string field)
        {
            JsonElement node = root.GetProperty(field);
            if (node.ValueKind != JsonValueKind.Number || !node.TryGetInt32(out int value) || value <= 0)
            {
                throw new InvalidDataException($"Effekseer runtime manifest field '{field}' must be a positive integer.");
            }
            return value;
        }

        private static string RequireLowerHex(JsonElement root, string field, int length)
        {
            string value = RequireCanonicalString(root, field);
            if (value.Length != length)
            {
                throw new InvalidDataException(
                    $"Effekseer runtime manifest field '{field}' must contain {length} lowercase hexadecimal characters.");
            }
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                {
                    throw new InvalidDataException(
                        $"Effekseer runtime manifest field '{field}' must contain {length} lowercase hexadecimal characters.");
                }
            }
            return value;
        }

        private static bool ContainsParentTraversal(string path)
        {
            string normalized = path.Replace('\\', '/');
            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return Array.Exists(segments, static segment => string.Equals(segment, "..", StringComparison.Ordinal));
        }
    }
}

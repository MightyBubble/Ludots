using System.Buffers.Binary;
using System.Text.Json;

namespace Ludots.Raylib.Render;

internal static class RaylibGltfMeshSkinBindings
{
    public const int SkinnedMesh = -1;
    public const int StaticMesh = -2;

    public static int[] Load(string path, int expectedMeshCount, ReadOnlySpan<string> boneNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedMeshCount);
        if (boneNames.Length > RaylibPoseTexturePalette.MaxBoneCount)
        {
            throw Invalid(path, $"Native bone count {boneNames.Length} exceeds capacity {RaylibPoseTexturePalette.MaxBoneCount}.");
        }

        using JsonDocument document = ReadDocument(path);
        JsonElement root = document.RootElement;
        if (Required(root, "asset", path).GetProperty("version").GetString() != "2.0")
        {
            throw Invalid(path, "Only glTF 2.0 is supported.");
        }

        JsonElement nodes = Array(root, "nodes", path);
        JsonElement meshes = Array(root, "meshes", path);
        int[] parents = ReadParents(nodes, path, out int[] hierarchyOrder);
        int[] nodeBones = ReadJointBones(root, nodes, boneNames, path);
        int[] nearestBones = new int[nodes.GetArrayLength()];
        foreach (int nodeIndex in hierarchyOrder)
        {
            nearestBones[nodeIndex] = nodeBones[nodeIndex] >= 0
                ? nodeBones[nodeIndex]
                : parents[nodeIndex] >= 0 ? nearestBones[parents[nodeIndex]] : StaticMesh;
        }

        int[] bindings = new int[expectedMeshCount];
        int nativeMeshIndex = 0;
        int skinCount = root.TryGetProperty("skins", out JsonElement skins) ? skins.GetArrayLength() : 0;
        int accessorCount = root.TryGetProperty("accessors", out JsonElement accessors) ? accessors.GetArrayLength() : 0;
        for (int nodeIndex = 0; nodeIndex < nodes.GetArrayLength(); nodeIndex++)
        {
            JsonElement node = nodes[nodeIndex];
            bool hasSkin = node.TryGetProperty("skin", out JsonElement skin);
            if (hasSkin) Index(skin, skinCount, $"node[{nodeIndex}].skin", path);
            if (!node.TryGetProperty("mesh", out JsonElement meshReference))
            {
                if (hasSkin) throw Invalid(path, $"node[{nodeIndex}] has a skin without a mesh.");
                continue;
            }

            int meshIndex = Index(meshReference, meshes.GetArrayLength(), $"node[{nodeIndex}].mesh", path);
            JsonElement primitives = Array(meshes[meshIndex], "primitives", path);
            if (primitives.GetArrayLength() == 0) throw Invalid(path, $"mesh[{meshIndex}] has no primitives.");
            foreach (JsonElement primitive in primitives.EnumerateArray())
            {
                if (primitive.TryGetProperty("mode", out JsonElement mode) && Integer(mode, "primitive.mode", path) != 4)
                {
                    throw Invalid(path, $"mesh[{meshIndex}] contains a non-triangle primitive.");
                }

                JsonElement attributes = Required(primitive, "attributes", path);
                if (attributes.ValueKind != JsonValueKind.Object) throw Invalid(path, "Primitive attributes must be an object.");
                if (!attributes.TryGetProperty("POSITION", out _)) throw Invalid(path, "Primitive POSITION is missing.");
                foreach (JsonProperty attribute in attributes.EnumerateObject())
                {
                    Index(attribute.Value, accessorCount, $"attribute {attribute.Name}", path);
                    if ((attribute.Name.StartsWith("JOINTS_", StringComparison.Ordinal) && attribute.Name != "JOINTS_0") ||
                        (attribute.Name.StartsWith("WEIGHTS_", StringComparison.Ordinal) && attribute.Name != "WEIGHTS_0"))
                    {
                        throw Invalid(path, $"Unsupported skin attribute '{attribute.Name}'.");
                    }
                }

                bool hasJoints = attributes.TryGetProperty("JOINTS_0", out _);
                bool hasWeights = attributes.TryGetProperty("WEIGHTS_0", out _);
                if (hasJoints != hasWeights) throw Invalid(path, $"mesh[{meshIndex}] must provide JOINTS_0 and WEIGHTS_0 together.");
                if (hasJoints != hasSkin) throw Invalid(path, $"node[{nodeIndex}] skin reference and primitive skin attributes must agree.");
                if (primitive.TryGetProperty("targets", out JsonElement targets) && targets.GetArrayLength() != 0)
                {
                    throw Invalid(path, $"mesh[{meshIndex}] morph targets are not supported.");
                }

                if (nativeMeshIndex >= bindings.Length) throw Invalid(path, $"Primitive count exceeds native mesh count {expectedMeshCount}.");
                bindings[nativeMeshIndex++] = hasJoints ? SkinnedMesh : nearestBones[nodeIndex];
            }
        }

        if (nativeMeshIndex != expectedMeshCount)
        {
            throw Invalid(path, $"Primitive count {nativeMeshIndex} does not match native mesh count {expectedMeshCount}.");
        }

        ValidateAnimationTargets(root, nodes, nodeBones, parents, hierarchyOrder, path);
        return bindings;
    }

    private static int[] ReadJointBones(JsonElement root, JsonElement nodes, ReadOnlySpan<string> boneNames, string path)
    {
        int[] nodeBones = new int[nodes.GetArrayLength()];
        System.Array.Fill(nodeBones, StaticMesh);
        int skinCount = root.TryGetProperty("skins", out JsonElement skins) ? skins.GetArrayLength() : 0;
        if (skinCount == 0)
        {
            if (!boneNames.IsEmpty) throw Invalid(path, "Native bones exist without a glTF skin.");
            return nodeBones;
        }

        if (skinCount != 1) throw Invalid(path, $"Expected one skin; found {skinCount}.");
        JsonElement joints = Array(skins[0], "joints", path);
        if (joints.GetArrayLength() == 0 || joints.GetArrayLength() != boneNames.Length)
        {
            throw Invalid(path, $"Skin joint count {joints.GetArrayLength()} does not match native bone count {boneNames.Length}.");
        }

        HashSet<string> names = new(StringComparer.Ordinal);
        for (int boneIndex = 0; boneIndex < boneNames.Length; boneIndex++)
        {
            int nodeIndex = Index(joints[boneIndex], nodes.GetArrayLength(), $"skin.joints[{boneIndex}]", path);
            if (nodeBones[nodeIndex] >= 0) throw Invalid(path, $"Joint node[{nodeIndex}] occurs more than once.");
            JsonElement nameElement = Required(nodes[nodeIndex], "name", path);
            string? name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name)) throw Invalid(path, $"Joint node[{nodeIndex}] has a missing or ambiguous name.");
            if (!string.Equals(name, boneNames[boneIndex], StringComparison.Ordinal))
            {
                throw Invalid(path, $"Joint '{name}' at bone index {boneIndex} does not match native bone '{boneNames[boneIndex]}'.");
            }

            nodeBones[nodeIndex] = boneIndex;
        }

        return nodeBones;
    }

    private static int[] ReadParents(JsonElement nodes, string path, out int[] hierarchyOrder)
    {
        int nodeCount = nodes.GetArrayLength();
        int[] parents = new int[nodeCount];
        System.Array.Fill(parents, -1);
        for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            if (!nodes[nodeIndex].TryGetProperty("children", out _)) continue;
            foreach (JsonElement child in Array(nodes[nodeIndex], "children", path).EnumerateArray())
            {
                int childIndex = Index(child, nodeCount, $"node[{nodeIndex}].children", path);
                if (parents[childIndex] >= 0) throw Invalid(path, $"node[{childIndex}] has multiple parent references.");
                parents[childIndex] = nodeIndex;
            }
        }

        hierarchyOrder = new int[nodeCount];
        int count = 0;
        for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            if (parents[nodeIndex] < 0) hierarchyOrder[count++] = nodeIndex;
        }

        for (int cursor = 0; cursor < count; cursor++)
        {
            if (!nodes[hierarchyOrder[cursor]].TryGetProperty("children", out JsonElement children)) continue;
            foreach (JsonElement child in children.EnumerateArray()) hierarchyOrder[count++] = child.GetInt32();
        }

        if (count != nodeCount) throw Invalid(path, "Node hierarchy contains a cycle.");
        return parents;
    }

    private static void ValidateAnimationTargets(JsonElement root, JsonElement nodes, int[] nodeBones, int[] parents, int[] hierarchyOrder, string path)
    {
        if (!root.TryGetProperty("animations", out _)) return;
        bool[] affectsModel = new bool[nodes.GetArrayLength()];
        for (int i = hierarchyOrder.Length - 1; i >= 0; i--)
        {
            int nodeIndex = hierarchyOrder[i];
            affectsModel[nodeIndex] |= nodeBones[nodeIndex] >= 0 || nodes[nodeIndex].TryGetProperty("mesh", out _);
            if (parents[nodeIndex] >= 0) affectsModel[parents[nodeIndex]] |= affectsModel[nodeIndex];
        }

        foreach (JsonElement animation in Array(root, "animations", path).EnumerateArray())
        {
            int samplerCount = Array(animation, "samplers", path).GetArrayLength();
            foreach (JsonElement channel in Array(animation, "channels", path).EnumerateArray())
            {
                Index(Required(channel, "sampler", path), samplerCount, "animation sampler", path);
                JsonElement target = Required(channel, "target", path);
                int nodeIndex = Index(Required(target, "node", path), nodes.GetArrayLength(), "animation target node", path);
                string? channelPath = Required(target, "path", path).GetString();
                if (channelPath is not ("translation" or "rotation" or "scale"))
                {
                    throw Invalid(path, $"Animation path '{channelPath}' is not supported.");
                }

                if (affectsModel[nodeIndex] && nodeBones[nodeIndex] < 0)
                {
                    throw Invalid(path, $"Animation targets non-joint node[{nodeIndex}]; a rigid bone binding cannot represent its motion.");
                }
            }
        }
    }

    private static JsonDocument ReadDocument(string path)
    {
        using FileStream stream = File.OpenRead(path);
        string extension = Path.GetExtension(path);
        try
        {
            if (extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase)) return JsonDocument.Parse(stream);
            if (!extension.Equals(".glb", StringComparison.OrdinalIgnoreCase)) throw Invalid(path, "Expected a .gltf or .glb source.");
            if (stream.Length < 20 || stream.Length > uint.MaxValue) throw Invalid(path, "Invalid GLB file length.");
            Span<byte> header = stackalloc byte[20];
            stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x46546C67 ||
                BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != 2 ||
                BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != stream.Length ||
                BinaryPrimitives.ReadUInt32LittleEndian(header[16..]) != 0x4E4F534A)
            {
                throw Invalid(path, "Invalid GLB header or first JSON chunk.");
            }

            uint jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
            if (jsonLength == 0 || jsonLength > int.MaxValue || jsonLength % 4 != 0 || jsonLength > stream.Length - stream.Position)
            {
                throw Invalid(path, "Invalid GLB JSON chunk length.");
            }

            byte[] json = new byte[(int)jsonLength];
            stream.ReadExactly(json);
            bool hasBinaryChunk = false;
            Span<byte> chunkHeader = stackalloc byte[8];
            while (stream.Position < stream.Length)
            {
                stream.ReadExactly(chunkHeader);
                uint chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader);
                uint chunkType = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
                if (hasBinaryChunk || chunkType != 0x004E4942 || chunkLength % 4 != 0 || chunkLength > stream.Length - stream.Position)
                {
                    throw Invalid(path, "Invalid or unsupported GLB binary chunk.");
                }

                hasBinaryChunk = true;
                stream.Seek(chunkLength, SeekOrigin.Current);
            }

            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"'{path}': Invalid glTF JSON.", exception);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException($"'{path}': Truncated GLB chunk.", exception);
        }
    }

    private static JsonElement Required(JsonElement parent, string name, string path)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out JsonElement value))
        {
            throw Invalid(path, $"Required property '{name}' is missing.");
        }

        return value;
    }

    private static JsonElement Array(JsonElement parent, string name, string path)
    {
        JsonElement value = Required(parent, name, path);
        if (value.ValueKind != JsonValueKind.Array) throw Invalid(path, $"Property '{name}' must be an array.");
        return value;
    }

    private static int Integer(JsonElement value, string label, string path)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result)) throw Invalid(path, $"{label} must be an integer.");
        return result;
    }

    private static int Index(JsonElement value, int count, string label, string path)
    {
        int index = Integer(value, label, path);
        if ((uint)index >= (uint)count) throw Invalid(path, $"{label} index {index} is outside count {count}.");
        return index;
    }

    private static InvalidDataException Invalid(string path, string message) => new($"'{path}': {message}");
}

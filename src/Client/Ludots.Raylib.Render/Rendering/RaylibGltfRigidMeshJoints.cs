using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Ludots.Raylib.Render;

/// <summary>
/// raylib 5.5 的 glTF 装载只给带 JOINTS_0 的图元写骨骼属性；挂在关节节点下的刚性网格（武器、头盔、披风）
/// 顶点已按节点世界变换烘进绑定姿势，却没有骨骼编号，动画播放时会留在原地。
/// 这里按 raylib 相同的网格展开顺序（节点序 × 三角图元）给出每个网格应整体跟随的最近祖先关节。
/// raylib 只读 skins[0]，多蒙皮模型的其余蒙皮骨骼编号会错位，因此直接拒绝。
/// </summary>
public static class RaylibGltfRigidMeshJoints
{
    private const int TrianglesMode = 4;

    public readonly struct MeshSkinSource
    {
        public MeshSkinSource(string nodeName, int vertexCount, bool hasJoints, int rigidJointIndex)
        {
            NodeName = nodeName;
            VertexCount = vertexCount;
            HasJoints = hasJoints;
            RigidJointIndex = rigidJointIndex;
        }

        public string NodeName { get; }
        public int VertexCount { get; }
        public bool HasJoints { get; }
        public int RigidJointIndex { get; }
    }

    public static bool IsGltf(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".glb", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>无蒙皮的 glTF 返回空数组（刚性模型不需要骨骼属性）。</summary>
    public static MeshSkinSource[] Read(string path)
    {
        using JsonDocument document = JsonDocument.Parse(ReadJson(path));
        return Read(document.RootElement, path);
    }

    public static MeshSkinSource[] Read(JsonElement root, string sourceName)
    {
        if (!root.TryGetProperty("skins", out JsonElement skins) || skins.GetArrayLength() == 0)
        {
            return Array.Empty<MeshSkinSource>();
        }

        if (skins.GetArrayLength() > 1)
        {
            throw new InvalidDataException(
                $"glTF '{sourceName}' has {skins.GetArrayLength()} skins; raylib loads only skins[0], so meshes bound to other skins would read wrong bone ids.");
        }

        JsonElement nodes = root.GetProperty("nodes");
        int nodeCount = nodes.GetArrayLength();
        var jointIndexByNode = new Dictionary<int, int>();
        int jointIndex = 0;
        foreach (JsonElement joint in skins[0].GetProperty("joints").EnumerateArray())
        {
            jointIndexByNode[joint.GetInt32()] = jointIndex++;
        }

        int[] parentByNode = new int[nodeCount];
        Array.Fill(parentByNode, -1);
        for (int node = 0; node < nodeCount; node++)
        {
            if (nodes[node].TryGetProperty("children", out JsonElement children))
            {
                foreach (JsonElement child in children.EnumerateArray())
                {
                    parentByNode[child.GetInt32()] = node;
                }
            }
        }

        JsonElement meshes = root.GetProperty("meshes");
        JsonElement accessors = root.GetProperty("accessors");
        var result = new List<MeshSkinSource>();
        for (int node = 0; node < nodeCount; node++)
        {
            JsonElement nodeElement = nodes[node];
            if (!nodeElement.TryGetProperty("mesh", out JsonElement meshRef))
            {
                continue;
            }

            string nodeName = nodeElement.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? $"node{node}" : $"node{node}";
            foreach (JsonElement primitive in meshes[meshRef.GetInt32()].GetProperty("primitives").EnumerateArray())
            {
                int mode = primitive.TryGetProperty("mode", out JsonElement modeElement) ? modeElement.GetInt32() : TrianglesMode;
                if (mode != TrianglesMode)
                {
                    continue;
                }

                JsonElement attributes = primitive.GetProperty("attributes");
                int vertexCount = accessors[attributes.GetProperty("POSITION").GetInt32()].GetProperty("count").GetInt32();
                bool hasJoints = attributes.TryGetProperty("JOINTS_0", out _) && attributes.TryGetProperty("WEIGHTS_0", out _);
                int rigidJoint = hasJoints ? -1 : FindNearestAncestorJoint(node, parentByNode, jointIndexByNode);
                if (!hasJoints && rigidJoint < 0)
                {
                    throw new InvalidDataException(
                        $"glTF '{sourceName}' node '{nodeName}' is a rigid mesh inside a skinned model but has no joint ancestor; it cannot follow the skeleton. Parent it under a joint or skin it.");
                }

                result.Add(new MeshSkinSource(nodeName, vertexCount, hasJoints, rigidJoint));
            }
        }

        return result.ToArray();
    }

    private static int FindNearestAncestorJoint(int node, int[] parentByNode, Dictionary<int, int> jointIndexByNode)
    {
        for (int ancestor = parentByNode[node]; ancestor >= 0; ancestor = parentByNode[ancestor])
        {
            if (jointIndexByNode.TryGetValue(ancestor, out int jointIndex))
            {
                return jointIndex;
            }
        }

        return -1;
    }

    private static string ReadJson(string path)
    {
        if (Path.GetExtension(path).Equals(".gltf", StringComparison.OrdinalIgnoreCase))
        {
            return File.ReadAllText(path);
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != 0x46546C67)
        {
            throw new InvalidDataException($"'{path}' is not a binary glTF (missing 'glTF' magic).");
        }

        int jsonLength = BitConverter.ToInt32(bytes, 12);
        uint chunkType = BitConverter.ToUInt32(bytes, 16);
        if (chunkType != 0x4E4F534A || 20 + jsonLength > bytes.Length)
        {
            throw new InvalidDataException($"'{path}' first glb chunk is not JSON.");
        }

        return Encoding.UTF8.GetString(bytes, 20, jsonLength);
    }
}

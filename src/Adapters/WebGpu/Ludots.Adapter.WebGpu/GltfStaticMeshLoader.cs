using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using Ludots.Client.WebGpu.Runtime;

namespace Ludots.Adapter.WebGpu;

internal static class GltfStaticMeshLoader
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinaryChunkType = 0x004E4942;
    private const int ComponentTypeUnsignedByte = 5121;
    private const int ComponentTypeUnsignedShort = 5123;
    private const int ComponentTypeUnsignedInt = 5125;
    private const int ComponentTypeFloat = 5126;

    public static WebGpuMeshFrame Load(Stream stream, int key)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        ReadOnlyMemory<byte> file = memory.ToArray();
        ReadOnlySpan<byte> bytes = file.Span;
        if (bytes.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != GlbMagic)
        {
            throw new InvalidDataException("The registered WebGPU model is not a GLB file.");
        }

        int declaredLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8, 4)));
        if (declaredLength != bytes.Length)
        {
            throw new InvalidDataException($"GLB length mismatch: header={declaredLength}, file={bytes.Length}.");
        }

        int jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(12, 4)));
        uint jsonType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(16, 4));
        if (jsonType != JsonChunkType || jsonLength <= 0 || 20 + jsonLength + 8 > bytes.Length)
        {
            throw new InvalidDataException("GLB JSON chunk is missing or invalid.");
        }

        int binaryHeader = 20 + jsonLength;
        int binaryLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(binaryHeader, 4)));
        uint binaryType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(binaryHeader + 4, 4));
        if (binaryType != BinaryChunkType || binaryLength < 0 || binaryHeader + 8 + binaryLength > bytes.Length)
        {
            throw new InvalidDataException("GLB binary chunk is missing or invalid.");
        }

        using JsonDocument document = JsonDocument.Parse(file.Slice(20, jsonLength));
        JsonElement root = document.RootElement;
        JsonElement accessors = root.GetProperty("accessors");
        JsonElement bufferViews = root.GetProperty("bufferViews");
        JsonElement meshes = root.GetProperty("meshes");
        JsonElement nodes = root.GetProperty("nodes");
        JsonElement materials = root.TryGetProperty("materials", out JsonElement materialArray)
            ? materialArray
            : default;
        ReadOnlyMemory<byte> binary = file.Slice(binaryHeader + 8, binaryLength);

        var vertices = new List<WebGpuWorldVertex>(8192);
        var indices = new List<uint>(16384);
        JsonElement scene = ResolveScene(root);
        foreach (JsonElement nodeIndexElement in scene.GetProperty("nodes").EnumerateArray())
        {
            VisitNode(
                nodeIndexElement.GetInt32(),
                Matrix4x4.Identity,
                nodes,
                meshes,
                accessors,
                bufferViews,
                materials,
                binary,
                vertices,
                indices);
        }

        if (vertices.Count == 0 || indices.Count == 0)
        {
            throw new InvalidDataException("The registered GLB scene contains no indexed triangle mesh data.");
        }

        return new WebGpuMeshFrame(key, revision: 1, vertices.ToArray(), indices.ToArray());
    }

    private static JsonElement ResolveScene(JsonElement root)
    {
        JsonElement scenes = root.GetProperty("scenes");
        int sceneIndex = root.TryGetProperty("scene", out JsonElement value) ? value.GetInt32() : 0;
        return scenes[sceneIndex];
    }

    private static void VisitNode(
        int nodeIndex,
        Matrix4x4 parentTransform,
        JsonElement nodes,
        JsonElement meshes,
        JsonElement accessors,
        JsonElement bufferViews,
        JsonElement materials,
        ReadOnlyMemory<byte> binary,
        List<WebGpuWorldVertex> vertices,
        List<uint> indices)
    {
        JsonElement node = nodes[nodeIndex];
        Matrix4x4 transform = ReadNodeTransform(node) * parentTransform;
        if (node.TryGetProperty("mesh", out JsonElement meshIndex))
        {
            AppendMesh(
                meshes[meshIndex.GetInt32()],
                transform,
                accessors,
                bufferViews,
                materials,
                binary,
                vertices,
                indices);
        }

        if (!node.TryGetProperty("children", out JsonElement children))
        {
            return;
        }

        foreach (JsonElement child in children.EnumerateArray())
        {
            VisitNode(
                child.GetInt32(),
                transform,
                nodes,
                meshes,
                accessors,
                bufferViews,
                materials,
                binary,
                vertices,
                indices);
        }
    }

    private static void AppendMesh(
        JsonElement mesh,
        Matrix4x4 transform,
        JsonElement accessors,
        JsonElement bufferViews,
        JsonElement materials,
        ReadOnlyMemory<byte> binary,
        List<WebGpuWorldVertex> vertices,
        List<uint> indices)
    {
        foreach (JsonElement primitive in mesh.GetProperty("primitives").EnumerateArray())
        {
            int mode = primitive.TryGetProperty("mode", out JsonElement modeElement) ? modeElement.GetInt32() : 4;
            if (mode != 4 || !primitive.TryGetProperty("indices", out JsonElement indexAccessorElement))
            {
                continue;
            }

            JsonElement attributes = primitive.GetProperty("attributes");
            int positionAccessorIndex = attributes.GetProperty("POSITION").GetInt32();
            int normalAccessorIndex = attributes.TryGetProperty("NORMAL", out JsonElement normalElement)
                ? normalElement.GetInt32()
                : -1;
            AccessorView positions = ResolveAccessor(positionAccessorIndex, accessors, bufferViews, binary);
            AccessorView normals = normalAccessorIndex >= 0
                ? ResolveAccessor(normalAccessorIndex, accessors, bufferViews, binary)
                : default;
            AccessorView sourceIndices = ResolveAccessor(indexAccessorElement.GetInt32(), accessors, bufferViews, binary);
            if (positions.ComponentType != ComponentTypeFloat || positions.ComponentCount != 3)
            {
                throw new InvalidDataException("GLB POSITION must be a float VEC3 accessor.");
            }

            if (!normals.IsEmpty && (normals.ComponentType != ComponentTypeFloat || normals.ComponentCount != 3))
            {
                throw new InvalidDataException("GLB NORMAL must be a float VEC3 accessor.");
            }

            Vector4 materialColor = ReadMaterialColor(primitive, materials);
            int vertexBase = vertices.Count;
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 position = Vector3.Transform(positions.ReadVector3(i), transform);
                Vector3 normal = normals.IsEmpty
                    ? Vector3.UnitY
                    : Vector3.TransformNormal(normals.ReadVector3(i), transform);
                normal = normal.LengthSquared() > 0.000001f ? Vector3.Normalize(normal) : Vector3.UnitY;
                vertices.Add(new WebGpuWorldVertex
                {
                    Position = position,
                    Normal = normal,
                    Color = materialColor,
                });
            }

            for (int i = 0; i < sourceIndices.Count; i++)
            {
                indices.Add(checked((uint)(vertexBase + sourceIndices.ReadIndex(i))));
            }
        }
    }

    private static AccessorView ResolveAccessor(
        int accessorIndex,
        JsonElement accessors,
        JsonElement bufferViews,
        ReadOnlyMemory<byte> binary)
    {
        JsonElement accessor = accessors[accessorIndex];
        if (!accessor.TryGetProperty("bufferView", out JsonElement bufferViewElement))
        {
            throw new InvalidDataException("Sparse or buffer-less GLB accessors are not supported by the WebGPU host.");
        }

        JsonElement bufferView = bufferViews[bufferViewElement.GetInt32()];
        if (bufferView.TryGetProperty("buffer", out JsonElement bufferElement) && bufferElement.GetInt32() != 0)
        {
            throw new InvalidDataException("Only the embedded GLB buffer is supported by the WebGPU host.");
        }

        int componentType = accessor.GetProperty("componentType").GetInt32();
        int componentCount = ResolveComponentCount(accessor.GetProperty("type").GetString());
        int componentSize = ResolveComponentSize(componentType);
        int elementSize = checked(componentCount * componentSize);
        int stride = bufferView.TryGetProperty("byteStride", out JsonElement strideElement)
            ? strideElement.GetInt32()
            : elementSize;
        int offset = (bufferView.TryGetProperty("byteOffset", out JsonElement viewOffset) ? viewOffset.GetInt32() : 0) +
            (accessor.TryGetProperty("byteOffset", out JsonElement accessorOffset) ? accessorOffset.GetInt32() : 0);
        int count = accessor.GetProperty("count").GetInt32();
        if (count < 0 || stride < elementSize || offset < 0 ||
            (count > 0 && checked(offset + ((count - 1) * stride) + elementSize) > binary.Length))
        {
            throw new InvalidDataException("GLB accessor exceeds its embedded binary payload.");
        }

        return new AccessorView(binary, offset, stride, count, componentType, componentCount);
    }

    private static Matrix4x4 ReadNodeTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out JsonElement matrixElement))
        {
            Span<float> values = stackalloc float[16];
            int index = 0;
            foreach (JsonElement value in matrixElement.EnumerateArray())
            {
                values[index++] = value.GetSingle();
            }

            if (index != 16)
            {
                throw new InvalidDataException("GLB node matrix must contain 16 values.");
            }

            return new Matrix4x4(
                values[0], values[1], values[2], values[3],
                values[4], values[5], values[6], values[7],
                values[8], values[9], values[10], values[11],
                values[12], values[13], values[14], values[15]);
        }

        Vector3 scale = ReadVector3(node, "scale", Vector3.One);
        Quaternion rotation = ReadQuaternion(node, "rotation", Quaternion.Identity);
        Vector3 translation = ReadVector3(node, "translation", Vector3.Zero);
        return Matrix4x4.CreateScale(scale) *
            Matrix4x4.CreateFromQuaternion(rotation) *
            Matrix4x4.CreateTranslation(translation);
    }

    private static Vector3 ReadVector3(JsonElement owner, string name, Vector3 defaultValue)
    {
        if (!owner.TryGetProperty(name, out JsonElement array))
        {
            return defaultValue;
        }

        JsonElement.ArrayEnumerator values = array.EnumerateArray();
        values.MoveNext();
        float x = values.Current.GetSingle();
        values.MoveNext();
        float y = values.Current.GetSingle();
        values.MoveNext();
        return new Vector3(x, y, values.Current.GetSingle());
    }

    private static Quaternion ReadQuaternion(JsonElement owner, string name, Quaternion defaultValue)
    {
        if (!owner.TryGetProperty(name, out JsonElement array))
        {
            return defaultValue;
        }

        JsonElement.ArrayEnumerator values = array.EnumerateArray();
        values.MoveNext();
        float x = values.Current.GetSingle();
        values.MoveNext();
        float y = values.Current.GetSingle();
        values.MoveNext();
        float z = values.Current.GetSingle();
        values.MoveNext();
        return new Quaternion(x, y, z, values.Current.GetSingle());
    }

    private static Vector4 ReadMaterialColor(JsonElement primitive, JsonElement materials)
    {
        if (materials.ValueKind != JsonValueKind.Array ||
            !primitive.TryGetProperty("material", out JsonElement materialIndex))
        {
            return Vector4.One;
        }

        JsonElement material = materials[materialIndex.GetInt32()];
        if (!material.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr) ||
            !pbr.TryGetProperty("baseColorFactor", out JsonElement color))
        {
            return Vector4.One;
        }

        JsonElement.ArrayEnumerator values = color.EnumerateArray();
        values.MoveNext();
        float red = values.Current.GetSingle();
        values.MoveNext();
        float green = values.Current.GetSingle();
        values.MoveNext();
        float blue = values.Current.GetSingle();
        values.MoveNext();
        return new Vector4(red, green, blue, values.Current.GetSingle());
    }

    private static int ResolveComponentCount(string? type) => type switch
    {
        "SCALAR" => 1,
        "VEC2" => 2,
        "VEC3" => 3,
        "VEC4" => 4,
        _ => throw new InvalidDataException($"Unsupported GLB accessor type '{type}'."),
    };

    private static int ResolveComponentSize(int componentType) => componentType switch
    {
        ComponentTypeUnsignedByte => sizeof(byte),
        ComponentTypeUnsignedShort => sizeof(ushort),
        ComponentTypeUnsignedInt => sizeof(uint),
        ComponentTypeFloat => sizeof(float),
        _ => throw new InvalidDataException($"Unsupported GLB component type {componentType}."),
    };

    private readonly struct AccessorView
    {
        private readonly ReadOnlyMemory<byte> _binary;
        private readonly int _offset;
        private readonly int _stride;

        public AccessorView(
            ReadOnlyMemory<byte> binary,
            int offset,
            int stride,
            int count,
            int componentType,
            int componentCount)
        {
            _binary = binary;
            _offset = offset;
            _stride = stride;
            Count = count;
            ComponentType = componentType;
            ComponentCount = componentCount;
        }

        public int Count { get; }

        public int ComponentType { get; }

        public int ComponentCount { get; }

        public bool IsEmpty => _binary.IsEmpty;

        public Vector3 ReadVector3(int index)
        {
            int offset = checked(_offset + (index * _stride));
            ReadOnlySpan<byte> value = _binary.Span.Slice(offset, 12);
            return new Vector3(
                BinaryPrimitives.ReadSingleLittleEndian(value),
                BinaryPrimitives.ReadSingleLittleEndian(value.Slice(4)),
                BinaryPrimitives.ReadSingleLittleEndian(value.Slice(8)));
        }

        public int ReadIndex(int index)
        {
            int offset = checked(_offset + (index * _stride));
            ReadOnlySpan<byte> value = _binary.Span.Slice(offset);
            return ComponentType switch
            {
                ComponentTypeUnsignedByte => value[0],
                ComponentTypeUnsignedShort => BinaryPrimitives.ReadUInt16LittleEndian(value),
                ComponentTypeUnsignedInt => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(value)),
                _ => throw new InvalidDataException($"Unsupported GLB index component type {ComponentType}."),
            };
        }
    }
}

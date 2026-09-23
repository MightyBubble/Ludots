using System.Globalization;
using System.Numerics;
using System.Text;
using Ludots.Client.WebGpu.Runtime;

namespace Ludots.Adapter.WebGpu;

internal static class ObjStaticMeshLoader
{
    public static WebGpuMeshFrame Load(Stream stream, int key)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        var positions = new List<Vector3>(64);
        var normals = new List<Vector3>(64);
        var vertices = new List<WebGpuWorldVertex>(128);
        var indices = new List<uint>(192);
        string? materialLibrary = null;
        string? activeMaterial = null;
        bool sawFace = false;
        int lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            ParseLine(
                line.AsSpan(),
                lineNumber,
                positions,
                normals,
                vertices,
                indices,
                ref materialLibrary,
                ref activeMaterial,
                ref sawFace);
        }

        if (vertices.Count == 0 || indices.Count == 0)
        {
            throw new InvalidDataException("The registered OBJ contains no triangle faces.");
        }

        return new WebGpuMeshFrame(key, revision: 1, vertices.ToArray(), indices.ToArray());
    }

    private static void ParseLine(
        ReadOnlySpan<char> source,
        int lineNumber,
        List<Vector3> positions,
        List<Vector3> normals,
        List<WebGpuWorldVertex> vertices,
        List<uint> indices,
        ref string? materialLibrary,
        ref string? activeMaterial,
        ref bool sawFace)
    {
        ReadOnlySpan<char> line = TrimComment(source).Trim();
        if (line.IsEmpty)
        {
            return;
        }

        int separator = line.IndexOfAny(' ', '\t');
        ReadOnlySpan<char> directive = separator < 0 ? line : line[..separator];
        ReadOnlySpan<char> body = separator < 0 ? ReadOnlySpan<char>.Empty : line[(separator + 1)..].Trim();
        if (directive.SequenceEqual("v"))
        {
            positions.Add(ParseVector3(body, lineNumber, "vertex"));
            return;
        }

        if (directive.SequenceEqual("vn"))
        {
            Vector3 normal = ParseVector3(body, lineNumber, "normal");
            if (!IsFinite(normal) || normal.LengthSquared() <= 0.000001f)
            {
                throw Error(lineNumber, "normal must be finite and non-zero");
            }

            normals.Add(Vector3.Normalize(normal));
            return;
        }

        if (directive.SequenceEqual("f"))
        {
            if (materialLibrary != null && activeMaterial == null)
            {
                throw Error(lineNumber, "a material library is declared but no material is active");
            }

            ParseFace(body, lineNumber, positions, normals, vertices, indices);
            sawFace = true;
            return;
        }

        if (directive.SequenceEqual("mtllib"))
        {
            if (sawFace)
            {
                throw Error(lineNumber, "material library must be declared before faces");
            }

            string name = ParseSingleName(body, lineNumber, "material library");
            if (materialLibrary != null && !string.Equals(materialLibrary, name, StringComparison.Ordinal))
            {
                throw Error(lineNumber, "multiple material libraries are unsupported");
            }

            materialLibrary = name;
            return;
        }

        if (directive.SequenceEqual("usemtl"))
        {
            string name = ParseSingleName(body, lineNumber, "material");
            if (activeMaterial != null && !string.Equals(activeMaterial, name, StringComparison.Ordinal))
            {
                throw Error(lineNumber, "multiple OBJ materials are unsupported");
            }

            activeMaterial = name;
            return;
        }

        if (directive.SequenceEqual("o") ||
            directive.SequenceEqual("g") ||
            directive.SequenceEqual("s"))
        {
            return;
        }

        throw Error(lineNumber, $"unsupported directive '{directive.ToString()}'");
    }

    private static void ParseFace(
        ReadOnlySpan<char> body,
        int lineNumber,
        List<Vector3> positions,
        List<Vector3> normals,
        List<WebGpuWorldVertex> vertices,
        List<uint> indices)
    {
        Span<FaceVertex> polygon = stackalloc FaceVertex[32];
        int count = 0;
        while (!body.IsEmpty)
        {
            ReadOnlySpan<char> token = TakeToken(ref body);
            if (token.IsEmpty)
            {
                continue;
            }

            if (count >= polygon.Length)
            {
                throw Error(lineNumber, $"face exceeds the supported {polygon.Length}-vertex limit");
            }

            polygon[count++] = ParseFaceVertex(token, lineNumber, positions.Count, normals.Count);
        }

        if (count < 3)
        {
            throw Error(lineNumber, "face must contain at least three vertices");
        }

        for (int i = 1; i < count - 1; i++)
        {
            AppendTriangle(
                polygon[0],
                polygon[i],
                polygon[i + 1],
                lineNumber,
                positions,
                normals,
                vertices,
                indices);
        }
    }

    private static void AppendTriangle(
        FaceVertex a,
        FaceVertex b,
        FaceVertex c,
        int lineNumber,
        List<Vector3> positions,
        List<Vector3> normals,
        List<WebGpuWorldVertex> vertices,
        List<uint> indices)
    {
        Vector3 p0 = positions[a.PositionIndex];
        Vector3 p1 = positions[b.PositionIndex];
        Vector3 p2 = positions[c.PositionIndex];
        Vector3 faceNormal = Vector3.Cross(p1 - p0, p2 - p0);
        if (!IsFinite(faceNormal) || faceNormal.LengthSquared() <= 0.000001f)
        {
            throw Error(lineNumber, "face contains a degenerate triangle");
        }

        faceNormal = Vector3.Normalize(faceNormal);
        uint vertexBase = checked((uint)vertices.Count);
        AppendVertex(a, p0, faceNormal, normals, vertices);
        AppendVertex(b, p1, faceNormal, normals, vertices);
        AppendVertex(c, p2, faceNormal, normals, vertices);
        indices.Add(vertexBase);
        indices.Add(vertexBase + 1u);
        indices.Add(vertexBase + 2u);
    }

    private static void AppendVertex(
        FaceVertex source,
        Vector3 position,
        Vector3 faceNormal,
        List<Vector3> normals,
        List<WebGpuWorldVertex> vertices)
    {
        vertices.Add(new WebGpuWorldVertex
        {
            Position = position,
            Normal = source.NormalIndex >= 0 ? normals[source.NormalIndex] : faceNormal,
            Color = Vector4.One,
        });
    }

    private static FaceVertex ParseFaceVertex(
        ReadOnlySpan<char> token,
        int lineNumber,
        int positionCount,
        int normalCount)
    {
        int firstSlash = token.IndexOf('/');
        ReadOnlySpan<char> positionToken = firstSlash < 0 ? token : token[..firstSlash];
        int positionIndex = ResolveIndex(positionToken, positionCount, lineNumber, "position");
        if (firstSlash < 0)
        {
            return new FaceVertex(positionIndex, -1);
        }

        ReadOnlySpan<char> remainder = token[(firstSlash + 1)..];
        int secondSlash = remainder.IndexOf('/');
        if (secondSlash < 0)
        {
            ValidateOptionalTextureIndex(remainder, lineNumber);
            return new FaceVertex(positionIndex, -1);
        }

        ValidateOptionalTextureIndex(remainder[..secondSlash], lineNumber);
        ReadOnlySpan<char> normalToken = remainder[(secondSlash + 1)..];
        int normalIndex = normalToken.IsEmpty
            ? -1
            : ResolveIndex(normalToken, normalCount, lineNumber, "normal");
        return new FaceVertex(positionIndex, normalIndex);
    }

    private static void ValidateOptionalTextureIndex(ReadOnlySpan<char> token, int lineNumber)
    {
        if (!token.IsEmpty && !int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            throw Error(lineNumber, $"invalid texture coordinate index '{token.ToString()}'");
        }
    }

    private static int ResolveIndex(
        ReadOnlySpan<char> token,
        int count,
        int lineNumber,
        string valueKind)
    {
        if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sourceIndex) ||
            sourceIndex == 0)
        {
            throw Error(lineNumber, $"invalid {valueKind} index '{token.ToString()}'");
        }

        int index = sourceIndex > 0 ? sourceIndex - 1 : count + sourceIndex;
        if ((uint)index >= (uint)count)
        {
            throw Error(lineNumber, $"{valueKind} index {sourceIndex} is outside the declared range");
        }

        return index;
    }

    private static Vector3 ParseVector3(ReadOnlySpan<char> body, int lineNumber, string valueKind)
    {
        float x = ParseFloat(TakeRequiredToken(ref body, lineNumber, valueKind), lineNumber, valueKind);
        float y = ParseFloat(TakeRequiredToken(ref body, lineNumber, valueKind), lineNumber, valueKind);
        float z = ParseFloat(TakeRequiredToken(ref body, lineNumber, valueKind), lineNumber, valueKind);
        if (!body.Trim().IsEmpty)
        {
            throw Error(lineNumber, $"{valueKind} must contain exactly three values");
        }

        Vector3 value = new(x, y, z);
        if (!IsFinite(value))
        {
            throw Error(lineNumber, $"{valueKind} must contain finite values");
        }

        return value;
    }

    private static float ParseFloat(ReadOnlySpan<char> token, int lineNumber, string valueKind)
    {
        if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            throw Error(lineNumber, $"invalid {valueKind} value '{token.ToString()}'");
        }

        return value;
    }

    private static string ParseSingleName(ReadOnlySpan<char> body, int lineNumber, string valueKind)
    {
        ReadOnlySpan<char> token = TakeRequiredToken(ref body, lineNumber, valueKind);
        if (!body.Trim().IsEmpty)
        {
            throw Error(lineNumber, $"{valueKind} must contain exactly one name");
        }

        return token.ToString();
    }

    private static ReadOnlySpan<char> TakeRequiredToken(
        ref ReadOnlySpan<char> source,
        int lineNumber,
        string valueKind)
    {
        ReadOnlySpan<char> token = TakeToken(ref source);
        if (token.IsEmpty)
        {
            throw Error(lineNumber, $"{valueKind} is missing a required value");
        }

        return token;
    }

    private static ReadOnlySpan<char> TakeToken(ref ReadOnlySpan<char> source)
    {
        source = source.TrimStart();
        if (source.IsEmpty)
        {
            return ReadOnlySpan<char>.Empty;
        }

        int separator = source.IndexOfAny(' ', '\t');
        if (separator < 0)
        {
            ReadOnlySpan<char> token = source;
            source = ReadOnlySpan<char>.Empty;
            return token;
        }

        ReadOnlySpan<char> result = source[..separator];
        source = source[(separator + 1)..];
        return result;
    }

    private static ReadOnlySpan<char> TrimComment(ReadOnlySpan<char> source)
    {
        int comment = source.IndexOf('#');
        return comment < 0 ? source : source[..comment];
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static InvalidDataException Error(int lineNumber, string message) =>
        new($"OBJ line {lineNumber}: {message}.");

    private readonly record struct FaceVertex(int PositionIndex, int NormalIndex);
}

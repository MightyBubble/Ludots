using System.Numerics;
using Ludots.Client.WebGpu.Runtime;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Adapter.WebGpu;

internal sealed class GroundOverlayMeshCompiler
{
    private const int MeshKey = -1;
    private const int ArcSegments = 32;
    private const int MaxVerticesPerItem = 384;
    private const int MaxIndicesPerItem = 576;
    private const float SurfaceOffset = 0.025f;
    private WebGpuWorldVertex[] _vertices = Array.Empty<WebGpuWorldVertex>();
    private uint[] _indices = Array.Empty<uint>();
    private int _vertexCount;
    private int _indexCount;
    private int _revision;

    public WebGpuMeshFrame Compile(ReadOnlySpan<GroundOverlayItem> items)
    {
        EnsureCapacity(
            checked(items.Length * MaxVerticesPerItem),
            checked(items.Length * MaxIndicesPerItem));
        _vertexCount = 0;
        _indexCount = 0;

        for (int i = 0; i < items.Length; i++)
        {
            ref readonly GroundOverlayItem item = ref items[i];
            Validate(in item, i);
            switch (item.Shape)
            {
                case GroundOverlayShape.Circle:
                    AppendCircle(in item);
                    break;
                case GroundOverlayShape.Cone:
                    AppendCone(in item);
                    break;
                case GroundOverlayShape.Line:
                    AppendLine(in item);
                    break;
                case GroundOverlayShape.Ring:
                    AppendRing(in item);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"WebGPU ground overlay {i} uses unknown shape value {(byte)item.Shape}.");
            }
        }

        _revision = _revision == int.MaxValue ? 1 : _revision + 1;
        return new WebGpuMeshFrame(
            MeshKey,
            _revision,
            _vertices.AsMemory(0, _vertexCount),
            _indices.AsMemory(0, _indexCount));
    }

    private void AppendCircle(in GroundOverlayItem item)
    {
        if (item.Radius <= 0f)
        {
            return;
        }

        Vector3 center = Offset(item.Center);
        if (item.FillColor.W > 0f)
        {
            AppendFan(center, item.Radius, 0f, MathF.Tau, ArcSegments, item.FillColor);
        }

        AppendArcStroke(center, item.Radius, 0f, MathF.Tau, ArcSegments, item.BorderWidth, item.BorderColor);
    }

    private void AppendCone(in GroundOverlayItem item)
    {
        if (item.Radius <= 0f)
        {
            return;
        }

        Vector3 center = Offset(item.Center);
        float start = item.Rotation - item.Angle;
        float end = item.Rotation + item.Angle;
        if (item.FillColor.W > 0f)
        {
            AppendFan(center, item.Radius, start, end, ArcSegments, item.FillColor);
        }

        AppendArcStroke(center, item.Radius, start, end, ArcSegments, item.BorderWidth, item.BorderColor);
        if (item.BorderColor.W > 0f && item.BorderWidth > 0f)
        {
            AppendLineQuad(center, Polar(center, item.Radius, start), item.BorderWidth, item.BorderColor);
            AppendLineQuad(center, Polar(center, item.Radius, end), item.BorderWidth, item.BorderColor);
        }
    }

    private void AppendLine(in GroundOverlayItem item)
    {
        float length = item.Length > 0f ? item.Length : item.Radius;
        if (length <= 0f)
        {
            return;
        }

        Vector3 start = Offset(item.Center);
        Vector3 end = new(
            start.X + (MathF.Cos(item.Rotation) * length),
            start.Y,
            start.Z + (MathF.Sin(item.Rotation) * length));
        float width = MathF.Max(0.001f, item.Width);
        Vector3 side = new(-MathF.Sin(item.Rotation), 0f, MathF.Cos(item.Rotation));
        Vector3 half = side * (width * 0.5f);
        Vector3 a = start - half;
        Vector3 b = start + half;
        Vector3 c = end + half;
        Vector3 d = end - half;
        if (item.FillColor.W > 0f)
        {
            AppendQuad(a, b, c, d, item.FillColor);
        }

        if (item.BorderColor.W > 0f && item.BorderWidth > 0f)
        {
            AppendLineQuad(a, b, item.BorderWidth, item.BorderColor);
            AppendLineQuad(b, c, item.BorderWidth, item.BorderColor);
            AppendLineQuad(c, d, item.BorderWidth, item.BorderColor);
            AppendLineQuad(d, a, item.BorderWidth, item.BorderColor);
        }
    }

    private void AppendRing(in GroundOverlayItem item)
    {
        float outer = item.Radius;
        float inner = Math.Clamp(item.InnerRadius, 0f, outer);
        if (outer <= 0f || outer <= inner)
        {
            return;
        }

        Vector3 center = Offset(item.Center);
        if (item.FillColor.W > 0f)
        {
            float step = MathF.Tau / ArcSegments;
            for (int segment = 0; segment < ArcSegments; segment++)
            {
                float a0 = segment * step;
                float a1 = (segment + 1) * step;
                AppendQuad(
                    Polar(center, inner, a0),
                    Polar(center, outer, a0),
                    Polar(center, outer, a1),
                    Polar(center, inner, a1),
                    item.FillColor);
            }
        }

        AppendArcStroke(center, outer, 0f, MathF.Tau, ArcSegments, item.BorderWidth, item.BorderColor);
        if (inner > 0f)
        {
            AppendArcStroke(center, inner, 0f, MathF.Tau, ArcSegments, item.BorderWidth, item.BorderColor);
        }
    }

    private void AppendFan(
        Vector3 center,
        float radius,
        float start,
        float end,
        int segments,
        Vector4 color)
    {
        for (int segment = 0; segment < segments; segment++)
        {
            float a0 = start + ((end - start) * segment / segments);
            float a1 = start + ((end - start) * (segment + 1) / segments);
            AppendTriangle(center, Polar(center, radius, a0), Polar(center, radius, a1), color);
        }
    }

    private void AppendArcStroke(
        Vector3 center,
        float radius,
        float start,
        float end,
        int segments,
        float width,
        Vector4 color)
    {
        if (color.W <= 0f || width <= 0f)
        {
            return;
        }

        for (int segment = 0; segment < segments; segment++)
        {
            float a0 = start + ((end - start) * segment / segments);
            float a1 = start + ((end - start) * (segment + 1) / segments);
            AppendLineQuad(Polar(center, radius, a0), Polar(center, radius, a1), width, color);
        }
    }

    private void AppendLineQuad(Vector3 start, Vector3 end, float width, Vector4 color)
    {
        Vector3 direction = end - start;
        float lengthSquared = (direction.X * direction.X) + (direction.Z * direction.Z);
        if (lengthSquared <= 0.0000001f)
        {
            return;
        }

        float inverseLength = 1f / MathF.Sqrt(lengthSquared);
        Vector3 half = new(-direction.Z * inverseLength * width * 0.5f, 0f, direction.X * inverseLength * width * 0.5f);
        AppendQuad(start - half, start + half, end + half, end - half, color);
    }

    private void AppendTriangle(Vector3 a, Vector3 b, Vector3 c, Vector4 color)
    {
        uint vertex = checked((uint)_vertexCount);
        AppendVertex(a, color);
        AppendVertex(b, color);
        AppendVertex(c, color);
        _indices[_indexCount++] = vertex;
        _indices[_indexCount++] = vertex + 1u;
        _indices[_indexCount++] = vertex + 2u;
    }

    private void AppendQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 color)
    {
        uint vertex = checked((uint)_vertexCount);
        AppendVertex(a, color);
        AppendVertex(b, color);
        AppendVertex(c, color);
        AppendVertex(d, color);
        _indices[_indexCount++] = vertex;
        _indices[_indexCount++] = vertex + 1u;
        _indices[_indexCount++] = vertex + 2u;
        _indices[_indexCount++] = vertex;
        _indices[_indexCount++] = vertex + 2u;
        _indices[_indexCount++] = vertex + 3u;
    }

    private void AppendVertex(Vector3 position, Vector4 color)
    {
        _vertices[_vertexCount++] = new WebGpuWorldVertex
        {
            Position = position,
            Normal = Vector3.UnitY,
            Color = color,
        };
    }

    private void EnsureCapacity(int vertexCount, int indexCount)
    {
        if (_vertices.Length < vertexCount)
        {
            Array.Resize(ref _vertices, Math.Max(vertexCount, Math.Max(1024, _vertices.Length * 2)));
        }

        if (_indices.Length < indexCount)
        {
            Array.Resize(ref _indices, Math.Max(indexCount, Math.Max(2048, _indices.Length * 2)));
        }
    }

    private static Vector3 Offset(Vector3 position) =>
        new(position.X, position.Y + SurfaceOffset, position.Z);

    private static Vector3 Polar(Vector3 center, float radius, float radians) =>
        new(
            center.X + (MathF.Cos(radians) * radius),
            center.Y,
            center.Z + (MathF.Sin(radians) * radius));

    private static void Validate(in GroundOverlayItem item, int index)
    {
        if (!IsFinite(item.Center) ||
            !float.IsFinite(item.Radius) ||
            !float.IsFinite(item.InnerRadius) ||
            !float.IsFinite(item.Angle) ||
            !float.IsFinite(item.Rotation) ||
            !float.IsFinite(item.Length) ||
            !float.IsFinite(item.Width) ||
            !float.IsFinite(item.BorderWidth) ||
            !IsFinite(item.FillColor) ||
            !IsFinite(item.BorderColor))
        {
            throw new InvalidOperationException($"WebGPU ground overlay {index} contains a non-finite value.");
        }

        if (item.Radius < 0f || item.InnerRadius < 0f || item.Length < 0f || item.Width < 0f || item.BorderWidth < 0f)
        {
            throw new InvalidOperationException($"WebGPU ground overlay {index} contains a negative size.");
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
}

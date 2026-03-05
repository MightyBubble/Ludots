using System;
using System.Numerics;
using Godot;
using Ludots.Core.Presentation.DebugDraw;

namespace Ludots.Client.Godot.Rendering
{
    /// <summary>
    /// Renders DebugDrawCommandBuffer using Godot ImmediateMesh or debug lines.
    /// Draws Lines, Circles, Boxes on a horizontal plane (Y = PlaneY).
    /// </summary>
    public sealed class GodotDebugDrawRenderer
    {
        public float PlaneY { get; set; } = 0.35f;
        public int CircleSegments { get; set; } = 32;

        private MeshInstance3D? _lineMeshInstance;
        private ImmediateMesh? _immediateMesh;
        private StandardMaterial3D? _material;

        public void Draw(DebugDrawCommandBuffer buffer, Node3D parent)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (parent == null) throw new ArgumentNullException(nameof(parent));

            EnsureMesh(parent);

            _immediateMesh!.ClearSurfaces();
            _immediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

            int segments = Math.Max(8, CircleSegments);

            for (int i = 0; i < buffer.Lines.Count; i++)
            {
                var line = buffer.Lines[i];
                var col = ToColor(line.Color);
                _immediateMesh.SurfaceSetColor(col);
                _immediateMesh.SurfaceAddVertex(ToV3(line.A));
                _immediateMesh.SurfaceAddVertex(ToV3(line.B));
            }

            for (int i = 0; i < buffer.Circles.Count; i++)
            {
                var circle = buffer.Circles[i];
                var col = ToColor(circle.Color);
                _immediateMesh.SurfaceSetColor(col);
                var c = ToV3(circle.Center);
                for (int s = 0; s < segments; s++)
                {
                    float a0 = (float)s / segments * MathF.Tau;
                    float a1 = (float)(s + 1) / segments * MathF.Tau;
                    var p0 = c + new global::Godot.Vector3(MathF.Cos(a0) * circle.Radius, 0, MathF.Sin(a0) * circle.Radius);
                    var p1 = c + new global::Godot.Vector3(MathF.Cos(a1) * circle.Radius, 0, MathF.Sin(a1) * circle.Radius);
                    _immediateMesh.SurfaceAddVertex(p0);
                    _immediateMesh.SurfaceAddVertex(p1);
                }
            }

            for (int i = 0; i < buffer.Boxes.Count; i++)
            {
                var box = buffer.Boxes[i];
                var col = ToColor(box.Color);
                _immediateMesh.SurfaceSetColor(col);
                var c = ToV3(box.Center);
                var hw = box.HalfWidth;
                var hh = box.HalfHeight;
                var p0 = c + new global::Godot.Vector3(-hw, 0, -hh);
                var p1 = c + new global::Godot.Vector3(hw, 0, -hh);
                var p2 = c + new global::Godot.Vector3(hw, 0, hh);
                var p3 = c + new global::Godot.Vector3(-hw, 0, hh);
                _immediateMesh.SurfaceAddVertex(p0);
                _immediateMesh.SurfaceAddVertex(p1);
                _immediateMesh.SurfaceAddVertex(p1);
                _immediateMesh.SurfaceAddVertex(p2);
                _immediateMesh.SurfaceAddVertex(p2);
                _immediateMesh.SurfaceAddVertex(p3);
                _immediateMesh.SurfaceAddVertex(p3);
                _immediateMesh.SurfaceAddVertex(p0);
            }

            _immediateMesh.SurfaceEnd();
        }

        private void EnsureMesh(Node3D parent)
        {
            if (_lineMeshInstance != null) return;

            _immediateMesh = new ImmediateMesh();
            _material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true
            };
            _lineMeshInstance = new MeshInstance3D
            {
                Mesh = _immediateMesh,
                MaterialOverride = _material
            };
            parent.AddChild(_lineMeshInstance);
        }

        private global::Godot.Vector3 ToV3(System.Numerics.Vector2 p) =>
            new global::Godot.Vector3(p.X, PlaneY, p.Y);

        private static global::Godot.Color ToColor(DebugDrawColor c) =>
            new global::Godot.Color(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
    }
}

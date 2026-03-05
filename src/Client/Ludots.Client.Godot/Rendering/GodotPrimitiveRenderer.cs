using System;
using System.Collections.Generic;
using System.Numerics;
using Godot;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Client.Godot.Rendering
{
    /// <summary>
    /// Renders PrimitiveDrawBuffer using Godot MeshInstance3D with BoxMesh/SphereMesh.
    /// Uses object pooling to avoid per-frame allocations.
    /// </summary>
    public sealed class GodotPrimitiveRenderer
    {
        private readonly Node3D _container;
        private readonly Stack<MeshInstance3D> _cubePool = new();
        private readonly Stack<MeshInstance3D> _spherePool = new();
        private readonly List<MeshInstance3D> _activeCubes = new(256);
        private readonly List<MeshInstance3D> _activeSpheres = new(256);
        private BoxMesh? _boxMesh;
        private SphereMesh? _sphereMesh;
        private StandardMaterial3D? _sharedMaterial;

        public GodotPrimitiveRenderer(Node3D container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public void Draw(PrimitiveDrawBuffer draw, MeshAssetRegistry meshes)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            EnsureMeshes();
            ReturnAllToPool();

            var span = draw.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (!meshes.TryGetPrimitiveKind(item.MeshAssetId, out var kind)) continue;

                if (kind == PrimitiveMeshKind.Cube)
                {
                    var inst = GetOrCreateCube();
                    ApplyTransform(inst, item.Position, item.Scale);
                    ApplyColor(inst, item.Color);
                }
                else if (kind == PrimitiveMeshKind.Sphere)
                {
                    var inst = GetOrCreateSphere();
                    float r = Math.Max(item.Scale.X, Math.Max(item.Scale.Y, item.Scale.Z)) * 0.5f;
                    ApplyTransform(inst, item.Position, new System.Numerics.Vector3(r * 2, r * 2, r * 2));
                    ApplyColor(inst, item.Color);
                }
            }
        }

        private void EnsureMeshes()
        {
            if (_boxMesh != null) return;

            _boxMesh = new BoxMesh { Size = global::Godot.Vector3.One };
            _sphereMesh = new SphereMesh { Radius = 0.5f, Height = 1f };
            _sharedMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        }

        private MeshInstance3D GetOrCreateCube()
        {
            MeshInstance3D? inst;
            if (_cubePool.Count > 0)
            {
                inst = _cubePool.Pop();
            }
            else
            {
                inst = new MeshInstance3D { Mesh = _boxMesh };
                var mat = _sharedMaterial!.Duplicate() as StandardMaterial3D;
                inst.MaterialOverride = mat;
                _container.AddChild(inst);
            }

            inst.Visible = true;
            inst.Mesh = _boxMesh;
            _activeCubes.Add(inst);
            return inst;
        }

        private MeshInstance3D GetOrCreateSphere()
        {
            MeshInstance3D? inst;
            if (_spherePool.Count > 0)
            {
                inst = _spherePool.Pop();
            }
            else
            {
                inst = new MeshInstance3D { Mesh = _sphereMesh };
                var mat = _sharedMaterial!.Duplicate() as StandardMaterial3D;
                inst.MaterialOverride = mat;
                _container.AddChild(inst);
            }

            inst.Visible = true;
            inst.Mesh = _sphereMesh;
            _activeSpheres.Add(inst);
            return inst;
        }

        private static void ApplyTransform(MeshInstance3D inst, System.Numerics.Vector3 pos, System.Numerics.Vector3 scale)
        {
            inst.Position = new global::Godot.Vector3(pos.X, pos.Y, pos.Z);
            inst.Scale = new global::Godot.Vector3(scale.X, scale.Y, scale.Z);
        }

        private static void ApplyColor(MeshInstance3D inst, System.Numerics.Vector4 color)
        {
            if (inst.MaterialOverride is StandardMaterial3D mat)
            {
                mat.AlbedoColor = new Color(
                    Clamp01(color.X),
                    Clamp01(color.Y),
                    Clamp01(color.Z),
                    Clamp01(color.W));
            }
        }

        private static float Clamp01(float v)
        {
            if (v <= 0f) return 0f;
            if (v >= 1f) return 1f;
            return v;
        }

        private void ReturnAllToPool()
        {
            foreach (var inst in _activeCubes)
            {
                inst.Visible = false;
                _cubePool.Push(inst);
            }
            _activeCubes.Clear();

            foreach (var inst in _activeSpheres)
            {
                inst.Visible = false;
                _spherePool.Push(inst);
            }
            _activeSpheres.Clear();
        }
    }
}

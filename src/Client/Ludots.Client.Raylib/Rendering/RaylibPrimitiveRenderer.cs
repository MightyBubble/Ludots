using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.AdapterSync;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    public enum RaylibPrimitiveRenderMode : byte
    {
        Immediate = 0,
        Instanced = 1
    }

    public sealed unsafe class RaylibPrimitiveRenderer : IDisposable
    {
        private readonly RaylibPrimitiveRenderMode _mode;
        private readonly IVirtualFileSystem? _vfs;
        private readonly string? _diagnosticPath;
        private const int MaxPrefabDepth = 6;

        private bool _initialized;
        private Mesh _cubeMesh;
        private Mesh _sphereMesh;
        private Shader _shader;
        private Material _material;
        private int _locColDiffuse;
        private int _locTint;

        private readonly List<Batch> _cubeBatches = new List<Batch>(16);
        private readonly List<Batch> _sphereBatches = new List<Batch>(16);
        private readonly StaticMeshAdapterSyncPlanner _persistentStaticLaneSync = new StaticMeshAdapterSyncPlanner();

        private readonly Dictionary<int, CachedModel> _modelCache = new Dictionary<int, CachedModel>();
        private readonly Dictionary<int, CachedTexture> _textureCache = new Dictionary<int, CachedTexture>();
        private readonly HashSet<int> _loggedTextureDiagnostics = new HashSet<int>();
        private readonly HashSet<int> _loggedBillboardDrawDiagnostics = new HashSet<int>();

        public int LastInstancedInstances { get; private set; }
        public int LastInstancedBatches { get; private set; }
        public int LastPersistentCreates { get; private set; }
        public int LastPersistentUpdates { get; private set; }
        public int LastPersistentRemoves { get; private set; }

        public RaylibPrimitiveRenderer(
            RaylibPrimitiveRenderMode mode = RaylibPrimitiveRenderMode.Immediate,
            IVirtualFileSystem? vfs = null)
        {
            _mode = mode;
            _vfs = vfs;
            _diagnosticPath = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_DIAGNOSTIC_PATH");
        }

        public void Draw(PrimitiveDrawBuffer draw, Camera3D camera, MeshAssetRegistry meshes, float scaleMul = 1f)
        {
            Draw(draw, camera, snapshot: null, skinnedBatch: null, meshes, scaleMul);
        }

        public void Draw(PrimitiveDrawBuffer draw, Camera3D camera, PrimitiveDrawBuffer? snapshot, MeshAssetRegistry meshes, float scaleMul = 1f)
        {
            Draw(draw, camera, snapshot, skinnedBatch: null, meshes, scaleMul);
        }

        public void Draw(
            PrimitiveDrawBuffer draw,
            Camera3D camera,
            PrimitiveDrawBuffer? snapshot,
            SkinnedVisualBatchBuffer? skinnedBatch,
            MeshAssetRegistry meshes,
            float scaleMul = 1f)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            LastInstancedInstances = 0;
            LastInstancedBatches = 0;
            LastPersistentCreates = 0;
            LastPersistentUpdates = 0;
            LastPersistentRemoves = 0;

            var span = draw.GetSpan();
            bool usePersistentStaticLanes = snapshot != null;
            if (usePersistentStaticLanes)
            {
                _persistentStaticLaneSync.Sync(snapshot);
                LastPersistentCreates = _persistentStaticLaneSync.LastCreateCount;
                LastPersistentUpdates = _persistentStaticLaneSync.LastUpdateCount;
                LastPersistentRemoves = _persistentStaticLaneSync.LastRemoveCount;
                DrawPersistentStaticLanes(camera, meshes, scaleMul);
                if (skinnedBatch != null)
                {
                    DrawSkinnedBatch(skinnedBatch, camera, meshes, scaleMul);
                }

                DrawImmediateWithDescriptors(span, camera, meshes, scaleMul, persistentStaticLanesActive: true, skinnedBatchActive: skinnedBatch != null);
                return;
            }

            if (_mode == RaylibPrimitiveRenderMode.Instanced)
            {
                DrawHybridInstanced(span, camera, meshes, scaleMul);
                return;
            }

            DrawImmediateWithDescriptors(span, camera, meshes, scaleMul, persistentStaticLanesActive: false, skinnedBatchActive: false);
        }

        private void DrawPersistentStaticLanes(Camera3D camera, MeshAssetRegistry meshes, float scaleMul)
        {
            foreach (var pair in _persistentStaticLaneSync.ActiveBindings)
            {
                var binding = pair.Value;
                var item = binding.Item;
                if (!binding.IsVisible)
                {
                    continue;
                }

                DrawAssetRecursive(
                    item.MeshAssetId,
                    item.Position,
                    item.Scale * scaleMul,
                    item.Color,
                    camera,
                    meshes,
                    0);
            }
        }

        private void DrawImmediateWithDescriptors(
            ReadOnlySpan<PrimitiveDrawItem> span,
            Camera3D camera,
            MeshAssetRegistry meshes,
            float scaleMul,
            bool persistentStaticLanesActive,
            bool skinnedBatchActive)
        {
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (skinnedBatchActive && item.RenderPath.IsSkinnedLane())
                {
                    continue;
                }

                if (ShouldSkipImmediateDraw(item, persistentStaticLanesActive))
                {
                    continue;
                }

                if (TryDrawPrototypeSkinned(item, meshes, scaleMul))
                {
                    continue;
                }

                DrawAssetRecursive(
                    item.MeshAssetId, item.Position,
                    item.Scale * scaleMul, item.Color,
                    camera,
                    meshes, 0);
            }
        }

        internal bool ShouldSkipImmediateDraw(in PrimitiveDrawItem item, bool persistentStaticLanesActive)
        {
            if (!persistentStaticLanesActive ||
                item.StableId <= 0 ||
                !StaticMeshLaneKey.Supports(item.RenderPath))
            {
                return false;
            }

            return _persistentStaticLaneSync.TryGetBinding(item.StableId, out _);
        }

        private void DrawHybridInstanced(ReadOnlySpan<PrimitiveDrawItem> span, Camera3D camera, MeshAssetRegistry meshes, float scaleMul)
        {
            EnsureInitialized();

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (TryDrawPrototypeSkinned(item, meshes, scaleMul))
                {
                    continue;
                }

                SubmitAssetRecursive(
                    item.MeshAssetId,
                    item.Position,
                    item.Scale * scaleMul,
                    item.Color,
                    camera,
                    meshes,
                    depth: 0);
            }

            FlushInstancedBatches();
        }

        private void SubmitAssetRecursive(int meshAssetId, Vector3 position, Vector3 scale, Vector4 color, Camera3D camera, MeshAssetRegistry meshes, int depth)
        {
            if (depth > MaxPrefabDepth) return;
            if (!meshes.TryGetDescriptor(meshAssetId, out var desc)) return;

            switch (desc.Type)
            {
                case MeshAssetType.Primitive:
                    SubmitPrimitive(desc.PrimitiveKind, position, scale, color);
                    break;

                case MeshAssetType.Model:
                    DrawModel(meshAssetId, desc, position, scale, color);
                    break;

                case MeshAssetType.Billboard:
                    DrawBillboard(meshAssetId, desc, position, scale, color, camera);
                    break;

                case MeshAssetType.Prefab:
                    if (desc.PrefabParts != null)
                    {
                        for (int p = 0; p < desc.PrefabParts.Length; p++)
                        {
                            ref var part = ref desc.PrefabParts[p];
                            var childPos = position + part.LocalPosition * scale;
                            var childScale = scale * part.LocalScale;
                            var childColor = new Vector4(
                                color.X * part.ColorTint.X,
                                color.Y * part.ColorTint.Y,
                                color.Z * part.ColorTint.Z,
                                color.W * part.ColorTint.W);
                            SubmitAssetRecursive(part.MeshAssetId, childPos, childScale, childColor, camera, meshes, depth + 1);
                        }
                    }
                    break;
            }
        }

        private void SubmitPrimitive(PrimitiveMeshKind kind, Vector3 position, Vector3 scale, Vector4 color)
        {
            uint key = PackRgba(color);
            var matrix = RaylibMatrix.FromScaleTranslation(position.X, position.Y, position.Z, scale.X, scale.Y, scale.Z);

            if (kind == PrimitiveMeshKind.Cube)
            {
                AddInstance(_cubeBatches, key, matrix);
                return;
            }

            if (kind == PrimitiveMeshKind.Sphere)
            {
                AddInstance(_sphereBatches, key, matrix);
                return;
            }

            DrawPrimitive(kind, position, scale, color);
        }

        private bool TryDrawPrototypeSkinned(in PrimitiveDrawItem item, MeshAssetRegistry meshes, float scaleMul)
        {
            if (!item.RenderPath.IsSkinnedLane() ||
                !item.AnimationOverlay.HasAnyClip ||
                !meshes.TryGetDescriptor(item.MeshAssetId, out var descriptor) ||
                descriptor.Type != MeshAssetType.Primitive)
            {
                return false;
            }

            Vector3 scale = item.Scale * scaleMul;
            float baseYaw = ExtractYawRad(item.Rotation);

            switch (descriptor.PrimitiveKind)
            {
                case PrimitiveMeshKind.Cube:
                    DrawTankPrototype(item.Position, scale, item.Color, baseYaw, item.AnimationOverlay);
                    return true;

                case PrimitiveMeshKind.Sphere:
                    DrawHumanoidPrototype(item.Position, scale, item.Color, baseYaw, item.AnimationOverlay);
                    return true;

                default:
                    return false;
            }
        }

        private void DrawSkinnedBatch(SkinnedVisualBatchBuffer skinnedBatch, Camera3D camera, MeshAssetRegistry meshes, float scaleMul)
        {
            var span = skinnedBatch.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (item.Visibility != VisualVisibility.Visible)
                {
                    continue;
                }

                if (TryDrawPrototypeSkinned(item, meshes, scaleMul))
                {
                    continue;
                }

                DrawAssetRecursive(
                    item.MeshAssetId,
                    item.Position,
                    item.Scale * scaleMul,
                    item.Color,
                    camera,
                    meshes,
                    0);
            }
        }

        private bool TryDrawPrototypeSkinned(in SkinnedVisualBatchItem item, MeshAssetRegistry meshes, float scaleMul)
        {
            if (!item.RenderPath.IsSkinnedLane() ||
                !item.AnimationOverlay.HasAnyClip ||
                !meshes.TryGetDescriptor(item.MeshAssetId, out var descriptor) ||
                descriptor.Type != MeshAssetType.Primitive)
            {
                return false;
            }

            Vector3 scale = item.Scale * scaleMul;
            float baseYaw = ExtractYawRad(item.Rotation);

            switch (descriptor.PrimitiveKind)
            {
                case PrimitiveMeshKind.Cube:
                    DrawTankPrototype(item.Position, scale, item.Color, baseYaw, item.AnimationOverlay);
                    return true;

                case PrimitiveMeshKind.Sphere:
                    DrawHumanoidPrototype(item.Position, scale, item.Color, baseYaw, item.AnimationOverlay);
                    return true;

                default:
                    return false;
            }
        }

        private void DrawAssetRecursive(int meshAssetId, Vector3 position, Vector3 scale, Vector4 color, Camera3D camera, MeshAssetRegistry meshes, int depth)
        {
            if (depth > MaxPrefabDepth) return;
            if (!meshes.TryGetDescriptor(meshAssetId, out var desc)) return;

            switch (desc.Type)
            {
                case MeshAssetType.Primitive:
                    DrawPrimitive(desc.PrimitiveKind, position, scale, color);
                    break;

                case MeshAssetType.Model:
                    DrawModel(meshAssetId, desc, position, scale, color);
                    break;

                case MeshAssetType.Billboard:
                    DrawBillboard(meshAssetId, desc, position, scale, color, camera);
                    break;

                case MeshAssetType.Prefab:
                    if (desc.PrefabParts != null)
                    {
                        for (int p = 0; p < desc.PrefabParts.Length; p++)
                        {
                            ref var part = ref desc.PrefabParts[p];
                            var childPos = position + part.LocalPosition * scale;
                            var childScale = scale * part.LocalScale;
                            var childColor = new Vector4(
                                color.X * part.ColorTint.X,
                                color.Y * part.ColorTint.Y,
                                color.Z * part.ColorTint.Z,
                                color.W * part.ColorTint.W);
                            DrawAssetRecursive(part.MeshAssetId, childPos, childScale, childColor, camera, meshes, depth + 1);
                        }
                    }
                    break;
            }
        }

        private void DrawPrimitive(PrimitiveMeshKind kind, Vector3 position, Vector3 scale, Vector4 color)
        {
            var c = ToRaylibColor(color);
            if (kind == PrimitiveMeshKind.Cube)
            {
                Rl.DrawCube(position, scale.X, scale.Y, scale.Z, c);
            }
            else if (kind == PrimitiveMeshKind.Sphere)
            {
                float r = MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z)) * 0.5f;
                Rl.DrawSphere(position, r, c);
            }
        }

        private void DrawTankPrototype(Vector3 position, Vector3 scale, Vector4 color, float baseYaw, in AnimationOverlayRequest overlay)
        {
            float unit = MathF.Max(0.12f, MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z)) * 0.45f);
            float locomotionPhase = ResolveClipTime01(overlay.BaseClip, AnimatorBuiltinClipId.LocomotionCycle);
            float locomotionWeight = ResolveClipWeight01(overlay.BaseClip, AnimatorBuiltinClipId.LocomotionCycle);
            float aimYaw = ResolveClipScalar0(overlay.LayerClip, AnimatorBuiltinClipId.AimYawOffset) * ResolveClipWeight01(overlay.LayerClip, AnimatorBuiltinClipId.AimYawOffset);
            float recoilPulse = ResolvePulse(overlay.OverlayClip, AnimatorBuiltinClipId.RecoilPulse);
            float treadBob = MathF.Sin(locomotionPhase * MathF.Tau) * unit * (0.03f + locomotionWeight * 0.08f);
            float turretYaw = baseYaw + aimYaw;
            float recoil = recoilPulse * unit * 0.35f;

            Vector4 hullColor = MultiplyColor(color, 0.72f, 0.78f, 0.84f, 1f);
            Vector4 turretColor = MultiplyColor(color, 0.95f, 0.95f, 0.82f, 1f);
            Vector4 accentColor = recoilPulse > 0.01f
                ? new Vector4(1f, 0.45f, 0.2f, 1f)
                : new Vector4(0.95f, 0.9f, 0.4f, 1f);

            DrawOrientedCube(
                TransformLocal(position, baseYaw, new Vector3(0f, unit * 0.52f + treadBob, 0f)),
                new Vector3(unit * 2.2f, unit * 0.7f, unit * 3.0f),
                baseYaw,
                hullColor);

            DrawOrientedCube(
                TransformLocal(position, baseYaw, new Vector3(unit * 0.92f, unit * 0.26f + treadBob, 0f)),
                new Vector3(unit * 0.38f, unit * 0.25f, unit * 2.7f),
                baseYaw,
                MultiplyColor(hullColor, 0.8f, 0.8f, 0.8f, 1f));

            DrawOrientedCube(
                TransformLocal(position, baseYaw, new Vector3(-unit * 0.92f, unit * 0.26f + treadBob, 0f)),
                new Vector3(unit * 0.38f, unit * 0.25f, unit * 2.7f),
                baseYaw,
                MultiplyColor(hullColor, 0.8f, 0.8f, 0.8f, 1f));

            Vector3 turretCenter = TransformLocal(position, baseYaw, new Vector3(0f, unit * 1.0f, 0f));
            DrawOrientedCube(
                turretCenter,
                new Vector3(unit * 1.1f, unit * 0.42f, unit * 1.3f),
                turretYaw,
                turretColor);

            DrawOrientedCube(
                TransformLocal(turretCenter, turretYaw, new Vector3(0f, unit * 0.02f, unit * 1.15f - recoil)),
                new Vector3(unit * 0.18f, unit * 0.18f, unit * 2.25f),
                turretYaw,
                accentColor);

            DrawPrototypeSphere(
                TransformLocal(turretCenter, turretYaw, new Vector3(0f, unit * 0.18f, unit * 2.15f - recoil)),
                unit * (0.1f + recoilPulse * 0.1f),
                accentColor);
        }

        private void DrawHumanoidPrototype(Vector3 position, Vector3 scale, Vector4 color, float baseYaw, in AnimationOverlayRequest overlay)
        {
            float unit = MathF.Max(0.1f, MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z)) * 0.42f);
            float locomotionPhase = ResolveClipTime01(overlay.BaseClip, AnimatorBuiltinClipId.LocomotionCycle);
            float locomotionWeight = ResolveClipWeight01(overlay.BaseClip, AnimatorBuiltinClipId.LocomotionCycle);
            float lowerPhase = locomotionPhase * MathF.Tau;
            float stride = MathF.Sin(lowerPhase) * unit * (0.08f + locomotionWeight * 0.34f);
            float aimWeight = ResolveClipWeight01(overlay.LayerClip, AnimatorBuiltinClipId.AimYawOffset);
            float upperYaw = baseYaw + ResolveClipScalar0(overlay.LayerClip, AnimatorBuiltinClipId.AimYawOffset) * aimWeight;
            float recoilPulse = ResolvePulse(overlay.OverlayClip, AnimatorBuiltinClipId.RecoilPulse);
            float chestLift = recoilPulse * unit * 0.08f;

            Vector4 legColor = MultiplyColor(color, 0.72f, 0.85f, 1f, 1f);
            Vector4 torsoColor = LerpColor(
                MultiplyColor(color, 0.95f, 0.8f, 0.75f, 1f),
                new Vector4(1f, 0.45f, 0.25f, 1f),
                Math.Clamp(aimWeight * 0.6f, 0f, 1f));
            Vector4 weaponColor = recoilPulse > 0.01f
                ? new Vector4(1f, 0.5f, 0.25f, 1f)
                : new Vector4(0.9f, 0.9f, 0.95f, 1f);

            DrawOrientedCube(
                TransformLocal(position, baseYaw, new Vector3(0f, unit * 0.55f, 0f)),
                new Vector3(unit * 0.75f, unit * 0.55f, unit * 0.45f),
                baseYaw,
                legColor);

            DrawOrientedCube(
                TransformLocal(position, baseYaw, new Vector3(unit * 0.2f, unit * 0.18f, stride)),
                new Vector3(unit * 0.2f, unit * 0.78f, unit * 0.2f),
                baseYaw,
                legColor);

            DrawOrientedCube(
                TransformLocal(position, baseYaw, new Vector3(-unit * 0.2f, unit * 0.18f, -stride)),
                new Vector3(unit * 0.2f, unit * 0.78f, unit * 0.2f),
                baseYaw,
                legColor);

            Vector3 chestCenter = TransformLocal(position, upperYaw, new Vector3(0f, unit * 1.3f + chestLift, 0f));
            DrawOrientedCube(
                chestCenter,
                new Vector3(unit * 0.82f, unit * 0.92f, unit * 0.4f),
                upperYaw,
                torsoColor);

            DrawPrototypeSphere(
                TransformLocal(chestCenter, upperYaw, new Vector3(0f, unit * 0.82f, 0f)),
                unit * 0.28f,
                MultiplyColor(color, 1f, 0.92f, 0.86f, 1f));

            DrawOrientedCube(
                TransformLocal(chestCenter, upperYaw, new Vector3(-unit * 0.48f, unit * 0.05f, unit * 0.05f)),
                new Vector3(unit * 0.16f, unit * 0.75f, unit * 0.16f),
                upperYaw - aimWeight * 0.15f,
                torsoColor);

            DrawOrientedCube(
                TransformLocal(chestCenter, upperYaw, new Vector3(unit * 0.5f, unit * 0.02f, unit * (0.18f + aimWeight * 0.25f))),
                new Vector3(unit * 0.16f, unit * 0.7f, unit * 0.16f),
                upperYaw + aimWeight * 0.35f,
                torsoColor);

            Vector3 weaponCenter = TransformLocal(chestCenter, upperYaw, new Vector3(unit * 0.18f, -unit * 0.02f, unit * 0.7f));
            DrawOrientedCube(
                weaponCenter,
                new Vector3(unit * 0.14f, unit * 0.14f, unit * 0.95f),
                upperYaw,
                weaponColor);

            if (recoilPulse > 0.01f)
            {
                DrawPrototypeSphere(
                    TransformLocal(weaponCenter, upperYaw, new Vector3(0f, 0f, unit * 0.68f)),
                    unit * 0.14f,
                    new Vector4(1f, 0.62f, 0.2f, 1f));
            }
        }

        private static float ResolveClipTime01(in AnimatorBuiltinClipState clip, AnimatorBuiltinClipId expectedId)
        {
            return clip.ClipId == expectedId ? clip.NormalizedTime01 : 0f;
        }

        private static float ResolveClipWeight01(in AnimatorBuiltinClipState clip, AnimatorBuiltinClipId expectedId)
        {
            return clip.ClipId == expectedId ? clip.Weight01 : 0f;
        }

        private static float ResolveClipScalar0(in AnimatorBuiltinClipState clip, AnimatorBuiltinClipId expectedId)
        {
            return clip.ClipId == expectedId ? clip.Scalar0 : 0f;
        }

        private static float ResolvePulse(in AnimatorBuiltinClipState clip, AnimatorBuiltinClipId expectedId)
        {
            if (clip.ClipId != expectedId || clip.Weight01 <= 0.001f)
            {
                return 0f;
            }

            return MathF.Sin(clip.NormalizedTime01 * MathF.PI) * clip.Weight01;
        }

        private void DrawOrientedCube(Vector3 center, Vector3 size, float yawRad, Vector4 color)
        {
            DrawWireBox(center, size, yawRad, color);
        }

        private static void DrawPrototypeSphere(Vector3 center, float radius, Vector4 color)
        {
            Rl.DrawSphere(center, radius, ToRaylibColor(color));
        }

        private static Vector3 TransformLocal(Vector3 origin, float yawRad, Vector3 local)
        {
            Vector3 right = new Vector3(MathF.Sin(yawRad), 0f, MathF.Cos(yawRad));
            Vector3 forward = new Vector3(MathF.Cos(yawRad), 0f, -MathF.Sin(yawRad));
            return origin + right * local.X + Vector3.UnitY * local.Y + forward * local.Z;
        }

        private static float ExtractYawRad(Quaternion rotation)
        {
            Quaternion normalized = Quaternion.Normalize(rotation);
            float sinyCosp = 2f * (normalized.W * normalized.Y + normalized.X * normalized.Z);
            float cosyCosp = 1f - 2f * (normalized.Y * normalized.Y + normalized.Z * normalized.Z);
            return MathF.Atan2(sinyCosp, cosyCosp);
        }

        private static Vector4 MultiplyColor(Vector4 color, float r, float g, float b, float a)
        {
            return new Vector4(
                Math.Clamp(color.X * r, 0f, 1f),
                Math.Clamp(color.Y * g, 0f, 1f),
                Math.Clamp(color.Z * b, 0f, 1f),
                Math.Clamp(color.W * a, 0f, 1f));
        }

        private static Vector4 LerpColor(Vector4 from, Vector4 to, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Vector4(
                from.X + (to.X - from.X) * t,
                from.Y + (to.Y - from.Y) * t,
                from.Z + (to.Z - from.Z) * t,
                from.W + (to.W - from.W) * t);
        }

        private void DrawModel(int meshAssetId, in MeshAssetDescriptor desc, Vector3 position, Vector3 scale, Vector4 color)
        {
            if (!TryGetOrLoadModel(meshAssetId, desc, out var cached))
            {
                DrawMissingModelMarker(position, scale);
                return;
            }

            var tint = ToRaylibColor(color);
            var model = cached.Model;
            Rl.DrawModelEx(model, position, Vector3.UnitY, 0f, scale, tint);
        }

        private void DrawBillboard(int meshAssetId, in MeshAssetDescriptor desc, Vector3 position, Vector3 scale, Vector4 color, Camera3D camera)
        {
            if (!TryGetOrLoadTexture(meshAssetId, desc, out var cached))
            {
                DrawMissingModelMarker(position, scale);
                return;
            }

            float height = MathF.Max(scale.Y, 0.05f);
            float width = height * cached.AspectRatio;
            var billboardPosition = new Vector3(position.X, position.Y + height * 0.5f, position.Z);
            var source = new Rectangle(0f, 0f, cached.Texture.width, cached.Texture.height);

            // Billboard art ships pre-colored, so preserve only caller alpha.
            byte alpha = Clamp01ToByte(color.W);
            var tint = new Color(255, 255, 255, alpha);
            LogBillboardDrawDiagnostic(
                meshAssetId,
                $"billboard-draw pos=({billboardPosition.X:F2},{billboardPosition.Y:F2},{billboardPosition.Z:F2}) scale=({scale.X:F2},{scale.Y:F2},{scale.Z:F2}) size=({width:F2}x{height:F2}) alpha={alpha} cameraPos=({camera.position.X:F2},{camera.position.Y:F2},{camera.position.Z:F2}) cameraTarget=({camera.target.X:F2},{camera.target.Y:F2},{camera.target.Z:F2})");
            Rl.DrawBillboardRec(camera, cached.Texture, source, billboardPosition, new Vector2(width, height), tint);
        }

        private bool TryGetOrLoadModel(int meshAssetId, in MeshAssetDescriptor desc, out CachedModel cached)
        {
            if (_modelCache.TryGetValue(meshAssetId, out cached))
                return cached.Loaded;

            cached = new CachedModel { Loaded = false };

            if (_vfs == null || desc.SourceUris == null || desc.SourceUris.Length == 0)
            {
                _modelCache[meshAssetId] = cached;
                return false;
            }

            for (int u = 0; u < desc.SourceUris.Length; u++)
            {
                string uri = desc.SourceUris[u];
                if (string.IsNullOrWhiteSpace(uri)) continue;

                if (!_vfs.TryResolveFullPath(uri, out string fullPath)) continue;
                if (!File.Exists(fullPath)) continue;

                var model = Rl.LoadModel(fullPath);
                if (model.meshCount > 0)
                {
                    cached = new CachedModel { Model = model, Loaded = true };
                    _modelCache[meshAssetId] = cached;
                    return true;
                }

                Rl.UnloadModel(model);
            }

            _modelCache[meshAssetId] = cached;
            return false;
        }

        private bool TryGetOrLoadTexture(int meshAssetId, in MeshAssetDescriptor desc, out CachedTexture cached)
        {
            if (_textureCache.TryGetValue(meshAssetId, out cached))
                return cached.Loaded;

            cached = new CachedTexture { Loaded = false, AspectRatio = 1f };

            if (_vfs == null || desc.SourceUris == null || desc.SourceUris.Length == 0)
            {
                LogTextureDiagnostic(meshAssetId, $"texture-load skipped; vfsMissing={_vfs == null}; uriCount={desc.SourceUris?.Length ?? 0}");
                _textureCache[meshAssetId] = cached;
                return false;
            }

            for (int u = 0; u < desc.SourceUris.Length; u++)
            {
                string uri = desc.SourceUris[u];
                if (string.IsNullOrWhiteSpace(uri)) continue;

                if (!_vfs.TryResolveFullPath(uri, out string fullPath))
                {
                    LogTextureDiagnostic(meshAssetId, $"texture-resolve failed; uri={uri}");
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    LogTextureDiagnostic(meshAssetId, $"texture-file missing; uri={uri}; fullPath={fullPath}");
                    continue;
                }

                var texture = Rl.LoadTexture(fullPath);
                if (texture.id != 0 && texture.width > 0 && texture.height > 0)
                {
                    cached = new CachedTexture
                    {
                        Texture = texture,
                        Loaded = true,
                        AspectRatio = texture.height > 0 ? (float)texture.width / texture.height : 1f,
                    };
                    _textureCache[meshAssetId] = cached;
                    LogTextureDiagnostic(meshAssetId, $"texture-load success; uri={uri}; fullPath={fullPath}; size={texture.width}x{texture.height}");
                    return true;
                }

                LogTextureDiagnostic(meshAssetId, $"texture-load failed; uri={uri}; fullPath={fullPath}; textureId={texture.id}; size={texture.width}x{texture.height}");

                if (texture.id != 0)
                    Rl.UnloadTexture(texture);
            }

            _textureCache[meshAssetId] = cached;
            return false;
        }

        private void LogTextureDiagnostic(int meshAssetId, string message)
        {
            if (string.IsNullOrWhiteSpace(_diagnosticPath))
                return;

            if (!_loggedTextureDiagnostics.Add(meshAssetId))
                return;

            string fullPath = Path.GetFullPath(_diagnosticPath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.AppendAllText(fullPath, $"[{DateTime.UtcNow:O}] meshAssetId={meshAssetId} {message}{Environment.NewLine}");
        }

        private void LogBillboardDrawDiagnostic(int meshAssetId, string message)
        {
            if (string.IsNullOrWhiteSpace(_diagnosticPath))
                return;

            if (!_loggedBillboardDrawDiagnostics.Add(meshAssetId))
                return;

            string fullPath = Path.GetFullPath(_diagnosticPath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.AppendAllText(fullPath, $"[{DateTime.UtcNow:O}] meshAssetId={meshAssetId} {message}{Environment.NewLine}");
        }

        private static void DrawMissingModelMarker(Vector3 position, Vector3 scale)
        {
            float s = MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z)) * 0.3f;
            if (s < 0.05f) s = 0.3f;
            Rl.DrawCube(position, s, s, s, new Color(255, 0, 255, 255));
        }

        private static void DrawWireBox(Vector3 center, Vector3 size, float yawRad, Vector4 color)
        {
            Vector3 half = size * 0.5f;
            Span<Vector3> corners = stackalloc Vector3[8];
            corners[0] = TransformLocal(center, yawRad, new Vector3(-half.X, -half.Y, -half.Z));
            corners[1] = TransformLocal(center, yawRad, new Vector3(half.X, -half.Y, -half.Z));
            corners[2] = TransformLocal(center, yawRad, new Vector3(half.X, -half.Y, half.Z));
            corners[3] = TransformLocal(center, yawRad, new Vector3(-half.X, -half.Y, half.Z));
            corners[4] = TransformLocal(center, yawRad, new Vector3(-half.X, half.Y, -half.Z));
            corners[5] = TransformLocal(center, yawRad, new Vector3(half.X, half.Y, -half.Z));
            corners[6] = TransformLocal(center, yawRad, new Vector3(half.X, half.Y, half.Z));
            corners[7] = TransformLocal(center, yawRad, new Vector3(-half.X, half.Y, half.Z));

            var lineColor = ToRaylibColor(color);
            DrawWireEdge(corners, 0, 1, lineColor);
            DrawWireEdge(corners, 1, 2, lineColor);
            DrawWireEdge(corners, 2, 3, lineColor);
            DrawWireEdge(corners, 3, 0, lineColor);

            DrawWireEdge(corners, 4, 5, lineColor);
            DrawWireEdge(corners, 5, 6, lineColor);
            DrawWireEdge(corners, 6, 7, lineColor);
            DrawWireEdge(corners, 7, 4, lineColor);

            DrawWireEdge(corners, 0, 4, lineColor);
            DrawWireEdge(corners, 1, 5, lineColor);
            DrawWireEdge(corners, 2, 6, lineColor);
            DrawWireEdge(corners, 3, 7, lineColor);

            // Mark the forward-facing top edge so layer orientation is easy to inspect in motion.
            DrawWireEdge(corners, 6, 7, ToRaylibColor(MultiplyColor(color, 1.2f, 1.2f, 0.8f, 1f)));
        }

        private static void DrawWireEdge(ReadOnlySpan<Vector3> corners, int start, int end, Color color)
        {
            Rl.DrawLine3D(corners[start], corners[end], color);
        }

        // ── Instanced rendering (unchanged from original) ──

        public void DrawInstanced(PrimitiveDrawBuffer draw, MeshAssetRegistry meshes)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            LastInstancedInstances = 0;
            LastInstancedBatches = 0;

            EnsureInitialized();

            var span = draw.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (!meshes.TryGetPrimitiveKind(item.MeshAssetId, out var kind)) continue;

                SubmitPrimitive(kind, item.Position, item.Scale, item.Color);
            }

            FlushInstancedBatches();
        }

        private void AddInstance(List<Batch> batches, uint colorKey, in RaylibMatrix matrix)
        {
            for (int i = 0; i < batches.Count; i++)
            {
                var b = batches[i];
                if (b.ColorKey != colorKey) continue;

                b.Add(matrix);
                batches[i] = b;
                return;
            }

            var nb = new Batch(colorKey);
            nb.Add(matrix);
            batches.Add(nb);
        }

        private void FlushInstancedBatches()
        {
            int totalInstances = 0;
            int batches = 0;

            FlushMeshBatches(_cubeBatches, ref totalInstances, ref batches, ref _cubeMesh);
            FlushMeshBatches(_sphereBatches, ref totalInstances, ref batches, ref _sphereMesh);

            LastInstancedInstances = totalInstances;
            LastInstancedBatches = batches;
        }

        private void FlushMeshBatches(List<Batch> batches, ref int totalInstances, ref int batchCount, ref Mesh mesh)
        {
            for (int i = 0; i < batches.Count; i++)
            {
                var b = batches[i];
                if (b.Count == 0) continue;

                SetTintUniform(b.ColorKey);

                fixed (RaylibMatrix* p = b.Transforms)
                {
                    Rl.DrawMeshInstanced(mesh, _material, p, b.Count);
                }

                totalInstances += b.Count;
                batchCount++;

                b.Count = 0;
                batches[i] = b;
            }
        }

        private void SetTintUniform(uint colorKey)
        {
            if (_locTint < 0) return;

            float r = (colorKey & 0xFF) / 255f;
            float g = ((colorKey >> 8) & 0xFF) / 255f;
            float b = ((colorKey >> 16) & 0xFF) / 255f;
            float a = ((colorKey >> 24) & 0xFF) / 255f;
            var cd = new Vector4(r, g, b, a);
            Rl.SetShaderValue(_shader, _locTint, &cd, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;

            _cubeMesh = Rl.GenMeshCube(1f, 1f, 1f);
            if (_cubeMesh.colors == null)
            {
                int bytes = _cubeMesh.vertexCount * 4;
                _cubeMesh.colors = (byte*)Rl.MemAlloc(bytes);
                for (int i = 0; i < bytes; i++) _cubeMesh.colors[i] = 255;
            }
            Rl.UploadMesh(ref _cubeMesh, false);

            _sphereMesh = Rl.GenMeshSphere(0.5f, 8, 8);
            if (_sphereMesh.colors == null)
            {
                int bytes = _sphereMesh.vertexCount * 4;
                _sphereMesh.colors = (byte*)Rl.MemAlloc(bytes);
                for (int i = 0; i < bytes; i++) _sphereMesh.colors[i] = 255;
            }
            Rl.UploadMesh(ref _sphereMesh, false);

            string baseDir = AppContext.BaseDirectory;
            _shader = Rl.LoadShader(Path.Combine(baseDir, "instancing.vs"), Path.Combine(baseDir, "instancing.fs"));
            if (_shader.id == 0) throw new InvalidOperationException("Failed to load instancing shader (shader.id == 0).");

            _material = Rl.LoadMaterialDefault();
            _material.shader = _shader;

            _locColDiffuse = Rl.GetShaderLocation(_shader, "colDiffuse");
            _locTint = Rl.GetShaderLocation(_shader, "tint");
            int locMvp = Rl.GetShaderLocation(_shader, "mvp");
            int locInstance = Rl.GetShaderLocationAttrib(_shader, "instanceTransform");

            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locInstance;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_COLOR_DIFFUSE] = _locColDiffuse;

            if (locMvp < 0) throw new InvalidOperationException("Shader uniform 'mvp' not found.");
            if (locInstance < 0) throw new InvalidOperationException("Shader attrib 'instanceTransform' not found.");
            if (_locTint < 0) throw new InvalidOperationException("Shader uniform 'tint' not found.");

            _initialized = true;
        }

        private static uint PackRgba(in Vector4 c)
        {
            uint r = Clamp01ToByte(c.X);
            uint g = Clamp01ToByte(c.Y);
            uint b = Clamp01ToByte(c.Z);
            uint a = Clamp01ToByte(c.W);
            return r | (g << 8) | (b << 16) | (a << 24);
        }

        private static Color ToRaylibColor(in Vector4 c) => RaylibColorUtil.ToRaylibColor(in c);

        private static byte Clamp01ToByte(float v) => RaylibColorUtil.Clamp01ToByte(v);

        public void Dispose()
        {
            foreach (var kvp in _modelCache)
            {
                if (kvp.Value.Loaded)
                    Rl.UnloadModel(kvp.Value.Model);
            }
            _modelCache.Clear();

            foreach (var kvp in _textureCache)
            {
                if (kvp.Value.Loaded)
                    Rl.UnloadTexture(kvp.Value.Texture);
            }
            _textureCache.Clear();

            if (!_initialized) return;

            if (_cubeMesh.vertexCount > 0) Rl.UnloadMesh(_cubeMesh);
            if (_sphereMesh.vertexCount > 0) Rl.UnloadMesh(_sphereMesh);
            Rl.UnloadMaterial(_material);
            Rl.UnloadShader(_shader);
        }

        private struct CachedModel
        {
            public Model Model;
            public bool Loaded;
        }

        private struct CachedTexture
        {
            public Texture2D Texture;
            public bool Loaded;
            public float AspectRatio;
        }

        private struct Batch
        {
            public readonly uint ColorKey;
            public RaylibMatrix[] Transforms;
            public int Count;

            public Batch(uint colorKey, int initialCapacity = 256)
            {
                ColorKey = colorKey;
                Transforms = new RaylibMatrix[Math.Max(4, initialCapacity)];
                Count = 0;
            }

            public void Add(in RaylibMatrix matrix)
            {
                if (Count >= Transforms.Length)
                {
                    Array.Resize(ref Transforms, Transforms.Length * 2);
                }
                Transforms[Count++] = matrix;
            }
        }
    }
}

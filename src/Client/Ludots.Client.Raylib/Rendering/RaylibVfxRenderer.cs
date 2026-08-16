using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Particles;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;

namespace Ludots.Client.Raylib.Rendering
{
    internal readonly record struct RaylibVfxKey(int StableId, int EffectAssetId);

    public sealed class RaylibVfxRenderer : IDisposable
    {
        private readonly IVirtualFileSystem? _vfs;
        private readonly Dictionary<RaylibVfxKey, RaylibParticleVfxInstance> _particleVfx = new();
        private readonly HashSet<RaylibVfxKey> _activeKeys = new();
        private readonly List<RaylibVfxKey> _inactiveKeys = new();
        private readonly Dictionary<int, Texture2D> _textureCache = new();

        public RaylibVfxRenderer(IVirtualFileSystem? vfs = null)
        {
            _vfs = vfs;
        }

        public int LastDrawnVfxCount { get; private set; }

        public int TotalDrawnVfxCount { get; private set; }

        public void BeginFrame()
        {
            _activeKeys.Clear();
            LastDrawnVfxCount = 0;
        }

        public void Draw(in PrimitiveDrawItem visual, MeshAssetRegistry effectAssets, Camera3D camera, double timeSeconds, float scaleMul = 1f)
        {
            if (effectAssets == null)
            {
                throw new ArgumentNullException(nameof(effectAssets));
            }

            if (visual.AssetKind != AssetKind.VFX)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibVfxRenderer)} can only draw VFX presentation items, but received AssetKind '{visual.AssetKind}'.");
            }

            if (visual.StableId <= 0)
            {
                throw new InvalidOperationException(
                    $"VFX item meshAssetId={visual.MeshAssetId} requires a positive stableId for renderer lifetime tracking.");
            }

            if (visual.MeshAssetId <= 0)
            {
                throw new InvalidOperationException(
                    $"VFX item stableId={visual.StableId} requires a positive effect meshAssetId.");
            }

            if (!effectAssets.TryGetDescriptor(visual.MeshAssetId, out MeshAssetDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"VFX item stableId={visual.StableId} references unknown effect asset id {visual.MeshAssetId}.");
            }

            RaylibVfxKey key = ComposeVfxKey(visual.StableId, visual.MeshAssetId);
            _activeKeys.Add(key);
            VfxAssetData effect = descriptor.VfxData;
            if (!effect.IsValid || effect.ParticleSystem is null)
            {
                throw new InvalidOperationException(
                    $"VFX effect asset id {visual.MeshAssetId} must reference a registered Quarks particle VFX.");
            }

            Vector3 scale = visual.Scale * scaleMul;
            RaylibParticleVfxInstance particleVfx = GetOrCreateParticleVfxInstance(key, effect.ParticleSystem);
            particleVfx.Update(effect.ParticleSystem, timeSeconds, visual.Position, visual.Rotation);
            DrawParticleVfx(in visual, scale, effect.ParticleSystem, particleVfx, effectAssets, camera);
            LastDrawnVfxCount++;
            TotalDrawnVfxCount++;
        }

        public void EndFrame()
        {
            if (_particleVfx.Count == _activeKeys.Count)
            {
                return;
            }

            _inactiveKeys.Clear();
            foreach (RaylibVfxKey key in _particleVfx.Keys)
            {
                if (!_activeKeys.Contains(key))
                {
                    _inactiveKeys.Add(key);
                }
            }

            for (int i = 0; i < _inactiveKeys.Count; i++)
            {
                _particleVfx.Remove(_inactiveKeys[i]);
            }
        }

        private RaylibParticleVfxInstance GetOrCreateParticleVfxInstance(
            in RaylibVfxKey key,
            ParticleVfxAssetData effect)
        {
            if (_particleVfx.TryGetValue(key, out RaylibParticleVfxInstance? existing))
            {
                return existing;
            }

            var created = new RaylibParticleVfxInstance(effect);
            _particleVfx.Add(key, created);
            return created;
        }

        private void DrawParticleVfx(
            in PrimitiveDrawItem visual,
            in Vector3 scale,
            ParticleVfxAssetData effect,
            RaylibParticleVfxInstance particleVfx,
            MeshAssetRegistry effectAssets,
            Camera3D camera)
        {
            ParticleSystemSnapshot snapshot = particleVfx.Runtime.GetSnapshot();
            float visualScale = MathF.Max(
                MathF.Abs(scale.X),
                MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z)));
            if (visualScale <= 0f)
            {
                throw new InvalidOperationException(
                    $"VFX item stableId={visual.StableId} requires a positive scale for particle sizing.");
            }

            Quaternion rotation = WorldPlane2D.NormalizeOrIdentity(visual.Rotation);
            Rl.BeginBlendMode(ToRaylibBlendMode(effect.BlendMode));
            try
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    Vector3 position = snapshot.Positions[i];
                    Vector3 velocity = snapshot.Velocities[i];
                    if (!effect.WorldSpace)
                    {
                        position = visual.Position + Vector3.Transform(position, rotation);
                        velocity = Vector3.TransformNormal(velocity, Matrix4x4.CreateFromQuaternion(rotation));
                    }

                    Vector4 color = ModulateColor(snapshot.Colors[i], visual.Color);
                    float size = snapshot.Sizes[i] * visualScale;
                    if (size <= 0f)
                    {
                        throw new InvalidOperationException(
                            $"Particle VFX requires a positive drawn size; sampled size={snapshot.Sizes[i]} visualScale={visualScale}.");
                    }

                    Color raylibColor = ToRaylibColor(color);
                    if (effect.RenderMode == ParticleRenderMode.Trail)
                    {
                        Vector3 previous = position - (velocity * effect.TrailLengthSeconds);
                        Rl.DrawLine3D(previous, position, raylibColor);
                        continue;
                    }

                    if (effect.RenderMode == ParticleRenderMode.Billboard ||
                        effect.RenderMode == ParticleRenderMode.StretchedBillboard)
                    {
                        DrawTexturedBillboard(effect, effectAssets, camera, position, velocity, size, snapshot.FrameIndices[i], raylibColor);
                        continue;
                    }

                    if (effect.PrimitiveKind == ParticlePrimitiveKind.Cube)
                    {
                        Rl.DrawCube(position, size, size, size, raylibColor);
                    }
                    else
                    {
                        Rl.DrawSphere(position, size * 0.5f, raylibColor);
                    }
                }
            }
            finally
            {
                Rl.EndBlendMode();
            }
        }

        private void DrawTexturedBillboard(
            ParticleVfxAssetData effect,
            MeshAssetRegistry effectAssets,
            Camera3D camera,
            Vector3 position,
            Vector3 velocity,
            float size,
            int frameIndex,
            Color tint)
        {
            Texture2D texture = RequireTexture(effect, effectAssets);
            ParticleTextureSheetAsset textureSheet = effect.TextureSheet
                ?? throw new InvalidOperationException("Billboard particle render modes require a texture sheet.");
            Rectangle source = BuildTextureSourceRectangle(texture, textureSheet, frameIndex);
            if (source.height <= 0f)
            {
                throw new InvalidOperationException(
                    $"Particle texture sheet '{textureSheet.TextureAssetId}' produced a non-positive frame height.");
            }

            float aspect = source.width / source.height;
            float width = size * aspect;
            float height = size;
            if (effect.RenderMode == ParticleRenderMode.StretchedBillboard)
            {
                height *= velocity.Length() * effect.StretchedLengthScale;
                if (height <= 0f)
                {
                    throw new InvalidOperationException(
                        "StretchedBillboard particles require a positive velocity * stretchedLengthScale product for drawn height.");
                }
            }

            Rl.DrawBillboardRec(camera, texture, source, position, new Vector2(width, height), tint);
        }

        private Texture2D RequireTexture(ParticleVfxAssetData effect, MeshAssetRegistry effectAssets)
        {
            ParticleTextureSheetAsset textureSheet = effect.TextureSheet
                ?? throw new InvalidOperationException("Billboard particle render modes require a texture sheet.");
            int textureAssetId = effectAssets.GetId(textureSheet.TextureAssetId);
            if (textureAssetId <= 0 || !effectAssets.TryGetDescriptor(textureAssetId, out MeshAssetDescriptor textureDescriptor))
            {
                throw new InvalidOperationException(
                    $"Particle texture sheet references unknown texture asset '{textureSheet.TextureAssetId}'.");
            }

            if (textureDescriptor.Type != MeshAssetType.Billboard)
            {
                throw new InvalidOperationException(
                    $"Particle texture sheet asset '{textureSheet.TextureAssetId}' must be a Billboard mesh asset, but was '{textureDescriptor.Type}'.");
            }

            if (_textureCache.TryGetValue(textureAssetId, out Texture2D cached))
            {
                return cached;
            }

            Texture2D loaded = LoadTexture(textureSheet.TextureAssetId, textureDescriptor);
            _textureCache.Add(textureAssetId, loaded);
            return loaded;
        }

        private Texture2D LoadTexture(string textureAssetKey, in MeshAssetDescriptor textureDescriptor)
        {
            if (_vfs == null)
            {
                throw new InvalidOperationException(
                    $"Particle texture sheet asset '{textureAssetKey}' requires a virtual file system to resolve Presentation/host_assets.json sourceUris.");
            }

            if (textureDescriptor.SourceUris == null || textureDescriptor.SourceUris.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Particle texture sheet asset '{textureAssetKey}' requires raylib sourceUris from Presentation/host_assets.json.");
            }

            var failures = new StringBuilder();
            for (int i = 0; i < textureDescriptor.SourceUris.Length; i++)
            {
                string uri = textureDescriptor.SourceUris[i];
                if (string.IsNullOrWhiteSpace(uri))
                {
                    failures.Append($"[{i}] blank uri; ");
                    continue;
                }

                if (!_vfs.TryResolveFullPath(uri, out string fullPath))
                {
                    failures.Append($"[{i}] unresolved uri '{uri}'; ");
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    failures.Append($"[{i}] missing file '{uri}' -> '{fullPath}'; ");
                    continue;
                }

                Texture2D texture = Rl.LoadTexture(fullPath);
                if (texture.id != 0 && texture.width > 0 && texture.height > 0)
                {
                    return texture;
                }

                if (texture.id != 0)
                {
                    Rl.UnloadTexture(texture);
                }

                failures.Append($"[{i}] raylib rejected '{uri}' ({fullPath}); ");
            }

            throw new InvalidOperationException(
                $"Particle texture sheet asset '{textureAssetKey}' could not load any sourceUri. Attempts: {failures}");
        }

        internal static Rectangle BuildTextureSourceRectangle(
            Texture2D texture,
            ParticleTextureSheetAsset textureSheet,
            int frameIndex)
        {
            if (texture.id == 0 || texture.width <= 0 || texture.height <= 0)
            {
                throw new InvalidOperationException("Particle texture sheet requires a loaded non-empty texture.");
            }

            if (textureSheet == null || !textureSheet.IsValid)
            {
                throw new ArgumentException("Particle texture sheet must be valid.", nameof(textureSheet));
            }

            if (texture.width % textureSheet.Columns != 0 ||
                texture.height % textureSheet.Rows != 0)
            {
                throw new InvalidOperationException(
                    $"Particle texture sheet '{textureSheet.TextureAssetId}' texture size {texture.width}x{texture.height} must be divisible by authored grid {textureSheet.Columns}x{textureSheet.Rows}.");
            }

            if (frameIndex < 0 || frameIndex >= textureSheet.FrameCount)
            {
                throw new ArgumentOutOfRangeException(nameof(frameIndex));
            }

            int frameWidth = texture.width / textureSheet.Columns;
            int frameHeight = texture.height / textureSheet.Rows;
            int column = frameIndex % textureSheet.Columns;
            int row = frameIndex / textureSheet.Columns;
            return new Rectangle(
                column * frameWidth,
                row * frameHeight,
                frameWidth,
                frameHeight);
        }

        internal static BlendMode ToRaylibBlendMode(ParticleBlendMode blendMode)
        {
            return blendMode switch
            {
                ParticleBlendMode.Alpha => BlendMode.BLEND_ALPHA,
                ParticleBlendMode.Additive => BlendMode.BLEND_ADDITIVE,
                ParticleBlendMode.PremultipliedAlpha => BlendMode.BLEND_ALPHA_PREMULTIPLY,
                ParticleBlendMode.Multiply => BlendMode.BLEND_MULTIPLIED,
                _ => throw new ArgumentOutOfRangeException(nameof(blendMode), blendMode, "Unsupported particle blend mode."),
            };
        }

        public void Dispose()
        {
            foreach (Texture2D texture in _textureCache.Values)
            {
                if (texture.id != 0)
                {
                    Rl.UnloadTexture(texture);
                }
            }

            _textureCache.Clear();
        }

        internal static RaylibVfxKey ComposeVfxKey(int stableId, int effectAssetId)
        {
            return new RaylibVfxKey(stableId, effectAssetId);
        }

        private static Vector4 ModulateColor(Vector4 authored, Vector4 tint)
        {
            return new Vector4(
                authored.X * tint.X,
                authored.Y * tint.Y,
                authored.Z * tint.Z,
                authored.W * tint.W);
        }

        private static Color ToRaylibColor(in Vector4 color)
        {
            return RaylibColorUtil.ToRaylibColor(in color);
        }

        private sealed class RaylibParticleVfxInstance
        {
            private bool _hasLastTime;
            private double _lastTimeSeconds;

            public RaylibParticleVfxInstance(ParticleVfxAssetData effect)
            {
                Runtime = new ParticleSystemRuntime(effect.MaxParticles, effect.Seed);
            }

            public ParticleSystemRuntime Runtime { get; }

            public void Update(
                ParticleVfxAssetData effect,
                double timeSeconds,
                in Vector3 position,
                in Quaternion rotation)
            {
                if (!double.IsFinite(timeSeconds))
                {
                    throw new ArgumentOutOfRangeException(nameof(timeSeconds));
                }

                float deltaSeconds = _hasLastTime
                    ? checked((float)(timeSeconds - _lastTimeSeconds))
                    : 0f;
                if (deltaSeconds < 0f)
                {
                    throw new InvalidOperationException(
                        "Raylib particle VFX time must be monotonic for a stable effect identity.");
                }

                Runtime.Update(effect, deltaSeconds, in position, in rotation);
                _lastTimeSeconds = timeSeconds;
                _hasLastTime = true;
            }
        }
    }
}

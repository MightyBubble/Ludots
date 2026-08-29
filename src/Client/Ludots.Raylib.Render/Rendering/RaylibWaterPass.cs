using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Ludots.Platform.Abstractions;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// Planar reflection + refraction RenderTexture pass for VertexMap water meshes
    /// or VisualHeightmap + ocean plane.
    /// Frame-graph intent mirrors tropical-island demos: reflection (camera flipped about waterPlaneY)
    /// then refraction, then main water sampling both RTs. Baseline omits GPU clip planes on terrain;
    /// submerged geometry may contribute to the mirrored RT until a future cullHeight lands.
    /// </summary>
    public sealed unsafe class RaylibWaterPass : IDisposable
    {
        public const string DefaultRelativePath = "Presentation/water_environments.json";
        public const string BackendIdRaylib = "raylib";

        private readonly IRenderAssetPathResolver _assetPaths;
        private readonly string _backendId;
        private readonly List<WaterEnvironmentDescriptor> _descriptors = new();

        private WaterEnvironmentDescriptor? _active;
        private string? _activeMapId;
        private RenderTexture2D _reflection;
        private RenderTexture2D _refraction;
        private Texture2D _dudvTexture;
        private bool _ownsDudvTexture;
        private bool _hasDudvMap;
        private int _rtWidth;
        private int _rtHeight;
        private float _moveFactor;
        private bool _targetsReady;
        private bool _disposed;

        public RaylibWaterPass(
            IRenderAssetPathResolver assetPaths,
            string backendId = BackendIdRaylib)
        {
            _assetPaths = assetPaths ?? throw new ArgumentNullException(nameof(assetPaths));
            if (string.IsNullOrWhiteSpace(backendId))
            {
                throw new ArgumentException("Water environment backendId must not be empty.", nameof(backendId));
            }

            _backendId = backendId.Trim();
        }

        public bool IsActive => _active != null;

        public float WaterPlaneY => _active?.WaterPlaneY
            ?? throw new InvalidOperationException($"{nameof(RaylibWaterPass)} has no active water environment.");

        public float WaveStrength => _active?.WaveStrength ?? 0f;

        public float MoveFactor => _moveFactor;

        public bool HasDudvMap => _hasDudvMap;

        public Texture2D ReflectionTexture
        {
            get
            {
                EnsureTargetsReadyOrThrow();
                return _reflection.texture;
            }
        }

        public Texture2D RefractionTexture
        {
            get
            {
                EnsureTargetsReadyOrThrow();
                return _refraction.texture;
            }
        }

        public Texture2D DudvTexture
        {
            get
            {
                EnsureTargetsReadyOrThrow();
                return _dudvTexture;
            }
        }

        public void LoadDescriptors(IReadOnlyList<MergedConfigEntry> merged)
        {
            ThrowIfDisposed();
            if (merged == null)
            {
                throw new ArgumentNullException(nameof(merged));
            }

            _descriptors.Clear();
            Deactivate();

            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].Node is not JsonObject obj)
                {
                    throw new InvalidOperationException(
                        $"{DefaultRelativePath} entry '{merged[i].Id}' must merge to a JSON object.");
                }

                WaterEnvironmentDescriptor descriptor = ParseDescriptor(obj, merged[i].Id);
                if (!string.Equals(descriptor.BackendId, _backendId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!descriptor.Enabled)
                {
                    continue;
                }

                _descriptors.Add(descriptor);
            }
        }

        public void EnsureActiveForMap(string? mapId)
        {
            ThrowIfDisposed();
            if (_active != null &&
                string.Equals(_activeMapId, mapId, StringComparison.Ordinal) &&
                _targetsReady)
            {
                return;
            }

            WaterEnvironmentDescriptor? match = FindMatchingDescriptor(mapId);
            _activeMapId = mapId;
            if (match == null)
            {
                Deactivate();
                return;
            }

            if (_active != null &&
                string.Equals(_active.Id, match.Id, StringComparison.Ordinal) &&
                _targetsReady)
            {
                return;
            }

            ActivateDescriptor(match);
        }

        public void EnsureRenderTargets(int screenWidth, int screenHeight)
        {
            ThrowIfDisposed();
            if (!IsActive)
            {
                return;
            }

            if (screenWidth <= 0 || screenHeight <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibWaterPass)} EnsureRenderTargets requires positive screen size, got {screenWidth}x{screenHeight}.");
            }

            float scale = _active!.ResolutionScale;
            int width = Math.Max(1, (int)MathF.Round(screenWidth * scale));
            int height = Math.Max(1, (int)MathF.Round(screenHeight * scale));
            if (_targetsReady && _rtWidth == width && _rtHeight == height)
            {
                return;
            }

            UnloadRenderTargets();
            _reflection = RaylibNativeResources.LoadRenderTexture(width, height);
            _refraction = RaylibNativeResources.LoadRenderTexture(width, height);
            if (_reflection.id == 0 || _reflection.texture.id == 0 ||
                _refraction.id == 0 || _refraction.texture.id == 0)
            {
                UnloadRenderTargets();
                throw new InvalidOperationException(
                    $"{nameof(RaylibWaterPass)} LoadRenderTexture failed for water '{_active.Id}' at {width}x{height}.");
            }

            _rtWidth = width;
            _rtHeight = height;
            _targetsReady = true;
        }

        public void Advance(float deltaSeconds)
        {
            ThrowIfDisposed();
            if (!IsActive)
            {
                return;
            }

            if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0f)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibWaterPass)} Advance requires non-negative finite deltaSeconds.");
            }

            _moveFactor += _active!.MoveSpeed * deltaSeconds;
            while (_moveFactor > 1f)
            {
                _moveFactor -= 1f;
            }
        }

        public Camera3D BuildReflectionCamera(in Camera3D camera)
        {
            ThrowIfDisposed();
            if (!IsActive)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibWaterPass)} BuildReflectionCamera requires an active water environment.");
            }

            float planeY = _active!.WaterPlaneY;
            Camera3D reflected = camera;
            reflected.position.Y = (2f * planeY) - camera.position.Y;
            reflected.target.Y = (2f * planeY) - camera.target.Y;
            return reflected;
        }

        public void BeginReflectionPass(Color clearColor)
        {
            BeginPass(_reflection, clearColor, "reflection");
        }

        public void BeginRefractionPass(Color clearColor)
        {
            BeginPass(_refraction, clearColor, "refraction");
        }

        public void EndPass()
        {
            ThrowIfDisposed();
            Rl.EndTextureMode();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Deactivate();
            _disposed = true;
        }

        private void BeginPass(RenderTexture2D target, Color clearColor, string passName)
        {
            ThrowIfDisposed();
            EnsureTargetsReadyOrThrow();
            if (target.id == 0 || target.texture.id == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibWaterPass)} {passName} RenderTexture is not configured for water '{_active!.Id}'.");
            }

            Rl.BeginTextureMode(target);
            Rl.ClearBackground(clearColor);
        }

        private void ActivateDescriptor(WaterEnvironmentDescriptor descriptor)
        {
            Deactivate();
            _active = descriptor;
            LoadDudv(descriptor);
            _moveFactor = 0f;
            _targetsReady = false;
            _rtWidth = 0;
            _rtHeight = 0;
        }

        private void Deactivate()
        {
            UnloadRenderTargets();
            UnloadDudv();
            _active = null;
            _targetsReady = false;
            _rtWidth = 0;
            _rtHeight = 0;
            _moveFactor = 0f;
            _hasDudvMap = false;
        }

        private void LoadDudv(WaterEnvironmentDescriptor descriptor)
        {
            UnloadDudv();
            if (string.IsNullOrWhiteSpace(descriptor.DudvUri))
            {
                _dudvTexture = CreateNeutralDudvTexture();
                _ownsDudvTexture = true;
                _hasDudvMap = false;
                return;
            }

            string dudvUri = descriptor.DudvUri;
            if (!_assetPaths.TryResolveFullPath(dudvUri, out string fullPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibWaterPass)} cannot resolve water DUDV URI '{dudvUri}'.");
            }

            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibWaterPass)} water DUDV file missing: uri='{dudvUri}' fullPath='{fullPath}'.");
            }

            Texture2D texture = RaylibNativeResources.LoadTexture(fullPath);
            if (texture.id == 0 || texture.width <= 0 || texture.height <= 0)
            {
                if (texture.id != 0)
                {
                    RaylibNativeResources.UnloadTexture(texture);
                }

                throw new InvalidOperationException(
                    $"{nameof(RaylibWaterPass)} LoadTexture failed for water DUDV uri='{dudvUri}' fullPath='{fullPath}'.");
            }

            _dudvTexture = texture;
            _ownsDudvTexture = true;
            _hasDudvMap = true;
        }

        private static Texture2D CreateNeutralDudvTexture()
        {
            // RG = 0.5 → zero distortion when sampled as (xy * 2 - 1).
            Image image = Rl.GenImageColor(1, 1, new Color(128, 128, 128, 255));
            Texture2D texture = RaylibNativeResources.LoadTextureFromImage(image);
            Rl.UnloadImage(image);
            if (texture.id == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibWaterPass)} failed to allocate neutral DUDV texture.");
            }

            return texture;
        }

        private void UnloadRenderTargets()
        {
            if (_reflection.id != 0)
            {
                RaylibNativeResources.UnloadRenderTexture(_reflection);
                _reflection = default;
            }

            if (_refraction.id != 0)
            {
                RaylibNativeResources.UnloadRenderTexture(_refraction);
                _refraction = default;
            }

            _targetsReady = false;
        }

        private void UnloadDudv()
        {
            if (_ownsDudvTexture && _dudvTexture.id != 0)
            {
                RaylibNativeResources.UnloadTexture(_dudvTexture);
            }

            _dudvTexture = default;
            _ownsDudvTexture = false;
            _hasDudvMap = false;
        }

        private void EnsureTargetsReadyOrThrow()
        {
            if (!IsActive)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibWaterPass)} reflective water is not active for the current map.");
            }

            if (!_targetsReady ||
                _reflection.id == 0 || _reflection.texture.id == 0 ||
                _refraction.id == 0 || _refraction.texture.id == 0 ||
                _dudvTexture.id == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibWaterPass)} water '{_active!.Id}' claims reflective/refractive sampling but RenderTextures are not configured. Call EnsureRenderTargets before binding water.");
            }
        }

        private WaterEnvironmentDescriptor? FindMatchingDescriptor(string? mapId)
        {
            for (int i = 0; i < _descriptors.Count; i++)
            {
                WaterEnvironmentDescriptor descriptor = _descriptors[i];
                if (descriptor.MatchesMap(mapId))
                {
                    return descriptor;
                }
            }

            return null;
        }

        private static WaterEnvironmentDescriptor ParseDescriptor(JsonObject obj, string fallbackId)
        {
            string id = RequireString(obj["id"], fallbackId, "id");
            string backendId = RequireString(obj["backendId"], id, "backendId");
            bool enabled = obj["enabled"]?.GetValue<bool>() ?? true;

            if (!obj.ContainsKey("waterPlaneY"))
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{id}' must declare waterPlaneY.");
            }

            float waterPlaneY = obj["waterPlaneY"]!.GetValue<float>();
            if (!float.IsFinite(waterPlaneY))
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{id}' waterPlaneY must be finite.");
            }

            float resolutionScale = ReadFloat(obj["resolutionScale"], 0.5f);
            if (!float.IsFinite(resolutionScale) || resolutionScale <= 0f || resolutionScale > 1f)
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{id}' resolutionScale must be in (0, 1].");
            }

            float waveStrength = ReadFloat(obj["waveStrength"], 0.03f);
            if (!float.IsFinite(waveStrength) || waveStrength < 0f)
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{id}' waveStrength must be a non-negative finite number.");
            }

            float moveSpeed = ReadFloat(obj["moveSpeed"], 0.03f);
            if (!float.IsFinite(moveSpeed) || moveSpeed < 0f)
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{id}' moveSpeed must be a non-negative finite number.");
            }

            string? dudvUri = obj["dudvUri"]?.GetValue<string>();
            if (dudvUri != null)
            {
                dudvUri = dudvUri.Trim();
                if (dudvUri.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"{DefaultRelativePath} entry '{id}' has empty dudvUri.");
                }
            }

            var mapIds = new List<string>();
            if (obj["mapIds"] is JsonArray mapArr)
            {
                for (int i = 0; i < mapArr.Count; i++)
                {
                    string mapId = mapArr[i]?.GetValue<string>()?.Trim() ?? string.Empty;
                    if (mapId.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"{DefaultRelativePath} entry '{id}' mapIds[{i}] must be a non-empty string.");
                    }

                    mapIds.Add(mapId);
                }
            }

            return new WaterEnvironmentDescriptor(
                id,
                backendId,
                enabled,
                mapIds,
                waterPlaneY,
                resolutionScale,
                waveStrength,
                moveSpeed,
                dudvUri);
        }

        private static string RequireString(JsonNode? node, string rowId, string fieldName)
        {
            string value = node?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{rowId}' must declare '{fieldName}'.");
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{rowId}' field '{fieldName}' must not include leading or trailing whitespace.");
            }

            return value;
        }

        private static float ReadFloat(JsonNode? node, float fallback)
        {
            if (node == null)
            {
                return fallback;
            }

            return node.GetValue<float>();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibWaterPass));
            }
        }

        private sealed class WaterEnvironmentDescriptor
        {
            public WaterEnvironmentDescriptor(
                string id,
                string backendId,
                bool enabled,
                List<string> mapIds,
                float waterPlaneY,
                float resolutionScale,
                float waveStrength,
                float moveSpeed,
                string? dudvUri)
            {
                Id = id;
                BackendId = backendId;
                Enabled = enabled;
                MapIds = mapIds;
                WaterPlaneY = waterPlaneY;
                ResolutionScale = resolutionScale;
                WaveStrength = waveStrength;
                MoveSpeed = moveSpeed;
                DudvUri = dudvUri;
            }

            public string Id { get; }
            public string BackendId { get; }
            public bool Enabled { get; }
            public IReadOnlyList<string> MapIds { get; }
            public float WaterPlaneY { get; }
            public float ResolutionScale { get; }
            public float WaveStrength { get; }
            public float MoveSpeed { get; }
            public string? DudvUri { get; }

            public bool MatchesMap(string? mapId)
            {
                if (MapIds.Count == 0)
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(mapId))
                {
                    return false;
                }

                for (int i = 0; i < MapIds.Count; i++)
                {
                    if (string.Equals(MapIds[i], "*", StringComparison.Ordinal) ||
                        string.Equals(MapIds[i], mapId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}

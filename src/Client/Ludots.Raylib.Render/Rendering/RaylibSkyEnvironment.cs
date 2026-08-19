using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    public sealed unsafe class RaylibSkyEnvironment : IDisposable
    {
        public const string DefaultRelativePath = "Presentation/sky_environments.json";
        public const string BackendIdRaylib = "raylib";

        private readonly IRenderAssetPathResolver _assetPaths;
        private readonly string _backendId;
        private readonly string _shaderBaseDirectory;
        private readonly List<SkyEnvironmentDescriptor> _descriptors = new();

        private Shader _shader;
        private Material _material;
        private Mesh _cubeMesh;
        private Texture2D _gradientTexture;
        private byte[]? _gradientRgba;
        private int _gradientWidth;
        private int _gradientHeight;
        private Color? _clearColorOverride;
        private int _locDayPhase = -1;
        private int _locSunDirection = -1;
        private int _locSunColor = -1;
        private int _locMatView = -1;
        private int _locMatProjection = -1;

        private SkyEnvironmentDescriptor? _active;
        private string? _activeMapId;
        private float _dayPhase01;
        private Vector3 _sunDirection = new(-0.36f, 0.82f, -0.44f);
        private Vector3 _sunColor = new(1f, 0.93f, 0.78f);
        private bool _hasDayPhase;
        private bool _requireDayPhase;
        private bool _gpuReady;
        private bool _disposed;

        public RaylibSkyEnvironment(
            IRenderAssetPathResolver assetPaths,
            string backendId = BackendIdRaylib,
            string? shaderBaseDirectory = null)
        {
            _assetPaths = assetPaths ?? throw new ArgumentNullException(nameof(assetPaths));
            if (string.IsNullOrWhiteSpace(backendId))
            {
                throw new ArgumentException("Sky environment backendId must not be empty.", nameof(backendId));
            }

            _backendId = backendId.Trim();
            _shaderBaseDirectory = string.IsNullOrWhiteSpace(shaderBaseDirectory)
                ? AppContext.BaseDirectory
                : shaderBaseDirectory;
        }

        public bool IsActive => _active != null;

        public float DayPhase01 => _dayPhase01;

        public bool HasDayPhase => _hasDayPhase;

        public void LoadDescriptors(IReadOnlyList<MergedConfigEntry> merged)
        {
            ThrowIfDisposed();
            if (merged == null)
            {
                throw new ArgumentNullException(nameof(merged));
            }

            _descriptors.Clear();
            _active = null;
            _activeMapId = null;

            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].Node is not JsonObject obj)
                {
                    throw new InvalidOperationException(
                        $"{DefaultRelativePath} entry '{merged[i].Id}' must merge to a JSON object.");
                }

                SkyEnvironmentDescriptor descriptor = ParseDescriptor(obj, merged[i].Id);
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

        public void SetPhaseSourceRequirement(bool requiredWhenActive)
        {
            ThrowIfDisposed();
            _requireDayPhase = requiredWhenActive;
        }

        public void ApplyDayPhase(float phase01)
        {
            ThrowIfDisposed();
            if (!float.IsFinite(phase01))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkyEnvironment)} received non-finite GlobalDayNight phase '{phase01}'.");
            }

            _dayPhase01 = Math.Clamp(phase01, 0f, 1f);
            _hasDayPhase = true;
        }

        public void SetSun(Vector3 direction, Vector3 color)
        {
            ThrowIfDisposed();
            _sunDirection = RaylibRenderEnvironmentConfig.RequireUnitDirection(direction, nameof(direction));
            RaylibRenderEnvironmentConfig.RequireColor(color, nameof(color));
            _sunColor = color;
        }

        public void EnsureActiveForMap(string? mapId)
        {
            ThrowIfDisposed();
            if (_descriptors.Count == 0)
            {
                _active = null;
                _activeMapId = mapId;
                return;
            }

            if (_requireDayPhase && !_hasDayPhase)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkyEnvironment)} sky is configured but no GlobalDayNight phase has been applied.");
            }

            if (_active != null &&
                string.Equals(_activeMapId, mapId, StringComparison.Ordinal) &&
                _gpuReady)
            {
                return;
            }

            SkyEnvironmentDescriptor? match = FindMatchingDescriptor(mapId);
            _activeMapId = mapId;
            if (match == null)
            {
                ReleaseGpuResources();
                _active = null;
                return;
            }

            if (_active != null &&
                string.Equals(_active.Id, match.Id, StringComparison.Ordinal) &&
                _gpuReady)
            {
                return;
            }

            ActivateDescriptor(match);
        }

        public Color ResolveClearColor()
        {
            ThrowIfDisposed();
            if (!IsActive)
            {
                return new Color(0, 0, 0, 255);
            }

            EnsurePhaseReadyForDraw();
            if (_gradientRgba != null && _gradientRgba.Length > 0 && _gradientWidth > 0 && _gradientHeight > 0)
            {
                float sampleV = _active!.ClearSampleV;
                int x = (int)MathF.Round(_dayPhase01 * (_gradientWidth - 1));
                int y = (int)MathF.Round(sampleV * (_gradientHeight - 1));
                x = Math.Clamp(x, 0, _gradientWidth - 1);
                y = Math.Clamp(y, 0, _gradientHeight - 1);
                int offset = ((y * _gradientWidth) + x) * 4;
                return new Color(
                    _gradientRgba[offset],
                    _gradientRgba[offset + 1],
                    _gradientRgba[offset + 2],
                    (byte)255);
            }

            if (_clearColorOverride.HasValue)
            {
                return _clearColorOverride.Value;
            }

            throw new InvalidOperationException(
                $"{nameof(RaylibSkyEnvironment)} sky '{_active!.Id}' cannot resolve ClearBackground color: bake gradientStops or declare clearRgb for gradientUri skies.");
        }

        public void Draw(in Camera3D camera, in CameraRenderState3D cameraState)
        {
            ThrowIfDisposed();
            if (!IsActive || !_gpuReady)
            {
                return;
            }

            EnsurePhaseReadyForDraw();
            EnsureShaderLocations();

            float phase = _dayPhase01;
            Rl.SetShaderValue(_shader, _locDayPhase, &phase, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Vector3 sunDirection = _sunDirection;
            Vector3 sunColor = _sunColor;
            Rl.SetShaderValue(_shader, _locSunDirection, &sunDirection, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_shader, _locSunColor, &sunColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);

            Matrix4x4 view = Matrix4x4.CreateLookAt(camera.position, camera.target, camera.up);
            view.Translation = Vector3.Zero;
            RaylibMatrix raylibView = RaylibMatrix.FromSystemNumerics(in view);
            Rl.SetShaderValueMatrix(_shader, _locMatView, raylibView);

            CameraClipPlanes clipPlanes = cameraState.ResolveClipPlanes();
            float aspect = MathF.Max(0.001f, Rl.GetScreenWidth() / (float)Math.Max(1, Rl.GetScreenHeight()));
            Matrix4x4 projection = BuildProjection(in camera, in clipPlanes, aspect);
            RaylibMatrix raylibProjection = RaylibMatrix.FromSystemNumerics(in projection);
            Rl.SetShaderValueMatrix(_shader, _locMatProjection, raylibProjection);

            int albedoIndex = (int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO;
            _material.maps[albedoIndex].texture = _gradientTexture;
            _material.maps[albedoIndex].color = Color.WHITE;

            Rl.rlDisableBackfaceCulling();
            Rl.rlDisableDepthMask();
            RaylibMatrix identity = RaylibMatrix.Identity;
            Rl.DrawMesh(_cubeMesh, _material, identity);
            Rl.rlEnableDepthMask();
            Rl.rlEnableBackfaceCulling();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            ReleaseGpuResources();
            _descriptors.Clear();
            _disposed = true;
        }

        private void EnsurePhaseReadyForDraw()
        {
            if (_hasDayPhase)
            {
                return;
            }

            if (_active == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkyEnvironment)} cannot draw without an active sky descriptor.");
            }

            if (_active.HasInitialPhase)
            {
                ApplyDayPhase(_active.InitialPhase);
                return;
            }

            throw new InvalidOperationException(
                $"{nameof(RaylibSkyEnvironment)} sky '{_active.Id}' is active but no GlobalDayNight phase has been observed and no initialPhase was declared.");
        }

        private SkyEnvironmentDescriptor? FindMatchingDescriptor(string? mapId)
        {
            for (int i = 0; i < _descriptors.Count; i++)
            {
                SkyEnvironmentDescriptor descriptor = _descriptors[i];
                if (descriptor.MatchesMap(mapId))
                {
                    return descriptor;
                }
            }

            return null;
        }

        private void ActivateDescriptor(SkyEnvironmentDescriptor descriptor)
        {
            ReleaseGpuResources();
            EnsureShaderAndMesh();
            LoadGradient(descriptor);
            _active = descriptor;
            _gpuReady = true;
            if (!_hasDayPhase && descriptor.HasInitialPhase)
            {
                ApplyDayPhase(descriptor.InitialPhase);
            }
        }

        private void EnsureShaderAndMesh()
        {
            if (_shader.id != 0 && _cubeMesh.vertexCount > 0 && _material.maps != null)
            {
                return;
            }

            string vsPath = Path.Combine(_shaderBaseDirectory, "sky_daynight.vs");
            string fsPath = Path.Combine(_shaderBaseDirectory, "sky_daynight.fs");
            if (!File.Exists(vsPath))
            {
                throw new FileNotFoundException(
                    $"{nameof(RaylibSkyEnvironment)} missing day-night sky vertex shader at '{vsPath}'.",
                    vsPath);
            }

            if (!File.Exists(fsPath))
            {
                throw new FileNotFoundException(
                    $"{nameof(RaylibSkyEnvironment)} missing day-night sky fragment shader at '{fsPath}'.",
                    fsPath);
            }

            _shader = Rl.LoadShader(vsPath, fsPath);
            if (_shader.id == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkyEnvironment)} failed to compile day-night sky shader from '{vsPath}' + '{fsPath}'.");
            }

            EnsureShaderLocations();
            _material = Rl.LoadMaterialDefault();
            _material.shader = _shader;
            _cubeMesh = Rl.GenMeshSphere(1f, 64, 32);
            if (_cubeMesh.vertexCount <= 0)
            {
                throw new InvalidOperationException($"{nameof(RaylibSkyEnvironment)} GenMeshSphere failed.");
            }
        }

        private void EnsureShaderLocations()
        {
            if (_locDayPhase >= 0 &&
                _locSunDirection >= 0 &&
                _locSunColor >= 0 &&
                _locMatView >= 0 &&
                _locMatProjection >= 0)
            {
                return;
            }

            int locVertexPosition = Rl.GetShaderLocationAttrib(_shader, "vertexPosition");
            _locMatView = Rl.GetShaderLocation(_shader, "matView");
            _locMatProjection = Rl.GetShaderLocation(_shader, "matProjection");
            _locDayPhase = Rl.GetShaderLocation(_shader, "uDayPhase");
            _locSunDirection = Rl.GetShaderLocation(_shader, "uSunDirection");
            _locSunColor = Rl.GetShaderLocation(_shader, "uSunColor");
            int locMapAlbedo = Rl.GetShaderLocation(_shader, "texture0");

            if (locVertexPosition < 0)
            {
                throw new InvalidOperationException("skybox shader is missing attrib 'vertexPosition'.");
            }

            if (_locMatView < 0)
            {
                throw new InvalidOperationException("skybox shader is missing uniform 'matView'.");
            }

            if (_locMatProjection < 0)
            {
                throw new InvalidOperationException("skybox shader is missing uniform 'matProjection'.");
            }

            if (_locDayPhase < 0)
            {
                throw new InvalidOperationException("skybox shader is missing uniform 'uDayPhase'.");
            }

            if (_locSunDirection < 0)
            {
                throw new InvalidOperationException("skybox shader is missing uniform 'uSunDirection'.");
            }

            if (_locSunColor < 0)
            {
                throw new InvalidOperationException("skybox shader is missing uniform 'uSunColor'.");
            }

            if (locMapAlbedo < 0)
            {
                throw new InvalidOperationException("skybox shader is missing sampler 'texture0'.");
            }

            if (_shader.locs != null)
            {
                _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locVertexPosition;
                _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locMapAlbedo;
                _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = -1;
                _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_VIEW] = -1;
                _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_PROJECTION] = -1;
                _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = -1;
            }
        }

        private void LoadGradient(SkyEnvironmentDescriptor descriptor)
        {
            _clearColorOverride = descriptor.ClearRgb;
            if (!string.IsNullOrWhiteSpace(descriptor.GradientUri))
            {
                LoadGradientFromUri(descriptor);
                return;
            }

            if (descriptor.GradientStops.Count > 0)
            {
                BakeGradientFromStops(descriptor);
                return;
            }

            throw new InvalidOperationException(
                $"{nameof(RaylibSkyEnvironment)} sky '{descriptor.Id}' must declare gradientUri or gradientStops.");
        }

        private void LoadGradientFromUri(SkyEnvironmentDescriptor descriptor)
        {
            string gradientUri = descriptor.GradientUri!;
            if (!_assetPaths.TryResolveFullPath(gradientUri, out string fullPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkyEnvironment)} cannot resolve sky gradient URI '{gradientUri}'.");
            }

            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkyEnvironment)} sky gradient file missing: uri='{gradientUri}' fullPath='{fullPath}'.");
            }

            Texture2D texture = Rl.LoadTexture(fullPath);
            if (texture.id == 0 || texture.width <= 0 || texture.height <= 0)
            {
                if (texture.id != 0)
                {
                    Rl.UnloadTexture(texture);
                }

                throw new InvalidOperationException(
                    $"{nameof(RaylibSkyEnvironment)} LoadTexture failed for sky gradient uri='{gradientUri}' fullPath='{fullPath}'.");
            }

            if (!descriptor.ClearRgb.HasValue && descriptor.GradientStops.Count == 0)
            {
                Rl.UnloadTexture(texture);
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkyEnvironment)} sky '{descriptor.Id}' uses gradientUri and must also declare clearRgb or gradientStops for ClearBackground sampling.");
            }

            _gradientTexture = texture;
            _gradientWidth = texture.width;
            _gradientHeight = texture.height;
            if (descriptor.GradientStops.Count > 0)
            {
                BakeCpuGradientPixels(descriptor, texture.width, texture.height);
            }
            else
            {
                _gradientRgba = null;
            }
        }

        private void BakeGradientFromStops(SkyEnvironmentDescriptor descriptor)
        {
            int width = Math.Max(2, descriptor.GradientWidth);
            int height = Math.Max(2, descriptor.GradientHeight);
            byte[] pixels = BakeCpuGradientPixels(descriptor, width, height);

            Image image = Rl.GenImageColor(width, height, new Color(0, 0, 0, 255));
            Texture2D texture = Rl.LoadTextureFromImage(image);
            Rl.UnloadImage(image);
            if (texture.id == 0 || texture.width != width || texture.height != height)
            {
                if (texture.id != 0)
                {
                    Rl.UnloadTexture(texture);
                }

                throw new InvalidOperationException(
                    $"{nameof(RaylibSkyEnvironment)} failed to allocate baked sky gradient texture for '{descriptor.Id}'.");
            }

            fixed (byte* ptr = pixels)
            {
                Rl.UpdateTexture(texture, ptr);
            }

            _gradientTexture = texture;
            _gradientWidth = width;
            _gradientHeight = height;
        }

        private byte[] BakeCpuGradientPixels(SkyEnvironmentDescriptor descriptor, int width, int height)
        {
            var pixels = new byte[width * height * 4];
            IReadOnlyList<SkyGradientStop> stops = descriptor.GradientStops;
            for (int x = 0; x < width; x++)
            {
                float phase = width == 1 ? 0f : x / (float)(width - 1);
                SampleStops(stops, phase, out Vector3 zenith, out Vector3 horizon);
                for (int y = 0; y < height; y++)
                {
                    float t = height == 1 ? 0f : y / (float)(height - 1);
                    Vector3 color = Vector3.Lerp(zenith, horizon, t);
                    int offset = ((y * width) + x) * 4;
                    pixels[offset] = ToByte(color.X);
                    pixels[offset + 1] = ToByte(color.Y);
                    pixels[offset + 2] = ToByte(color.Z);
                    pixels[offset + 3] = 255;
                }
            }

            _gradientRgba = pixels;
            _gradientWidth = width;
            _gradientHeight = height;
            return pixels;
        }

        private static void SampleStops(
            IReadOnlyList<SkyGradientStop> stops,
            float phase,
            out Vector3 zenith,
            out Vector3 horizon)
        {
            if (stops.Count == 1)
            {
                zenith = stops[0].Zenith;
                horizon = stops[0].Horizon;
                return;
            }

            if (phase <= stops[0].Phase)
            {
                zenith = stops[0].Zenith;
                horizon = stops[0].Horizon;
                return;
            }

            SkyGradientStop last = stops[stops.Count - 1];
            if (phase >= last.Phase)
            {
                zenith = last.Zenith;
                horizon = last.Horizon;
                return;
            }

            for (int i = 0; i < stops.Count - 1; i++)
            {
                SkyGradientStop a = stops[i];
                SkyGradientStop b = stops[i + 1];
                if (phase < a.Phase || phase > b.Phase)
                {
                    continue;
                }

                float span = MathF.Max(1e-6f, b.Phase - a.Phase);
                float t = (phase - a.Phase) / span;
                zenith = Vector3.Lerp(a.Zenith, b.Zenith, t);
                horizon = Vector3.Lerp(a.Horizon, b.Horizon, t);
                return;
            }

            zenith = last.Zenith;
            horizon = last.Horizon;
        }

        private static Matrix4x4 BuildProjection(in Camera3D camera, in CameraClipPlanes clipPlanes, float aspect)
        {
            if (camera.projection == CameraProjection.CAMERA_ORTHOGRAPHIC)
            {
                float top = camera.fovy * 0.5f;
                float right = top * aspect;
                return Matrix4x4.CreateOrthographicOffCenter(
                    -right,
                    right,
                    -top,
                    top,
                    clipPlanes.NearMeters,
                    clipPlanes.FarMeters);
            }

            float fovYRad = camera.fovy * MathF.PI / 180f;
            return Matrix4x4.CreatePerspectiveFieldOfView(
                fovYRad,
                aspect,
                clipPlanes.NearMeters,
                clipPlanes.FarMeters);
        }

        private void ReleaseGpuResources()
        {
            if (_gradientTexture.id != 0)
            {
                Rl.UnloadTexture(_gradientTexture);
                _gradientTexture = default;
            }

            _gradientRgba = null;
            _gradientWidth = 0;
            _gradientHeight = 0;
            _clearColorOverride = null;

            if (_cubeMesh.vertexCount > 0)
            {
                Rl.UnloadMesh(_cubeMesh);
                _cubeMesh = default;
            }

            if (_material.maps != null)
            {
                _material.shader = default;
                Rl.UnloadMaterial(_material);
                _material = default;
            }

            if (_shader.id != 0)
            {
                Rl.UnloadShader(_shader);
                _shader = default;
            }

            _locDayPhase = -1;
            _locSunDirection = -1;
            _locSunColor = -1;
            _locMatView = -1;
            _locMatProjection = -1;
            _gpuReady = false;
        }

        private static SkyEnvironmentDescriptor ParseDescriptor(JsonObject obj, string fallbackId)
        {
            string id = RequireString(obj["id"], fallbackId, "id");
            string backendId = RequireString(obj["backendId"], id, "backendId");
            bool enabled = obj["enabled"]?.GetValue<bool>() ?? true;
            float clearSampleV = Math.Clamp(ReadFloat(obj["clearSampleV"], 0.85f), 0f, 1f);

            string? gradientUri = obj["gradientUri"]?.GetValue<string>();
            if (gradientUri != null)
            {
                gradientUri = gradientUri.Trim();
                if (gradientUri.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"{DefaultRelativePath} entry '{id}' has empty gradientUri.");
                }
            }

            bool hasInitialPhase = obj.ContainsKey("initialPhase");
            float initialPhase = hasInitialPhase
                ? Math.Clamp(ReadFloat(obj["initialPhase"], 0f), 0f, 1f)
                : 0f;

            int gradientWidth = ReadInt(obj["gradientWidth"], 256);
            int gradientHeight = ReadInt(obj["gradientHeight"], 64);
            if (gradientWidth < 2 || gradientHeight < 2)
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{id}' gradientWidth/Height must be >= 2.");
            }

            Color? clearRgb = null;
            if (obj["clearRgb"] is JsonArray clearArr)
            {
                Vector3 clear = ReadRgb(clearArr, id, -1, "clearRgb");
                clearRgb = new Color(ToByte(clear.X), ToByte(clear.Y), ToByte(clear.Z), 255);
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

            var stops = new List<SkyGradientStop>();
            if (obj["gradientStops"] is JsonArray stopArr)
            {
                for (int i = 0; i < stopArr.Count; i++)
                {
                    if (stopArr[i] is not JsonObject stopObj)
                    {
                        throw new InvalidOperationException(
                            $"{DefaultRelativePath} entry '{id}' gradientStops[{i}] must be an object.");
                    }

                    float phase = ReadFloat(stopObj["phase"], float.NaN);
                    if (!float.IsFinite(phase))
                    {
                        throw new InvalidOperationException(
                            $"{DefaultRelativePath} entry '{id}' gradientStops[{i}] must declare phase.");
                    }

                    phase = Math.Clamp(phase, 0f, 1f);
                    Vector3 zenith = ReadRgb(stopObj["zenith"], id, i, "zenith");
                    Vector3 horizon = ReadRgb(stopObj["horizon"], id, i, "horizon");
                    stops.Add(new SkyGradientStop(phase, zenith, horizon));
                }

                stops.Sort(static (a, b) => a.Phase.CompareTo(b.Phase));
            }

            if (string.IsNullOrWhiteSpace(gradientUri) && stops.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{id}' must declare gradientUri or non-empty gradientStops.");
            }

            if (!string.IsNullOrWhiteSpace(gradientUri) && stops.Count == 0 && !clearRgb.HasValue)
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{id}' with gradientUri must declare clearRgb or gradientStops.");
            }

            return new SkyEnvironmentDescriptor(
                id,
                backendId,
                enabled,
                mapIds,
                gradientUri,
                stops,
                gradientWidth,
                gradientHeight,
                hasInitialPhase,
                initialPhase,
                clearSampleV,
                clearRgb);
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

        private static int ReadInt(JsonNode? node, int fallback)
        {
            if (node == null)
            {
                return fallback;
            }

            return node.GetValue<int>();
        }

        private static Vector3 ReadRgb(JsonNode? node, string rowId, int stopIndex, string fieldName)
        {
            string label = stopIndex < 0
                ? fieldName
                : $"gradientStops[{stopIndex}].{fieldName}";
            if (node is not JsonArray arr || arr.Count != 3)
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{rowId}' {label} must be an RGB array of 3 numbers in 0..1.");
            }

            float r = arr[0]!.GetValue<float>();
            float g = arr[1]!.GetValue<float>();
            float b = arr[2]!.GetValue<float>();
            if (!float.IsFinite(r) || !float.IsFinite(g) || !float.IsFinite(b))
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{rowId}' {label} contains non-finite values.");
            }

            return new Vector3(Math.Clamp(r, 0f, 1f), Math.Clamp(g, 0f, 1f), Math.Clamp(b, 0f, 1f));
        }

        private static byte ToByte(float unit)
        {
            return (byte)Math.Clamp((int)MathF.Round(unit * 255f), 0, 255);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibSkyEnvironment));
            }
        }

        private sealed class SkyEnvironmentDescriptor
        {
            public SkyEnvironmentDescriptor(
                string id,
                string backendId,
                bool enabled,
                List<string> mapIds,
                string? gradientUri,
                List<SkyGradientStop> gradientStops,
                int gradientWidth,
                int gradientHeight,
                bool hasInitialPhase,
                float initialPhase,
                float clearSampleV,
                Color? clearRgb)
            {
                Id = id;
                BackendId = backendId;
                Enabled = enabled;
                MapIds = mapIds;
                GradientUri = gradientUri;
                GradientStops = gradientStops;
                GradientWidth = gradientWidth;
                GradientHeight = gradientHeight;
                HasInitialPhase = hasInitialPhase;
                InitialPhase = initialPhase;
                ClearSampleV = clearSampleV;
                ClearRgb = clearRgb;
            }

            public string Id { get; }
            public string BackendId { get; }
            public bool Enabled { get; }
            public IReadOnlyList<string> MapIds { get; }
            public string? GradientUri { get; }
            public IReadOnlyList<SkyGradientStop> GradientStops { get; }
            public int GradientWidth { get; }
            public int GradientHeight { get; }
            public bool HasInitialPhase { get; }
            public float InitialPhase { get; }
            public float ClearSampleV { get; }
            public Color? ClearRgb { get; }

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

        private readonly struct SkyGradientStop
        {
            public SkyGradientStop(float phase, Vector3 zenith, Vector3 horizon)
            {
                Phase = phase;
                Zenith = zenith;
                Horizon = horizon;
            }

            public float Phase { get; }
            public Vector3 Zenith { get; }
            public Vector3 Horizon { get; }
        }
    }
}

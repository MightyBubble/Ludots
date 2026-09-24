using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib.Effekseer
{
    internal readonly record struct RaylibEmitterSnapshotItem(
        int StableId,
        int AssetId,
        EffekseerNodeType NodeType,
        string FullPath,
        string Sha256,
        Vector3 Position,
        Quaternion Rotation,
        Vector3 Scale,
        Vector4 Color,
        VisualVisibility Visibility,
        bool HasTarget,
        Vector3 TargetPosition,
        byte DynamicInputCount,
        float DynamicInput0,
        float DynamicInput1,
        float DynamicInput2,
        float DynamicInput3)
    {
        public float GetDynamicInput(int index)
        {
            return index switch
            {
                0 => DynamicInput0,
                1 => DynamicInput1,
                2 => DynamicInput2,
                3 => DynamicInput3,
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };
        }
    }

    internal interface IRaylibEmitterSnapshotResolver
    {
        bool TryResolve(in PrimitiveDrawItem item, out RaylibEmitterSnapshotItem emitter);
    }

    internal sealed class RaylibEffekseerRuntime : IDisposable
    {
        internal const int DefaultMaxInstances = 8192;
        internal const int DefaultMaxSquares = 32768;

        private readonly IEffekseerNativeApi _native;
        private readonly IRaylibEmitterSnapshotResolver _resolver;
        private readonly Dictionary<int, InstanceState> _instances = new();
        private readonly Dictionary<int, LoadedAssetState> _loadedAssets = new();
        private readonly List<int> _staleStableIds = new();
        private RaylibOpenGlStateScope? _openGlState;
        private IntPtr _context;
        private int _touchGeneration;
        private bool _hasSynchronized;
        private bool _updatedSinceSync;
        private bool _disposed;

        public RaylibEffekseerRuntime(
            IRaylibEmitterSnapshotResolver resolver,
            IEffekseerNativeApi? native = null,
            int maxInstances = DefaultMaxInstances,
            int maxSquares = DefaultMaxSquares)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _native = native ?? new EffekseerNativeLibrary();
            try
            {
                _context = ExecuteWithOpenGlState(
                    () => _native.Create(maxInstances, maxSquares));
            }
            catch
            {
                _native.Dispose();
                throw;
            }
        }

        internal int InstanceCount => _instances.Count;
        internal int LoadedAssetCount => _loadedAssets.Count;

        public void Sync(PrimitiveDrawBuffer snapshot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(snapshot);
            if (_hasSynchronized && !_updatedSinceSync)
            {
                throw new InvalidOperationException(
                    "Effekseer Sync was called again before Update. HostLoop must execute exactly one Sync and one Update per frame.");
            }

            _touchGeneration = checked(_touchGeneration + 1);
            _updatedSinceSync = false;
            _hasSynchronized = true;

            ReadOnlySpan<PrimitiveDrawItem> items = snapshot.GetSpan();
            for (int i = 0; i < items.Length; i++)
            {
                ref readonly PrimitiveDrawItem item = ref items[i];
                if (!_resolver.TryResolve(in item, out RaylibEmitterSnapshotItem emitter))
                {
                    continue;
                }

                ValidateSnapshotItem(in emitter);
                if (_instances.TryGetValue(emitter.StableId, out InstanceState? state) &&
                    state.LastTouched == _touchGeneration)
                {
                    throw new InvalidOperationException(
                        $"Effekseer snapshot contains duplicate stableId={emitter.StableId}.");
                }

                if (state == null || state.AssetId != emitter.AssetId || state.NodeType != emitter.NodeType)
                {
                    if (state != null)
                    {
                        StopIfAlive(state.Handle);
                    }

                    EnsureAssetLoaded(in emitter);
                    int handle = _native.Play(_context, emitter.AssetId, emitter.Position);
                    state = new InstanceState(emitter.AssetId, emitter.NodeType, handle);
                    _instances[emitter.StableId] = state;
                }
                else if (!state.CompletedOnce && !_native.Exists(_context, state.Handle))
                {
                    state.CompletedOnce = true;
                }

                state.LastTouched = _touchGeneration;
                if (!state.CompletedOnce)
                {
                    ApplySnapshot(state.Handle, in emitter);
                }
            }

            RemoveUntouchedInstances();
        }

        public void Update(float deltaSeconds)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_hasSynchronized)
            {
                throw new InvalidOperationException("Effekseer Update requires Sync earlier in the same host frame.");
            }
            if (_updatedSinceSync)
            {
                throw new InvalidOperationException("Effekseer Update may execute only once per host frame.");
            }

            _native.Update(_context, deltaSeconds);
            _updatedSinceSync = true;
        }

        public void Draw(
            in Camera3D camera,
            float aspect,
            float nearPlane,
            float farPlane)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_hasSynchronized || !_updatedSinceSync)
            {
                throw new InvalidOperationException("Effekseer Draw requires Sync and Update earlier in the same host frame.");
            }
            if (camera.projection != CameraProjection.CAMERA_PERSPECTIVE)
            {
                throw new NotSupportedException("Effekseer native bridge currently supports perspective cameras only.");
            }
            if (_instances.Count == 0)
            {
                return;
            }

            Vector3 cameraPosition = camera.position;
            Vector3 cameraTarget = camera.target;
            Vector3 cameraUp = camera.up;
            float verticalFovRadians = camera.fovy * (MathF.PI / 180f);
            ExecuteWithOpenGlState(() =>
            {
                _native.DrawPerspective(
                    _context,
                    cameraPosition,
                    cameraTarget,
                    cameraUp,
                    verticalFovRadians,
                    aspect,
                    nearPlane,
                    farPlane);
            });
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            List<Exception>? failures = null;
            foreach (InstanceState instance in _instances.Values)
            {
                try
                {
                    StopIfAlive(instance.Handle);
                }
                catch (Exception ex)
                {
                    (failures ??= new List<Exception>()).Add(ex);
                }
            }
            _instances.Clear();

            foreach (int assetId in _loadedAssets.Keys)
            {
                try
                {
                    ExecuteWithOpenGlState(() => _native.UnloadAsset(_context, assetId));
                }
                catch (Exception ex)
                {
                    (failures ??= new List<Exception>()).Add(ex);
                }
            }
            _loadedAssets.Clear();

            try
            {
                ExecuteWithOpenGlState(() => _native.Destroy(_context));
                _context = IntPtr.Zero;
            }
            catch (Exception ex)
            {
                (failures ??= new List<Exception>()).Add(ex);
            }

            try
            {
                _native.Dispose();
            }
            catch (Exception ex)
            {
                (failures ??= new List<Exception>()).Add(ex);
            }

            if (failures is { Count: > 0 })
            {
                throw new AggregateException("Effekseer runtime disposal failed.", failures);
            }
        }

        private void EnsureAssetLoaded(in RaylibEmitterSnapshotItem emitter)
        {
            string fullPath = emitter.FullPath;
            if (_loadedAssets.TryGetValue(emitter.AssetId, out LoadedAssetState? loaded))
            {
                if (loaded.NodeType != emitter.NodeType ||
                    !string.Equals(loaded.FullPath, fullPath, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(loaded.Sha256, emitter.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Emitter assetId={emitter.AssetId} resolved inconsistently. " +
                        $"Loaded type={loaded.NodeType} path='{loaded.FullPath}' sha256={loaded.Sha256}, " +
                        $"current type={emitter.NodeType} path='{fullPath}' sha256={emitter.Sha256}.");
                }
                return;
            }

            VerifySha256(fullPath, emitter.Sha256, emitter.AssetId);
            int assetId = emitter.AssetId;
            EffekseerNodeType nodeType = emitter.NodeType;
            ExecuteWithOpenGlState(
                () => _native.LoadAsset(_context, assetId, fullPath, nodeType));
            _loadedAssets.Add(
                emitter.AssetId,
                new LoadedAssetState(emitter.NodeType, fullPath, emitter.Sha256));
        }

        private static void VerifySha256(string fullPath, string expectedSha256, int assetId)
        {
            byte[] expected;
            try
            {
                expected = Convert.FromHexString(expectedSha256);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"Emitter assetId={assetId} has invalid configured SHA-256 '{expectedSha256}'.",
                    ex);
            }
            if (expected.Length != SHA256.HashSizeInBytes)
            {
                throw new InvalidOperationException(
                    $"Emitter assetId={assetId} configured SHA-256 must decode to {SHA256.HashSizeInBytes} bytes.");
            }

            Span<byte> actual = stackalloc byte[SHA256.HashSizeInBytes];
            using (FileStream stream = new(
                       fullPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       bufferSize: 64 * 1024,
                       FileOptions.SequentialScan))
            {
                SHA256.HashData(stream, actual);
            }

            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Emitter assetId={assetId} SHA-256 mismatch for '{fullPath}'. " +
                    $"Expected {expectedSha256}, actual {Convert.ToHexString(actual).ToLowerInvariant()}. Native LoadAsset was not called.");
            }
        }

        private T ExecuteWithOpenGlState<T>(Func<T> action)
        {
            if (_native is not EffekseerNativeLibrary)
            {
                return action();
            }

            RaylibOpenGlStateScope state = _openGlState ??= new RaylibOpenGlStateScope();
            state.ThrowIfError("entering the Effekseer OpenGL boundary before flushing the Raylib render batch");
            Rl.rlDrawRenderBatchActive();
            state.ThrowIfError("flushing the Raylib render batch before Effekseer");
            state.Capture();
            T result = default!;
            Exception? actionFailure = null;
            try
            {
                result = action();
                state.ThrowIfError("executing the Effekseer native OpenGL operation");
            }
            catch (Exception ex)
            {
                actionFailure = ex;
            }

            try
            {
                state.Restore();
            }
            catch (Exception restoreFailure)
            {
                if (actionFailure != null)
                {
                    throw new AggregateException(
                        "Effekseer native operation failed and Raylib OpenGL state restoration also failed.",
                        actionFailure,
                        restoreFailure);
                }
                throw;
            }

            if (actionFailure != null)
            {
                ExceptionDispatchInfo.Capture(actionFailure).Throw();
            }
            return result;
        }

        private void ExecuteWithOpenGlState(Action action)
        {
            ExecuteWithOpenGlState(() =>
            {
                action();
                return true;
            });
        }

        private void ApplySnapshot(int handle, in RaylibEmitterSnapshotItem emitter)
        {
            _native.SetTransform(
                _context,
                handle,
                emitter.Position,
                QuaternionToEulerRadians(emitter.Rotation),
                emitter.Scale);
            _native.SetTarget(
                _context,
                handle,
                emitter.HasTarget ? emitter.TargetPosition : emitter.Position);
            _native.SetColor(
                _context,
                handle,
                ToColorByte(emitter.Color.X, nameof(emitter.Color.X)),
                ToColorByte(emitter.Color.Y, nameof(emitter.Color.Y)),
                ToColorByte(emitter.Color.Z, nameof(emitter.Color.Z)),
                ToColorByte(emitter.Color.W, nameof(emitter.Color.W)));
            for (int i = 0; i < emitter.DynamicInputCount; i++)
            {
                _native.SetDynamicInput(_context, handle, i, emitter.GetDynamicInput(i));
            }
            _native.SetShown(_context, handle, emitter.Visibility == VisualVisibility.Visible);
        }

        private void RemoveUntouchedInstances()
        {
            _staleStableIds.Clear();
            foreach ((int stableId, InstanceState state) in _instances)
            {
                if (state.LastTouched != _touchGeneration)
                {
                    _staleStableIds.Add(stableId);
                }
            }

            for (int i = 0; i < _staleStableIds.Count; i++)
            {
                int stableId = _staleStableIds[i];
                InstanceState state = _instances[stableId];
                StopIfAlive(state.Handle);
                _instances.Remove(stableId);
            }
        }

        private void StopIfAlive(int handle)
        {
            if (_context != IntPtr.Zero && _native.Exists(_context, handle))
            {
                _native.Stop(_context, handle);
            }
        }

        private static void ValidateSnapshotItem(in RaylibEmitterSnapshotItem emitter)
        {
            if (emitter.StableId <= 0)
            {
                throw new InvalidOperationException($"Emitter snapshot requires positive stableId, got {emitter.StableId}.");
            }
            if (emitter.AssetId <= 0)
            {
                throw new InvalidOperationException(
                    $"Emitter stableId={emitter.StableId} requires positive assetId, got {emitter.AssetId}.");
            }
            if (string.IsNullOrWhiteSpace(emitter.FullPath) || !System.IO.Path.IsPathFullyQualified(emitter.FullPath))
            {
                throw new InvalidOperationException(
                    $"Emitter assetId={emitter.AssetId} must resolve to an absolute physical path, got '{emitter.FullPath}'.");
            }
            if (string.IsNullOrWhiteSpace(emitter.Sha256) || emitter.Sha256.Length != 64)
            {
                throw new InvalidOperationException(
                    $"Emitter assetId={emitter.AssetId} requires a fixed 64-character SHA-256 digest.");
            }
            if (!IsFinite(emitter.Position))
            {
                throw new InvalidOperationException($"Emitter stableId={emitter.StableId} position must contain finite components.");
            }
            if (!IsFinite(emitter.Scale))
            {
                throw new InvalidOperationException($"Emitter stableId={emitter.StableId} scale must contain finite components.");
            }
            if (!IsFinite(emitter.Color))
            {
                throw new InvalidOperationException($"Emitter stableId={emitter.StableId} color must contain finite components.");
            }
            if (!float.IsFinite(emitter.Rotation.X) ||
                !float.IsFinite(emitter.Rotation.Y) ||
                !float.IsFinite(emitter.Rotation.Z) ||
                !float.IsFinite(emitter.Rotation.W) ||
                emitter.Rotation.LengthSquared() < 1e-12f)
            {
                throw new InvalidOperationException($"Emitter stableId={emitter.StableId} rotation must be a finite non-zero quaternion.");
            }
            if (emitter.HasTarget)
            {
                if (!IsFinite(emitter.TargetPosition))
                {
                    throw new InvalidOperationException($"Emitter stableId={emitter.StableId} target must contain finite components.");
                }
            }
            if (emitter.DynamicInputCount > 4)
            {
                throw new InvalidOperationException(
                    $"Emitter stableId={emitter.StableId} exposes {emitter.DynamicInputCount} dynamic inputs; ABI supports at most 4.");
            }
            for (int i = 0; i < emitter.DynamicInputCount; i++)
            {
                if (!float.IsFinite(emitter.GetDynamicInput(i)))
                {
                    throw new InvalidOperationException(
                        $"Emitter stableId={emitter.StableId} dynamic input {i} must be finite.");
                }
            }
        }

        private static Vector3 QuaternionToEulerRadians(Quaternion rotation)
        {
            rotation = Quaternion.Normalize(rotation);

            float x = MathF.Atan2(
                2f * ((rotation.W * rotation.X) + (rotation.Y * rotation.Z)),
                1f - (2f * ((rotation.X * rotation.X) + (rotation.Y * rotation.Y))));
            float sinY = 2f * ((rotation.W * rotation.Y) - (rotation.Z * rotation.X));
            float y = MathF.Asin(Math.Clamp(sinY, -1f, 1f));
            float z = MathF.Atan2(
                2f * ((rotation.W * rotation.Z) + (rotation.X * rotation.Y)),
                1f - (2f * ((rotation.Y * rotation.Y) + (rotation.Z * rotation.Z))));
            return new Vector3(x, y, z);
        }

        private static byte ToColorByte(float value, string component)
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
            {
                throw new InvalidOperationException($"Emitter color component {component} must be in [0, 1], got {value}.");
            }
            return (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
        }

        private static bool IsFinite(Vector4 value)
        {
            return float.IsFinite(value.X) &&
                   float.IsFinite(value.Y) &&
                   float.IsFinite(value.Z) &&
                   float.IsFinite(value.W);
        }

        private sealed class InstanceState
        {
            public InstanceState(int assetId, EffekseerNodeType nodeType, int handle)
            {
                AssetId = assetId;
                NodeType = nodeType;
                Handle = handle;
            }

            public int AssetId { get; }
            public EffekseerNodeType NodeType { get; }
            public int Handle { get; }
            public int LastTouched { get; set; }
            public bool CompletedOnce { get; set; }
        }

        private sealed record LoadedAssetState(EffekseerNodeType NodeType, string FullPath, string Sha256);
    }
}

using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Ludots.Adapter.Raylib.Effekseer
{
    internal enum EffekseerNodeType
    {
        Sprite = 2,
        Ribbon = 3,
        Ring = 4,
        Model = 5,
        Track = 6,
    }

    internal sealed class EffekseerNativeException : InvalidOperationException
    {
        public EffekseerNativeException(string message)
            : base(message)
        {
        }
    }

    internal interface IEffekseerNativeApi : IDisposable
    {
        IntPtr Create(int maxInstances, int maxSquares);
        void Destroy(IntPtr context);
        void ValidateAsset(string fullPath, EffekseerNodeType expectedNodeType);
        void LoadAsset(IntPtr context, int assetId, string fullPath, EffekseerNodeType expectedNodeType);
        void UnloadAsset(IntPtr context, int assetId);
        int Play(IntPtr context, int assetId, Vector3 position);
        bool Exists(IntPtr context, int handle);
        void Stop(IntPtr context, int handle);
        void SetTransform(IntPtr context, int handle, Vector3 position, Vector3 rotationRadians, Vector3 scale);
        void SetTarget(IntPtr context, int handle, Vector3 target);
        void SetColor(IntPtr context, int handle, byte red, byte green, byte blue, byte alpha);
        void SetDynamicInput(IntPtr context, int handle, int index, float value);
        void SetShown(IntPtr context, int handle, bool shown);
        void Update(IntPtr context, float deltaSeconds);
        void DrawPerspective(
            IntPtr context,
            Vector3 cameraPosition,
            Vector3 cameraTarget,
            Vector3 cameraUp,
            float verticalFovRadians,
            float aspect,
            float nearPlane,
            float farPlane);
    }

    internal sealed class EffekseerNativeLibrary : IEffekseerNativeApi
    {
        internal const int RequiredApiVersion = 1;
        internal const string LibraryFileName = "ludots_effekseer.dll";

        private IntPtr _library;
        private readonly GetApiVersionDelegate _getApiVersion;
        private readonly GetLastErrorDelegate _getLastError;
        private readonly ValidateAssetDelegate _validateAsset;
        private readonly CreateDelegate _create;
        private readonly DestroyDelegate _destroy;
        private readonly LoadAssetDelegate _loadAsset;
        private readonly UnloadAssetDelegate _unloadAsset;
        private readonly PlayDelegate _play;
        private readonly ExistsDelegate _exists;
        private readonly StopDelegate _stop;
        private readonly SetTransformDelegate _setTransform;
        private readonly SetTargetDelegate _setTarget;
        private readonly SetColorDelegate _setColor;
        private readonly SetDynamicInputDelegate _setDynamicInput;
        private readonly SetShownDelegate _setShown;
        private readonly UpdateDelegate _update;
        private readonly DrawPerspectiveDelegate _drawPerspective;

        public EffekseerNativeLibrary(string? libraryPath = null)
        {
            if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                throw new PlatformNotSupportedException(
                    $"Effekseer native runtime requires Windows x64; current platform is {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}.");
            }

            string fullPath = ResolveLibraryPath(libraryPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Effekseer native bridge was not found at its required absolute path: {fullPath}",
                    fullPath);
            }

            try
            {
                _library = NativeLibrary.Load(fullPath);
                _getApiVersion = LoadExport<GetApiVersionDelegate>("ludots_effekseer_get_api_version");
                _getLastError = LoadExport<GetLastErrorDelegate>("ludots_effekseer_get_last_error");
                _validateAsset = LoadExport<ValidateAssetDelegate>("ludots_effekseer_validate_asset");
                _create = LoadExport<CreateDelegate>("ludots_effekseer_create");
                _destroy = LoadExport<DestroyDelegate>("ludots_effekseer_destroy");
                _loadAsset = LoadExport<LoadAssetDelegate>("ludots_effekseer_load_asset");
                _unloadAsset = LoadExport<UnloadAssetDelegate>("ludots_effekseer_unload_asset");
                _play = LoadExport<PlayDelegate>("ludots_effekseer_play");
                _exists = LoadExport<ExistsDelegate>("ludots_effekseer_exists");
                _stop = LoadExport<StopDelegate>("ludots_effekseer_stop");
                _setTransform = LoadExport<SetTransformDelegate>("ludots_effekseer_set_transform");
                _setTarget = LoadExport<SetTargetDelegate>("ludots_effekseer_set_target");
                _setColor = LoadExport<SetColorDelegate>("ludots_effekseer_set_color");
                _setDynamicInput = LoadExport<SetDynamicInputDelegate>("ludots_effekseer_set_dynamic_input");
                _setShown = LoadExport<SetShownDelegate>("ludots_effekseer_set_shown");
                _update = LoadExport<UpdateDelegate>("ludots_effekseer_update");
                _drawPerspective = LoadExport<DrawPerspectiveDelegate>("ludots_effekseer_draw_perspective");

                int actualApiVersion = _getApiVersion();
                if (actualApiVersion != RequiredApiVersion)
                {
                    throw new EffekseerNativeException(
                        $"Effekseer native ABI mismatch. Managed binding requires {RequiredApiVersion}, bridge reports {actualApiVersion} at '{fullPath}'.");
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public IntPtr Create(int maxInstances, int maxSquares)
        {
            ThrowIfDisposed();
            if (maxInstances <= 0) throw new ArgumentOutOfRangeException(nameof(maxInstances));
            if (maxSquares <= 0) throw new ArgumentOutOfRangeException(nameof(maxSquares));

            IntPtr context = _create(maxInstances, maxSquares);
            if (context == IntPtr.Zero)
            {
                ThrowNativeFailure(IntPtr.Zero, "create context");
            }

            return context;
        }

        public void Destroy(IntPtr context)
        {
            ThrowIfDisposed();
            if (context != IntPtr.Zero)
            {
                _destroy(context);
            }
        }

        public void ValidateAsset(string fullPath, EffekseerNodeType expectedNodeType)
        {
            ThrowIfDisposed();
            fullPath = RequireExistingAbsoluteAssetPath(fullPath);
            if (_validateAsset(fullPath, (int)expectedNodeType) == 0)
            {
                ThrowNativeFailure(IntPtr.Zero, $"validate {expectedNodeType} asset '{fullPath}'");
            }
        }

        public void LoadAsset(IntPtr context, int assetId, string fullPath, EffekseerNodeType expectedNodeType)
        {
            ThrowIfDisposed();
            RequireContext(context);
            if (assetId <= 0) throw new ArgumentOutOfRangeException(nameof(assetId));
            fullPath = RequireExistingAbsoluteAssetPath(fullPath);
            if (_loadAsset(context, assetId, fullPath, (int)expectedNodeType) == 0)
            {
                ThrowNativeFailure(context, $"load assetId={assetId} type={expectedNodeType} path='{fullPath}'");
            }
        }

        public void UnloadAsset(IntPtr context, int assetId)
        {
            ThrowIfDisposed();
            RequireContext(context);
            if (_unloadAsset(context, assetId) == 0)
            {
                ThrowNativeFailure(context, $"unload assetId={assetId}");
            }
        }

        public int Play(IntPtr context, int assetId, Vector3 position)
        {
            ThrowIfDisposed();
            RequireContext(context);
            RequireFinite(position, nameof(position));
            int handle = _play(context, assetId, position.X, position.Y, position.Z);
            if (handle < 0)
            {
                ThrowNativeFailure(context, $"play assetId={assetId}");
            }

            return handle;
        }

        public bool Exists(IntPtr context, int handle)
        {
            ThrowIfDisposed();
            RequireContext(context);
            if (handle < 0) throw new ArgumentOutOfRangeException(nameof(handle));
            return _exists(context, handle) != 0;
        }

        public void Stop(IntPtr context, int handle)
        {
            ThrowIfDisposed();
            RequireContext(context);
            if (_stop(context, handle) == 0)
            {
                ThrowNativeFailure(context, $"stop handle={handle}");
            }
        }

        public void SetTransform(IntPtr context, int handle, Vector3 position, Vector3 rotationRadians, Vector3 scale)
        {
            ThrowIfDisposed();
            RequireContext(context);
            RequireFinite(position, nameof(position));
            RequireFinite(rotationRadians, nameof(rotationRadians));
            RequireFinite(scale, nameof(scale));
            if (_setTransform(
                    context,
                    handle,
                    position.X,
                    position.Y,
                    position.Z,
                    rotationRadians.X,
                    rotationRadians.Y,
                    rotationRadians.Z,
                    scale.X,
                    scale.Y,
                    scale.Z) == 0)
            {
                ThrowNativeFailure(context, $"set transform handle={handle}");
            }
        }

        public void SetTarget(IntPtr context, int handle, Vector3 target)
        {
            ThrowIfDisposed();
            RequireContext(context);
            RequireFinite(target, nameof(target));
            if (_setTarget(context, handle, target.X, target.Y, target.Z) == 0)
            {
                ThrowNativeFailure(context, $"set target handle={handle}");
            }
        }

        public void SetColor(IntPtr context, int handle, byte red, byte green, byte blue, byte alpha)
        {
            ThrowIfDisposed();
            RequireContext(context);
            if (_setColor(context, handle, red, green, blue, alpha) == 0)
            {
                ThrowNativeFailure(context, $"set color handle={handle}");
            }
        }

        public void SetDynamicInput(IntPtr context, int handle, int index, float value)
        {
            ThrowIfDisposed();
            RequireContext(context);
            if (index is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(index));
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "Dynamic input must be finite.");
            if (_setDynamicInput(context, handle, index, value) == 0)
            {
                ThrowNativeFailure(context, $"set dynamic input {index} on handle={handle}");
            }
        }

        public void SetShown(IntPtr context, int handle, bool shown)
        {
            ThrowIfDisposed();
            RequireContext(context);
            if (_setShown(context, handle, shown ? 1 : 0) == 0)
            {
                ThrowNativeFailure(context, $"set shown={shown} on handle={handle}");
            }
        }

        public void Update(IntPtr context, float deltaSeconds)
        {
            ThrowIfDisposed();
            RequireContext(context);
            if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "Update delta must be finite and non-negative.");
            }
            if (_update(context, deltaSeconds) == 0)
            {
                ThrowNativeFailure(context, "update");
            }
        }

        public void DrawPerspective(
            IntPtr context,
            Vector3 cameraPosition,
            Vector3 cameraTarget,
            Vector3 cameraUp,
            float verticalFovRadians,
            float aspect,
            float nearPlane,
            float farPlane)
        {
            ThrowIfDisposed();
            RequireContext(context);
            RequireFinite(cameraPosition, nameof(cameraPosition));
            RequireFinite(cameraTarget, nameof(cameraTarget));
            RequireFinite(cameraUp, nameof(cameraUp));
            if (_drawPerspective(
                    context,
                    cameraPosition.X,
                    cameraPosition.Y,
                    cameraPosition.Z,
                    cameraTarget.X,
                    cameraTarget.Y,
                    cameraTarget.Z,
                    cameraUp.X,
                    cameraUp.Y,
                    cameraUp.Z,
                    verticalFovRadians,
                    aspect,
                    nearPlane,
                    farPlane) == 0)
            {
                ThrowNativeFailure(context, "draw perspective");
            }
        }

        public void Dispose()
        {
            IntPtr library = _library;
            _library = IntPtr.Zero;
            if (library != IntPtr.Zero)
            {
                NativeLibrary.Free(library);
            }
        }

        internal static string ResolveLibraryPath(string? libraryPath)
        {
            string candidate = string.IsNullOrWhiteSpace(libraryPath)
                ? Path.Combine(AppContext.BaseDirectory, LibraryFileName)
                : libraryPath;
            return Path.GetFullPath(candidate);
        }

        private T LoadExport<T>(string exportName)
            where T : Delegate
        {
            if (!NativeLibrary.TryGetExport(_library, exportName, out IntPtr address) || address == IntPtr.Zero)
            {
                throw new EntryPointNotFoundException(
                    $"Effekseer native bridge is missing required export '{exportName}'.");
            }

            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        private void ThrowNativeFailure(IntPtr context, string operation)
        {
            IntPtr errorPointer = _getLastError(context);
            string error = errorPointer == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringUTF8(errorPointer) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(error))
            {
                error = "The native bridge returned failure without an error message, which violates the ABI contract.";
            }

            throw new EffekseerNativeException($"Effekseer failed to {operation}: {error}");
        }

        private static string RequireExistingAbsoluteAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Emitter asset path is required.", nameof(path));
            }

            if (!Path.IsPathFullyQualified(path))
            {
                throw new ArgumentException($"Emitter asset path must be absolute: '{path}'.", nameof(path));
            }

            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Emitter asset was not found: {fullPath}", fullPath);
            }

            return fullPath;
        }

        private static void RequireContext(IntPtr context)
        {
            if (context == IntPtr.Zero)
            {
                throw new ArgumentException("Effekseer context must not be null.", nameof(context));
            }
        }

        private static void RequireFinite(Vector3 value, string name)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            {
                throw new ArgumentOutOfRangeException(name, $"{name} must contain finite components.");
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_library == IntPtr.Zero, this);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetApiVersionDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetLastErrorDelegate(IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int ValidateAssetDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string pathUtf8, int expectedNodeType);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr CreateDelegate(int maxInstances, int maxSquares);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DestroyDelegate(IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int LoadAssetDelegate(IntPtr context, int assetId, [MarshalAs(UnmanagedType.LPUTF8Str)] string pathUtf8, int expectedNodeType);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int UnloadAssetDelegate(IntPtr context, int assetId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PlayDelegate(IntPtr context, int assetId, float x, float y, float z);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ExistsDelegate(IntPtr context, int handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int StopDelegate(IntPtr context, int handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetTransformDelegate(
            IntPtr context,
            int handle,
            float x,
            float y,
            float z,
            float rotationX,
            float rotationY,
            float rotationZ,
            float scaleX,
            float scaleY,
            float scaleZ);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetTargetDelegate(IntPtr context, int handle, float x, float y, float z);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetColorDelegate(IntPtr context, int handle, byte red, byte green, byte blue, byte alpha);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetDynamicInputDelegate(IntPtr context, int handle, int index, float value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetShownDelegate(IntPtr context, int handle, int shown);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int UpdateDelegate(IntPtr context, float deltaSeconds);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DrawPerspectiveDelegate(
            IntPtr context,
            float cameraX,
            float cameraY,
            float cameraZ,
            float targetX,
            float targetY,
            float targetZ,
            float upX,
            float upY,
            float upZ,
            float verticalFovRadians,
            float aspect,
            float nearPlane,
            float farPlane);
    }
}

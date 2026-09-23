using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Ludots.Client.WebGpu.Runtime
{
    /// <summary>
    /// Fail-fast checks for the wgpu-native library shipped by Silk.NET.WebGPU.Native.WGPU.
    /// </summary>
    public static class WebGpuNativeRuntimeGuard
    {
        public const string WindowsLibraryFileName = "wgpu_native.dll";
        public const string LinuxLibraryFileName = "libwgpu_native.so";
        public const string MacLibraryFileName = "libwgpu_native.dylib";

        public static string ResolveExpectedLibraryFileName()
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsLibraryFileName;
            }

            if (OperatingSystem.IsLinux())
            {
                return LinuxLibraryFileName;
            }

            if (OperatingSystem.IsMacOS())
            {
                return MacLibraryFileName;
            }

            throw new PlatformNotSupportedException(
                "WebGPU adapter requires Windows, Linux, or macOS with a wgpu-native runtime package.");
        }

        public static void EnsureNativeLibraryLoadable(string? probeDirectory = null)
        {
            string searchRoot = string.IsNullOrWhiteSpace(probeDirectory)
                ? AppContext.BaseDirectory
                : probeDirectory;
            string candidatePath = ResolveNativeLibraryPath(searchRoot);

            try
            {
                if (!NativeLibrary.TryLoad(candidatePath, out IntPtr handle) || handle == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        $"WebGPU native runtime failed to load from '{candidatePath}'. " +
                        "The file exists but could not be mapped by the OS loader. " +
                        "No alternate graphics backend will be used.");
                }

                NativeLibrary.Free(handle);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"WebGPU native runtime failed to load from '{candidatePath}': {ex.Message}. " +
                    "No alternate graphics backend will be used.",
                    ex);
            }
        }

        public static string ResolveNativeLibraryPath(string searchRoot)
        {
            if (string.IsNullOrWhiteSpace(searchRoot))
            {
                throw new ArgumentException("WebGPU native runtime search root is required.", nameof(searchRoot));
            }

            string libraryFileName = ResolveExpectedLibraryFileName();
            string candidatePath = Path.Combine(searchRoot, libraryFileName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }

            string? ridNative = FindUnderRuntimes(searchRoot, libraryFileName);
            if (ridNative != null)
            {
                return ridNative;
            }

            throw new InvalidOperationException(
                "WebGPU native runtime is missing. " +
                $"Expected '{libraryFileName}' next to the app or under runtimes/{ResolveExpectedRuntimeNativeDirectoryName()}/native " +
                $"(search root: '{searchRoot}'). " +
                "Install/restore NuGet package Silk.NET.WebGPU.Native.WGPU so wgpu-native is copied to the output. " +
                "No Raylib/WebGL/OpenGL/Vulkan/Direct3D fallback is available.");
        }

        private static string? FindUnderRuntimes(string searchRoot, string libraryFileName)
        {
            string path = Path.Combine(
                searchRoot,
                "runtimes",
                ResolveExpectedRuntimeNativeDirectoryName(),
                "native",
                libraryFileName);
            return File.Exists(path) ? path : null;
        }

        public static string ResolveExpectedRuntimeNativeDirectoryName()
        {
            string os = ResolveRuntimeOs();
            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => throw new PlatformNotSupportedException(
                    $"WebGPU adapter does not support process architecture {RuntimeInformation.ProcessArchitecture}.")
            };

            return $"{os}-{arch}";
        }

        private static string ResolveRuntimeOs()
        {
            if (OperatingSystem.IsWindows())
            {
                return "win";
            }

            if (OperatingSystem.IsLinux())
            {
                return "linux";
            }

            if (OperatingSystem.IsMacOS())
            {
                return "osx";
            }

            throw new PlatformNotSupportedException(
                "WebGPU adapter requires Windows, Linux, or macOS with a wgpu-native runtime package.");
        }
    }
}

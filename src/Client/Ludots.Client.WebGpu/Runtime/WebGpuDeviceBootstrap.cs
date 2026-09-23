using System;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;

namespace Ludots.Client.WebGpu.Runtime
{
    public static unsafe class WebGpuDeviceBootstrap
    {
        public static void EnsureAdapter(Adapter* adapter, string? message)
        {
            if (adapter == null)
            {
                string detail = string.IsNullOrWhiteSpace(message) ? "no message from wgpu" : message.Trim();
                throw new InvalidOperationException(
                    "WebGPU adapter request failed: no compatible GPU adapter was returned. " +
                    $"Detail: {detail}. " +
                    "Install a GPU/driver that supports wgpu-native backends, keep Silk.NET.WebGPU.Native.WGPU restored, " +
                    "and do not expect an automatic switch to Raylib/WebGL/OpenGL/Vulkan/Direct3D.");
            }
        }

        public static void EnsureDevice(Device* device, string? message)
        {
            if (device == null)
            {
                string detail = string.IsNullOrWhiteSpace(message) ? "no message from wgpu" : message.Trim();
                throw new InvalidOperationException(
                    "WebGPU device request failed: adapter could not create a device. " +
                    $"Detail: {detail}. " +
                    "No alternate graphics backend will be used.");
            }
        }

        public static void EnsureSurface(Surface* surface)
        {
            if (surface == null)
            {
                throw new InvalidOperationException(
                    "WebGPU surface creation failed: window did not yield a valid Surface*. " +
                    "No alternate graphics backend will be used.");
            }
        }

        public static void EnsureQueue(Queue* queue)
        {
            if (queue == null)
            {
                throw new InvalidOperationException(
                    "WebGPU device queue is null after DeviceGetQueue. " +
                    "No alternate graphics backend will be used.");
            }
        }

        public static string? PtrToManagedString(byte* ptr)
        {
            return ptr == null ? null : SilkMarshal.PtrToString((nint)ptr);
        }
    }
}

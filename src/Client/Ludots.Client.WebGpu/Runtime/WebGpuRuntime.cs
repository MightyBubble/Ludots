using System;
using System.Runtime.InteropServices;
using Ludots.Client.WebGpu.Rendering;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;

namespace Ludots.Client.WebGpu.Runtime
{
    public sealed unsafe class WebGpuRuntime : IDisposable
    {
        private readonly IWindow _window;
        private WebGPU _api = null!;
        private Instance* _instance;
        private Surface* _surface;
        private Adapter* _adapter;
        private Device* _device;
        private Queue* _queue;
        private TextureFormat _surfaceFormat;
        private SurfaceConfiguration _surfaceConfiguration;
        private bool _configured;
        private bool _disposed;
        private string? _lastAdapterMessage;
        private string? _lastDeviceMessage;

        public WebGpuRuntime(IWindow window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
        }

        public WebGPU Api => _api;
        public Device* Device => _device;
        public Queue* Queue => _queue;
        public Surface* Surface => _surface;
        public TextureFormat SurfaceFormat => _surfaceFormat;
        public WebGpuWorldRenderer? WorldRenderer { get; private set; }
        public WebGpuUiRenderer? UiRenderer { get; private set; }

        public void Initialize()
        {
            WebGpuNativeRuntimeGuard.EnsureNativeLibraryLoadable();

            try
            {
                _api = WebGPU.GetApi();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Failed to load Silk.NET.WebGPU API bindings / native entry points. " +
                    "Ensure Silk.NET.WebGPU and Silk.NET.WebGPU.Native.WGPU are restored. " +
                    $"Inner: {ex.Message}",
                    ex);
            }

            InstanceDescriptor instanceDescriptor = new InstanceDescriptor();
            _instance = _api.CreateInstance(&instanceDescriptor);
            if (_instance == null)
            {
                throw new InvalidOperationException(
                    "WebGPU CreateInstance returned null. wgpu-native may be missing or incompatible. " +
                    "No alternate graphics backend will be used.");
            }

            _surface = _window.CreateWebGPUSurface(_api, _instance);
            WebGpuDeviceBootstrap.EnsureSurface(_surface);

            RequestAdapterOptions adapterOptions = new RequestAdapterOptions
            {
                CompatibleSurface = _surface
            };

            _lastAdapterMessage = null;
            _adapter = null;
            _api.InstanceRequestAdapter(
                _instance,
                in adapterOptions,
                new PfnRequestAdapterCallback(OnAdapterReady),
                null);
            WebGpuDeviceBootstrap.EnsureAdapter(_adapter, _lastAdapterMessage);

            DeviceDescriptor deviceDescriptor = new DeviceDescriptor
            {
                DeviceLostCallback = new PfnDeviceLostCallback(OnDeviceLost)
            };

            _lastDeviceMessage = null;
            _device = null;
            _api.AdapterRequestDevice(
                _adapter,
                in deviceDescriptor,
                new PfnRequestDeviceCallback(OnDeviceReady),
                null);
            WebGpuDeviceBootstrap.EnsureDevice(_device, _lastDeviceMessage);

            _api.DeviceSetUncapturedErrorCallback(_device, new PfnErrorCallback(OnUncapturedError), null);
            _queue = _api.DeviceGetQueue(_device);
            WebGpuDeviceBootstrap.EnsureQueue(_queue);

            SurfaceCapabilities capabilities = default;
            _api.SurfaceGetCapabilities(_surface, _adapter, ref capabilities);
            if (capabilities.FormatCount == 0 || capabilities.Formats == null)
            {
                throw new InvalidOperationException(
                    "WebGPU surface reports zero usable texture formats for the current adapter. " +
                    "No alternate graphics backend will be used.");
            }

            _surfaceFormat = capabilities.Formats[0];
            ConfigureSurface();
            WorldRenderer = new WebGpuWorldRenderer(_api, _device, _queue, _surfaceFormat);
            WorldRenderer.Initialize();
            UiRenderer = new WebGpuUiRenderer(_api, _device, _queue, _surfaceFormat);
            UiRenderer.Initialize();
        }

        public void Resize(Vector2D<int> framebufferSize)
        {
            if (_disposed || _device == null || _surface == null)
            {
                return;
            }

            if (framebufferSize.X <= 0 || framebufferSize.Y <= 0)
            {
                throw new InvalidOperationException(
                    $"WebGPU framebuffer resize received invalid size {framebufferSize.X}x{framebufferSize.Y}.");
            }

            ConfigureSurface(framebufferSize);
        }

        public void PresentClearWorldAndUi(
            in Silk.NET.WebGPU.Color clearColor,
            ReadOnlySpan<WebGpuInstanceDraw> instances,
            in Matrix4x4Gpu viewProjection,
            ReadOnlySpan<byte> uiPixels,
            int uiWidth,
            int uiHeight,
            int uiBytesPerRow,
            bool hasUi,
            out string? frameDiagnostic)
        {
            frameDiagnostic = null;
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WebGpuRuntime));
            }

            if (!_configured)
            {
                throw new InvalidOperationException("WebGPU surface is not configured before present.");
            }

            SurfaceTexture surfaceTexture;
            _api.SurfaceGetCurrentTexture(_surface, &surfaceTexture);
            switch (surfaceTexture.Status)
            {
                case SurfaceGetCurrentTextureStatus.Timeout:
                case SurfaceGetCurrentTextureStatus.Outdated:
                case SurfaceGetCurrentTextureStatus.Lost:
                    if (surfaceTexture.Texture != null)
                    {
                        _api.TextureRelease(surfaceTexture.Texture);
                    }

                    ConfigureSurface();
                    frameDiagnostic = $"swapchain recreate required ({surfaceTexture.Status})";
                    return;
                case SurfaceGetCurrentTextureStatus.OutOfMemory:
                case SurfaceGetCurrentTextureStatus.DeviceLost:
                case SurfaceGetCurrentTextureStatus.Force32:
                    throw new InvalidOperationException(
                        $"WebGPU SurfaceGetCurrentTexture failed with status {surfaceTexture.Status}. " +
                        "No alternate graphics backend will be used.");
                case SurfaceGetCurrentTextureStatus.Success:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"WebGPU SurfaceGetCurrentTexture returned unexpected status {surfaceTexture.Status}.");
            }

            TextureView* view = _api.TextureCreateView(surfaceTexture.Texture, null);
            if (view == null)
            {
                _api.TextureRelease(surfaceTexture.Texture);
                throw new InvalidOperationException("WebGPU TextureCreateView returned null for the swapchain texture.");
            }

            CommandEncoderDescriptor encoderDescriptor = new CommandEncoderDescriptor();
            CommandEncoder* encoder = _api.DeviceCreateCommandEncoder(_device, in encoderDescriptor);
            if (encoder == null)
            {
                _api.TextureViewRelease(view);
                _api.TextureRelease(surfaceTexture.Texture);
                throw new InvalidOperationException("WebGPU DeviceCreateCommandEncoder returned null.");
            }

            RenderPassColorAttachment colorAttachment = new RenderPassColorAttachment
            {
                View = view,
                ResolveTarget = null,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = clearColor
            };

            RenderPassDescriptor passDescriptor = new RenderPassDescriptor
            {
                ColorAttachments = &colorAttachment,
                ColorAttachmentCount = 1,
                DepthStencilAttachment = null
            };

            RenderPassEncoder* pass = _api.CommandEncoderBeginRenderPass(encoder, in passDescriptor);
            if (pass == null)
            {
                _api.CommandEncoderRelease(encoder);
                _api.TextureViewRelease(view);
                _api.TextureRelease(surfaceTexture.Texture);
                throw new InvalidOperationException("WebGPU CommandEncoderBeginRenderPass returned null.");
            }

            if (WorldRenderer == null)
            {
                throw new InvalidOperationException("WebGPU world renderer was not initialized.");
            }

            WorldRenderer.Encode(pass, instances, in viewProjection, out string? worldDiagnostic);
            string? uiDiagnostic = null;
            if (hasUi)
            {
                if (UiRenderer == null)
                {
                    throw new InvalidOperationException("WebGPU UI renderer was not initialized.");
                }

                UiRenderer.Encode(pass, uiPixels, uiWidth, uiHeight, uiBytesPerRow, out uiDiagnostic);
            }
            else
            {
                uiDiagnostic = "uiComposite=none";
            }

            frameDiagnostic = string.IsNullOrWhiteSpace(uiDiagnostic)
                ? worldDiagnostic
                : $"{worldDiagnostic}; {uiDiagnostic}";

            _api.RenderPassEncoderEnd(pass);
            CommandBufferDescriptor commandBufferDescriptor = new CommandBufferDescriptor();
            CommandBuffer* commandBuffer = _api.CommandEncoderFinish(encoder, in commandBufferDescriptor);
            if (commandBuffer == null)
            {
                _api.RenderPassEncoderRelease(pass);
                _api.CommandEncoderRelease(encoder);
                _api.TextureViewRelease(view);
                _api.TextureRelease(surfaceTexture.Texture);
                throw new InvalidOperationException("WebGPU CommandEncoderFinish returned null.");
            }

            _api.QueueSubmit(_queue, 1, &commandBuffer);
            _api.SurfacePresent(_surface);

            _api.CommandBufferRelease(commandBuffer);
            _api.RenderPassEncoderRelease(pass);
            _api.CommandEncoderRelease(encoder);
            _api.TextureViewRelease(view);
            _api.TextureRelease(surfaceTexture.Texture);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            UiRenderer?.Dispose();
            UiRenderer = null;
            WorldRenderer?.Dispose();
            WorldRenderer = null;

            if (_api != null)
            {
                if (_device != null)
                {
                    _api.DeviceRelease(_device);
                    _device = null;
                }

                if (_adapter != null)
                {
                    _api.AdapterRelease(_adapter);
                    _adapter = null;
                }

                if (_surface != null)
                {
                    _api.SurfaceRelease(_surface);
                    _surface = null;
                }

                if (_instance != null)
                {
                    _api.InstanceRelease(_instance);
                    _instance = null;
                }

                _api.Dispose();
                _api = null!;
            }

            _queue = null;
        }

        private void ConfigureSurface(Vector2D<int>? framebufferSize = null)
        {
            Vector2D<int> size = framebufferSize ?? _window.FramebufferSize;
            if (size.X <= 0 || size.Y <= 0)
            {
                throw new InvalidOperationException(
                    $"WebGPU surface configure requires positive framebuffer size, got {size.X}x{size.Y}.");
            }

            _surfaceConfiguration = new SurfaceConfiguration
            {
                Usage = TextureUsage.RenderAttachment,
                Format = _surfaceFormat,
                PresentMode = PresentMode.Fifo,
                Device = _device,
                Width = (uint)size.X,
                Height = (uint)size.Y
            };

            _api.SurfaceConfigure(_surface, in _surfaceConfiguration);
            _configured = true;
        }

        private void OnAdapterReady(RequestAdapterStatus status, Adapter* adapter, byte* message, void* userdata)
        {
            _lastAdapterMessage = WebGpuDeviceBootstrap.PtrToManagedString(message);
            if (status != RequestAdapterStatus.Success || adapter == null)
            {
                _adapter = null;
                return;
            }

            _adapter = adapter;
        }

        private void OnDeviceReady(RequestDeviceStatus status, Device* device, byte* message, void* userdata)
        {
            _lastDeviceMessage = WebGpuDeviceBootstrap.PtrToManagedString(message);
            if (status != RequestDeviceStatus.Success || device == null)
            {
                _device = null;
                return;
            }

            _device = device;
        }

        private static void OnDeviceLost(DeviceLostReason reason, byte* message, void* userdata)
        {
            string detail = WebGpuDeviceBootstrap.PtrToManagedString(message) ?? "no message";
            Console.Error.WriteLine($"[WebGPU] Device lost. Reason={reason}. Detail={detail}");
        }

        private static void OnUncapturedError(ErrorType type, byte* message, void* userdata)
        {
            string detail = WebGpuDeviceBootstrap.PtrToManagedString(message) ?? "no message";
            Console.Error.WriteLine($"[WebGPU] Uncaptured error {type}: {detail}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Matrix4x4Gpu
    {
        public readonly float M11, M12, M13, M14;
        public readonly float M21, M22, M23, M24;
        public readonly float M31, M32, M33, M34;
        public readonly float M41, M42, M43, M44;

        public Matrix4x4Gpu(System.Numerics.Matrix4x4 m)
        {
            M11 = m.M11; M12 = m.M12; M13 = m.M13; M14 = m.M14;
            M21 = m.M21; M22 = m.M22; M23 = m.M23; M24 = m.M24;
            M31 = m.M31; M32 = m.M32; M33 = m.M33; M34 = m.M34;
            M41 = m.M41; M42 = m.M42; M43 = m.M43; M44 = m.M44;
        }
    }
}

using System.Runtime.InteropServices;

namespace Ludots.Client.WebGpu.Native;

internal static unsafe class WebGpuNative
{
    private const string Module = "__Internal_emscripten";

    [DllImport(Module, EntryPoint = "wgpuCreateInstance", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuInstance* CreateInstance(WgpuInstanceDescriptor* descriptor);

    [DllImport(Module, EntryPoint = "wgpuInstanceCreateSurface", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuSurface* InstanceCreateSurface(WgpuInstance* instance, WgpuSurfaceDescriptor* descriptor);

    [DllImport(Module, EntryPoint = "wgpuInstanceRequestAdapter", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void InstanceRequestAdapter(
        WgpuInstance* instance,
        WgpuRequestAdapterOptions* options,
        nint callback,
        void* userData);

    [DllImport(Module, EntryPoint = "wgpuAdapterRequestDevice", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void AdapterRequestDevice(
        WgpuAdapter* adapter,
        void* descriptor,
        nint callback,
        void* userData);

    [DllImport(Module, EntryPoint = "wgpuDeviceGetQueue", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuQueue* DeviceGetQueue(WgpuDevice* device);

    [DllImport(Module, EntryPoint = "wgpuDeviceSetUncapturedErrorCallback", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DeviceSetUncapturedErrorCallback(
        WgpuDevice* device,
        nint callback,
        void* userData);

    [DllImport(Module, EntryPoint = "wgpuSurfaceGetPreferredFormat", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuTextureFormat SurfaceGetPreferredFormat(WgpuSurface* surface, WgpuAdapter* adapter);

    [DllImport(Module, EntryPoint = "wgpuDeviceCreateSwapChain", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuSwapChain* DeviceCreateSwapChain(
        WgpuDevice* device,
        WgpuSurface* surface,
        WgpuSwapChainDescriptor* descriptor);

    [DllImport(Module, EntryPoint = "wgpuDeviceCreateShaderModule", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuShaderModule* DeviceCreateShaderModule(
        WgpuDevice* device,
        WgpuShaderModuleDescriptor* descriptor);

    [DllImport(Module, EntryPoint = "wgpuDeviceCreateRenderPipeline", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuRenderPipeline* DeviceCreateRenderPipeline(
        WgpuDevice* device,
        WgpuRenderPipelineDescriptor* descriptor);

    [DllImport(Module, EntryPoint = "wgpuDeviceCreateBuffer", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuBuffer* DeviceCreateBuffer(
        WgpuDevice* device,
        WgpuBufferDescriptor* descriptor);

    [DllImport(Module, EntryPoint = "wgpuDeviceCreateBindGroupLayout", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuBindGroupLayout* DeviceCreateBindGroupLayout(
        WgpuDevice* device,
        WgpuBindGroupLayoutDescriptor* descriptor);

    [DllImport(Module, EntryPoint = "wgpuQueueWriteBuffer", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void QueueWriteBuffer(
        WgpuQueue* queue,
        WgpuBuffer* buffer,
        ulong bufferOffset,
        void* data,
        nuint size);

    [DllImport(Module, EntryPoint = "wgpuQueueWriteTexture", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void QueueWriteTexture(
        WgpuQueue* queue,
        WgpuImageCopyTexture* destination,
        void* data,
        nuint dataSize,
        WgpuTextureDataLayout* dataLayout,
        WgpuExtent3D* writeSize);

    [DllImport(Module, EntryPoint = "wgpuDeviceCreatePipelineLayout", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuPipelineLayout* DeviceCreatePipelineLayout(
        WgpuDevice* device,
        WgpuPipelineLayoutDescriptor* descriptor);

    [DllImport(Module, EntryPoint = "wgpuDeviceCreateBindGroup", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuBindGroup* DeviceCreateBindGroup(
        WgpuDevice* device,
        WgpuBindGroupDescriptor* descriptor);

    [DllImport(Module, EntryPoint = "wgpuDeviceCreateTexture", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuTexture* DeviceCreateTexture(
        WgpuDevice* device,
        WgpuTextureDescriptor* descriptor);

    [DllImport(Module, EntryPoint = "wgpuTextureCreateView", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuTextureView* TextureCreateView(WgpuTexture* texture, void* descriptor);

    [DllImport(Module, EntryPoint = "wgpuDeviceCreateSampler", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuSampler* DeviceCreateSampler(
        WgpuDevice* device,
        WgpuSamplerDescriptor* descriptor);

    [DllImport(Module, EntryPoint = "wgpuDeviceCreateCommandEncoder", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuCommandEncoder* DeviceCreateCommandEncoder(
        WgpuDevice* device,
        WgpuCommandEncoderDescriptor* descriptor);

    [DllImport(Module, EntryPoint = "wgpuSwapChainGetCurrentTextureView", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuTextureView* SwapChainGetCurrentTextureView(WgpuSwapChain* swapChain);

    [DllImport(Module, EntryPoint = "wgpuCommandEncoderBeginRenderPass", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuRenderPassEncoder* CommandEncoderBeginRenderPass(
        WgpuCommandEncoder* commandEncoder,
        WgpuRenderPassDescriptor* descriptor);

    [DllImport(Module, EntryPoint = "wgpuRenderPassEncoderSetPipeline", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RenderPassEncoderSetPipeline(
        WgpuRenderPassEncoder* renderPassEncoder,
        WgpuRenderPipeline* pipeline);

    [DllImport(Module, EntryPoint = "wgpuRenderPassEncoderSetBindGroup", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RenderPassEncoderSetBindGroup(
        WgpuRenderPassEncoder* renderPassEncoder,
        uint groupIndex,
        WgpuBindGroup* bindGroup,
        nuint dynamicOffsetCount,
        uint* dynamicOffsets);

    [DllImport(Module, EntryPoint = "wgpuRenderPassEncoderSetVertexBuffer", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RenderPassEncoderSetVertexBuffer(
        WgpuRenderPassEncoder* renderPassEncoder,
        uint slot,
        WgpuBuffer* buffer,
        ulong offset,
        ulong size);

    [DllImport(Module, EntryPoint = "wgpuRenderPassEncoderSetIndexBuffer", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RenderPassEncoderSetIndexBuffer(
        WgpuRenderPassEncoder* renderPassEncoder,
        WgpuBuffer* buffer,
        WgpuIndexFormat format,
        ulong offset,
        ulong size);

    [DllImport(Module, EntryPoint = "wgpuRenderPassEncoderDraw", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RenderPassEncoderDraw(
        WgpuRenderPassEncoder* renderPassEncoder,
        uint vertexCount,
        uint instanceCount,
        uint firstVertex,
        uint firstInstance);

    [DllImport(Module, EntryPoint = "wgpuRenderPassEncoderDrawIndexed", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RenderPassEncoderDrawIndexed(
        WgpuRenderPassEncoder* renderPassEncoder,
        uint indexCount,
        uint instanceCount,
        uint firstIndex,
        int baseVertex,
        uint firstInstance);

    [DllImport(Module, EntryPoint = "wgpuRenderPassEncoderEnd", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RenderPassEncoderEnd(WgpuRenderPassEncoder* renderPassEncoder);

    [DllImport(Module, EntryPoint = "wgpuCommandEncoderFinish", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WgpuCommandBuffer* CommandEncoderFinish(
        WgpuCommandEncoder* commandEncoder,
        WgpuCommandBufferDescriptor* descriptor);

    [DllImport(Module, EntryPoint = "wgpuQueueSubmit", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void QueueSubmit(
        WgpuQueue* queue,
        nuint commandCount,
        WgpuCommandBuffer** commands);

    [DllImport(Module, EntryPoint = "wgpuTextureViewRelease", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void TextureViewRelease(WgpuTextureView* textureView);

    [DllImport(Module, EntryPoint = "wgpuRenderPassEncoderRelease", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RenderPassEncoderRelease(WgpuRenderPassEncoder* renderPassEncoder);

    [DllImport(Module, EntryPoint = "wgpuCommandEncoderRelease", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CommandEncoderRelease(WgpuCommandEncoder* commandEncoder);

    [DllImport(Module, EntryPoint = "wgpuCommandBufferRelease", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CommandBufferRelease(WgpuCommandBuffer* commandBuffer);

    [DllImport(Module, EntryPoint = "wgpuSwapChainRelease", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SwapChainRelease(WgpuSwapChain* swapChain);

    [DllImport(Module, EntryPoint = "wgpuBufferRelease", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void BufferRelease(WgpuBuffer* buffer);

    [DllImport(Module, EntryPoint = "wgpuTextureRelease", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void TextureRelease(WgpuTexture* texture);

    [DllImport(Module, EntryPoint = "wgpuSamplerRelease", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SamplerRelease(WgpuSampler* sampler);

    [DllImport(Module, EntryPoint = "wgpuBindGroupRelease", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void BindGroupRelease(WgpuBindGroup* bindGroup);

    [DllImport(Module, EntryPoint = "wgpuBindGroupLayoutRelease", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void BindGroupLayoutRelease(WgpuBindGroupLayout* bindGroupLayout);
}

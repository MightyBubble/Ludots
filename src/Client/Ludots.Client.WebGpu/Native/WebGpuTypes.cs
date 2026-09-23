using System.Runtime.InteropServices;

namespace Ludots.Client.WebGpu.Native;

public enum WgpuRequestAdapterStatus : uint
{
    Success = 0,
    Unavailable = 1,
    Error = 2,
    Unknown = 3,
}

public enum WgpuRequestDeviceStatus : uint
{
    Success = 0,
    Error = 1,
    Unknown = 2,
}

public enum WgpuErrorType : uint
{
    NoError = 0,
    Validation = 1,
    OutOfMemory = 2,
    Internal = 3,
    Unknown = 4,
    DeviceLost = 5,
}

public enum WgpuSType : uint
{
    Invalid = 0,
    SurfaceDescriptorFromCanvasHtmlSelector = 4,
    ShaderModuleWgslDescriptor = 6,
}

public enum WgpuPowerPreference : uint
{
    Undefined = 0,
    LowPower = 1,
    HighPerformance = 2,
}

public enum WgpuBackendType : uint
{
    Undefined = 0,
}

public enum WgpuPresentMode : uint
{
    Fifo = 1,
    Immediate = 3,
    Mailbox = 4,
}

public enum WgpuTextureFormat : uint
{
    Undefined = 0,
    R8Unorm = 0x01,
    Rgba8Unorm = 0x12,
    Rgba8UnormSrgb = 0x13,
    Bgra8Unorm = 0x17,
    Bgra8UnormSrgb = 0x18,
    Depth24Plus = 0x28,
}

public enum WgpuTextureDimension : uint
{
    Undefined = 0,
    OneDimensional = 1,
    TwoDimensional = 2,
    ThreeDimensional = 3,
}

public enum WgpuTextureAspect : uint
{
    Undefined = 0,
    All = 1,
    StencilOnly = 2,
    DepthOnly = 3,
}

[Flags]
public enum WgpuTextureUsage : uint
{
    None = 0,
    CopySource = 0x01,
    CopyDestination = 0x02,
    TextureBinding = 0x04,
    StorageBinding = 0x08,
    RenderAttachment = 0x10,
}

public enum WgpuLoadOp : uint
{
    Undefined = 0,
    Clear = 1,
    Load = 2,
}

public enum WgpuStoreOp : uint
{
    Undefined = 0,
    Store = 1,
    Discard = 2,
}

public enum WgpuPrimitiveTopology : uint
{
    Undefined = 0,
    PointList = 1,
    LineList = 2,
    LineStrip = 3,
    TriangleList = 4,
    TriangleStrip = 5,
}

public enum WgpuIndexFormat : uint
{
    Undefined = 0,
    Uint16 = 1,
    Uint32 = 2,
}

public enum WgpuVertexFormat : uint
{
    Undefined = 0,
    Float32 = 0x13,
    Float32x2 = 0x14,
    Float32x3 = 0x15,
    Float32x4 = 0x16,
}

public enum WgpuVertexStepMode : uint
{
    Undefined = 0,
    VertexBufferNotUsed = 1,
    Vertex = 2,
    Instance = 3,
}

[Flags]
public enum WgpuShaderStage : uint
{
    None = 0,
    Vertex = 0x01,
    Fragment = 0x02,
    Compute = 0x04,
}

public enum WgpuBufferBindingType : uint
{
    Undefined = 0,
    Uniform = 1,
    Storage = 2,
    ReadOnlyStorage = 3,
}

public enum WgpuSamplerBindingType : uint
{
    Undefined = 0,
    Filtering = 1,
    NonFiltering = 2,
    Comparison = 3,
}

public enum WgpuTextureSampleType : uint
{
    Undefined = 0,
    Float = 1,
    UnfilterableFloat = 2,
    Depth = 3,
    Sint = 4,
    Uint = 5,
}

public enum WgpuTextureViewDimension : uint
{
    Undefined = 0,
    OneDimensional = 1,
    TwoDimensional = 2,
    TwoDimensionalArray = 3,
    Cube = 4,
    CubeArray = 5,
    ThreeDimensional = 6,
}

public enum WgpuAddressMode : uint
{
    Undefined = 0,
    ClampToEdge = 1,
    Repeat = 2,
    MirrorRepeat = 3,
}

public enum WgpuFilterMode : uint
{
    Undefined = 0,
    Nearest = 1,
    Linear = 2,
}

public enum WgpuMipmapFilterMode : uint
{
    Undefined = 0,
    Nearest = 1,
    Linear = 2,
}

public enum WgpuStorageTextureAccess : uint
{
    Undefined = 0,
}

[Flags]
public enum WgpuBufferUsage : uint
{
    None = 0,
    MapRead = 0x01,
    MapWrite = 0x02,
    CopySource = 0x04,
    CopyDestination = 0x08,
    Index = 0x10,
    Vertex = 0x20,
    Uniform = 0x40,
    Storage = 0x80,
}

public enum WgpuCompareFunction : uint
{
    Undefined = 0,
    Never = 1,
    Less = 2,
    Equal = 3,
    LessEqual = 4,
    Greater = 5,
    NotEqual = 6,
    GreaterEqual = 7,
    Always = 8,
}

public enum WgpuStencilOperation : uint
{
    Undefined = 0,
}

public enum WgpuBlendFactor : uint
{
    Undefined = 0,
    Zero = 1,
    One = 2,
    SourceAlpha = 5,
    OneMinusSourceAlpha = 6,
}

public enum WgpuBlendOperation : uint
{
    Undefined = 0,
    Add = 1,
}

public enum WgpuFrontFace : uint
{
    Undefined = 0,
    CounterClockwise = 1,
    Clockwise = 2,
}

public enum WgpuCullMode : uint
{
    Undefined = 0,
    None = 1,
    Front = 2,
    Back = 3,
}

[Flags]
public enum WgpuColorWriteMask : uint
{
    None = 0,
    Red = 0x01,
    Green = 0x02,
    Blue = 0x04,
    Alpha = 0x08,
    All = Red | Green | Blue | Alpha,
}

public struct WgpuInstance;
public struct WgpuSurface;
public struct WgpuAdapter;
public struct WgpuDevice;
public struct WgpuQueue;
public struct WgpuSwapChain;
public struct WgpuShaderModule;
public struct WgpuPipelineLayout;
public struct WgpuRenderPipeline;
public struct WgpuCommandEncoder;
public struct WgpuRenderPassEncoder;
public struct WgpuCommandBuffer;
public struct WgpuTextureView;
public struct WgpuTexture;
public struct WgpuBuffer;
public struct WgpuBindGroup;
public struct WgpuBindGroupLayout;
public struct WgpuSampler;
public struct WgpuQuerySet;
public struct WgpuRenderPassTimestampWrites;
public struct WgpuConstantEntry;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuChainedStruct
{
    public WgpuChainedStruct* Next;
    public WgpuSType SType;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuInstanceFeatures
{
    public WgpuChainedStruct* NextInChain;
    public uint TimedWaitAnyEnable;
    public nuint TimedWaitAnyMaxCount;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuInstanceDescriptor
{
    public WgpuChainedStruct* NextInChain;
    public WgpuInstanceFeatures Features;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuSurfaceDescriptor
{
    public WgpuChainedStruct* NextInChain;
    public byte* Label;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuSurfaceDescriptorFromCanvasHtmlSelector
{
    public WgpuChainedStruct Chain;
    public byte* Selector;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuRequestAdapterOptions
{
    public WgpuChainedStruct* NextInChain;
    public WgpuSurface* CompatibleSurface;
    public WgpuPowerPreference PowerPreference;
    public WgpuBackendType BackendType;
    public uint ForceFallbackAdapter;
    public uint CompatibilityMode;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuSwapChainDescriptor
{
    public WgpuChainedStruct* NextInChain;
    public byte* Label;
    public WgpuTextureUsage Usage;
    public WgpuTextureFormat Format;
    public uint Width;
    public uint Height;
    public WgpuPresentMode PresentMode;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuShaderModuleWgslDescriptor
{
    public WgpuChainedStruct Chain;
    public byte* Code;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuShaderModuleDescriptor
{
    public WgpuChainedStruct* NextInChain;
    public byte* Label;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuBufferDescriptor
{
    public WgpuChainedStruct* NextInChain;
    public byte* Label;
    public WgpuBufferUsage Usage;
    public ulong Size;
    public uint MappedAtCreation;
}

[StructLayout(LayoutKind.Sequential)]
public struct WgpuVertexAttribute
{
    public WgpuVertexFormat Format;
    public ulong Offset;
    public uint ShaderLocation;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuVertexBufferLayout
{
    public ulong ArrayStride;
    public WgpuVertexStepMode StepMode;
    public nuint AttributeCount;
    public WgpuVertexAttribute* Attributes;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuVertexState
{
    public WgpuChainedStruct* NextInChain;
    public WgpuShaderModule* Module;
    public byte* EntryPoint;
    public nuint ConstantCount;
    public WgpuConstantEntry* Constants;
    public nuint BufferCount;
    public WgpuVertexBufferLayout* Buffers;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuColorTargetState
{
    public WgpuChainedStruct* NextInChain;
    public WgpuTextureFormat Format;
    public WgpuBlendState* Blend;
    public WgpuColorWriteMask WriteMask;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuFragmentState
{
    public WgpuChainedStruct* NextInChain;
    public WgpuShaderModule* Module;
    public byte* EntryPoint;
    public nuint ConstantCount;
    public WgpuConstantEntry* Constants;
    public nuint TargetCount;
    public WgpuColorTargetState* Targets;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuPrimitiveState
{
    public WgpuChainedStruct* NextInChain;
    public WgpuPrimitiveTopology Topology;
    public WgpuIndexFormat StripIndexFormat;
    public WgpuFrontFace FrontFace;
    public WgpuCullMode CullMode;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuMultisampleState
{
    public WgpuChainedStruct* NextInChain;
    public uint Count;
    public uint Mask;
    public uint AlphaToCoverageEnabled;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuRenderPipelineDescriptor
{
    public WgpuChainedStruct* NextInChain;
    public byte* Label;
    public WgpuPipelineLayout* Layout;
    public WgpuVertexState Vertex;
    public WgpuPrimitiveState Primitive;
    public WgpuDepthStencilState* DepthStencil;
    public WgpuMultisampleState Multisample;
    public WgpuFragmentState* Fragment;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuPipelineLayoutDescriptor
{
    public WgpuChainedStruct* NextInChain;
    public byte* Label;
    public nuint BindGroupLayoutCount;
    public WgpuBindGroupLayout** BindGroupLayouts;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuBindGroupEntry
{
    public WgpuChainedStruct* NextInChain;
    public uint Binding;
    public WgpuBuffer* Buffer;
    public ulong Offset;
    public ulong Size;
    public WgpuSampler* Sampler;
    public WgpuTextureView* TextureView;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuBufferBindingLayout
{
    public WgpuChainedStruct* NextInChain;
    public WgpuBufferBindingType Type;
    public uint HasDynamicOffset;
    public ulong MinBindingSize;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuSamplerBindingLayout
{
    public WgpuChainedStruct* NextInChain;
    public WgpuSamplerBindingType Type;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuTextureBindingLayout
{
    public WgpuChainedStruct* NextInChain;
    public WgpuTextureSampleType SampleType;
    public WgpuTextureViewDimension ViewDimension;
    public uint Multisampled;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuStorageTextureBindingLayout
{
    public WgpuChainedStruct* NextInChain;
    public WgpuStorageTextureAccess Access;
    public WgpuTextureFormat Format;
    public WgpuTextureViewDimension ViewDimension;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuBindGroupLayoutEntry
{
    public WgpuChainedStruct* NextInChain;
    public uint Binding;
    public WgpuShaderStage Visibility;
    public WgpuBufferBindingLayout Buffer;
    public WgpuSamplerBindingLayout Sampler;
    public WgpuTextureBindingLayout Texture;
    public WgpuStorageTextureBindingLayout StorageTexture;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuBindGroupLayoutDescriptor
{
    public WgpuChainedStruct* NextInChain;
    public byte* Label;
    public nuint EntryCount;
    public WgpuBindGroupLayoutEntry* Entries;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuBindGroupDescriptor
{
    public WgpuChainedStruct* NextInChain;
    public byte* Label;
    public WgpuBindGroupLayout* Layout;
    public nuint EntryCount;
    public WgpuBindGroupEntry* Entries;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuSamplerDescriptor
{
    public WgpuChainedStruct* NextInChain;
    public byte* Label;
    public WgpuAddressMode AddressModeU;
    public WgpuAddressMode AddressModeV;
    public WgpuAddressMode AddressModeW;
    public WgpuFilterMode MagFilter;
    public WgpuFilterMode MinFilter;
    public WgpuMipmapFilterMode MipmapFilter;
    public float LodMinClamp;
    public float LodMaxClamp;
    public WgpuCompareFunction Compare;
    public ushort MaxAnisotropy;
}

[StructLayout(LayoutKind.Sequential)]
public struct WgpuBlendComponent
{
    public WgpuBlendOperation Operation;
    public WgpuBlendFactor SourceFactor;
    public WgpuBlendFactor DestinationFactor;
}

[StructLayout(LayoutKind.Sequential)]
public struct WgpuBlendState
{
    public WgpuBlendComponent Color;
    public WgpuBlendComponent Alpha;
}

[StructLayout(LayoutKind.Sequential)]
public struct WgpuStencilFaceState
{
    public WgpuCompareFunction Compare;
    public WgpuStencilOperation FailOperation;
    public WgpuStencilOperation DepthFailOperation;
    public WgpuStencilOperation PassOperation;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuDepthStencilState
{
    public WgpuChainedStruct* NextInChain;
    public WgpuTextureFormat Format;
    public uint DepthWriteEnabled;
    public WgpuCompareFunction DepthCompare;
    public WgpuStencilFaceState StencilFront;
    public WgpuStencilFaceState StencilBack;
    public uint StencilReadMask;
    public uint StencilWriteMask;
    public int DepthBias;
    public float DepthBiasSlopeScale;
    public float DepthBiasClamp;
}

[StructLayout(LayoutKind.Sequential)]
public struct WgpuExtent3D
{
    public uint Width;
    public uint Height;
    public uint DepthOrArrayLayers;
}

[StructLayout(LayoutKind.Sequential)]
public struct WgpuOrigin3D
{
    public uint X;
    public uint Y;
    public uint Z;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuTextureDataLayout
{
    public WgpuChainedStruct* NextInChain;
    public ulong Offset;
    public uint BytesPerRow;
    public uint RowsPerImage;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuImageCopyTexture
{
    public WgpuChainedStruct* NextInChain;
    public WgpuTexture* Texture;
    public uint MipLevel;
    public WgpuOrigin3D Origin;
    public WgpuTextureAspect Aspect;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuTextureDescriptor
{
    public WgpuChainedStruct* NextInChain;
    public byte* Label;
    public WgpuTextureUsage Usage;
    public WgpuTextureDimension Dimension;
    public WgpuExtent3D Size;
    public WgpuTextureFormat Format;
    public uint MipLevelCount;
    public uint SampleCount;
    public nuint ViewFormatCount;
    public WgpuTextureFormat* ViewFormats;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuCommandEncoderDescriptor
{
    public WgpuChainedStruct* NextInChain;
    public byte* Label;
}

[StructLayout(LayoutKind.Sequential)]
public struct WgpuColor
{
    public double Red;
    public double Green;
    public double Blue;
    public double Alpha;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuRenderPassColorAttachment
{
    public WgpuChainedStruct* NextInChain;
    public WgpuTextureView* View;
    public uint DepthSlice;
    public WgpuTextureView* ResolveTarget;
    public WgpuLoadOp LoadOp;
    public WgpuStoreOp StoreOp;
    public WgpuColor ClearValue;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuRenderPassDescriptor
{
    public WgpuChainedStruct* NextInChain;
    public byte* Label;
    public nuint ColorAttachmentCount;
    public WgpuRenderPassColorAttachment* ColorAttachments;
    public WgpuRenderPassDepthStencilAttachment* DepthStencilAttachment;
    public WgpuQuerySet* OcclusionQuerySet;
    public WgpuRenderPassTimestampWrites* TimestampWrites;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuRenderPassDepthStencilAttachment
{
    public WgpuTextureView* View;
    public WgpuLoadOp DepthLoadOp;
    public WgpuStoreOp DepthStoreOp;
    public float DepthClearValue;
    public uint DepthReadOnly;
    public WgpuLoadOp StencilLoadOp;
    public WgpuStoreOp StencilStoreOp;
    public uint StencilClearValue;
    public uint StencilReadOnly;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WgpuCommandBufferDescriptor
{
    public WgpuChainedStruct* NextInChain;
    public byte* Label;
}

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Ludots.Client.WebGpu.Runtime;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace Ludots.Client.WebGpu.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    public struct WebGpuInstanceDraw
    {
        public Vector4 PositionScaleX;
        public Vector4 ScaleYzColorRg;
        public Vector4 ColorBaUnused;

        public Vector3 Position
        {
            readonly get => new Vector3(PositionScaleX.X, PositionScaleX.Y, PositionScaleX.Z);
            set
            {
                PositionScaleX.X = value.X;
                PositionScaleX.Y = value.Y;
                PositionScaleX.Z = value.Z;
            }
        }

        public Vector3 Scale
        {
            readonly get => new Vector3(PositionScaleX.W, ScaleYzColorRg.X, ScaleYzColorRg.Y);
            set
            {
                PositionScaleX.W = value.X;
                ScaleYzColorRg.X = value.Y;
                ScaleYzColorRg.Y = value.Z;
            }
        }

        public Vector4 Color
        {
            readonly get => new Vector4(ScaleYzColorRg.Z, ScaleYzColorRg.W, ColorBaUnused.X, ColorBaUnused.Y);
            set
            {
                ScaleYzColorRg.Z = value.X;
                ScaleYzColorRg.W = value.Y;
                ColorBaUnused.X = value.Z;
                ColorBaUnused.Y = value.W;
            }
        }
    }

    public sealed unsafe class WebGpuWorldRenderer : IDisposable
    {
        public const int MaxInstances = 16384;

        private const string ShaderSource = @"
struct FrameUniforms {
    view_proj : mat4x4<f32>,
};

struct VsOut {
    @builtin(position) clip_pos : vec4<f32>,
    @location(0) color : vec4<f32>,
};

struct InstanceDraw {
    position_scale_x : vec4<f32>,
    scale_yz_color_rg : vec4<f32>,
    color_ba_unused : vec4<f32>,
};

struct InstanceBuffer {
    draws : array<InstanceDraw>,
};

@group(0) @binding(0) var<uniform> frame : FrameUniforms;
@group(0) @binding(1) var<storage, read> instances : InstanceBuffer;

@vertex
fn vs_main(
    @builtin(vertex_index) vid : u32,
    @builtin(instance_index) instance_id : u32) -> VsOut {
    var cube : array<vec3<f32>, 36> = array<vec3<f32>, 36>(
        vec3<f32>(-0.5, -0.5,  0.5), vec3<f32>( 0.5, -0.5,  0.5), vec3<f32>( 0.5,  0.5,  0.5),
        vec3<f32>(-0.5, -0.5,  0.5), vec3<f32>( 0.5,  0.5,  0.5), vec3<f32>(-0.5,  0.5,  0.5),
        vec3<f32>( 0.5, -0.5, -0.5), vec3<f32>(-0.5, -0.5, -0.5), vec3<f32>(-0.5,  0.5, -0.5),
        vec3<f32>( 0.5, -0.5, -0.5), vec3<f32>(-0.5,  0.5, -0.5), vec3<f32>( 0.5,  0.5, -0.5),
        vec3<f32>(-0.5,  0.5,  0.5), vec3<f32>( 0.5,  0.5,  0.5), vec3<f32>( 0.5,  0.5, -0.5),
        vec3<f32>(-0.5,  0.5,  0.5), vec3<f32>( 0.5,  0.5, -0.5), vec3<f32>(-0.5,  0.5, -0.5),
        vec3<f32>(-0.5, -0.5, -0.5), vec3<f32>( 0.5, -0.5, -0.5), vec3<f32>( 0.5, -0.5,  0.5),
        vec3<f32>(-0.5, -0.5, -0.5), vec3<f32>( 0.5, -0.5,  0.5), vec3<f32>(-0.5, -0.5,  0.5),
        vec3<f32>( 0.5, -0.5,  0.5), vec3<f32>( 0.5, -0.5, -0.5), vec3<f32>( 0.5,  0.5, -0.5),
        vec3<f32>( 0.5, -0.5,  0.5), vec3<f32>( 0.5,  0.5, -0.5), vec3<f32>( 0.5,  0.5,  0.5),
        vec3<f32>(-0.5, -0.5, -0.5), vec3<f32>(-0.5, -0.5,  0.5), vec3<f32>(-0.5,  0.5,  0.5),
        vec3<f32>(-0.5, -0.5, -0.5), vec3<f32>(-0.5,  0.5,  0.5), vec3<f32>(-0.5,  0.5, -0.5)
    );

    let instance = instances.draws[instance_id];
    let instance_position = instance.position_scale_x.xyz;
    let instance_scale = vec3<f32>(
        instance.position_scale_x.w,
        instance.scale_yz_color_rg.x,
        instance.scale_yz_color_rg.y);
    let instance_color = vec4<f32>(
        instance.scale_yz_color_rg.z,
        instance.scale_yz_color_rg.w,
        instance.color_ba_unused.x,
        instance.color_ba_unused.y);

    var local = cube[vid];
    var world = vec3<f32>(
        local.x * instance_scale.x + instance_position.x,
        local.y * instance_scale.y + instance_position.y,
        local.z * instance_scale.z + instance_position.z);
    var out : VsOut;
    out.clip_pos = frame.view_proj * vec4<f32>(world, 1.0);
    out.color = instance_color;
    return out;
}

@fragment
fn fs_main(in : VsOut) -> @location(0) vec4<f32> {
    return in.color;
}
";

        private readonly WebGPU _api;
        private readonly Device* _device;
        private readonly Queue* _queue;
        private readonly TextureFormat _surfaceFormat;
        private readonly WebGpuInstanceDraw[] _staging = new WebGpuInstanceDraw[MaxInstances];

        private ShaderModule* _shader;
        private BindGroupLayout* _bindGroupLayout;
        private PipelineLayout* _pipelineLayout;
        private RenderPipeline* _pipeline;
        private WgpuBuffer* _uniformBuffer;
        private WgpuBuffer* _instanceBuffer;
        private BindGroup* _bindGroup;
        private bool _disposed;
        private int _droppedInstances;

        public WebGpuWorldRenderer(WebGPU api, Device* device, Queue* queue, TextureFormat surfaceFormat)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _device = device;
            _queue = queue;
            _surfaceFormat = surfaceFormat;
            if (_device == null || _queue == null)
            {
                throw new InvalidOperationException("WebGpuWorldRenderer requires a valid device and queue.");
            }
        }

        public int DroppedInstances => _droppedInstances;

        public void Initialize()
        {
            byte* shaderCodePtr = (byte*)SilkMarshal.StringToPtr(ShaderSource, NativeStringEncoding.UTF8);
            try
            {
                ShaderModuleWGSLDescriptor wgsl = new ShaderModuleWGSLDescriptor
                {
                    Code = shaderCodePtr,
                    Chain = new ChainedStruct
                    {
                        SType = SType.ShaderModuleWgslDescriptor
                    }
                };

                ShaderModuleDescriptor moduleDescriptor = new ShaderModuleDescriptor
                {
                    NextInChain = (ChainedStruct*)(&wgsl)
                };

                _shader = _api.DeviceCreateShaderModule(_device, in moduleDescriptor);
                if (_shader == null)
                {
                    throw new InvalidOperationException("WebGPU DeviceCreateShaderModule returned null for the cube shader.");
                }
            }
            finally
            {
                SilkMarshal.Free((nint)shaderCodePtr);
            }

            BindGroupLayoutEntry frameLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.Uniform,
                    HasDynamicOffset = false,
                    MinBindingSize = (ulong)sizeof(Matrix4x4Gpu)
                }
            };

            BindGroupLayoutEntry instanceLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 1,
                Visibility = ShaderStage.Vertex,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.ReadOnlyStorage,
                    HasDynamicOffset = false,
                    MinBindingSize = (ulong)sizeof(WebGpuInstanceDraw)
                }
            };

            BindGroupLayoutEntry* layoutEntries = stackalloc BindGroupLayoutEntry[2];
            layoutEntries[0] = frameLayoutEntry;
            layoutEntries[1] = instanceLayoutEntry;

            BindGroupLayoutDescriptor bindGroupLayoutDescriptor = new BindGroupLayoutDescriptor
            {
                EntryCount = 2,
                Entries = layoutEntries
            };
            _bindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, in bindGroupLayoutDescriptor);
            if (_bindGroupLayout == null)
            {
                throw new InvalidOperationException("WebGPU DeviceCreateBindGroupLayout returned null.");
            }

            BindGroupLayout* layoutPtr = _bindGroupLayout;
            PipelineLayoutDescriptor pipelineLayoutDescriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = &layoutPtr
            };
            _pipelineLayout = _api.DeviceCreatePipelineLayout(_device, in pipelineLayoutDescriptor);
            if (_pipelineLayout == null)
            {
                throw new InvalidOperationException("WebGPU DeviceCreatePipelineLayout returned null.");
            }

            byte* vsEntry = (byte*)SilkMarshal.StringToPtr("vs_main", NativeStringEncoding.UTF8);
            byte* fsEntry = (byte*)SilkMarshal.StringToPtr("fs_main", NativeStringEncoding.UTF8);
            try
            {
                BlendState blendState = new BlendState
                {
                    Color = new BlendComponent
                    {
                        SrcFactor = BlendFactor.One,
                        DstFactor = BlendFactor.Zero,
                        Operation = BlendOperation.Add
                    },
                    Alpha = new BlendComponent
                    {
                        SrcFactor = BlendFactor.One,
                        DstFactor = BlendFactor.Zero,
                        Operation = BlendOperation.Add
                    }
                };

                ColorTargetState colorTarget = new ColorTargetState
                {
                    Format = _surfaceFormat,
                    Blend = &blendState,
                    WriteMask = ColorWriteMask.All
                };

                FragmentState fragmentState = new FragmentState
                {
                    Module = _shader,
                    EntryPoint = fsEntry,
                    TargetCount = 1,
                    Targets = &colorTarget
                };

                RenderPipelineDescriptor pipelineDescriptor = new RenderPipelineDescriptor
                {
                    Layout = _pipelineLayout,
                    Vertex = new VertexState
                    {
                        Module = _shader,
                        EntryPoint = vsEntry,
                        BufferCount = 0,
                        Buffers = null
                    },
                    Primitive = new PrimitiveState
                    {
                        Topology = PrimitiveTopology.TriangleList,
                        StripIndexFormat = IndexFormat.Undefined,
                        FrontFace = FrontFace.Ccw,
                        CullMode = CullMode.None
                    },
                    Multisample = new MultisampleState
                    {
                        Count = 1,
                        Mask = ~0u,
                        AlphaToCoverageEnabled = false
                    },
                    Fragment = &fragmentState,
                    DepthStencil = null
                };

                _pipeline = _api.DeviceCreateRenderPipeline(_device, in pipelineDescriptor);
                if (_pipeline == null)
                {
                    throw new InvalidOperationException("WebGPU DeviceCreateRenderPipeline returned null.");
                }
            }
            finally
            {
                SilkMarshal.Free((nint)vsEntry);
                SilkMarshal.Free((nint)fsEntry);
            }

            BufferDescriptor uniformDesc = new BufferDescriptor
            {
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = 256,
                MappedAtCreation = false
            };
            _uniformBuffer = _api.DeviceCreateBuffer(_device, in uniformDesc);
            if (_uniformBuffer == null)
            {
                throw new InvalidOperationException("WebGPU failed to create the frame uniform buffer.");
            }

            BufferDescriptor instanceDesc = new BufferDescriptor
            {
                Usage = BufferUsage.Storage | BufferUsage.CopyDst,
                Size = (ulong)(sizeof(WebGpuInstanceDraw) * MaxInstances),
                MappedAtCreation = false
            };
            _instanceBuffer = _api.DeviceCreateBuffer(_device, in instanceDesc);
            if (_instanceBuffer == null)
            {
                throw new InvalidOperationException("WebGPU failed to create the instance vertex buffer.");
            }

            BindGroupEntry frameBindEntry = new BindGroupEntry
            {
                Binding = 0,
                Buffer = _uniformBuffer,
                Offset = 0,
                Size = 256
            };
            BindGroupEntry instanceBindEntry = new BindGroupEntry
            {
                Binding = 1,
                Buffer = _instanceBuffer,
                Offset = 0,
                Size = (ulong)(sizeof(WebGpuInstanceDraw) * MaxInstances)
            };
            BindGroupEntry* bindEntries = stackalloc BindGroupEntry[2];
            bindEntries[0] = frameBindEntry;
            bindEntries[1] = instanceBindEntry;

            BindGroupDescriptor bindGroupDescriptor = new BindGroupDescriptor
            {
                Layout = _bindGroupLayout,
                EntryCount = 2,
                Entries = bindEntries
            };
            _bindGroup = _api.DeviceCreateBindGroup(_device, in bindGroupDescriptor);
            if (_bindGroup == null)
            {
                throw new InvalidOperationException("WebGPU DeviceCreateBindGroup returned null.");
            }
        }

        public void Encode(
            RenderPassEncoder* pass,
            ReadOnlySpan<WebGpuInstanceDraw> instances,
            in Matrix4x4Gpu viewProjection,
            out string? diagnostic)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WebGpuWorldRenderer));
            }

            _droppedInstances = 0;
            int drawCount = instances.Length;
            if (drawCount > MaxInstances)
            {
                _droppedInstances = drawCount - MaxInstances;
                drawCount = MaxInstances;
            }

            if (drawCount > 0)
            {
                instances.Slice(0, drawCount).CopyTo(_staging);
                fixed (WebGpuInstanceDraw* stagingPtr = _staging)
                {
                    _api.QueueWriteBuffer(
                        _queue,
                        _instanceBuffer,
                        0,
                        stagingPtr,
                        (nuint)(sizeof(WebGpuInstanceDraw) * drawCount));
                }
            }

            Matrix4x4Gpu vp = viewProjection;
            _api.QueueWriteBuffer(_queue, _uniformBuffer, 0, &vp, (nuint)sizeof(Matrix4x4Gpu));

            _api.RenderPassEncoderSetPipeline(pass, _pipeline);
            _api.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
            if (drawCount > 0)
            {
                _api.RenderPassEncoderDraw(pass, 36, (uint)drawCount, 0, 0);
            }

            var sb = new StringBuilder();
            sb.Append($"primitivesDrawn={drawCount}");
            if (_droppedInstances > 0)
            {
                sb.Append($"; dropped={_droppedInstances} (capacity={MaxInstances})");
            }

            if (drawCount == 0)
            {
                sb.Append("; no Core primitive instances this frame - clear pass + empty world diagnostic");
            }

            diagnostic = sb.ToString();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_bindGroup != null)
            {
                _api.BindGroupRelease(_bindGroup);
                _bindGroup = null;
            }

            if (_instanceBuffer != null)
            {
                _api.BufferRelease(_instanceBuffer);
                _instanceBuffer = null;
            }

            if (_uniformBuffer != null)
            {
                _api.BufferRelease(_uniformBuffer);
                _uniformBuffer = null;
            }

            if (_pipeline != null)
            {
                _api.RenderPipelineRelease(_pipeline);
                _pipeline = null;
            }

            if (_pipelineLayout != null)
            {
                _api.PipelineLayoutRelease(_pipelineLayout);
                _pipelineLayout = null;
            }

            if (_bindGroupLayout != null)
            {
                _api.BindGroupLayoutRelease(_bindGroupLayout);
                _bindGroupLayout = null;
            }

            if (_shader != null)
            {
                _api.ShaderModuleRelease(_shader);
                _shader = null;
            }
        }
    }
}

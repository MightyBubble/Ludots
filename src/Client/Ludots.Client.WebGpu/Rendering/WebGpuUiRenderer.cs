using System;
using System.Text;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;

namespace Ludots.Client.WebGpu.Rendering
{
    public sealed unsafe class WebGpuUiRenderer : IDisposable
    {
        public const int BytesPerPixel = 4;
        public const int BytesPerRowAlignment = 256;

        private const string ShaderSource = @"
@group(0) @binding(0) var ui_sampler : sampler;
@group(0) @binding(1) var ui_texture : texture_2d<f32>;

struct VsOut {
    @builtin(position) clip_pos : vec4<f32>,
    @location(0) uv : vec2<f32>,
};

@vertex
fn vs_main(@builtin(vertex_index) vid : u32) -> VsOut {
    var positions : array<vec2<f32>, 3> = array<vec2<f32>, 3>(
        vec2<f32>(-1.0, -1.0),
        vec2<f32>( 3.0, -1.0),
        vec2<f32>(-1.0,  3.0)
    );
    var uvs : array<vec2<f32>, 3> = array<vec2<f32>, 3>(
        vec2<f32>(0.0, 1.0),
        vec2<f32>(2.0, 1.0),
        vec2<f32>(0.0, -1.0)
    );

    var out : VsOut;
    out.clip_pos = vec4<f32>(positions[vid], 0.0, 1.0);
    out.uv = uvs[vid];
    return out;
}

@fragment
fn fs_main(in : VsOut) -> @location(0) vec4<f32> {
    return textureSample(ui_texture, ui_sampler, in.uv);
}
";

        private readonly WebGPU _api;
        private readonly Device* _device;
        private readonly Queue* _queue;
        private readonly TextureFormat _surfaceFormat;

        private ShaderModule* _shader;
        private BindGroupLayout* _bindGroupLayout;
        private PipelineLayout* _pipelineLayout;
        private RenderPipeline* _pipeline;
        private Sampler* _sampler;
        private Texture* _texture;
        private TextureView* _textureView;
        private BindGroup* _bindGroup;
        private bool _disposed;
        private int _width;
        private int _height;

        public WebGpuUiRenderer(WebGPU api, Device* device, Queue* queue, TextureFormat surfaceFormat)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _device = device;
            _queue = queue;
            _surfaceFormat = surfaceFormat;
            if (_device == null || _queue == null)
            {
                throw new InvalidOperationException("WebGpuUiRenderer requires a valid device and queue.");
            }
        }

        public static int AlignBytesPerRow(int width)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width, "UI texture width must be positive.");
            }

            int bytesPerRow = checked(width * BytesPerPixel);
            int remainder = bytesPerRow % BytesPerRowAlignment;
            return remainder == 0 ? bytesPerRow : checked(bytesPerRow + BytesPerRowAlignment - remainder);
        }

        public static int CalculateUploadByteCount(int width, int height)
        {
            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), height, "UI texture height must be positive.");
            }

            return checked(AlignBytesPerRow(width) * height);
        }

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
                    throw new InvalidOperationException("WebGPU DeviceCreateShaderModule returned null for the UI composite shader.");
                }
            }
            finally
            {
                SilkMarshal.Free((nint)shaderCodePtr);
            }

            BindGroupLayoutEntry samplerEntry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Fragment,
                Sampler = new SamplerBindingLayout
                {
                    Type = SamplerBindingType.Filtering
                }
            };
            BindGroupLayoutEntry textureEntry = new BindGroupLayoutEntry
            {
                Binding = 1,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Float,
                    ViewDimension = TextureViewDimension.Dimension2D,
                    Multisampled = false
                }
            };

            BindGroupLayoutEntry* layoutEntries = stackalloc BindGroupLayoutEntry[2];
            layoutEntries[0] = samplerEntry;
            layoutEntries[1] = textureEntry;

            BindGroupLayoutDescriptor bindGroupLayoutDescriptor = new BindGroupLayoutDescriptor
            {
                EntryCount = 2,
                Entries = layoutEntries
            };
            _bindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, in bindGroupLayoutDescriptor);
            if (_bindGroupLayout == null)
            {
                throw new InvalidOperationException("WebGPU DeviceCreateBindGroupLayout returned null for the UI composite pass.");
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
                throw new InvalidOperationException("WebGPU DeviceCreatePipelineLayout returned null for the UI composite pass.");
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
                        DstFactor = BlendFactor.OneMinusSrcAlpha,
                        Operation = BlendOperation.Add
                    },
                    Alpha = new BlendComponent
                    {
                        SrcFactor = BlendFactor.One,
                        DstFactor = BlendFactor.OneMinusSrcAlpha,
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
                    throw new InvalidOperationException("WebGPU DeviceCreateRenderPipeline returned null for the UI composite pass.");
                }
            }
            finally
            {
                SilkMarshal.Free((nint)vsEntry);
                SilkMarshal.Free((nint)fsEntry);
            }

            SamplerDescriptor samplerDescriptor = new SamplerDescriptor
            {
                AddressModeU = AddressMode.ClampToEdge,
                AddressModeV = AddressMode.ClampToEdge,
                AddressModeW = AddressMode.ClampToEdge,
                MagFilter = FilterMode.Linear,
                MinFilter = FilterMode.Linear,
                MipmapFilter = MipmapFilterMode.Nearest,
                LodMinClamp = 0f,
                LodMaxClamp = 32f,
                MaxAnisotropy = 1
            };
            _sampler = _api.DeviceCreateSampler(_device, in samplerDescriptor);
            if (_sampler == null)
            {
                throw new InvalidOperationException("WebGPU DeviceCreateSampler returned null for the UI composite pass.");
            }
        }

        public void Encode(
            RenderPassEncoder* pass,
            ReadOnlySpan<byte> rgbaPremulPixels,
            int width,
            int height,
            int bytesPerRow,
            out string diagnostic)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WebGpuUiRenderer));
            }

            if (pass == null)
            {
                throw new ArgumentNullException(nameof(pass));
            }

            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException($"WebGPU UI composite received invalid texture size {width}x{height}.");
            }

            int minimumBytesPerRow = checked(width * BytesPerPixel);
            if (bytesPerRow < minimumBytesPerRow)
            {
                throw new InvalidOperationException(
                    $"WebGPU UI composite bytesPerRow {bytesPerRow} is smaller than required {minimumBytesPerRow}.");
            }

            int requiredBytes = checked(bytesPerRow * height);
            if (rgbaPremulPixels.Length < requiredBytes)
            {
                throw new InvalidOperationException(
                    $"WebGPU UI composite pixel buffer is too small: got {rgbaPremulPixels.Length}, required {requiredBytes}.");
            }

            EnsureTexture(width, height);

            ImageCopyTexture destination = new ImageCopyTexture
            {
                Texture = _texture,
                MipLevel = 0,
                Origin = new Origin3D(),
                Aspect = TextureAspect.All
            };
            TextureDataLayout layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = (uint)bytesPerRow,
                RowsPerImage = (uint)height
            };
            Extent3D writeSize = new Extent3D
            {
                Width = (uint)width,
                Height = (uint)height,
                DepthOrArrayLayers = 1
            };

            fixed (byte* pixels = rgbaPremulPixels)
            {
                _api.QueueWriteTexture(
                    _queue,
                    in destination,
                    pixels,
                    (nuint)requiredBytes,
                    in layout,
                    in writeSize);
            }

            _api.RenderPassEncoderSetPipeline(pass, _pipeline);
            _api.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
            _api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);

            diagnostic = $"uiComposite=rgba8 {width}x{height} bytesPerRow={bytesPerRow}";
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseTextureResources();

            if (_sampler != null)
            {
                _api.SamplerRelease(_sampler);
                _sampler = null;
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

        private void EnsureTexture(int width, int height)
        {
            if (_texture != null && _width == width && _height == height)
            {
                return;
            }

            ReleaseTextureResources();

            TextureDescriptor textureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    DepthOrArrayLayers = 1
                },
                Format = TextureFormat.Rgba8Unorm,
                MipLevelCount = 1,
                SampleCount = 1
            };
            _texture = _api.DeviceCreateTexture(_device, in textureDescriptor);
            if (_texture == null)
            {
                throw new InvalidOperationException("WebGPU DeviceCreateTexture returned null for the UI composite texture.");
            }

            _textureView = _api.TextureCreateView(_texture, null);
            if (_textureView == null)
            {
                throw new InvalidOperationException("WebGPU TextureCreateView returned null for the UI composite texture.");
            }

            BindGroupEntry samplerEntry = new BindGroupEntry
            {
                Binding = 0,
                Sampler = _sampler
            };
            BindGroupEntry textureEntry = new BindGroupEntry
            {
                Binding = 1,
                TextureView = _textureView
            };
            BindGroupEntry* bindEntries = stackalloc BindGroupEntry[2];
            bindEntries[0] = samplerEntry;
            bindEntries[1] = textureEntry;

            BindGroupDescriptor bindGroupDescriptor = new BindGroupDescriptor
            {
                Layout = _bindGroupLayout,
                EntryCount = 2,
                Entries = bindEntries
            };
            _bindGroup = _api.DeviceCreateBindGroup(_device, in bindGroupDescriptor);
            if (_bindGroup == null)
            {
                throw new InvalidOperationException("WebGPU DeviceCreateBindGroup returned null for the UI composite texture.");
            }

            _width = width;
            _height = height;
        }

        private void ReleaseTextureResources()
        {
            if (_bindGroup != null)
            {
                _api.BindGroupRelease(_bindGroup);
                _bindGroup = null;
            }

            if (_textureView != null)
            {
                _api.TextureViewRelease(_textureView);
                _textureView = null;
            }

            if (_texture != null)
            {
                _api.TextureRelease(_texture);
                _texture = null;
            }

            _width = 0;
            _height = 0;
        }
    }
}

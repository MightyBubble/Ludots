using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Ludots.Client.WebGpu.Native;

namespace Ludots.Client.WebGpu.Runtime;

public sealed unsafe class WebGpuBrowserRuntime
{
    private const uint UndefinedDepthSlice = uint.MaxValue;
    private const string TerrainShader = """
        struct FrameUniforms {
            viewProjection: mat4x4<f32>,
            viewportSize: vec2<f32>,
            padding: vec2<f32>,
        };

        @group(0) @binding(0) var<uniform> frame: FrameUniforms;

        struct VertexInput {
            @location(0) position: vec3<f32>,
            @location(1) normal: vec3<f32>,
            @location(2) color: vec4<f32>,
        };

        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
            @location(0) normal: vec3<f32>,
            @location(1) color: vec4<f32>,
        };

        @vertex
        fn vertexMain(input: VertexInput) -> VertexOutput {
            var output: VertexOutput;
            output.position = frame.viewProjection * vec4<f32>(input.position, 1.0);
            output.normal = input.normal;
            output.color = input.color;
            return output;
        }

        @fragment
        fn fragmentMain(input: VertexOutput) -> @location(0) vec4<f32> {
            let normal = normalize(input.normal);
            let light = normalize(vec3<f32>(0.35, 0.86, 0.38));
            let diffuse = max(dot(normal, light), 0.0);
            let shade = 0.48 + diffuse * 0.52;
            return vec4<f32>(input.color.rgb * shade, input.color.a);
        }
        """;

    private const string UnitShader = """
        struct FrameUniforms {
            viewProjection: mat4x4<f32>,
            viewportSize: vec2<f32>,
            padding: vec2<f32>,
        };

        @group(0) @binding(0) var<uniform> frame: FrameUniforms;

        struct VertexInput {
            @location(0) position: vec3<f32>,
            @location(1) normal: vec3<f32>,
            @location(2) vertexColor: vec4<f32>,
            @location(3) instancePosition: vec3<f32>,
            @location(4) instanceScale: vec3<f32>,
            @location(5) instanceRotation: vec4<f32>,
            @location(6) instanceColor: vec4<f32>,
        };

        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
            @location(0) normal: vec3<f32>,
            @location(1) color: vec4<f32>,
        };

        fn rotateByQuaternion(value: vec3<f32>, rotation: vec4<f32>) -> vec3<f32> {
            let q = normalize(rotation);
            let twiceCross = 2.0 * cross(q.xyz, value);
            return value + q.w * twiceCross + cross(q.xyz, twiceCross);
        }

        @vertex
        fn vertexMain(input: VertexInput) -> VertexOutput {
            let worldPosition = input.instancePosition +
                rotateByQuaternion(input.position * input.instanceScale, input.instanceRotation);
            var output: VertexOutput;
            output.position = frame.viewProjection * vec4<f32>(worldPosition, 1.0);
            output.normal = rotateByQuaternion(input.normal, input.instanceRotation);
            output.color = input.vertexColor * input.instanceColor;
            return output;
        }

        @fragment
        fn fragmentMain(input: VertexOutput) -> @location(0) vec4<f32> {
            let normal = normalize(input.normal);
            let light = normalize(vec3<f32>(0.35, 0.86, 0.38));
            let diffuse = max(dot(normal, light), 0.0);
            let shade = 0.42 + diffuse * 0.58;
            return vec4<f32>(input.color.rgb * shade, input.color.a);
        }
        """;

    private const string ScreenShader = """
        struct FrameUniforms {
            viewProjection: mat4x4<f32>,
            viewportSize: vec2<f32>,
            padding: vec2<f32>,
        };

        @group(0) @binding(0) var<uniform> frame: FrameUniforms;

        struct InstanceInput {
            @location(0) centerPx: vec2<f32>,
            @location(1) halfSizePx: vec2<f32>,
            @location(2) color: vec4<f32>,
            @location(3) clipRectPx: vec4<f32>,
            @location(4) rotationRad: f32,
            @location(5) shape: f32,
            @location(6) clipShape: f32,
        };

        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
            @location(0) localPosition: vec2<f32>,
            @location(1) pixelPosition: vec2<f32>,
            @location(2) color: vec4<f32>,
            @location(3) clipRectPx: vec4<f32>,
            @location(4) shape: f32,
            @location(5) clipShape: f32,
        };

        @vertex
        fn vertexMain(@builtin(vertex_index) vertexIndex: u32, input: InstanceInput) -> VertexOutput {
            var corners = array<vec2<f32>, 6>(
                vec2<f32>(-1.0, -1.0),
                vec2<f32>( 1.0, -1.0),
                vec2<f32>(-1.0,  1.0),
                vec2<f32>(-1.0,  1.0),
                vec2<f32>( 1.0, -1.0),
                vec2<f32>( 1.0,  1.0)
            );
            let local = corners[vertexIndex];
            let scaled = local * input.halfSizePx;
            let cosine = cos(input.rotationRad);
            let sine = sin(input.rotationRad);
            let rotated = vec2<f32>(
                scaled.x * cosine - scaled.y * sine,
                scaled.x * sine + scaled.y * cosine);
            let pixel = input.centerPx + rotated;
            let ndc = vec2<f32>(
                pixel.x / frame.viewportSize.x * 2.0 - 1.0,
                1.0 - pixel.y / frame.viewportSize.y * 2.0);

            var output: VertexOutput;
            output.position = vec4<f32>(ndc, 0.0, 1.0);
            output.localPosition = local;
            output.pixelPosition = pixel;
            output.color = input.color;
            output.clipRectPx = input.clipRectPx;
            output.shape = input.shape;
            output.clipShape = input.clipShape;
            return output;
        }

        @fragment
        fn fragmentMain(input: VertexOutput) -> @location(0) vec4<f32> {
            if (input.shape > 0.5 && dot(input.localPosition, input.localPosition) > 1.0) {
                discard;
            }

            if (input.clipShape > 0.5) {
                let clipCenter = input.clipRectPx.xy + input.clipRectPx.zw * 0.5;
                let clipHalf = max(input.clipRectPx.zw * 0.5, vec2<f32>(0.0001));
                let clipLocal = (input.pixelPosition - clipCenter) / clipHalf;
                if (input.clipShape < 1.5) {
                    if (abs(clipLocal.x) > 1.0 || abs(clipLocal.y) > 1.0) { discard; }
                } else if (input.clipShape < 2.5) {
                    if (dot(clipLocal, clipLocal) > 1.0) { discard; }
                } else {
                    if (abs(clipLocal.x) + abs(clipLocal.y) > 1.0) { discard; }
                }
            }

            return input.color;
        }
        """;

    private const string TextShader = """
        struct FrameUniforms {
            viewProjection: mat4x4<f32>,
            viewportSize: vec2<f32>,
            padding: vec2<f32>,
        };

        @group(0) @binding(0) var<uniform> frame: FrameUniforms;
        @group(0) @binding(1) var fontSampler: sampler;
        @group(0) @binding(2) var fontAtlas: texture_2d<f32>;

        struct InstanceInput {
            @location(0) centerPx: vec2<f32>,
            @location(1) halfSizePx: vec2<f32>,
            @location(2) color: vec4<f32>,
            @location(3) uvRect: vec4<f32>,
            @location(4) clipRectPx: vec4<f32>,
            @location(5) clipShape: f32,
        };

        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
            @location(0) uv: vec2<f32>,
            @location(1) pixelPosition: vec2<f32>,
            @location(2) color: vec4<f32>,
            @location(3) clipRectPx: vec4<f32>,
            @location(4) clipShape: f32,
        };

        @vertex
        fn vertexMain(@builtin(vertex_index) vertexIndex: u32, input: InstanceInput) -> VertexOutput {
            var corners = array<vec2<f32>, 6>(
                vec2<f32>(-1.0, -1.0),
                vec2<f32>( 1.0, -1.0),
                vec2<f32>(-1.0,  1.0),
                vec2<f32>(-1.0,  1.0),
                vec2<f32>( 1.0, -1.0),
                vec2<f32>( 1.0,  1.0)
            );
            let local = corners[vertexIndex];
            let pixel = input.centerPx + local * input.halfSizePx;
            let ndc = vec2<f32>(
                pixel.x / frame.viewportSize.x * 2.0 - 1.0,
                1.0 - pixel.y / frame.viewportSize.y * 2.0);

            var output: VertexOutput;
            output.position = vec4<f32>(ndc, 0.0, 1.0);
            output.uv = mix(input.uvRect.xy, input.uvRect.zw, local * 0.5 + 0.5);
            output.pixelPosition = pixel;
            output.color = input.color;
            output.clipRectPx = input.clipRectPx;
            output.clipShape = input.clipShape;
            return output;
        }

        @fragment
        fn fragmentMain(input: VertexOutput) -> @location(0) vec4<f32> {
            if (input.clipShape > 0.5) {
                let clipCenter = input.clipRectPx.xy + input.clipRectPx.zw * 0.5;
                let clipHalf = max(input.clipRectPx.zw * 0.5, vec2<f32>(0.0001));
                let clipLocal = (input.pixelPosition - clipCenter) / clipHalf;
                if (input.clipShape < 1.5) {
                    if (abs(clipLocal.x) > 1.0 || abs(clipLocal.y) > 1.0) { discard; }
                } else if (input.clipShape < 2.5) {
                    if (dot(clipLocal, clipLocal) > 1.0) { discard; }
                } else {
                    if (abs(clipLocal.x) + abs(clipLocal.y) > 1.0) { discard; }
                }
            }

            let coverage = textureSample(fontAtlas, fontSampler, input.uv).r;
            return vec4<f32>(input.color.rgb, input.color.a * coverage);
        }
        """;

    private const string WorldBarShader = """
        struct FrameUniforms {
            viewProjection: mat4x4<f32>,
            viewportSize: vec2<f32>,
            padding: vec2<f32>,
        };

        struct WorldHudAnchor {
            worldPosition: vec3<f32>,
            visibility: f32,
        };

        @group(0) @binding(0) var<uniform> frame: FrameUniforms;
        @group(0) @binding(1) var<storage, read> anchors: array<WorldHudAnchor>;

        struct InstanceInput {
            @location(0) halfSizePx: vec2<f32>,
            @location(1) healthRatio: f32,
            @location(2) anchorIndex: f32,
            @location(3) backgroundColor: vec4<f32>,
            @location(4) foregroundColor: vec4<f32>,
            @location(5) paddingPx: vec2<f32>,
        };

        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
            @location(0) localPosition: vec2<f32>,
            @location(1) halfSizePx: vec2<f32>,
            @location(2) paddingPx: vec2<f32>,
            @location(3) healthRatio: f32,
            @location(4) backgroundColor: vec4<f32>,
            @location(5) foregroundColor: vec4<f32>,
        };

        @vertex
        fn vertexMain(@builtin(vertex_index) vertexIndex: u32, input: InstanceInput) -> VertexOutput {
            var corners = array<vec2<f32>, 6>(
                vec2<f32>(-1.0, -1.0),
                vec2<f32>( 1.0, -1.0),
                vec2<f32>(-1.0,  1.0),
                vec2<f32>(-1.0,  1.0),
                vec2<f32>( 1.0, -1.0),
                vec2<f32>( 1.0,  1.0)
            );
            let local = corners[vertexIndex];
            let anchor = anchors[u32(input.anchorIndex)];
            let clip = frame.viewProjection * vec4<f32>(anchor.worldPosition, 1.0);
            var output: VertexOutput;
            // Match WebGpuWorldHudAnchorProjection.ShouldSuppress: never divide by <= epsilon clip.w.
            if (anchor.visibility <= 0.0 || clip.w <= 0.0001) {
                output.position = vec4<f32>(2.0, 2.0, 0.0, 1.0);
                output.localPosition = local;
                output.halfSizePx = input.halfSizePx;
                output.paddingPx = input.paddingPx;
                output.healthRatio = 0.0;
                output.backgroundColor = vec4<f32>(0.0);
                output.foregroundColor = vec4<f32>(0.0);
                return output;
            }

            let ndc = clip.xy / clip.w;
            let centerPx = vec2<f32>(
                (ndc.x * 0.5 + 0.5) * frame.viewportSize.x,
                (1.0 - (ndc.y * 0.5 + 0.5)) * frame.viewportSize.y);
            let outerHalf = input.halfSizePx + input.paddingPx;
            let pixel = centerPx + local * outerHalf;

            output.position = vec4<f32>(
                pixel.x / frame.viewportSize.x * 2.0 - 1.0,
                1.0 - pixel.y / frame.viewportSize.y * 2.0,
                0.0,
                1.0);
            output.localPosition = local;
            output.halfSizePx = input.halfSizePx;
            output.paddingPx = input.paddingPx;
            output.healthRatio = clamp(input.healthRatio, 0.0, 1.0);
            output.backgroundColor = input.backgroundColor;
            output.foregroundColor = input.foregroundColor;
            return output;
        }

        @fragment
        fn fragmentMain(input: VertexOutput) -> @location(0) vec4<f32> {
            let outerHalf = input.halfSizePx + input.paddingPx;
            let innerHalf = input.halfSizePx;
            if (abs(input.localPosition.x) > 1.0 || abs(input.localPosition.y) > 1.0) {
                discard;
            }

            let px = input.localPosition * outerHalf;
            let borderColor = vec4<f32>(0.01, 0.015, 0.02, input.backgroundColor.a * 0.9);
            if (abs(px.x) > innerHalf.x || abs(px.y) > innerHalf.y) {
                return borderColor;
            }

            let fillEdge = -innerHalf.x + max(innerHalf.x * input.healthRatio * 2.0, 0.0001);
            if (px.x <= fillEdge) {
                return input.foregroundColor;
            }

            return input.backgroundColor;
        }
        """;

    private const string WorldGlyphShader = """
        struct FrameUniforms {
            viewProjection: mat4x4<f32>,
            viewportSize: vec2<f32>,
            padding: vec2<f32>,
        };

        struct WorldHudAnchor {
            worldPosition: vec3<f32>,
            visibility: f32,
        };

        @group(0) @binding(0) var<uniform> frame: FrameUniforms;
        @group(0) @binding(1) var fontSampler: sampler;
        @group(0) @binding(2) var fontAtlas: texture_2d<f32>;
        @group(0) @binding(3) var<storage, read> anchors: array<WorldHudAnchor>;

        struct InstanceInput {
            @location(0) localOffsetPx: vec2<f32>,
            @location(1) halfSizePx: vec2<f32>,
            @location(2) color: vec4<f32>,
            @location(3) uvRect: vec4<f32>,
            @location(4) anchorIndex: f32,
        };

        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
            @location(0) uv: vec2<f32>,
            @location(1) color: vec4<f32>,
        };

        @vertex
        fn vertexMain(@builtin(vertex_index) vertexIndex: u32, input: InstanceInput) -> VertexOutput {
            var corners = array<vec2<f32>, 6>(
                vec2<f32>(-1.0, -1.0),
                vec2<f32>( 1.0, -1.0),
                vec2<f32>(-1.0,  1.0),
                vec2<f32>(-1.0,  1.0),
                vec2<f32>( 1.0, -1.0),
                vec2<f32>( 1.0,  1.0)
            );
            let local = corners[vertexIndex];
            let anchor = anchors[u32(input.anchorIndex)];
            let clip = frame.viewProjection * vec4<f32>(anchor.worldPosition, 1.0);
            var output: VertexOutput;
            // Match WebGpuWorldHudAnchorProjection.ShouldSuppress: never divide by <= epsilon clip.w.
            if (anchor.visibility <= 0.0 || clip.w <= 0.0001) {
                output.position = vec4<f32>(2.0, 2.0, 0.0, 1.0);
                output.uv = vec2<f32>(0.0);
                output.color = vec4<f32>(0.0);
                return output;
            }

            let ndc = clip.xy / clip.w;
            let anchorPx = vec2<f32>(
                (ndc.x * 0.5 + 0.5) * frame.viewportSize.x,
                (1.0 - (ndc.y * 0.5 + 0.5)) * frame.viewportSize.y);
            let centerPx = anchorPx + input.localOffsetPx;
            let pixel = centerPx + local * input.halfSizePx;

            output.position = vec4<f32>(
                pixel.x / frame.viewportSize.x * 2.0 - 1.0,
                1.0 - pixel.y / frame.viewportSize.y * 2.0,
                0.0,
                1.0);
            // UvRect is min.xy/max.zw (same contract as TextShader / WebGpuFontUvSampling).
            output.uv = mix(input.uvRect.xy, input.uvRect.zw, local * 0.5 + 0.5);
            output.color = input.color;
            return output;
        }

        @fragment
        fn fragmentMain(input: VertexOutput) -> @location(0) vec4<f32> {
            let alpha = textureSample(fontAtlas, fontSampler, input.uv).r * input.color.a;
            if (alpha <= 0.001) {
                discard;
            }
            return vec4<f32>(input.color.rgb, alpha);
        }
        """;

    private static WebGpuBrowserRuntime? s_active;

    private readonly nint _selectorUtf8;
    private readonly IWebGpuFrameSource _frameSource;
    private readonly FrameTimingWindow _frameTiming = new(1.0);
    private readonly WebGpuPerformanceOverlay _performanceOverlay;
    private readonly Dictionary<int, GpuMesh> _terrainMeshes = new();
    private readonly Dictionary<int, GpuMesh> _worldMeshes = new();
    private readonly Dictionary<int, GpuWorldBatch> _worldBatches = new();
    private WgpuInstance* _instance;
    private WgpuSurface* _surface;
    private WgpuAdapter* _adapter;
    private WgpuDevice* _device;
    private WgpuQueue* _queue;
    private WgpuSwapChain* _swapChain;
    private WgpuTexture* _depthTexture;
    private WgpuTextureView* _depthTextureView;
    private WgpuRenderPipeline* _terrainPipeline;
    private WgpuRenderPipeline* _unitPipeline;
    private WgpuRenderPipeline* _groundOverlayPipeline;
    private WgpuRenderPipeline* _screenPipeline;
    private WgpuRenderPipeline* _textPipeline;
    private WgpuRenderPipeline* _worldBarPipeline;
    private WgpuRenderPipeline* _worldGlyphPipeline;
    private WgpuBindGroupLayout* _bindGroupLayout;
    private WgpuBindGroupLayout* _textBindGroupLayout;
    private WgpuBindGroupLayout* _worldBarBindGroupLayout;
    private WgpuBindGroupLayout* _worldGlyphBindGroupLayout;
    private WgpuPipelineLayout* _pipelineLayout;
    private WgpuPipelineLayout* _textPipelineLayout;
    private WgpuPipelineLayout* _worldBarPipelineLayout;
    private WgpuPipelineLayout* _worldGlyphPipelineLayout;
    private WgpuBindGroup* _bindGroup;
    private WgpuBindGroup* _textBindGroup;
    private WgpuBindGroup* _worldBarBindGroup;
    private WgpuBindGroup* _worldGlyphBindGroup;
    private WgpuBuffer* _uniformBuffer;
    private WgpuBuffer* _groundOverlayVertexBuffer;
    private WgpuBuffer* _groundOverlayIndexBuffer;
    private WgpuBuffer* _underUiWorldBarInstanceBuffer;
    private WgpuBuffer* _underUiWorldAnchorBuffer;
    private WgpuBuffer* _underUiWorldGlyphInstanceBuffer;
    private WgpuBuffer* _topMostScreenInstanceBuffer;
    private WgpuBuffer* _topMostTextInstanceBuffer;
    private WgpuBuffer* _performanceScreenInstanceBuffer;
    private WgpuBuffer* _performanceTextInstanceBuffer;
    private WgpuTexture* _fontTexture;
    private WgpuTextureView* _fontTextureView;
    private WgpuSampler* _fontSampler;
    private ulong _groundOverlayVertexCapacity;
    private ulong _groundOverlayIndexCapacity;
    private ulong _underUiWorldBarInstanceCapacity;
    private ulong _underUiWorldAnchorCapacity;
    private ulong _underUiWorldGlyphInstanceCapacity;
    private ulong _topMostScreenInstanceCapacity;
    private ulong _topMostTextInstanceCapacity;
    private ulong _performanceScreenInstanceCapacity;
    private ulong _performanceTextInstanceCapacity;
    private uint _groundOverlayIndexCount;
    private WgpuTextureFormat _surfaceFormat;
    private uint _canvasWidth;
    private uint _canvasHeight;
    private uint _frameNumber;
    private long _lastFrameTimestamp;
    private long _lastAllocatedBytes;
    private SetupState _state;

    public WebGpuBrowserRuntime(string canvasSelector, IWebGpuFrameSource frameSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canvasSelector);
        ArgumentNullException.ThrowIfNull(frameSource);
        _selectorUtf8 = Marshal.StringToCoTaskMemUTF8(canvasSelector);
        _frameSource = frameSource;
        _performanceOverlay = new WebGpuPerformanceOverlay(frameSource.TextAtlas);
    }

    public void Run()
    {
        if (s_active != null)
        {
            throw new InvalidOperationException("Only one browser WebGPU runtime may be active.");
        }

        s_active = this;
        try
        {
            CreateInstanceAndSurface();
            RequestAdapter();
            EmscriptenNative.SetMainLoop(&OnFrame);
            Console.WriteLine("[Ludots WebGPU] C# main loop registered.");
        }
        catch (Exception exception)
        {
            Fail("startup", exception);
            throw;
        }
    }

    private void CreateInstanceAndSurface()
    {
        _instance = WebGpuNative.CreateInstance(null);
        Require(_instance != null, "wgpuCreateInstance returned null.");

        WgpuSurfaceDescriptorFromCanvasHtmlSelector canvasDescriptor = new()
        {
            Chain = new WgpuChainedStruct
            {
                Next = null,
                SType = WgpuSType.SurfaceDescriptorFromCanvasHtmlSelector,
            },
            Selector = (byte*)_selectorUtf8,
        };
        WgpuSurfaceDescriptor surfaceDescriptor = new()
        {
            NextInChain = &canvasDescriptor.Chain,
            Label = null,
        };

        _surface = WebGpuNative.InstanceCreateSurface(_instance, &surfaceDescriptor);
        Require(_surface != null, "wgpuInstanceCreateSurface returned null.");
        _state = SetupState.SurfaceReady;
    }

    private void RequestAdapter()
    {
        WgpuRequestAdapterOptions options = new()
        {
            CompatibleSurface = _surface,
            PowerPreference = WgpuPowerPreference.HighPerformance,
            ForceFallbackAdapter = 0,
            CompatibilityMode = 0,
        };
        delegate* unmanaged[Cdecl]<WgpuRequestAdapterStatus, WgpuAdapter*, byte*, void*, void> callback = &OnAdapterRequested;
        _state = SetupState.RequestingAdapter;
        WebGpuNative.InstanceRequestAdapter(_instance, &options, (nint)callback, null);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnAdapterRequested(
        WgpuRequestAdapterStatus status,
        WgpuAdapter* adapter,
        byte* message,
        void* userData)
    {
        WebGpuBrowserRuntime? runtime = s_active;
        if (runtime == null)
        {
            return;
        }

        try
        {
            if (status != WgpuRequestAdapterStatus.Success || adapter == null)
            {
                runtime.Fail("requestAdapter", status, message);
                return;
            }

            runtime._adapter = adapter;
            runtime._state = SetupState.RequestingDevice;
            delegate* unmanaged[Cdecl]<WgpuRequestDeviceStatus, WgpuDevice*, byte*, void*, void> callback = &OnDeviceRequested;
            WebGpuNative.AdapterRequestDevice(adapter, null, (nint)callback, null);
            Console.WriteLine("[Ludots WebGPU] Adapter acquired.");
        }
        catch (Exception exception)
        {
            runtime.Fail("requestAdapter callback", exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDeviceRequested(
        WgpuRequestDeviceStatus status,
        WgpuDevice* device,
        byte* message,
        void* userData)
    {
        WebGpuBrowserRuntime? runtime = s_active;
        if (runtime == null)
        {
            return;
        }

        try
        {
            if (status != WgpuRequestDeviceStatus.Success || device == null)
            {
                runtime.Fail("requestDevice", status, message);
                return;
            }

            runtime._device = device;
            delegate* unmanaged[Cdecl]<WgpuErrorType, byte*, void*, void> errorCallback = &OnUncapturedError;
            WebGpuNative.DeviceSetUncapturedErrorCallback(device, (nint)errorCallback, null);
            runtime.InitializeRendering();
            runtime._state = SetupState.Ready;
            Console.WriteLine(
                $"[Ludots WebGPU] Device and formal pipelines ready ({runtime._canvasWidth}x{runtime._canvasHeight}, {runtime._surfaceFormat}).");
        }
        catch (Exception exception)
        {
            runtime.Fail("requestDevice callback", exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnUncapturedError(WgpuErrorType type, byte* message, void* userData)
    {
        WebGpuBrowserRuntime? runtime = s_active;
        if (runtime == null)
        {
            return;
        }

        string detail = message == null
            ? "No browser diagnostic was provided."
            : Marshal.PtrToStringUTF8((nint)message) ?? "Empty browser diagnostic.";
        runtime.Fail("uncaptured WebGPU error", new InvalidOperationException($"{type}: {detail}"));
    }

    private void InitializeRendering()
    {
        _queue = WebGpuNative.DeviceGetQueue(_device);
        Require(_queue != null, "wgpuDeviceGetQueue returned null.");

        _surfaceFormat = WebGpuNative.SurfaceGetPreferredFormat(_surface, _adapter);
        Require(_surfaceFormat != WgpuTextureFormat.Undefined, "The browser returned an undefined surface format.");

        ResizeSwapChain(force: true);
        CreatePipelinesAndBindings();
    }

    private void CreatePipelinesAndBindings()
    {
        WgpuVertexAttribute* worldAttributes = stackalloc WgpuVertexAttribute[3];
        worldAttributes[0] = new WgpuVertexAttribute
        {
            Format = WgpuVertexFormat.Float32x3,
            Offset = 0,
            ShaderLocation = 0,
        };
        worldAttributes[1] = new WgpuVertexAttribute
        {
            Format = WgpuVertexFormat.Float32x3,
            Offset = 12,
            ShaderLocation = 1,
        };
        worldAttributes[2] = new WgpuVertexAttribute
        {
            Format = WgpuVertexFormat.Float32x4,
            Offset = 24,
            ShaderLocation = 2,
        };
        WgpuVertexBufferLayout worldLayout = new()
        {
            ArrayStride = checked((ulong)sizeof(WebGpuWorldVertex)),
            StepMode = WgpuVertexStepMode.Vertex,
            AttributeCount = 3,
            Attributes = worldAttributes,
        };

        WgpuBindGroupLayoutEntry uniformLayoutEntry = new()
        {
            Binding = 0,
            Visibility = WgpuShaderStage.Vertex,
            Buffer = new WgpuBufferBindingLayout
            {
                Type = WgpuBufferBindingType.Uniform,
                MinBindingSize = checked((ulong)sizeof(WebGpuCameraFrame)),
            },
        };
        WgpuBindGroupLayoutDescriptor bindGroupLayoutDescriptor = new()
        {
            EntryCount = 1,
            Entries = &uniformLayoutEntry,
        };
        _bindGroupLayout = WebGpuNative.DeviceCreateBindGroupLayout(_device, &bindGroupLayoutDescriptor);
        Require(_bindGroupLayout != null, "wgpuDeviceCreateBindGroupLayout returned null.");

        WgpuBindGroupLayout* bindGroupLayoutHandle = _bindGroupLayout;
        WgpuPipelineLayoutDescriptor pipelineLayoutDescriptor = new()
        {
            BindGroupLayoutCount = 1,
            BindGroupLayouts = &bindGroupLayoutHandle,
        };
        _pipelineLayout = WebGpuNative.DeviceCreatePipelineLayout(_device, &pipelineLayoutDescriptor);
        Require(_pipelineLayout != null, "wgpuDeviceCreatePipelineLayout returned null.");

        _terrainPipeline = CreatePipeline(
            TerrainShader,
            &worldLayout,
            bufferCount: 1,
            layout: _pipelineLayout,
            depthEnabled: true,
            alphaBlend: false);
        _groundOverlayPipeline = CreatePipeline(
            TerrainShader,
            &worldLayout,
            bufferCount: 1,
            layout: _pipelineLayout,
            depthEnabled: true,
            alphaBlend: true,
            depthWriteEnabled: false);

        WgpuVertexAttribute* instanceAttributes = stackalloc WgpuVertexAttribute[4];
        instanceAttributes[0] = new WgpuVertexAttribute
        {
            Format = WgpuVertexFormat.Float32x3,
            Offset = 0,
            ShaderLocation = 3,
        };
        instanceAttributes[1] = new WgpuVertexAttribute
        {
            Format = WgpuVertexFormat.Float32x3,
            Offset = 12,
            ShaderLocation = 4,
        };
        instanceAttributes[2] = new WgpuVertexAttribute
        {
            Format = WgpuVertexFormat.Float32x4,
            Offset = 24,
            ShaderLocation = 5,
        };
        instanceAttributes[3] = new WgpuVertexAttribute
        {
            Format = WgpuVertexFormat.Float32x4,
            Offset = 40,
            ShaderLocation = 6,
        };
        WgpuVertexBufferLayout* unitLayouts = stackalloc WgpuVertexBufferLayout[2];
        unitLayouts[0] = worldLayout;
        unitLayouts[1] = new WgpuVertexBufferLayout
        {
            ArrayStride = checked((ulong)sizeof(WebGpuWorldInstance)),
            StepMode = WgpuVertexStepMode.Instance,
            AttributeCount = 4,
            Attributes = instanceAttributes,
        };
        _unitPipeline = CreatePipeline(
            UnitShader,
            unitLayouts,
            bufferCount: 2,
            layout: _pipelineLayout,
            depthEnabled: true,
            alphaBlend: false);

        WgpuVertexAttribute* screenAttributes = stackalloc WgpuVertexAttribute[7];
        screenAttributes[0] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 };
        screenAttributes[1] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x2, Offset = 8, ShaderLocation = 1 };
        screenAttributes[2] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x4, Offset = 16, ShaderLocation = 2 };
        screenAttributes[3] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x4, Offset = 32, ShaderLocation = 3 };
        screenAttributes[4] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32, Offset = 48, ShaderLocation = 4 };
        screenAttributes[5] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32, Offset = 52, ShaderLocation = 5 };
        screenAttributes[6] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32, Offset = 56, ShaderLocation = 6 };
        WgpuVertexBufferLayout screenLayout = new()
        {
            ArrayStride = checked((ulong)sizeof(WebGpuScreenInstance)),
            StepMode = WgpuVertexStepMode.Instance,
            AttributeCount = 7,
            Attributes = screenAttributes,
        };
        _screenPipeline = CreatePipeline(
            ScreenShader,
            &screenLayout,
            bufferCount: 1,
            layout: _pipelineLayout,
            depthEnabled: false,
            alphaBlend: true);

        _uniformBuffer = CreateBuffer(
            checked((ulong)sizeof(WebGpuCameraFrame)),
            WgpuBufferUsage.Uniform | WgpuBufferUsage.CopyDestination);
        WgpuBindGroupEntry bindGroupEntry = new()
        {
            Binding = 0,
            Buffer = _uniformBuffer,
            Offset = 0,
            Size = checked((ulong)sizeof(WebGpuCameraFrame)),
        };
        WgpuBindGroupDescriptor bindGroupDescriptor = new()
        {
            Layout = _bindGroupLayout,
            EntryCount = 1,
            Entries = &bindGroupEntry,
        };
        _bindGroup = WebGpuNative.DeviceCreateBindGroup(_device, &bindGroupDescriptor);
        Require(_bindGroup != null, "wgpuDeviceCreateBindGroup returned null.");

        CreateTextPipelineAndBindings();
    }

    private void CreateTextPipelineAndBindings()
    {
        WebGpuFontAtlas atlas = _frameSource.TextAtlas
            ?? throw new InvalidOperationException("The WebGPU frame source did not provide a text atlas.");
        if ((atlas.Width & 255) != 0)
        {
            throw new InvalidOperationException(
                $"WebGPU R8 font atlas width {atlas.Width} is not aligned to the required 256-byte upload row.");
        }

        WgpuTextureDescriptor textureDescriptor = new()
        {
            Usage = WgpuTextureUsage.CopyDestination | WgpuTextureUsage.TextureBinding,
            Dimension = WgpuTextureDimension.TwoDimensional,
            Size = new WgpuExtent3D
            {
                Width = checked((uint)atlas.Width),
                Height = checked((uint)atlas.Height),
                DepthOrArrayLayers = 1,
            },
            Format = WgpuTextureFormat.R8Unorm,
            MipLevelCount = 1,
            SampleCount = 1,
        };
        _fontTexture = WebGpuNative.DeviceCreateTexture(_device, &textureDescriptor);
        Require(_fontTexture != null, "wgpuDeviceCreateTexture returned null for the font atlas.");
        _fontTextureView = WebGpuNative.TextureCreateView(_fontTexture, null);
        Require(_fontTextureView != null, "wgpuTextureCreateView returned null for the font atlas.");

        WgpuSamplerDescriptor samplerDescriptor = new()
        {
            AddressModeU = WgpuAddressMode.ClampToEdge,
            AddressModeV = WgpuAddressMode.ClampToEdge,
            AddressModeW = WgpuAddressMode.ClampToEdge,
            MagFilter = WgpuFilterMode.Linear,
            MinFilter = WgpuFilterMode.Linear,
            MipmapFilter = WgpuMipmapFilterMode.Nearest,
            LodMinClamp = 0f,
            LodMaxClamp = 0f,
            MaxAnisotropy = 1,
        };
        _fontSampler = WebGpuNative.DeviceCreateSampler(_device, &samplerDescriptor);
        Require(_fontSampler != null, "wgpuDeviceCreateSampler returned null for the font atlas.");

        ReadOnlySpan<byte> pixels = atlas.Pixels.Span;
        fixed (byte* pixelPointer = pixels)
        {
            WgpuImageCopyTexture destination = new()
            {
                Texture = _fontTexture,
                MipLevel = 0,
                Origin = default,
                Aspect = WgpuTextureAspect.All,
            };
            WgpuTextureDataLayout dataLayout = new()
            {
                Offset = 0,
                BytesPerRow = checked((uint)atlas.Width),
                RowsPerImage = checked((uint)atlas.Height),
            };
            WgpuExtent3D writeSize = textureDescriptor.Size;
            WebGpuNative.QueueWriteTexture(
                _queue,
                &destination,
                pixelPointer,
                checked((nuint)pixels.Length),
                &dataLayout,
                &writeSize);
        }

        WgpuBindGroupLayoutEntry* layoutEntries = stackalloc WgpuBindGroupLayoutEntry[3];
        layoutEntries[0] = new WgpuBindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = WgpuShaderStage.Vertex,
            Buffer = new WgpuBufferBindingLayout
            {
                Type = WgpuBufferBindingType.Uniform,
                MinBindingSize = checked((ulong)sizeof(WebGpuCameraFrame)),
            },
        };
        layoutEntries[1] = new WgpuBindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = WgpuShaderStage.Fragment,
            Sampler = new WgpuSamplerBindingLayout
            {
                Type = WgpuSamplerBindingType.Filtering,
            },
        };
        layoutEntries[2] = new WgpuBindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = WgpuShaderStage.Fragment,
            Texture = new WgpuTextureBindingLayout
            {
                SampleType = WgpuTextureSampleType.Float,
                ViewDimension = WgpuTextureViewDimension.TwoDimensional,
                Multisampled = 0,
            },
        };
        WgpuBindGroupLayoutDescriptor layoutDescriptor = new()
        {
            EntryCount = 3,
            Entries = layoutEntries,
        };
        _textBindGroupLayout = WebGpuNative.DeviceCreateBindGroupLayout(_device, &layoutDescriptor);
        Require(_textBindGroupLayout != null, "wgpuDeviceCreateBindGroupLayout returned null for text.");

        WgpuBindGroupLayout* layoutHandle = _textBindGroupLayout;
        WgpuPipelineLayoutDescriptor pipelineLayoutDescriptor = new()
        {
            BindGroupLayoutCount = 1,
            BindGroupLayouts = &layoutHandle,
        };
        _textPipelineLayout = WebGpuNative.DeviceCreatePipelineLayout(_device, &pipelineLayoutDescriptor);
        Require(_textPipelineLayout != null, "wgpuDeviceCreatePipelineLayout returned null for text.");

        WgpuVertexAttribute* textAttributes = stackalloc WgpuVertexAttribute[6];
        textAttributes[0] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 };
        textAttributes[1] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x2, Offset = 8, ShaderLocation = 1 };
        textAttributes[2] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x4, Offset = 16, ShaderLocation = 2 };
        textAttributes[3] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x4, Offset = 32, ShaderLocation = 3 };
        textAttributes[4] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x4, Offset = 48, ShaderLocation = 4 };
        textAttributes[5] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32, Offset = 64, ShaderLocation = 5 };
        WgpuVertexBufferLayout textLayout = new()
        {
            ArrayStride = checked((ulong)sizeof(WebGpuGlyphInstance)),
            StepMode = WgpuVertexStepMode.Instance,
            AttributeCount = 6,
            Attributes = textAttributes,
        };
        _textPipeline = CreatePipeline(
            TextShader,
            &textLayout,
            bufferCount: 1,
            layout: _textPipelineLayout,
            depthEnabled: false,
            alphaBlend: true);

        WgpuBindGroupEntry* bindEntries = stackalloc WgpuBindGroupEntry[3];
        bindEntries[0] = new WgpuBindGroupEntry
        {
            Binding = 0,
            Buffer = _uniformBuffer,
            Offset = 0,
            Size = checked((ulong)sizeof(WebGpuCameraFrame)),
        };
        bindEntries[1] = new WgpuBindGroupEntry
        {
            Binding = 1,
            Sampler = _fontSampler,
        };
        bindEntries[2] = new WgpuBindGroupEntry
        {
            Binding = 2,
            TextureView = _fontTextureView,
        };
        WgpuBindGroupDescriptor bindGroupDescriptor = new()
        {
            Layout = _textBindGroupLayout,
            EntryCount = 3,
            Entries = bindEntries,
        };
        _textBindGroup = WebGpuNative.DeviceCreateBindGroup(_device, &bindGroupDescriptor);
        Require(_textBindGroup != null, "wgpuDeviceCreateBindGroup returned null for text.");

        CreateWorldHudPipelinesAndBindings();

        Console.WriteLine(
            $"[Ludots WebGPU] DroidSans atlas ready: {atlas.Width}x{atlas.Height}, glyphs={atlas.GlyphCount}, sourceSha256={atlas.SourceSha256}.");
    }

    private void CreateWorldHudPipelinesAndBindings()
    {
        _underUiWorldAnchorBuffer = CreateBuffer(
            checked((ulong)sizeof(WebGpuWorldHudAnchor) * 4096u),
            WgpuBufferUsage.Storage | WgpuBufferUsage.CopyDestination);
        _underUiWorldAnchorCapacity = checked((ulong)sizeof(WebGpuWorldHudAnchor) * 4096u);

        WgpuBindGroupLayoutEntry* barLayoutEntries = stackalloc WgpuBindGroupLayoutEntry[2];
        barLayoutEntries[0] = new WgpuBindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = WgpuShaderStage.Vertex,
            Buffer = new WgpuBufferBindingLayout
            {
                Type = WgpuBufferBindingType.Uniform,
                MinBindingSize = checked((ulong)sizeof(WebGpuCameraFrame)),
            },
        };
        barLayoutEntries[1] = new WgpuBindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = WgpuShaderStage.Vertex,
            Buffer = new WgpuBufferBindingLayout
            {
                Type = WgpuBufferBindingType.ReadOnlyStorage,
                MinBindingSize = checked((ulong)sizeof(WebGpuWorldHudAnchor)),
            },
        };
        WgpuBindGroupLayoutDescriptor barLayoutDescriptor = new()
        {
            EntryCount = 2,
            Entries = barLayoutEntries,
        };
        _worldBarBindGroupLayout = WebGpuNative.DeviceCreateBindGroupLayout(_device, &barLayoutDescriptor);
        Require(_worldBarBindGroupLayout != null, "wgpuDeviceCreateBindGroupLayout returned null for world bars.");

        WgpuBindGroupLayout* barLayoutHandle = _worldBarBindGroupLayout;
        WgpuPipelineLayoutDescriptor barPipelineLayoutDescriptor = new()
        {
            BindGroupLayoutCount = 1,
            BindGroupLayouts = &barLayoutHandle,
        };
        _worldBarPipelineLayout = WebGpuNative.DeviceCreatePipelineLayout(_device, &barPipelineLayoutDescriptor);
        Require(_worldBarPipelineLayout != null, "wgpuDeviceCreatePipelineLayout returned null for world bars.");

        WgpuVertexAttribute* barAttributes = stackalloc WgpuVertexAttribute[6];
        barAttributes[0] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 };
        barAttributes[1] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32, Offset = 8, ShaderLocation = 1 };
        barAttributes[2] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32, Offset = 12, ShaderLocation = 2 };
        barAttributes[3] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x4, Offset = 16, ShaderLocation = 3 };
        barAttributes[4] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x4, Offset = 32, ShaderLocation = 4 };
        barAttributes[5] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x2, Offset = 48, ShaderLocation = 5 };
        WgpuVertexBufferLayout barLayout = new()
        {
            ArrayStride = checked((ulong)sizeof(WebGpuWorldBarInstance)),
            StepMode = WgpuVertexStepMode.Instance,
            AttributeCount = 6,
            Attributes = barAttributes,
        };
        _worldBarPipeline = CreatePipeline(
            WorldBarShader,
            &barLayout,
            bufferCount: 1,
            layout: _worldBarPipelineLayout,
            depthEnabled: false,
            alphaBlend: true);

        WgpuBindGroupLayoutEntry* layoutEntries = stackalloc WgpuBindGroupLayoutEntry[4];
        layoutEntries[0] = new WgpuBindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = WgpuShaderStage.Vertex,
            Buffer = new WgpuBufferBindingLayout
            {
                Type = WgpuBufferBindingType.Uniform,
                MinBindingSize = checked((ulong)sizeof(WebGpuCameraFrame)),
            },
        };
        layoutEntries[1] = new WgpuBindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = WgpuShaderStage.Fragment,
            Sampler = new WgpuSamplerBindingLayout { Type = WgpuSamplerBindingType.Filtering },
        };
        layoutEntries[2] = new WgpuBindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = WgpuShaderStage.Fragment,
            Texture = new WgpuTextureBindingLayout
            {
                SampleType = WgpuTextureSampleType.Float,
                ViewDimension = WgpuTextureViewDimension.TwoDimensional,
                Multisampled = 0,
            },
        };
        layoutEntries[3] = new WgpuBindGroupLayoutEntry
        {
            Binding = 3,
            Visibility = WgpuShaderStage.Vertex,
            Buffer = new WgpuBufferBindingLayout
            {
                Type = WgpuBufferBindingType.ReadOnlyStorage,
                MinBindingSize = checked((ulong)sizeof(WebGpuWorldHudAnchor)),
            },
        };
        WgpuBindGroupLayoutDescriptor layoutDescriptor = new()
        {
            EntryCount = 4,
            Entries = layoutEntries,
        };
        _worldGlyphBindGroupLayout = WebGpuNative.DeviceCreateBindGroupLayout(_device, &layoutDescriptor);
        Require(_worldGlyphBindGroupLayout != null, "wgpuDeviceCreateBindGroupLayout returned null for world glyphs.");

        WgpuBindGroupLayout* layoutHandle = _worldGlyphBindGroupLayout;
        WgpuPipelineLayoutDescriptor pipelineLayoutDescriptor = new()
        {
            BindGroupLayoutCount = 1,
            BindGroupLayouts = &layoutHandle,
        };
        _worldGlyphPipelineLayout = WebGpuNative.DeviceCreatePipelineLayout(_device, &pipelineLayoutDescriptor);
        Require(_worldGlyphPipelineLayout != null, "wgpuDeviceCreatePipelineLayout returned null for world glyphs.");

        WgpuVertexAttribute* glyphAttributes = stackalloc WgpuVertexAttribute[5];
        glyphAttributes[0] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 };
        glyphAttributes[1] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x2, Offset = 8, ShaderLocation = 1 };
        glyphAttributes[2] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x4, Offset = 16, ShaderLocation = 2 };
        glyphAttributes[3] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32x4, Offset = 32, ShaderLocation = 3 };
        glyphAttributes[4] = new WgpuVertexAttribute { Format = WgpuVertexFormat.Float32, Offset = 48, ShaderLocation = 4 };
        WgpuVertexBufferLayout glyphLayout = new()
        {
            ArrayStride = checked((ulong)sizeof(WebGpuWorldGlyphInstance)),
            StepMode = WgpuVertexStepMode.Instance,
            AttributeCount = 5,
            Attributes = glyphAttributes,
        };
        _worldGlyphPipeline = CreatePipeline(
            WorldGlyphShader,
            &glyphLayout,
            bufferCount: 1,
            layout: _worldGlyphPipelineLayout,
            depthEnabled: false,
            alphaBlend: true);

        RecreateWorldHudBindGroups();
    }

    private void RecreateWorldHudBindGroups()
    {
        RecreateWorldBarBindGroup();
        RecreateWorldGlyphBindGroup();
    }

    private void RecreateWorldBarBindGroup()
    {
        if (_worldBarBindGroup != null)
        {
            WebGpuNative.BindGroupRelease(_worldBarBindGroup);
            _worldBarBindGroup = null;
        }

        if (_worldBarBindGroupLayout == null || _underUiWorldAnchorBuffer == null || _uniformBuffer == null)
        {
            throw new InvalidOperationException(
                "World HUD bar bind group requires camera uniform, anchor storage buffer, and layout.");
        }

        WgpuBindGroupEntry* bindEntries = stackalloc WgpuBindGroupEntry[2];
        bindEntries[0] = new WgpuBindGroupEntry
        {
            Binding = 0,
            Buffer = _uniformBuffer,
            Offset = 0,
            Size = checked((ulong)sizeof(WebGpuCameraFrame)),
        };
        bindEntries[1] = new WgpuBindGroupEntry
        {
            Binding = 1,
            Buffer = _underUiWorldAnchorBuffer,
            Offset = 0,
            Size = Math.Max(_underUiWorldAnchorCapacity, checked((ulong)sizeof(WebGpuWorldHudAnchor))),
        };
        WgpuBindGroupDescriptor bindGroupDescriptor = new()
        {
            Layout = _worldBarBindGroupLayout,
            EntryCount = 2,
            Entries = bindEntries,
        };
        _worldBarBindGroup = WebGpuNative.DeviceCreateBindGroup(_device, &bindGroupDescriptor);
        Require(_worldBarBindGroup != null, "wgpuDeviceCreateBindGroup returned null for world bars.");
    }

    private void RecreateWorldGlyphBindGroup()
    {
        if (_worldGlyphBindGroup != null)
        {
            WebGpuNative.BindGroupRelease(_worldGlyphBindGroup);
            _worldGlyphBindGroup = null;
        }

        if (_worldGlyphBindGroupLayout == null ||
            _underUiWorldAnchorBuffer == null ||
            _uniformBuffer == null ||
            _fontSampler == null ||
            _fontTextureView == null)
        {
            throw new InvalidOperationException(
                "World HUD glyph bind group requires camera uniform, font atlas, and anchor storage buffer.");
        }

        WgpuBindGroupEntry* bindEntries = stackalloc WgpuBindGroupEntry[4];
        bindEntries[0] = new WgpuBindGroupEntry
        {
            Binding = 0,
            Buffer = _uniformBuffer,
            Offset = 0,
            Size = checked((ulong)sizeof(WebGpuCameraFrame)),
        };
        bindEntries[1] = new WgpuBindGroupEntry { Binding = 1, Sampler = _fontSampler };
        bindEntries[2] = new WgpuBindGroupEntry { Binding = 2, TextureView = _fontTextureView };
        bindEntries[3] = new WgpuBindGroupEntry
        {
            Binding = 3,
            Buffer = _underUiWorldAnchorBuffer,
            Offset = 0,
            Size = Math.Max(_underUiWorldAnchorCapacity, checked((ulong)sizeof(WebGpuWorldHudAnchor))),
        };
        WgpuBindGroupDescriptor bindGroupDescriptor = new()
        {
            Layout = _worldGlyphBindGroupLayout,
            EntryCount = 4,
            Entries = bindEntries,
        };
        _worldGlyphBindGroup = WebGpuNative.DeviceCreateBindGroup(_device, &bindGroupDescriptor);
        Require(_worldGlyphBindGroup != null, "wgpuDeviceCreateBindGroup returned null for world glyphs.");
    }

    private WgpuRenderPipeline* CreatePipeline(
        string shaderCode,
        WgpuVertexBufferLayout* buffers,
        nuint bufferCount,
        WgpuPipelineLayout* layout,
        bool depthEnabled,
        bool alphaBlend,
        bool depthWriteEnabled = true)
    {
        byte[] shaderBytes = Encoding.UTF8.GetBytes(shaderCode + '\0');
        byte[] vertexEntryBytes = "vertexMain\0"u8.ToArray();
        byte[] fragmentEntryBytes = "fragmentMain\0"u8.ToArray();

        fixed (byte* shaderCodePointer = shaderBytes)
        fixed (byte* vertexEntry = vertexEntryBytes)
        fixed (byte* fragmentEntry = fragmentEntryBytes)
        {
            WgpuShaderModuleWgslDescriptor wgslDescriptor = new()
            {
                Chain = new WgpuChainedStruct
                {
                    Next = null,
                    SType = WgpuSType.ShaderModuleWgslDescriptor,
                },
                Code = shaderCodePointer,
            };
            WgpuShaderModuleDescriptor shaderDescriptor = new()
            {
                NextInChain = &wgslDescriptor.Chain,
            };
            WgpuShaderModule* shaderModule = WebGpuNative.DeviceCreateShaderModule(_device, &shaderDescriptor);
            Require(shaderModule != null, "wgpuDeviceCreateShaderModule returned null.");

            WgpuBlendState blend = new()
            {
                Color = new WgpuBlendComponent
                {
                    Operation = WgpuBlendOperation.Add,
                    SourceFactor = WgpuBlendFactor.SourceAlpha,
                    DestinationFactor = WgpuBlendFactor.OneMinusSourceAlpha,
                },
                Alpha = new WgpuBlendComponent
                {
                    Operation = WgpuBlendOperation.Add,
                    SourceFactor = WgpuBlendFactor.One,
                    DestinationFactor = WgpuBlendFactor.OneMinusSourceAlpha,
                },
            };
            WgpuColorTargetState colorTarget = new()
            {
                Format = _surfaceFormat,
                Blend = alphaBlend ? &blend : null,
                WriteMask = WgpuColorWriteMask.All,
            };
            WgpuFragmentState fragment = new()
            {
                Module = shaderModule,
                EntryPoint = fragmentEntry,
                TargetCount = 1,
                Targets = &colorTarget,
            };
            WgpuDepthStencilState depth = new()
            {
                Format = WgpuTextureFormat.Depth24Plus,
                DepthWriteEnabled = depthEnabled && depthWriteEnabled ? 1u : 0u,
                DepthCompare = depthEnabled ? WgpuCompareFunction.LessEqual : WgpuCompareFunction.Always,
                StencilReadMask = uint.MaxValue,
                StencilWriteMask = uint.MaxValue,
            };
            WgpuRenderPipelineDescriptor pipelineDescriptor = new()
            {
                Layout = layout,
                Vertex = new WgpuVertexState
                {
                    Module = shaderModule,
                    EntryPoint = vertexEntry,
                    BufferCount = bufferCount,
                    Buffers = buffers,
                },
                Primitive = new WgpuPrimitiveState
                {
                    Topology = WgpuPrimitiveTopology.TriangleList,
                    StripIndexFormat = WgpuIndexFormat.Undefined,
                    FrontFace = WgpuFrontFace.CounterClockwise,
                    CullMode = WgpuCullMode.None,
                },
                DepthStencil = &depth,
                Multisample = new WgpuMultisampleState
                {
                    Count = 1,
                    Mask = uint.MaxValue,
                    AlphaToCoverageEnabled = 0,
                },
                Fragment = &fragment,
            };

            WgpuRenderPipeline* pipeline = WebGpuNative.DeviceCreateRenderPipeline(_device, &pipelineDescriptor);
            Require(pipeline != null, "wgpuDeviceCreateRenderPipeline returned null.");
            return pipeline;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnFrame()
    {
        WebGpuBrowserRuntime? runtime = s_active;
        if (runtime == null || runtime._state == SetupState.Failed)
        {
            return;
        }

        try
        {
            if (runtime._state == SetupState.Ready)
            {
                runtime.RenderFrame();
            }
        }
        catch (Exception exception)
        {
            runtime.Fail("frame", exception);
        }
    }

    private void RenderFrame()
    {
        UpdateFrameTiming();
        _frameNumber++;
        if ((_frameNumber % 30) == 0)
        {
            ResizeSwapChain(force: false);
        }

        _frameSource.Update(1f / 60f, _canvasWidth, _canvasHeight);
        UploadFrameData();

        WgpuTextureView* textureView = WebGpuNative.SwapChainGetCurrentTextureView(_swapChain);
        Require(textureView != null, "wgpuSwapChainGetCurrentTextureView returned null.");

        WgpuCommandEncoderDescriptor encoderDescriptor = default;
        WgpuCommandEncoder* encoder = WebGpuNative.DeviceCreateCommandEncoder(_device, &encoderDescriptor);
        Require(encoder != null, "wgpuDeviceCreateCommandEncoder returned null.");

        WgpuRenderPassColorAttachment colorAttachment = new()
        {
            View = textureView,
            DepthSlice = UndefinedDepthSlice,
            LoadOp = WgpuLoadOp.Clear,
            StoreOp = WgpuStoreOp.Store,
            ClearValue = new WgpuColor
            {
                Red = 0.023,
                Green = 0.034,
                Blue = 0.043,
                Alpha = 1.0,
            },
        };
        WgpuRenderPassDepthStencilAttachment depthAttachment = new()
        {
            View = _depthTextureView,
            DepthLoadOp = WgpuLoadOp.Clear,
            DepthStoreOp = WgpuStoreOp.Store,
            DepthClearValue = 1f,
            DepthReadOnly = 0,
            StencilLoadOp = WgpuLoadOp.Undefined,
            StencilStoreOp = WgpuStoreOp.Undefined,
            StencilReadOnly = 1,
        };
        WgpuRenderPassDescriptor passDescriptor = new()
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttachment,
            DepthStencilAttachment = &depthAttachment,
        };

        WgpuRenderPassEncoder* pass = WebGpuNative.CommandEncoderBeginRenderPass(encoder, &passDescriptor);
        Require(pass != null, "wgpuCommandEncoderBeginRenderPass returned null.");
        WebGpuNative.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
        DrawTerrain(pass);
        DrawWorld(pass);
        DrawGroundOverlay(pass);
        DrawWorldBars(pass);
        DrawWorldGlyphs(pass);
        DrawScreen(pass, _frameSource.TopMostScreenInstances, _topMostScreenInstanceBuffer);
        DrawText(pass, _frameSource.TopMostTextGlyphInstances, _topMostTextInstanceBuffer);
        DrawScreen(pass, _performanceOverlay.ScreenInstances, _performanceScreenInstanceBuffer);
        DrawText(pass, _performanceOverlay.TextInstances, _performanceTextInstanceBuffer);
        WebGpuNative.RenderPassEncoderEnd(pass);

        WgpuCommandBufferDescriptor commandBufferDescriptor = default;
        WgpuCommandBuffer* commandBuffer = WebGpuNative.CommandEncoderFinish(encoder, &commandBufferDescriptor);
        Require(commandBuffer != null, "wgpuCommandEncoderFinish returned null.");
        WebGpuNative.QueueSubmit(_queue, 1, &commandBuffer);

        WebGpuNative.CommandBufferRelease(commandBuffer);
        WebGpuNative.RenderPassEncoderRelease(pass);
        WebGpuNative.CommandEncoderRelease(encoder);
        WebGpuNative.TextureViewRelease(textureView);

        if (_frameNumber == 1)
        {
            int worldInstances = 0;
            for (int i = 0; i < _frameSource.WorldBatchCount; i++)
            {
                worldInstances = checked(worldInstances + _frameSource.GetWorldBatch(i).InstanceCount);
            }

            Console.WriteLine(
                $"[Ludots WebGPU] FIRST_FORMAL_FRAME_SUBMITTED terrainChunks={_frameSource.TerrainChunkCount} worldBatches={_frameSource.WorldBatchCount} worldInstances={worldInstances} groundIndices={_frameSource.GroundOverlayMesh.Indices.Length} worldHudBars={_frameSource.WorldHudBarInstances.Length} worldHudGlyphs={_frameSource.WorldHudGlyphInstances.Length} anchors={_frameSource.WorldHudAnchors.Length} topMost={_frameSource.TopMostScreenInstances.Length}/{_frameSource.TopMostTextGlyphInstances.Length}.");
        }
    }

    private void UpdateFrameTiming()
    {
        long timestamp = Stopwatch.GetTimestamp();
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
        if (_lastFrameTimestamp != 0)
        {
            double frameSeconds = (timestamp - _lastFrameTimestamp) / (double)Stopwatch.Frequency;
            long frameAllocatedBytes = allocatedBytes - _lastAllocatedBytes;
            if (_frameTiming.TryObserve(frameSeconds, frameAllocatedBytes, out FrameTimingSnapshot timing))
            {
                WebGpuFrameDiagnostics diagnostics = _frameSource.Diagnostics;
                _performanceOverlay.Update(in timing, in diagnostics);
            }
        }

        _lastFrameTimestamp = timestamp;
        _lastAllocatedBytes = allocatedBytes;
    }

    private void UploadFrameData()
    {
        WebGpuCameraFrame camera = _frameSource.Camera;
        Upload(_uniformBuffer, new ReadOnlySpan<WebGpuCameraFrame>(in camera));
        UploadWorldBatches();
        UploadGroundOverlay();

        WebGpuHudUploadHint worldHudHint = _frameSource.WorldHudUploadHint;
        long barUploadStart = Stopwatch.GetTimestamp();
        int barUploadBytes = UploadInstanceRange(
            _frameSource.WorldHudBarInstances,
            worldHudHint.BarUploadStart,
            worldHudHint.BarUploadCount,
            worldHudHint.BarFullUpload,
            ref _underUiWorldBarInstanceBuffer,
            ref _underUiWorldBarInstanceCapacity);
        double barUploadMilliseconds =
            (Stopwatch.GetTimestamp() - barUploadStart) * 1000d / Stopwatch.Frequency;

        long anchorUploadStart = Stopwatch.GetTimestamp();
        ulong anchorCapacityBefore = _underUiWorldAnchorCapacity;
        int anchorUploadBytes = UploadInstanceRange(
            _frameSource.WorldHudAnchors,
            worldHudHint.AnchorUploadStart,
            worldHudHint.AnchorUploadCount,
            worldHudHint.AnchorFullUpload,
            ref _underUiWorldAnchorBuffer,
            ref _underUiWorldAnchorCapacity,
            WgpuBufferUsage.Storage | WgpuBufferUsage.CopyDestination);
        if (_underUiWorldAnchorCapacity != anchorCapacityBefore)
        {
            RecreateWorldHudBindGroups();
        }

        double anchorUploadMilliseconds =
            (Stopwatch.GetTimestamp() - anchorUploadStart) * 1000d / Stopwatch.Frequency;

        UploadInstances(
            _frameSource.TopMostScreenInstances,
            ref _topMostScreenInstanceBuffer,
            ref _topMostScreenInstanceCapacity);

        long glyphUploadStart = Stopwatch.GetTimestamp();
        int glyphUploadBytes = UploadInstanceRange(
            _frameSource.WorldHudGlyphInstances,
            worldHudHint.GlyphUploadStart,
            worldHudHint.GlyphUploadCount,
            worldHudHint.GlyphFullUpload,
            ref _underUiWorldGlyphInstanceBuffer,
            ref _underUiWorldGlyphInstanceCapacity);
        double glyphUploadMilliseconds =
            (Stopwatch.GetTimestamp() - glyphUploadStart) * 1000d / Stopwatch.Frequency;

        UploadInstances(
            _frameSource.TopMostTextGlyphInstances,
            ref _topMostTextInstanceBuffer,
            ref _topMostTextInstanceCapacity);
        UploadInstances(
            _performanceOverlay.ScreenInstances,
            ref _performanceScreenInstanceBuffer,
            ref _performanceScreenInstanceCapacity);
        UploadInstances(
            _performanceOverlay.TextInstances,
            ref _performanceTextInstanceBuffer,
            ref _performanceTextInstanceCapacity);

        _frameSource.ReportWorldHudUploadTiming(
            barUploadBytes,
            barUploadMilliseconds,
            anchorUploadBytes,
            anchorUploadMilliseconds,
            glyphUploadBytes,
            glyphUploadMilliseconds);
    }

    private void UploadWorldBatches()
    {
        for (int i = 0; i < _frameSource.WorldBatchCount; i++)
        {
            WebGpuWorldBatchFrame frame = _frameSource.GetWorldBatch(i);
            if (frame.Mesh.IsEmpty)
            {
                throw new InvalidOperationException($"WebGPU world batch {frame.BatchKey} references an empty mesh.");
            }

            if (!_worldMeshes.TryGetValue(frame.Mesh.Key, out GpuMesh? mesh) || mesh.Revision != frame.Mesh.Revision)
            {
                mesh?.Release();
                mesh = CreateGpuMesh(frame.Mesh);
                _worldMeshes[frame.Mesh.Key] = mesh;
            }

            if (!_worldBatches.TryGetValue(frame.BatchKey, out GpuWorldBatch? batch))
            {
                batch = new GpuWorldBatch();
                _worldBatches.Add(frame.BatchKey, batch);
            }

            ReadOnlySpan<WebGpuWorldInstance> instances = frame.Instances;
            EnsureDynamicBuffer(
                ref batch.InstanceBuffer,
                ref batch.InstanceCapacity,
                ByteSize(instances),
                WgpuBufferUsage.Vertex | WgpuBufferUsage.CopyDestination);
            Upload(batch.InstanceBuffer, instances);
            batch.MeshKey = frame.Mesh.Key;
            batch.InstanceCount = checked((uint)instances.Length);
        }
    }

    private void UploadGroundOverlay()
    {
        WebGpuMeshFrame frame = _frameSource.GroundOverlayMesh;
        if (frame.IsEmpty)
        {
            _groundOverlayIndexCount = 0;
            return;
        }

        ReadOnlySpan<WebGpuWorldVertex> vertices = frame.Vertices.Span;
        ReadOnlySpan<uint> indices = frame.Indices.Span;
        EnsureDynamicBuffer(
            ref _groundOverlayVertexBuffer,
            ref _groundOverlayVertexCapacity,
            ByteSize(vertices),
            WgpuBufferUsage.Vertex | WgpuBufferUsage.CopyDestination);
        EnsureDynamicBuffer(
            ref _groundOverlayIndexBuffer,
            ref _groundOverlayIndexCapacity,
            ByteSize(indices),
            WgpuBufferUsage.Index | WgpuBufferUsage.CopyDestination);
        Upload(_groundOverlayVertexBuffer, vertices);
        Upload(_groundOverlayIndexBuffer, indices);
        _groundOverlayIndexCount = checked((uint)indices.Length);
    }

    private void UploadInstances<T>(ReadOnlySpan<T> instances, ref WgpuBuffer* buffer, ref ulong capacity)
        where T : unmanaged
    {
        EnsureDynamicBuffer(
            ref buffer,
            ref capacity,
            ByteSize(instances),
            WgpuBufferUsage.Vertex | WgpuBufferUsage.CopyDestination);
        if (!instances.IsEmpty)
        {
            Upload(buffer, instances);
        }
    }

    private int UploadInstanceRange<T>(
        ReadOnlySpan<T> instances,
        int uploadStart,
        int uploadCount,
        bool fullUpload,
        ref WgpuBuffer* buffer,
        ref ulong capacity)
        where T : unmanaged =>
        UploadInstanceRange(
            instances,
            uploadStart,
            uploadCount,
            fullUpload,
            ref buffer,
            ref capacity,
            WgpuBufferUsage.Vertex | WgpuBufferUsage.CopyDestination);

    private int UploadInstanceRange<T>(
        ReadOnlySpan<T> instances,
        int uploadStart,
        int uploadCount,
        bool fullUpload,
        ref WgpuBuffer* buffer,
        ref ulong capacity,
        WgpuBufferUsage usage)
        where T : unmanaged
    {
        ulong requiredBytes = ByteSize(instances);
        bool grew = EnsureDynamicBufferGrew(
            ref buffer,
            ref capacity,
            requiredBytes,
            usage);
        if (instances.IsEmpty)
        {
            return 0;
        }

        if (grew || fullUpload)
        {
            Upload(buffer, instances);
            return checked(instances.Length * sizeof(T));
        }

        if (uploadCount == 0)
        {
            return 0;
        }

        if (uploadStart < 0 ||
            uploadCount < 0 ||
            (uint)uploadStart >= (uint)instances.Length ||
            uploadStart > instances.Length - uploadCount)
        {
            throw new InvalidOperationException(
                $"WebGPU under-UI upload range [{uploadStart},{uploadCount}) is outside instance count {instances.Length}.");
        }

        Upload(
            buffer,
            instances.Slice(uploadStart, uploadCount),
            WorldHudUploadMath.DestinationByteOffset(uploadStart, sizeof(T)));
        return checked(uploadCount * sizeof(T));
    }

    private void EnsureDynamicBuffer(
        ref WgpuBuffer* buffer,
        ref ulong capacity,
        ulong required,
        WgpuBufferUsage usage)
    {
        _ = EnsureDynamicBufferGrew(ref buffer, ref capacity, required, usage);
    }

    private bool EnsureDynamicBufferGrew(
        ref WgpuBuffer* buffer,
        ref ulong capacity,
        ulong required,
        WgpuBufferUsage usage)
    {
        if (required == 0 || capacity >= required)
        {
            return false;
        }

        ulong newCapacity = 4096;
        while (newCapacity < required)
        {
            newCapacity = checked(newCapacity * 2);
        }

        if (buffer != null)
        {
            WebGpuNative.BufferRelease(buffer);
        }

        buffer = CreateBuffer(newCapacity, usage);
        capacity = newCapacity;
        return true;
    }

    private void DrawTerrain(WgpuRenderPassEncoder* pass)
    {
        int count = _frameSource.TerrainChunkCount;
        if (count <= 0)
        {
            return;
        }

        WebGpuNative.RenderPassEncoderSetPipeline(pass, _terrainPipeline);
        WebGpuNative.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
        for (int i = 0; i < count; i++)
        {
            WebGpuMeshFrame frame = _frameSource.GetTerrainChunk(i);
            if (!_terrainMeshes.TryGetValue(frame.Key, out GpuMesh? mesh) || mesh.Revision != frame.Revision)
            {
                mesh?.Release();
                mesh = CreateGpuMesh(frame);
                _terrainMeshes[frame.Key] = mesh;
            }

            WebGpuNative.RenderPassEncoderSetVertexBuffer(pass, 0, mesh.VertexBuffer, 0, mesh.VertexBytes);
            WebGpuNative.RenderPassEncoderSetIndexBuffer(pass, mesh.IndexBuffer, WgpuIndexFormat.Uint32, 0, mesh.IndexBytes);
            WebGpuNative.RenderPassEncoderDrawIndexed(pass, mesh.IndexCount, 1, 0, 0, 0);
        }
    }

    private void DrawWorld(WgpuRenderPassEncoder* pass)
    {
        int batchCount = _frameSource.WorldBatchCount;
        if (batchCount <= 0)
        {
            return;
        }

        WebGpuNative.RenderPassEncoderSetPipeline(pass, _unitPipeline);
        WebGpuNative.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
        for (int i = 0; i < batchCount; i++)
        {
            WebGpuWorldBatchFrame frame = _frameSource.GetWorldBatch(i);
            if (!_worldBatches.TryGetValue(frame.BatchKey, out GpuWorldBatch? batch) ||
                !_worldMeshes.TryGetValue(batch.MeshKey, out GpuMesh? mesh) ||
                batch.InstanceBuffer == null ||
                batch.InstanceCount == 0)
            {
                throw new InvalidOperationException($"WebGPU world batch {frame.BatchKey} was not uploaded before draw.");
            }

            WebGpuNative.RenderPassEncoderSetVertexBuffer(pass, 0, mesh.VertexBuffer, 0, mesh.VertexBytes);
            WebGpuNative.RenderPassEncoderSetVertexBuffer(
                pass,
                1,
                batch.InstanceBuffer,
                0,
                checked((ulong)batch.InstanceCount * (ulong)sizeof(WebGpuWorldInstance)));
            WebGpuNative.RenderPassEncoderSetIndexBuffer(pass, mesh.IndexBuffer, WgpuIndexFormat.Uint32, 0, mesh.IndexBytes);
            WebGpuNative.RenderPassEncoderDrawIndexed(pass, mesh.IndexCount, batch.InstanceCount, 0, 0, 0);
        }
    }

    private void DrawGroundOverlay(WgpuRenderPassEncoder* pass)
    {
        if (_groundOverlayIndexCount == 0 || _groundOverlayVertexBuffer == null || _groundOverlayIndexBuffer == null)
        {
            return;
        }

        WebGpuNative.RenderPassEncoderSetPipeline(pass, _groundOverlayPipeline);
        WebGpuNative.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
        WebGpuNative.RenderPassEncoderSetVertexBuffer(
            pass,
            0,
            _groundOverlayVertexBuffer,
            0,
            _groundOverlayVertexCapacity);
        WebGpuNative.RenderPassEncoderSetIndexBuffer(
            pass,
            _groundOverlayIndexBuffer,
            WgpuIndexFormat.Uint32,
            0,
            _groundOverlayIndexCapacity);
        WebGpuNative.RenderPassEncoderDrawIndexed(pass, _groundOverlayIndexCount, 1, 0, 0, 0);
    }

    private void DrawWorldBars(WgpuRenderPassEncoder* pass)
    {
        ReadOnlySpan<WebGpuWorldBarInstance> instances = _frameSource.WorldHudBarInstances;
        int instanceCount = instances.Length;
        if (instanceCount <= 0)
        {
            return;
        }

        if (_worldBarPipeline == null || _worldBarBindGroup == null || _underUiWorldBarInstanceBuffer == null)
        {
            throw new InvalidOperationException(
                "World HUD bar pipeline/bind group/instance buffer is missing before draw.");
        }

        WebGpuNative.RenderPassEncoderSetPipeline(pass, _worldBarPipeline);
        WebGpuNative.RenderPassEncoderSetBindGroup(pass, 0, _worldBarBindGroup, 0, null);
        WebGpuNative.RenderPassEncoderSetVertexBuffer(
            pass,
            0,
            _underUiWorldBarInstanceBuffer,
            0,
            checked((ulong)instanceCount * (ulong)sizeof(WebGpuWorldBarInstance)));
        WebGpuNative.RenderPassEncoderDraw(pass, 6, checked((uint)instanceCount), 0, 0);
    }

    private void DrawWorldGlyphs(WgpuRenderPassEncoder* pass)
    {
        ReadOnlySpan<WebGpuWorldGlyphInstance> instances = _frameSource.WorldHudGlyphInstances;
        int instanceCount = instances.Length;
        if (instanceCount <= 0)
        {
            return;
        }

        if (_worldGlyphPipeline == null || _worldGlyphBindGroup == null || _underUiWorldGlyphInstanceBuffer == null)
        {
            throw new InvalidOperationException(
                "World HUD glyph pipeline/bind group/instance buffer is missing before draw.");
        }

        WebGpuNative.RenderPassEncoderSetPipeline(pass, _worldGlyphPipeline);
        WebGpuNative.RenderPassEncoderSetBindGroup(pass, 0, _worldGlyphBindGroup, 0, null);
        WebGpuNative.RenderPassEncoderSetVertexBuffer(
            pass,
            0,
            _underUiWorldGlyphInstanceBuffer,
            0,
            checked((ulong)instanceCount * (ulong)sizeof(WebGpuWorldGlyphInstance)));
        WebGpuNative.RenderPassEncoderDraw(pass, 6, checked((uint)instanceCount), 0, 0);
    }

    private void DrawScreen(
        WgpuRenderPassEncoder* pass,
        ReadOnlySpan<WebGpuScreenInstance> instances,
        WgpuBuffer* instanceBuffer)
    {
        int instanceCount = instances.Length;
        if (instanceCount <= 0 || instanceBuffer == null)
        {
            return;
        }

        WebGpuNative.RenderPassEncoderSetPipeline(pass, _screenPipeline);
        WebGpuNative.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
        WebGpuNative.RenderPassEncoderSetVertexBuffer(
            pass,
            0,
            instanceBuffer,
            0,
            checked((ulong)instanceCount * (ulong)sizeof(WebGpuScreenInstance)));
        WebGpuNative.RenderPassEncoderDraw(pass, 6, checked((uint)instanceCount), 0, 0);
    }

    private void DrawText(
        WgpuRenderPassEncoder* pass,
        ReadOnlySpan<WebGpuGlyphInstance> instances,
        WgpuBuffer* instanceBuffer)
    {
        int instanceCount = instances.Length;
        if (instanceCount <= 0 || instanceBuffer == null)
        {
            return;
        }

        WebGpuNative.RenderPassEncoderSetPipeline(pass, _textPipeline);
        WebGpuNative.RenderPassEncoderSetBindGroup(pass, 0, _textBindGroup, 0, null);
        WebGpuNative.RenderPassEncoderSetVertexBuffer(
            pass,
            0,
            instanceBuffer,
            0,
            checked((ulong)instanceCount * (ulong)sizeof(WebGpuGlyphInstance)));
        WebGpuNative.RenderPassEncoderDraw(pass, 6, checked((uint)instanceCount), 0, 0);
    }

    private GpuMesh CreateGpuMesh(WebGpuMeshFrame frame)
    {
        if (frame.IsEmpty)
        {
            throw new InvalidOperationException($"WebGPU mesh {frame.Key} is empty.");
        }

        ReadOnlySpan<WebGpuWorldVertex> vertices = frame.Vertices.Span;
        ReadOnlySpan<uint> indices = frame.Indices.Span;
        ulong vertexBytes = ByteSize(vertices);
        ulong indexBytes = ByteSize(indices);
        WgpuBuffer* vertexBuffer = CreateBuffer(
            vertexBytes,
            WgpuBufferUsage.Vertex | WgpuBufferUsage.CopyDestination);
        WgpuBuffer* indexBuffer = CreateBuffer(
            indexBytes,
            WgpuBufferUsage.Index | WgpuBufferUsage.CopyDestination);
        Upload(vertexBuffer, vertices);
        Upload(indexBuffer, indices);
        return new GpuMesh(
            frame.Key,
            frame.Revision,
            vertexBuffer,
            indexBuffer,
            vertexBytes,
            indexBytes,
            checked((uint)indices.Length));
    }

    private WgpuBuffer* CreateBuffer(ulong size, WgpuBufferUsage usage)
    {
        WgpuBufferDescriptor descriptor = new()
        {
            Usage = usage,
            Size = Math.Max(4ul, size),
            MappedAtCreation = 0,
        };
        WgpuBuffer* buffer = WebGpuNative.DeviceCreateBuffer(_device, &descriptor);
        Require(buffer != null, $"wgpuDeviceCreateBuffer returned null for {descriptor.Size} bytes.");
        return buffer;
    }

    private void Upload<T>(WgpuBuffer* buffer, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        Upload(buffer, data, bufferOffset: 0);
    }

    private void Upload<T>(WgpuBuffer* buffer, ReadOnlySpan<T> data, ulong bufferOffset)
        where T : unmanaged
    {
        if (data.IsEmpty)
        {
            return;
        }

        fixed (T* pointer = data)
        {
            WebGpuNative.QueueWriteBuffer(
                _queue,
                buffer,
                bufferOffset,
                pointer,
                checked((nuint)ByteSize(data)));
        }
    }

    private static ulong ByteSize<T>(ReadOnlySpan<T> data)
        where T : unmanaged => checked((ulong)data.Length * (ulong)sizeof(T));

    private void ResizeSwapChain(bool force)
    {
        double cssWidth;
        double cssHeight;
        int sizeResult = EmscriptenNative.GetElementCssSize((byte*)_selectorUtf8, &cssWidth, &cssHeight);
        Require(sizeResult == 0, $"emscripten_get_element_css_size failed with code {sizeResult}.");

        uint width = checked((uint)Math.Max(1, Math.Round(cssWidth)));
        uint height = checked((uint)Math.Max(1, Math.Round(cssHeight)));
        if (!force && width == _canvasWidth && height == _canvasHeight)
        {
            return;
        }

        int canvasResult = EmscriptenNative.SetCanvasElementSize(
            (byte*)_selectorUtf8,
            checked((int)width),
            checked((int)height));
        Require(canvasResult == 0, $"emscripten_set_canvas_element_size failed with code {canvasResult}.");

        if (_depthTextureView != null)
        {
            WebGpuNative.TextureViewRelease(_depthTextureView);
            _depthTextureView = null;
        }

        if (_depthTexture != null)
        {
            WebGpuNative.TextureRelease(_depthTexture);
            _depthTexture = null;
        }

        if (_swapChain != null)
        {
            WebGpuNative.SwapChainRelease(_swapChain);
        }

        WgpuSwapChainDescriptor descriptor = new()
        {
            Usage = WgpuTextureUsage.RenderAttachment,
            Format = _surfaceFormat,
            Width = width,
            Height = height,
            PresentMode = WgpuPresentMode.Fifo,
        };
        _swapChain = WebGpuNative.DeviceCreateSwapChain(_device, _surface, &descriptor);
        Require(_swapChain != null, "wgpuDeviceCreateSwapChain returned null.");

        WgpuTextureDescriptor depthDescriptor = new()
        {
            Usage = WgpuTextureUsage.RenderAttachment,
            Dimension = WgpuTextureDimension.TwoDimensional,
            Size = new WgpuExtent3D
            {
                Width = width,
                Height = height,
                DepthOrArrayLayers = 1,
            },
            Format = WgpuTextureFormat.Depth24Plus,
            MipLevelCount = 1,
            SampleCount = 1,
        };
        _depthTexture = WebGpuNative.DeviceCreateTexture(_device, &depthDescriptor);
        Require(_depthTexture != null, "wgpuDeviceCreateTexture returned null for the depth buffer.");
        _depthTextureView = WebGpuNative.TextureCreateView(_depthTexture, null);
        Require(_depthTextureView != null, "wgpuTextureCreateView returned null for the depth buffer.");

        _canvasWidth = width;
        _canvasHeight = height;
    }

    private void Fail<TStatus>(string stage, TStatus status, byte* message)
        where TStatus : struct, Enum
    {
        string detail = message == null
            ? "No browser diagnostic was provided."
            : Marshal.PtrToStringUTF8((nint)message) ?? "Empty browser diagnostic.";
        Fail(stage, new InvalidOperationException($"{status}: {detail}"));
    }

    private void Fail(string stage, Exception exception)
    {
        _state = SetupState.Failed;
        Console.Error.WriteLine($"[Ludots WebGPU] FATAL at {stage}: {exception}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private enum SetupState
    {
        None,
        SurfaceReady,
        RequestingAdapter,
        RequestingDevice,
        Ready,
        Failed,
    }

    private sealed class GpuWorldBatch
    {
        public int MeshKey;
        public WgpuBuffer* InstanceBuffer;
        public ulong InstanceCapacity;
        public uint InstanceCount;
    }

    private sealed class GpuMesh
    {
        public GpuMesh(
            int key,
            int revision,
            WgpuBuffer* vertexBuffer,
            WgpuBuffer* indexBuffer,
            ulong vertexBytes,
            ulong indexBytes,
            uint indexCount)
        {
            Key = key;
            Revision = revision;
            VertexBuffer = vertexBuffer;
            IndexBuffer = indexBuffer;
            VertexBytes = vertexBytes;
            IndexBytes = indexBytes;
            IndexCount = indexCount;
        }

        public int Key { get; }

        public int Revision { get; }

        public WgpuBuffer* VertexBuffer { get; }

        public WgpuBuffer* IndexBuffer { get; }

        public ulong VertexBytes { get; }

        public ulong IndexBytes { get; }

        public uint IndexCount { get; }

        public void Release()
        {
            WebGpuNative.BufferRelease(VertexBuffer);
            WebGpuNative.BufferRelease(IndexBuffer);
        }
    }
}

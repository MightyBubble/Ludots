using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Ludots.Raylib.Render;

/// <summary>
/// raylib 官方二进制固定 GL 3.3 上下文；SSBO/compute 经 ARB 扩展在同上下文使用。
/// 本类是渲染线程的 GL 直连装载点（先于一切 GL 调用完成 fail-closed 能力探针），
/// GL 对象生命周期记账走 RaylibNativeResourceLedger（由 SSBO/Pipeline 持有者负责）。
/// x64 下 Winapi 与 Cdecl 同布局，非 Windows 平台运行时把 Winapi 映射为 Cdecl。
/// </summary>
internal static unsafe class Gl43
{
    public const int GL_FALSE = 0;
    public const int GL_TRUE = 1;
    public const uint GL_ARRAY_BUFFER = 0x8892;
    public const uint GL_FLOAT = 0x1406;
    public const int GL_CURRENT_PROGRAM = 0x8B8D;
    public const int GL_VERTEX_ARRAY_BINDING = 0x85B5;
    public const int GL_ARRAY_BUFFER_BINDING = 0x8B8C;
    public const int GL_ELEMENT_ARRAY_BUFFER_BINDING = 0x8895;
    public const int GL_ACTIVE_TEXTURE = 0x84E0;
    public const uint GL_ELEMENT_ARRAY_BUFFER = 0x8893;
    public const uint GL_SHADER_STORAGE_BUFFER = 0x90D2;
    public const uint GL_STATIC_DRAW = 0x88E4;
    public const uint GL_DYNAMIC_DRAW = 0x88E8;
    public const uint GL_STREAM_DRAW = 0x88E0;
    public const uint GL_STREAM_READ = 0x88E1;
    public const uint GL_STREAM_COPY = 0x88E2;
    public const uint GL_MAP_READ_BIT = 0x0001;
    public const uint GL_COMPUTE_SHADER = 0x91B9;
    public const uint GL_VERTEX_SHADER = 0x8B31;
    public const uint GL_FRAGMENT_SHADER = 0x8B30;
    public const int GL_COMPILE_STATUS = 0x8B81;
    public const int GL_LINK_STATUS = 0x8B82;
    public const int GL_INFO_LOG_LENGTH = 0x8B84;
    public const uint GL_SHADER_STORAGE_BARRIER_BIT = 0x0200;
    public const uint GL_ALL_BARRIER_BITS = 0xFFFFFFFF;
    public const uint GL_TEXTURE0 = 0x84C0;
    public const uint GL_UNSIGNED_SHORT = 0x1403;
    public const uint GL_UNSIGNED_INT = 0x1405;
    public const uint GL_TRIANGLES = 0x0004;
    public const int GL_NUM_EXTENSIONS = 0x821D;
    public const uint GL_EXTENSIONS = 0x1F03;
    public const uint GL_VERSION = 0x1F00;
    public const uint GL_RENDERER = 0x1F01;
    public const uint GL_QUERY_RESULT = 0x8866;
    public const uint GL_QUERY_RESULT_AVAILABLE = 0x8865;
    public const uint GL_TIMESTAMP = 0x8E28;
    public const uint GL_QUERY_RESULT_NO_WAIT = 0x919D;
    public const uint GL_DRAW_INDIRECT_BUFFER = 0x8F3F;
    public const uint GL_COMMAND_BARRIER_BIT = 0x0040;
    public const uint GL_COPY_READ_BUFFER = 0x8F36;
    public const uint GL_COPY_WRITE_BUFFER = 0x8F37;
    public const uint GL_ARRAY_BUFFER_ATTRIB = 0x8892;
    public const uint GL_TEXTURE_2D = 0x0DE1;
    public const uint GL_TEXTURE_CUBE_MAP = 0x8513;

    public const string ExtShaderStorage = "GL_ARB_shader_storage_buffer_object";
    public const string ExtComputeShader = "GL_ARB_compute_shader";
    public const string ExtMultiDrawIndirect = "GL_ARB_multi_draw_indirect";

    public static readonly string[] RequiredExtensions = { ExtShaderStorage, ExtComputeShader };

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GlGetStringDelegate(uint name);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WglGetProcAddressDelegate(string name);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void GetIntegervDelegate(int pname, int* data);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr GetStringiDelegate(uint name, uint index);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate uint GetErrorDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void GenBuffersDelegate(int n, uint* buffers);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void DeleteBuffersDelegate(int n, uint* buffers);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void BindBufferDelegate(uint target, uint buffer);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void BindBufferBaseDelegate(uint target, uint index, uint buffer);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void BufferDataDelegate(uint target, nint size, IntPtr data, uint usage);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void BufferSubDataDelegate(uint target, nint offset, nint size, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void CopyBufferSubDataDelegate(uint readTarget, uint writeTarget, nint readOffset, nint writeOffset, nint size);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void GetBufferSubDataDelegate(uint target, nint offset, nint size, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr MapBufferRangeDelegate(uint target, nint offset, nint length, uint access);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void UnmapBufferDelegate(uint target);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate uint CreateShaderDelegate(uint type);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void ShaderSourceDelegate(uint shader, int count, IntPtr* @string, int* length);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void CompileShaderDelegate(uint shader);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void GetShaderivDelegate(uint shader, int pname, int* @params);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void GetShaderInfoLogDelegate(uint shader, int maxLength, int* length, IntPtr infoLog);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate uint CreateProgramDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void AttachShaderDelegate(uint program, uint shader);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void LinkProgramDelegate(uint program);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void GetProgramivDelegate(uint program, int pname, int* @params);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void GetProgramInfoLogDelegate(uint program, int maxLength, int* length, IntPtr infoLog);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void DeleteShaderDelegate(uint shader);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void DeleteProgramDelegate(uint program);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void UseProgramDelegate(uint program);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate int GetUniformLocationDelegate(uint program, string name);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void Uniform1fDelegate(int location, float value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void Uniform1iDelegate(int location, int value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void Uniform3fDelegate(int location, float x, float y, float z);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void UniformMatrix4fvDelegate(int location, int count, bool transpose, float* value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void DispatchComputeDelegate(uint groupsX, uint groupsY, uint groupsZ);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void MemoryBarrierDelegate(uint barriers);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void BindVertexArrayDelegate(uint array);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void DrawElementsInstancedDelegate(uint mode, int count, uint type, IntPtr indices, int instanceCount);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void DrawArraysInstancedDelegate(uint mode, int first, int count, int instanceCount);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void MultiDrawElementsIndirectDelegate(uint mode, uint type, IntPtr indirect, int drawCount, int stride);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void ActiveTextureDelegate(uint texture);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void BindTextureDelegate(uint target, uint texture);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void GenQueriesDelegate(int n, uint* ids);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void DeleteQueriesDelegate(int n, uint* ids);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void QueryCounterDelegate(uint id, uint target);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void GetQueryObjectui64vDelegate(uint id, uint pname, ulong* @params);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void ReadPixelsDelegate(int x, int y, int width, int height, uint format, uint type, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void ViewportDelegate(int x, int y, int width, int height);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void VertexAttribPointerDelegate(uint index, int size, uint type, bool normalized, int stride, IntPtr pointer);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void EnableVertexAttribArrayDelegate(uint index);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void VertexAttribDivisorDelegate(uint index, uint divisor);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void GetUniformfvDelegate(uint program, int location, float* parameters);

    public static GetIntegervDelegate GetIntegerv = null!;
    public static GetStringiDelegate GetStringi = null!;
    public static GetErrorDelegate GetError = null!;
    public static GenBuffersDelegate GenBuffers = null!;
    public static DeleteBuffersDelegate DeleteBuffers = null!;
    public static BindBufferDelegate BindBuffer = null!;
    public static BindBufferBaseDelegate BindBufferBase = null!;
    public static BufferDataDelegate BufferData = null!;
    public static BufferSubDataDelegate BufferSubData = null!;
    public static CopyBufferSubDataDelegate CopyBufferSubData = null!;
    public static GetBufferSubDataDelegate GetBufferSubData = null!;
    public static MapBufferRangeDelegate MapBufferRange = null!;
    public static UnmapBufferDelegate UnmapBuffer = null!;
    public static CreateShaderDelegate CreateShader = null!;
    public static ShaderSourceDelegate ShaderSource = null!;
    public static CompileShaderDelegate CompileShader = null!;
    public static GetShaderivDelegate GetShaderiv = null!;
    public static GetShaderInfoLogDelegate GetShaderInfoLog = null!;
    public static CreateProgramDelegate CreateProgram = null!;
    public static AttachShaderDelegate AttachShader = null!;
    public static LinkProgramDelegate LinkProgram = null!;
    public static GetProgramivDelegate GetProgramiv = null!;
    public static GetProgramInfoLogDelegate GetProgramInfoLog = null!;
    public static DeleteShaderDelegate DeleteShader = null!;
    public static DeleteProgramDelegate DeleteProgram = null!;
    public static UseProgramDelegate UseProgram = null!;
    public static GetUniformLocationDelegate GetUniformLocation = null!;
    public static Uniform1fDelegate Uniform1f = null!;
    public static Uniform1iDelegate Uniform1i = null!;
    public static Uniform3fDelegate Uniform3f = null!;
    public static UniformMatrix4fvDelegate UniformMatrix4fv = null!;
    public static DispatchComputeDelegate DispatchCompute = null!;
    public static MemoryBarrierDelegate MemoryBarrier = null!;
    public static BindVertexArrayDelegate BindVertexArray = null!;
    public static DrawElementsInstancedDelegate DrawElementsInstanced = null!;
    public static DrawArraysInstancedDelegate DrawArraysInstanced = null!;
    public static MultiDrawElementsIndirectDelegate MultiDrawElementsIndirect = null!;
    public static ActiveTextureDelegate ActiveTexture = null!;
    public static BindTextureDelegate BindTexture = null!;
    public static GenQueriesDelegate GenQueries = null!;
    public static DeleteQueriesDelegate DeleteQueries = null!;
    public static QueryCounterDelegate QueryCounter = null!;
    public static GetQueryObjectui64vDelegate GetQueryObjectui64v = null!;
    public static ReadPixelsDelegate ReadPixels = null!;
    public static ViewportDelegate Viewport = null!;
    public static VertexAttribPointerDelegate VertexAttribPointer = null!;
    public static EnableVertexAttribArrayDelegate EnableVertexAttribArray = null!;
    public static VertexAttribDivisorDelegate VertexAttribDivisor = null!;
    public static GetUniformfvDelegate GetUniformfv = null!;

    public static Gl43Capabilities? Capabilities { get; private set; }

    public static bool Initialized => Capabilities != null;

    /// <summary>必须在 GL 上下文 current 时调用；缺必需扩展即抛合同错误（fail-closed）。</summary>
    public static Gl43Capabilities Initialize()
    {
        if (Capabilities != null)
        {
            return Capabilities;
        }

        if (!TryLoadLibrary(out IntPtr lib))
        {
            throw new InvalidOperationException(
                $"GPU-skinned SSBO pipeline requires a GL library; none of opengl32/libGL.so.1/libGL.dylib could be loaded.");
        }

        WglGetProcAddressDelegate? procLookup = null;
        if (OperatingSystem.IsWindows())
        {
            if (NativeLibrary.TryGetExport(lib, "wglGetProcAddress", out IntPtr wglPtr))
            {
                procLookup = Marshal.GetDelegateForFunctionPointer<WglGetProcAddressDelegate>(wglPtr);
            }
        }

        LoadAll(name => ResolveEntry(lib, procLookup, name));

        var glGetString = Bind<GlGetStringDelegate>(lib, procLookup, "glGetString");
        string version = ReadString(glGetString, GL_VERSION) ?? "<null>";
        string renderer = ReadString(glGetString, GL_RENDERER) ?? "<null>";
        List<string> extensions = new(256);
        int extensionCount;
        GetIntegerv(GL_NUM_EXTENSIONS, &extensionCount);
        for (uint i = 0; i < extensionCount; i++)
        {
            string? extension = ReadString(GetStringi, GL_EXTENSIONS, i);
            if (extension != null)
            {
                extensions.Add(extension);
            }
        }

        Gl43Capabilities capabilities = Gl43Capabilities.Probe(version, renderer, extensions);
        if (capabilities.MissingRequired.Count > 0)
        {
            throw new InvalidOperationException(
                $"GPU-skinned SSBO pipeline requires GL ARB extensions unavailable on this context " +
                $"(renderer='{renderer}', version='{version}'): missing [{string.Join(", ", capabilities.MissingRequired)}]. " +
                "The GPU-driven skinning lane is a hard contract, not a fallback path.");
        }

        RequireGlsl430Accepted();

        Capabilities = capabilities;
        return capabilities;
    }

    /// <summary>SSBO 顶点阶段按驱动约定门控在 GLSL 4.30 源级——探针期用一个最小 430 着色器实测，
    /// 拒绝只看扩展字符串的假阳性（fail-closed）。</summary>
    private static void RequireGlsl430Accepted()
    {
        uint shader = CreateShader(GL_VERTEX_SHADER);
        byte[] source = System.Text.Encoding.UTF8.GetBytes("#version 430\nvoid main() { gl_Position = vec4(0.0); }\0");
        fixed (byte* sourcePtr = source)
        {
            IntPtr sourceIntPtr = (IntPtr)sourcePtr;
            ShaderSource(shader, 1, &sourceIntPtr, null);
        }

        CompileShader(shader);
        int compiled;
        GetShaderiv(shader, GL_COMPILE_STATUS, &compiled);
        int logLength;
        GetShaderiv(shader, GL_INFO_LOG_LENGTH, &logLength);
        string log = string.Empty;
        if (logLength > 1)
        {
            byte[] logBuffer = new byte[logLength];
            fixed (byte* logPtr = logBuffer)
            {
                GetShaderInfoLog(shader, logBuffer.Length, null, (IntPtr)logPtr);
            }

            log = System.Text.Encoding.UTF8.GetString(logBuffer).TrimEnd('\0');
        }

        DeleteShader(shader);
        if (compiled == GL_FALSE)
        {
            throw new InvalidOperationException(
                $"GPU-skinned SSBO pipeline requires the context to accept GLSL 4.30 shader sources " +
                $"(vertex-stage SSBO is gated at GLSL 4.30 on this driver); probe compile failed: {log}");
        }
    }

    private static bool TryLoadLibrary(out IntPtr lib)
    {
        return NativeLibrary.TryLoad("opengl32", out lib) ||
               NativeLibrary.TryLoad("libGL.so.1", out lib) ||
               NativeLibrary.TryLoad("libGL.dylib", out lib) ||
               NativeLibrary.TryLoad("libGL", out lib);
    }

    private static IntPtr ResolveEntry(IntPtr lib, WglGetProcAddressDelegate? procLookup, string name)
    {
        if (procLookup != null)
        {
            IntPtr address = procLookup(name);
            if (address != IntPtr.Zero)
            {
                return address;
            }
        }

        return NativeLibrary.TryGetExport(lib, name, out IntPtr direct) ? direct : IntPtr.Zero;
    }

    private static T Bind<T>(IntPtr lib, WglGetProcAddressDelegate? procLookup, string name)
        where T : class
    {
        IntPtr address = ResolveEntry(lib, procLookup, name);
        return address == IntPtr.Zero
            ? throw new InvalidOperationException($"GL entry point '{name}' is unavailable on this driver.")
            : Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static void LoadAll(Func<string, IntPtr> resolve)
    {
        // 逐项显式装载保持调用点可审计。
        GetIntegerv = Marshal.GetDelegateForFunctionPointer<GetIntegervDelegate>(Require(resolve, "glGetIntegerv"));
        GetStringi = Marshal.GetDelegateForFunctionPointer<GetStringiDelegate>(Require(resolve, "glGetStringi"));
        GetError = Marshal.GetDelegateForFunctionPointer<GetErrorDelegate>(Require(resolve, "glGetError"));
        GenBuffers = Marshal.GetDelegateForFunctionPointer<GenBuffersDelegate>(Require(resolve, "glGenBuffers"));
        DeleteBuffers = Marshal.GetDelegateForFunctionPointer<DeleteBuffersDelegate>(Require(resolve, "glDeleteBuffers"));
        BindBuffer = Marshal.GetDelegateForFunctionPointer<BindBufferDelegate>(Require(resolve, "glBindBuffer"));
        BindBufferBase = Marshal.GetDelegateForFunctionPointer<BindBufferBaseDelegate>(Require(resolve, "glBindBufferBase"));
        BufferData = Marshal.GetDelegateForFunctionPointer<BufferDataDelegate>(Require(resolve, "glBufferData"));
        BufferSubData = Marshal.GetDelegateForFunctionPointer<BufferSubDataDelegate>(Require(resolve, "glBufferSubData"));
        CopyBufferSubData = Marshal.GetDelegateForFunctionPointer<CopyBufferSubDataDelegate>(Require(resolve, "glCopyBufferSubData"));
        GetBufferSubData = Marshal.GetDelegateForFunctionPointer<GetBufferSubDataDelegate>(Require(resolve, "glGetBufferSubData"));
        MapBufferRange = Marshal.GetDelegateForFunctionPointer<MapBufferRangeDelegate>(Require(resolve, "glMapBufferRange"));
        UnmapBuffer = Marshal.GetDelegateForFunctionPointer<UnmapBufferDelegate>(Require(resolve, "glUnmapBuffer"));
        CreateShader = Marshal.GetDelegateForFunctionPointer<CreateShaderDelegate>(Require(resolve, "glCreateShader"));
        ShaderSource = Marshal.GetDelegateForFunctionPointer<ShaderSourceDelegate>(Require(resolve, "glShaderSource"));
        CompileShader = Marshal.GetDelegateForFunctionPointer<CompileShaderDelegate>(Require(resolve, "glCompileShader"));
        GetShaderiv = Marshal.GetDelegateForFunctionPointer<GetShaderivDelegate>(Require(resolve, "glGetShaderiv"));
        GetShaderInfoLog = Marshal.GetDelegateForFunctionPointer<GetShaderInfoLogDelegate>(Require(resolve, "glGetShaderInfoLog"));
        CreateProgram = Marshal.GetDelegateForFunctionPointer<CreateProgramDelegate>(Require(resolve, "glCreateProgram"));
        AttachShader = Marshal.GetDelegateForFunctionPointer<AttachShaderDelegate>(Require(resolve, "glAttachShader"));
        LinkProgram = Marshal.GetDelegateForFunctionPointer<LinkProgramDelegate>(Require(resolve, "glLinkProgram"));
        GetProgramiv = Marshal.GetDelegateForFunctionPointer<GetProgramivDelegate>(Require(resolve, "glGetProgramiv"));
        GetProgramInfoLog = Marshal.GetDelegateForFunctionPointer<GetProgramInfoLogDelegate>(Require(resolve, "glGetProgramInfoLog"));
        DeleteShader = Marshal.GetDelegateForFunctionPointer<DeleteShaderDelegate>(Require(resolve, "glDeleteShader"));
        DeleteProgram = Marshal.GetDelegateForFunctionPointer<DeleteProgramDelegate>(Require(resolve, "glDeleteProgram"));
        UseProgram = Marshal.GetDelegateForFunctionPointer<UseProgramDelegate>(Require(resolve, "glUseProgram"));
        GetUniformLocation = Marshal.GetDelegateForFunctionPointer<GetUniformLocationDelegate>(Require(resolve, "glGetUniformLocation"));
        Uniform1f = Marshal.GetDelegateForFunctionPointer<Uniform1fDelegate>(Require(resolve, "glUniform1f"));
        Uniform1i = Marshal.GetDelegateForFunctionPointer<Uniform1iDelegate>(Require(resolve, "glUniform1i"));
        Uniform3f = Marshal.GetDelegateForFunctionPointer<Uniform3fDelegate>(Require(resolve, "glUniform3f"));
        UniformMatrix4fv = Marshal.GetDelegateForFunctionPointer<UniformMatrix4fvDelegate>(Require(resolve, "glUniformMatrix4fv"));
        DispatchCompute = Marshal.GetDelegateForFunctionPointer<DispatchComputeDelegate>(Require(resolve, "glDispatchCompute"));
        MemoryBarrier = Marshal.GetDelegateForFunctionPointer<MemoryBarrierDelegate>(Require(resolve, "glMemoryBarrier"));
        BindVertexArray = Marshal.GetDelegateForFunctionPointer<BindVertexArrayDelegate>(Require(resolve, "glBindVertexArray"));
        DrawElementsInstanced = Marshal.GetDelegateForFunctionPointer<DrawElementsInstancedDelegate>(Require(resolve, "glDrawElementsInstanced"));
        // 本机 NVIDIA GL 3.3 上下文实测：glDrawElementsInstancedIndirect 不可用、glMultiDrawElementsIndirect 可用
        IntPtr mdiAddr = resolve("glMultiDrawElementsIndirect");
        if (mdiAddr == IntPtr.Zero) mdiAddr = resolve("glMultiDrawElementsIndirectARB");
        DrawArraysInstanced = Marshal.GetDelegateForFunctionPointer<DrawArraysInstancedDelegate>(Require(resolve, "glDrawArraysInstanced"));
        MultiDrawElementsIndirect = mdiAddr != IntPtr.Zero
            ? Marshal.GetDelegateForFunctionPointer<MultiDrawElementsIndirectDelegate>(mdiAddr)
            : throw new InvalidOperationException("GPU crowd indirect draw requires glMultiDrawElementsIndirect (or ARB variant).");
        ActiveTexture = Marshal.GetDelegateForFunctionPointer<ActiveTextureDelegate>(Require(resolve, "glActiveTexture"));
        BindTexture = Marshal.GetDelegateForFunctionPointer<BindTextureDelegate>(Require(resolve, "glBindTexture"));
        GenQueries = Marshal.GetDelegateForFunctionPointer<GenQueriesDelegate>(Require(resolve, "glGenQueries"));
        DeleteQueries = Marshal.GetDelegateForFunctionPointer<DeleteQueriesDelegate>(Require(resolve, "glDeleteQueries"));
        QueryCounter = Marshal.GetDelegateForFunctionPointer<QueryCounterDelegate>(Require(resolve, "glQueryCounter"));
        GetQueryObjectui64v = Marshal.GetDelegateForFunctionPointer<GetQueryObjectui64vDelegate>(Require(resolve, "glGetQueryObjectui64v"));
        ReadPixels = Marshal.GetDelegateForFunctionPointer<ReadPixelsDelegate>(Require(resolve, "glReadPixels"));
        Viewport = Marshal.GetDelegateForFunctionPointer<ViewportDelegate>(Require(resolve, "glViewport"));
        VertexAttribPointer = Marshal.GetDelegateForFunctionPointer<VertexAttribPointerDelegate>(Require(resolve, "glVertexAttribPointer"));
        EnableVertexAttribArray = Marshal.GetDelegateForFunctionPointer<EnableVertexAttribArrayDelegate>(Require(resolve, "glEnableVertexAttribArray"));
        VertexAttribDivisor = Marshal.GetDelegateForFunctionPointer<VertexAttribDivisorDelegate>(Require(resolve, "glVertexAttribDivisor"));
        GetUniformfv = Marshal.GetDelegateForFunctionPointer<GetUniformfvDelegate>(Require(resolve, "glGetUniformfv"));
    }

    private static IntPtr Require(Func<string, IntPtr> resolve, string name)
    {
        return resolve(name) is IntPtr address && address != IntPtr.Zero
            ? address
            : throw new InvalidOperationException($"GL entry point '{name}' is unavailable on this driver.");
    }

    private static string? ReadString(GlGetStringDelegate glGetString, uint name)
    {
        IntPtr ptr = glGetString(name);
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(ptr);
    }

    private static string? ReadString(GetStringiDelegate getStringi, uint name, uint index)
    {
        IntPtr ptr = getStringi(name, index);
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(ptr);
    }

    internal static void ResetForTests()
    {
        Capabilities = null;
    }
}

/// <summary>纯数据探针结果：给定扩展名集合判定 SSBO 管线可用性（无 GL 依赖，可单测）。</summary>
internal sealed record Gl43Capabilities(
    string Version,
    string Renderer,
    bool HasShaderStorage,
    bool HasComputeShader,
    bool HasMultiDrawIndirect,
    IReadOnlyList<string> MissingRequired)
{
    public static Gl43Capabilities Probe(string version, string renderer, IReadOnlyList<string> extensions)
    {
        bool Has(string extension) => extensions.Contains(extension);
        List<string> missing = new();
        foreach (string required in Gl43.RequiredExtensions)
        {
            if (!Has(required))
            {
                missing.Add(required);
            }
        }

        return new Gl43Capabilities(
            version,
            renderer,
            Has(Gl43.ExtShaderStorage),
            Has(Gl43.ExtComputeShader),
            Has(Gl43.ExtMultiDrawIndirect),
            missing);
    }
}

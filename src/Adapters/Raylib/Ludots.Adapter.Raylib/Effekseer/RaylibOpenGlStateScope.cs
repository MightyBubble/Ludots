using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Ludots.Adapter.Raylib.Effekseer
{
    internal sealed class RaylibOpenGlStateScope
    {
        // Effekseer 1.80.6 exposes exactly eight renderer texture slots.
        private const int EffekseerTextureSlotCount = 8;
        private const uint GlNoError = 0;
        private const uint GlCurrentProgram = 0x8B8D;
        private const uint GlVertexArrayBinding = 0x85B5;
        private const uint GlArrayBufferBinding = 0x8894;
        private const uint GlElementArrayBufferBinding = 0x8895;
        private const uint GlDrawFramebufferBinding = 0x8CA6;
        private const uint GlReadFramebufferBinding = 0x8CAA;
        private const uint GlActiveTexture = 0x84E0;
        private const uint GlMaxCombinedTextureImageUnits = 0x8B4D;
        private const uint GlTexture0 = 0x84C0;
        private const uint GlTexture2D = 0x0DE1;
        private const uint GlTexture3D = 0x806F;
        private const uint GlTextureCubeMap = 0x8513;
        private const uint GlTexture2DArray = 0x8C1A;
        private const uint GlTextureBinding2D = 0x8069;
        private const uint GlTextureBinding3D = 0x806A;
        private const uint GlTextureBindingCubeMap = 0x8514;
        private const uint GlTextureBinding2DArray = 0x8C1D;
        private const uint GlSamplerBinding = 0x8919;
        private const uint GlViewport = 0x0BA2;
        private const uint GlScissorBox = 0x0C10;
        private const uint GlScissorTest = 0x0C11;
        private const uint GlBlend = 0x0BE2;
        private const uint GlBlendSrcRgb = 0x80C9;
        private const uint GlBlendDstRgb = 0x80C8;
        private const uint GlBlendSrcAlpha = 0x80CB;
        private const uint GlBlendDstAlpha = 0x80CA;
        private const uint GlBlendEquationRgb = 0x8009;
        private const uint GlBlendEquationAlpha = 0x883D;
        private const uint GlBlendColor = 0x8005;
        private const uint GlColorWritemask = 0x0C23;
        private const uint GlDepthTest = 0x0B71;
        private const uint GlDepthWritemask = 0x0B72;
        private const uint GlDepthFunc = 0x0B74;
        private const uint GlCullFace = 0x0B44;
        private const uint GlCullFaceMode = 0x0B45;
        private const uint GlFrontFace = 0x0B46;
        private const uint GlDrawFramebuffer = 0x8CA9;
        private const uint GlReadFramebuffer = 0x8CA8;
        private const uint GlArrayBuffer = 0x8892;
        private const uint GlElementArrayBuffer = 0x8893;

        private readonly OpenGlApi _gl;
        private readonly TextureUnitState[] _textureUnits;
        private readonly int[] _viewport = new int[4];
        private readonly int[] _scissorBox = new int[4];
        private readonly float[] _blendColor = new float[4];
        private readonly byte[] _colorMask = new byte[4];
        private int _program;
        private int _vertexArray;
        private int _arrayBuffer;
        private int _elementArrayBuffer;
        private int _drawFramebuffer;
        private int _readFramebuffer;
        private int _activeTexture;
        private bool _scissorEnabled;
        private bool _blendEnabled;
        private int _blendSrcRgb;
        private int _blendDstRgb;
        private int _blendSrcAlpha;
        private int _blendDstAlpha;
        private int _blendEquationRgb;
        private int _blendEquationAlpha;
        private bool _depthEnabled;
        private bool _depthWriteEnabled;
        private int _depthFunction;
        private bool _cullEnabled;
        private int _cullMode;
        private int _frontFace;
        private bool _captured;

        public RaylibOpenGlStateScope()
        {
            _gl = OpenGlApi.Instance;
            int textureUnitCount = _gl.GetInteger(GlMaxCombinedTextureImageUnits);
            if (textureUnitCount < EffekseerTextureSlotCount)
            {
                throw new InvalidOperationException(
                    $"Effekseer requires at least {EffekseerTextureSlotCount} combined texture image units, " +
                    $"but OpenGL reported GL_MAX_COMBINED_TEXTURE_IMAGE_UNITS={textureUnitCount}.");
            }

            _textureUnits = new TextureUnitState[EffekseerTextureSlotCount];
        }

        public void Capture()
        {
            if (_captured)
            {
                throw new InvalidOperationException("OpenGL state guard is already captured.");
            }
            _captured = true;
            try
            {
                _program = _gl.GetInteger(GlCurrentProgram);
                _vertexArray = _gl.GetInteger(GlVertexArrayBinding);
                _arrayBuffer = _gl.GetInteger(GlArrayBufferBinding);
                _elementArrayBuffer = _gl.GetInteger(GlElementArrayBufferBinding);
                _drawFramebuffer = _gl.GetInteger(GlDrawFramebufferBinding);
                _readFramebuffer = _gl.GetInteger(GlReadFramebufferBinding);
                _activeTexture = _gl.GetInteger(GlActiveTexture);

                for (int i = 0; i < _textureUnits.Length; i++)
                {
                    _gl.ActiveTexture(GlTexture0 + (uint)i);
                    _textureUnits[i] = new TextureUnitState(
                        _gl.GetInteger(GlTextureBinding2D),
                        _gl.GetInteger(GlTextureBinding3D),
                        _gl.GetInteger(GlTextureBindingCubeMap),
                        _gl.GetInteger(GlTextureBinding2DArray),
                        _gl.GetInteger(GlSamplerBinding));
                }
                _gl.ActiveTexture((uint)_activeTexture);

                _gl.GetIntegers(GlViewport, _viewport);
                _gl.GetIntegers(GlScissorBox, _scissorBox);
                _scissorEnabled = _gl.IsEnabled(GlScissorTest);

                _blendEnabled = _gl.IsEnabled(GlBlend);
                _blendSrcRgb = _gl.GetInteger(GlBlendSrcRgb);
                _blendDstRgb = _gl.GetInteger(GlBlendDstRgb);
                _blendSrcAlpha = _gl.GetInteger(GlBlendSrcAlpha);
                _blendDstAlpha = _gl.GetInteger(GlBlendDstAlpha);
                _blendEquationRgb = _gl.GetInteger(GlBlendEquationRgb);
                _blendEquationAlpha = _gl.GetInteger(GlBlendEquationAlpha);
                _gl.GetFloats(GlBlendColor, _blendColor);
                _gl.GetBooleans(GlColorWritemask, _colorMask);

                _depthEnabled = _gl.IsEnabled(GlDepthTest);
                _depthWriteEnabled = _gl.GetBoolean(GlDepthWritemask);
                _depthFunction = _gl.GetInteger(GlDepthFunc);

                _cullEnabled = _gl.IsEnabled(GlCullFace);
                _cullMode = _gl.GetInteger(GlCullFaceMode);
                _frontFace = _gl.GetInteger(GlFrontFace);
                _gl.ThrowIfError("capturing Effekseer OpenGL state");
            }
            catch
            {
                _captured = false;
                throw;
            }
        }

        public void ThrowIfError(string operation)
        {
            _gl.ThrowIfError(operation);
        }

        public void Restore()
        {
            if (!_captured)
            {
                throw new InvalidOperationException("OpenGL state guard cannot restore before capture.");
            }

            try
            {
                _gl.UseProgram((uint)_program);
                _gl.BindVertexArray((uint)_vertexArray);
                _gl.BindBuffer(GlArrayBuffer, (uint)_arrayBuffer);
                _gl.BindBuffer(GlElementArrayBuffer, (uint)_elementArrayBuffer);
                _gl.BindFramebuffer(GlDrawFramebuffer, (uint)_drawFramebuffer);
                _gl.BindFramebuffer(GlReadFramebuffer, (uint)_readFramebuffer);

                for (int i = 0; i < _textureUnits.Length; i++)
                {
                    ref readonly TextureUnitState unit = ref _textureUnits[i];
                    _gl.ActiveTexture(GlTexture0 + (uint)i);
                    _gl.BindTexture(GlTexture2D, (uint)unit.Texture2D);
                    _gl.BindTexture(GlTexture3D, (uint)unit.Texture3D);
                    _gl.BindTexture(GlTextureCubeMap, (uint)unit.TextureCubeMap);
                    _gl.BindTexture(GlTexture2DArray, (uint)unit.Texture2DArray);
                    _gl.BindSampler((uint)i, (uint)unit.Sampler);
                }
                _gl.ActiveTexture((uint)_activeTexture);

                _gl.Viewport(_viewport[0], _viewport[1], _viewport[2], _viewport[3]);
                _gl.Scissor(_scissorBox[0], _scissorBox[1], _scissorBox[2], _scissorBox[3]);
                _gl.SetEnabled(GlScissorTest, _scissorEnabled);

                _gl.BlendFuncSeparate(
                    (uint)_blendSrcRgb,
                    (uint)_blendDstRgb,
                    (uint)_blendSrcAlpha,
                    (uint)_blendDstAlpha);
                _gl.BlendEquationSeparate((uint)_blendEquationRgb, (uint)_blendEquationAlpha);
                _gl.BlendColor(_blendColor[0], _blendColor[1], _blendColor[2], _blendColor[3]);
                _gl.ColorMask(_colorMask[0] != 0, _colorMask[1] != 0, _colorMask[2] != 0, _colorMask[3] != 0);
                _gl.SetEnabled(GlBlend, _blendEnabled);

                _gl.DepthMask(_depthWriteEnabled);
                _gl.DepthFunc((uint)_depthFunction);
                _gl.SetEnabled(GlDepthTest, _depthEnabled);

                _gl.CullFace((uint)_cullMode);
                _gl.FrontFace((uint)_frontFace);
                _gl.SetEnabled(GlCullFace, _cullEnabled);
                _gl.ThrowIfError("restoring OpenGL state after Effekseer draw");
            }
            finally
            {
                _captured = false;
            }
        }

        private readonly record struct TextureUnitState(
            int Texture2D,
            int Texture3D,
            int TextureCubeMap,
            int Texture2DArray,
            int Sampler);

        private sealed class OpenGlApi
        {
            private static readonly Lazy<OpenGlApi> LazyInstance = new(() => new OpenGlApi());
            private readonly IntPtr _openGlLibrary;
            private readonly WglGetCurrentContextDelegate _wglGetCurrentContext;
            private readonly WglGetProcAddressDelegate _wglGetProcAddress;
            private readonly GlGetErrorDelegate _getError;
            private readonly GlGetIntegervDelegate _getIntegerv;
            private readonly GlGetFloatvDelegate _getFloatv;
            private readonly GlGetBooleanvDelegate _getBooleanv;
            private readonly GlIsEnabledDelegate _isEnabled;
            private readonly GlEnableDelegate _enable;
            private readonly GlDisableDelegate _disable;
            private readonly GlUseProgramDelegate _useProgram;
            private readonly GlBindVertexArrayDelegate _bindVertexArray;
            private readonly GlBindBufferDelegate _bindBuffer;
            private readonly GlBindFramebufferDelegate _bindFramebuffer;
            private readonly GlActiveTextureDelegate _activeTexture;
            private readonly GlBindTextureDelegate _bindTexture;
            private readonly GlBindSamplerDelegate _bindSampler;
            private readonly GlViewportDelegate _viewport;
            private readonly GlScissorDelegate _scissor;
            private readonly GlBlendFuncSeparateDelegate _blendFuncSeparate;
            private readonly GlBlendEquationSeparateDelegate _blendEquationSeparate;
            private readonly GlBlendColorDelegate _blendColor;
            private readonly GlColorMaskDelegate _colorMask;
            private readonly GlDepthMaskDelegate _depthMask;
            private readonly GlDepthFuncDelegate _depthFunc;
            private readonly GlCullFaceDelegate _cullFace;
            private readonly GlFrontFaceDelegate _frontFace;

            private OpenGlApi()
            {
                if (!OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException("The bundled Effekseer OpenGL bridge currently supports Windows only.");
                }

                string libraryPath = Path.Combine(Environment.SystemDirectory, "opengl32.dll");
                _openGlLibrary = NativeLibrary.Load(libraryPath);
                _wglGetCurrentContext = LoadLibraryExport<WglGetCurrentContextDelegate>("wglGetCurrentContext");
                _wglGetProcAddress = LoadLibraryExport<WglGetProcAddressDelegate>("wglGetProcAddress");
                if (_wglGetCurrentContext() == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Effekseer OpenGL state capture requires a current Raylib OpenGL context.");
                }

                _getError = LoadFunction<GlGetErrorDelegate>("glGetError");
                _getIntegerv = LoadFunction<GlGetIntegervDelegate>("glGetIntegerv");
                _getFloatv = LoadFunction<GlGetFloatvDelegate>("glGetFloatv");
                _getBooleanv = LoadFunction<GlGetBooleanvDelegate>("glGetBooleanv");
                _isEnabled = LoadFunction<GlIsEnabledDelegate>("glIsEnabled");
                _enable = LoadFunction<GlEnableDelegate>("glEnable");
                _disable = LoadFunction<GlDisableDelegate>("glDisable");
                _useProgram = LoadFunction<GlUseProgramDelegate>("glUseProgram");
                _bindVertexArray = LoadFunction<GlBindVertexArrayDelegate>("glBindVertexArray");
                _bindBuffer = LoadFunction<GlBindBufferDelegate>("glBindBuffer");
                _bindFramebuffer = LoadFunction<GlBindFramebufferDelegate>("glBindFramebuffer");
                _activeTexture = LoadFunction<GlActiveTextureDelegate>("glActiveTexture");
                _bindTexture = LoadFunction<GlBindTextureDelegate>("glBindTexture");
                _bindSampler = LoadFunction<GlBindSamplerDelegate>("glBindSampler");
                _viewport = LoadFunction<GlViewportDelegate>("glViewport");
                _scissor = LoadFunction<GlScissorDelegate>("glScissor");
                _blendFuncSeparate = LoadFunction<GlBlendFuncSeparateDelegate>("glBlendFuncSeparate");
                _blendEquationSeparate = LoadFunction<GlBlendEquationSeparateDelegate>("glBlendEquationSeparate");
                _blendColor = LoadFunction<GlBlendColorDelegate>("glBlendColor");
                _colorMask = LoadFunction<GlColorMaskDelegate>("glColorMask");
                _depthMask = LoadFunction<GlDepthMaskDelegate>("glDepthMask");
                _depthFunc = LoadFunction<GlDepthFuncDelegate>("glDepthFunc");
                _cullFace = LoadFunction<GlCullFaceDelegate>("glCullFace");
                _frontFace = LoadFunction<GlFrontFaceDelegate>("glFrontFace");
            }

            public static OpenGlApi Instance => LazyInstance.Value;

            public unsafe int GetInteger(uint name)
            {
                int value;
                _getIntegerv(name, &value);
                return value;
            }

            public unsafe void GetIntegers(uint name, int[] values)
            {
                fixed (int* pointer = values)
                {
                    _getIntegerv(name, pointer);
                }
            }

            public unsafe void GetFloats(uint name, float[] values)
            {
                fixed (float* pointer = values)
                {
                    _getFloatv(name, pointer);
                }
            }

            public unsafe void GetBooleans(uint name, byte[] values)
            {
                fixed (byte* pointer = values)
                {
                    _getBooleanv(name, pointer);
                }
            }

            public unsafe bool GetBoolean(uint name)
            {
                byte value;
                _getBooleanv(name, &value);
                return value != 0;
            }

            public bool IsEnabled(uint capability) => _isEnabled(capability) != 0;
            public void SetEnabled(uint capability, bool enabled) { if (enabled) _enable(capability); else _disable(capability); }
            public void UseProgram(uint value) => _useProgram(value);
            public void BindVertexArray(uint value) => _bindVertexArray(value);
            public void BindBuffer(uint target, uint value) => _bindBuffer(target, value);
            public void BindFramebuffer(uint target, uint value) => _bindFramebuffer(target, value);
            public void ActiveTexture(uint value) => _activeTexture(value);
            public void BindTexture(uint target, uint value) => _bindTexture(target, value);
            public void BindSampler(uint unit, uint value) => _bindSampler(unit, value);
            public void Viewport(int x, int y, int width, int height) => _viewport(x, y, width, height);
            public void Scissor(int x, int y, int width, int height) => _scissor(x, y, width, height);
            public void BlendFuncSeparate(uint srcRgb, uint dstRgb, uint srcAlpha, uint dstAlpha) => _blendFuncSeparate(srcRgb, dstRgb, srcAlpha, dstAlpha);
            public void BlendEquationSeparate(uint rgb, uint alpha) => _blendEquationSeparate(rgb, alpha);
            public void BlendColor(float red, float green, float blue, float alpha) => _blendColor(red, green, blue, alpha);
            public void ColorMask(bool red, bool green, bool blue, bool alpha) => _colorMask(red ? (byte)1 : (byte)0, green ? (byte)1 : (byte)0, blue ? (byte)1 : (byte)0, alpha ? (byte)1 : (byte)0);
            public void DepthMask(bool enabled) => _depthMask(enabled ? (byte)1 : (byte)0);
            public void DepthFunc(uint value) => _depthFunc(value);
            public void CullFace(uint value) => _cullFace(value);
            public void FrontFace(uint value) => _frontFace(value);

            public void ThrowIfError(string operation)
            {
                uint error = _getError();
                if (error != GlNoError)
                {
                    throw new InvalidOperationException($"OpenGL error 0x{error:X4} while {operation}.");
                }
            }

            private T LoadLibraryExport<T>(string name)
                where T : Delegate
            {
                IntPtr address = NativeLibrary.GetExport(_openGlLibrary, name);
                return Marshal.GetDelegateForFunctionPointer<T>(address);
            }

            private T LoadFunction<T>(string name)
                where T : Delegate
            {
                IntPtr address = IntPtr.Zero;
                if (NativeLibrary.TryGetExport(_openGlLibrary, name, out IntPtr exported))
                {
                    address = exported;
                }
                if (address == IntPtr.Zero)
                {
                    address = _wglGetProcAddress(name);
                }
                long raw = address.ToInt64();
                if (address == IntPtr.Zero || raw is 1 or 2 or 3 or -1)
                {
                    throw new EntryPointNotFoundException($"Current OpenGL context does not expose required function '{name}'.");
                }
                return Marshal.GetDelegateForFunctionPointer<T>(address);
            }

            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr WglGetCurrentContextDelegate();
            [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)] private delegate IntPtr WglGetProcAddressDelegate(string name);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint GlGetErrorDelegate();
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private unsafe delegate void GlGetIntegervDelegate(uint name, int* values);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private unsafe delegate void GlGetFloatvDelegate(uint name, float* values);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private unsafe delegate void GlGetBooleanvDelegate(uint name, byte* values);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate byte GlIsEnabledDelegate(uint capability);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlEnableDelegate(uint capability);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlDisableDelegate(uint capability);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlUseProgramDelegate(uint value);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlBindVertexArrayDelegate(uint value);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlBindBufferDelegate(uint target, uint value);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlBindFramebufferDelegate(uint target, uint value);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlActiveTextureDelegate(uint value);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlBindTextureDelegate(uint target, uint value);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlBindSamplerDelegate(uint unit, uint value);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlViewportDelegate(int x, int y, int width, int height);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlScissorDelegate(int x, int y, int width, int height);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlBlendFuncSeparateDelegate(uint srcRgb, uint dstRgb, uint srcAlpha, uint dstAlpha);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlBlendEquationSeparateDelegate(uint rgb, uint alpha);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlBlendColorDelegate(float red, float green, float blue, float alpha);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlColorMaskDelegate(byte red, byte green, byte blue, byte alpha);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlDepthMaskDelegate(byte enabled);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlDepthFuncDelegate(uint value);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlCullFaceDelegate(uint value);
            [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GlFrontFaceDelegate(uint value);
        }
    }
}

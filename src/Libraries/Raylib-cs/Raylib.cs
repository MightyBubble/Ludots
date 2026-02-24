using System.Numerics;
using System.Runtime.InteropServices;

namespace Raylib_cs
{
    public struct Color
    {
        public byte r;
        public byte g;
        public byte b;
        public byte a;

        public Color(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static readonly Color WHITE = new Color(255, 255, 255, 255);
        public static readonly Color RED = new Color(230, 41, 55, 255);
        public static readonly Color GREEN = new Color(0, 228, 48, 255);
        public static readonly Color BLUE = new Color(0, 121, 241, 255);
        public static readonly Color YELLOW = new Color(253, 249, 0, 255);
        public static readonly Color LIGHTGRAY = new Color(200, 200, 200, 255);
        public static readonly Color DARKGRAY = new Color(80, 80, 80, 255);
        public static readonly Color BLANK = new Color(0, 0, 0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Image
    {
        public void* data;
        public int width;
        public int height;
        public int mipmaps;
        public int format;
    }

    public enum MouseButton
    {
        MOUSE_LEFT_BUTTON = 0,
        MOUSE_RIGHT_BUTTON = 1,
        MOUSE_MIDDLE_BUTTON = 2
    }

    public enum KeyboardKey
    {
        KEY_NULL = 0,
        // Alphanumeric keys
        KEY_APOSTROPHE = 39,
        KEY_COMMA = 44,
        KEY_MINUS = 45,
        KEY_PERIOD = 46,
        KEY_SLASH = 47,
        KEY_ZERO = 48,
        KEY_ONE = 49,
        KEY_TWO = 50,
        KEY_THREE = 51,
        KEY_FOUR = 52,
        KEY_FIVE = 53,
        KEY_SIX = 54,
        KEY_SEVEN = 55,
        KEY_EIGHT = 56,
        KEY_NINE = 57,
        KEY_SEMICOLON = 59,
        KEY_EQUAL = 61,
        KEY_A = 65,
        KEY_B = 66,
        KEY_C = 67,
        KEY_D = 68,
        KEY_E = 69,
        KEY_F = 70,
        KEY_G = 71,
        KEY_H = 72,
        KEY_I = 73,
        KEY_J = 74,
        KEY_K = 75,
        KEY_L = 76,
        KEY_M = 77,
        KEY_N = 78,
        KEY_O = 79,
        KEY_P = 80,
        KEY_Q = 81,
        KEY_R = 82,
        KEY_S = 83,
        KEY_T = 84,
        KEY_U = 85,
        KEY_V = 86,
        KEY_W = 87,
        KEY_X = 88,
        KEY_Y = 89,
        KEY_Z = 90,
        KEY_SPACE = 32,
        KEY_ESCAPE = 256,
        KEY_ENTER = 257,
        KEY_TAB = 258,
        KEY_BACKSPACE = 259,
        KEY_INSERT = 260,
        KEY_DELETE = 261,
        KEY_RIGHT = 262,
        KEY_LEFT = 263,
        KEY_DOWN = 264,
        KEY_UP = 265,
        KEY_PAGE_UP = 266,
        KEY_PAGE_DOWN = 267,
        KEY_HOME = 268,
        KEY_END = 269,
        KEY_CAPS_LOCK = 280,
        KEY_SCROLL_LOCK = 281,
        KEY_NUM_LOCK = 282,
        KEY_PRINT_SCREEN = 283,
        KEY_PAUSE = 284,
        KEY_F1 = 290,
        KEY_F2 = 291,
        KEY_F3 = 292,
        KEY_F4 = 293,
        KEY_F5 = 294,
        KEY_F6 = 295,
        KEY_F7 = 296,
        KEY_F8 = 297,
        KEY_F9 = 298,
        KEY_F10 = 299,
        KEY_F11 = 300,
        KEY_F12 = 301,
        KEY_LEFT_SHIFT = 340,
        KEY_LEFT_CONTROL = 341,
        KEY_LEFT_ALT = 342,
        KEY_LEFT_SUPER = 343,
        KEY_RIGHT_SHIFT = 344,
        KEY_RIGHT_CONTROL = 345,
        KEY_RIGHT_ALT = 346,
        KEY_RIGHT_SUPER = 347,
        KEY_KB_MENU = 348,
    }

    public enum CameraProjection
    {
        CAMERA_PERSPECTIVE = 0,
        CAMERA_ORTHOGRAPHIC
    }

    public enum CameraMode
    {
        CAMERA_CUSTOM = 0,
        CAMERA_FREE,
        CAMERA_ORBITAL,
        CAMERA_FIRST_PERSON,
        CAMERA_THIRD_PERSON
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Camera3D
    {
        public Vector3 position;
        public Vector3 target;
        public Vector3 up;
        public float fovy;
        public CameraProjection projection;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RaylibMatrix
    {
        public float m0, m4, m8, m12;
        public float m1, m5, m9, m13;
        public float m2, m6, m10, m14;
        public float m3, m7, m11, m15;

        public static RaylibMatrix Identity => new RaylibMatrix
        {
            m0 = 1f,
            m5 = 1f,
            m10 = 1f,
            m15 = 1f
        };

        public static RaylibMatrix FromSystemNumerics(in Matrix4x4 m) => new RaylibMatrix
        {
            m0 = m.M11,
            m4 = m.M21,
            m8 = m.M31,
            m12 = m.M41,
            m1 = m.M12,
            m5 = m.M22,
            m9 = m.M32,
            m13 = m.M42,
            m2 = m.M13,
            m6 = m.M23,
            m10 = m.M33,
            m14 = m.M43,
            m3 = m.M14,
            m7 = m.M24,
            m11 = m.M34,
            m15 = m.M44
        };

        public static RaylibMatrix FromScaleTranslation(float tx, float ty, float tz, float sx, float sy, float sz) => new RaylibMatrix
        {
            m0 = sx,
            m5 = sy,
            m10 = sz,
            m12 = tx,
            m13 = ty,
            m14 = tz,
            m15 = 1f
        };
    }

    // --- Instancing Support Structures ---

    [StructLayout(LayoutKind.Sequential)]
    public struct Texture2D
    {
        public uint id;
        public int width;
        public int height;
        public int mipmaps;
        public int format;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MaterialMap
    {
        public Texture2D texture;
        public Color color;
        public float value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Shader
    {
        public uint id;
        public int* locs;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Material
    {
        public Shader shader;
        public MaterialMap* maps;
        public fixed float @params[4];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Mesh
    {
        public int vertexCount;
        public int triangleCount;

        // Vertex attributes data
        public float* vertices;
        public float* texcoords;
        public float* texcoords2;
        public float* normals;
        public float* tangents;
        public byte* colors;
        public ushort* indices;

        // Animation vertex data
        public float* animVertices;
        public float* animNormals;
        public byte* boneIds;
        public float* boneWeights;

        // OpenGL identifiers
        public uint vaoId;
        public uint* vboId;
    }

    public static class Raylib
    {
        private const string NativeLib = "raylib";

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void InitWindow(int width, int height, string title);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CloseWindow();

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool WindowShouldClose();

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void BeginDrawing();

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void EndDrawing();

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ClearBackground(Color color);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void BeginMode3D(Camera3D camera);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void EndMode3D();

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DrawCube(Vector3 position, float width, float height, float length, Color color);
        
        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DrawLine3D(Vector3 startPos, Vector3 endPos, Color color);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DrawSphere(Vector3 centerPos, float radius, Color color);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DrawGrid(int slices, float spacing);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetTargetFPS(int fps);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern float GetFrameTime();

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern double GetTime();
        
        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void UpdateCamera(ref Camera3D camera, CameraMode mode);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DrawFPS(int posX, int posY);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DrawText(string text, int posX, int posY, int fontSize, Color color);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "DrawText")]
        public static extern unsafe void DrawText(byte* text, int posX, int posY, int fontSize, Color color);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DrawRectangle(int posX, int posY, int width, int height, Color color);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DrawRectangleLines(int posX, int posY, int width, int height, Color color);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool IsKeyDown(KeyboardKey key);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern Vector2 GetMousePosition();

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetScreenWidth();

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetScreenHeight();

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern float GetMouseWheelMove();

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool IsMouseButtonDown(MouseButton button);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool IsMouseButtonPressed(MouseButton button);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool IsMouseButtonReleased(MouseButton button);

        // --- Instancing APIs ---

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern Mesh GenMeshCube(float width, float height, float length);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern Mesh GenMeshSphere(float radius, int rings, int slices);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern Material LoadMaterialDefault();

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void UnloadMaterial(Material material);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void UnloadMesh(Mesh mesh);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe void* MemAlloc(int size);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe void MemFree(void* ptr);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe void UploadMesh(ref Mesh mesh, [MarshalAs(UnmanagedType.I1)] bool dynamic);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe void UpdateMeshBuffer(Mesh mesh, int index, void* data, int dataSize, int offset);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern Shader LoadShader(string vsFileName, string fsFileName);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void UnloadShader(Shader shader);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetShaderLocation(Shader shader, string uniformName);

        public enum ShaderUniformDataType
        {
            SHADER_UNIFORM_FLOAT = 0,
            SHADER_UNIFORM_VEC2,
            SHADER_UNIFORM_VEC3,
            SHADER_UNIFORM_VEC4,
            SHADER_UNIFORM_INT,
            SHADER_UNIFORM_IVEC2,
            SHADER_UNIFORM_IVEC3,
            SHADER_UNIFORM_IVEC4,
            SHADER_UNIFORM_SAMPLER2D
        }

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe void SetShaderValue(Shader shader, int locIndex, void* value, int uniformType);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetShaderValueMatrix(Shader shader, int locIndex, RaylibMatrix mat);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DrawMesh(Mesh mesh, Material material, RaylibMatrix transform);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe void DrawMeshInstanced(Mesh mesh, Material material, RaylibMatrix* transforms, int instances);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetShaderLocationAttrib(Shader shader, string attribName);

        public enum ShaderLocationIndex
        {
            SHADER_LOC_VERTEX_POSITION = 0,
            SHADER_LOC_VERTEX_TEXCOORD01,
            SHADER_LOC_VERTEX_TEXCOORD02,
            SHADER_LOC_VERTEX_NORMAL,
            SHADER_LOC_VERTEX_TANGENT,
            SHADER_LOC_VERTEX_COLOR,
            SHADER_LOC_MATRIX_MVP,
            SHADER_LOC_MATRIX_VIEW,
            SHADER_LOC_MATRIX_PROJECTION,
            SHADER_LOC_MATRIX_MODEL,
            SHADER_LOC_MATRIX_NORMAL,
            SHADER_LOC_VECTOR_VIEW,
            SHADER_LOC_COLOR_DIFFUSE,
            SHADER_LOC_COLOR_SPECULAR,
            SHADER_LOC_COLOR_AMBIENT,
            SHADER_LOC_MAP_ALBEDO,
            SHADER_LOC_MAP_METALNESS,
            SHADER_LOC_MAP_NORMAL,
            SHADER_LOC_MAP_ROUGHNESS,
            SHADER_LOC_MAP_OCCLUSION,
            SHADER_LOC_MAP_EMISSION,
            SHADER_LOC_MAP_HEIGHT,
            SHADER_LOC_MAP_CUBEMAP,
            SHADER_LOC_MAP_IRRADIANCE,
            SHADER_LOC_MAP_PREFILTER,
            SHADER_LOC_MAP_BRDF
        }

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe Image GenImageColor(int width, int height, Color color);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe Texture2D LoadTextureFromImage(Image image);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe void UnloadImage(Image image);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void UnloadTexture(Texture2D texture);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe void UpdateTexture(Texture2D texture, void* pixels);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DrawTexture(Texture2D texture, int posX, int posY, Color tint);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern Vector2 GetWorldToScreen(Vector3 position, Camera3D camera);

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetCharPressed();
    }
}

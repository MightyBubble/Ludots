using System;
using System.Runtime.InteropServices;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render;

/// <summary>
/// raylib 原生资源加载/卸载的唯一生产入口：直连 Rl 的加载/卸载只允许出现在本文件（源码契约测试强制），
/// 其余生产代码一律经本门面，保证 RaylibNativeResourceLedger 台账完整。
/// 字节为估算值：纹理按 raylib 5.5 raylib.h PixelFormat（1..24）的 bpp 与 mip 链推算，网格按顶点人均字节推算，
/// 着色器/材质/声音别名只计个数。模型内部由 raylib 自行加载的贴图不经过本门面，属于已知低估项。
/// 身份键：纹理/着色器/RT 用 GL 名，网格用 VAO 名，模型用 meshes 数组指针，材质用 maps 指针，声音用 buffer 指针。
/// 身份为 0（加载失败的哨兵值：id==0 / 指针==null）不进台账，交由调用方 fail-loud；
/// 对应卸载同样跳过，避免失败对象制造假驻留或未知卸载计数。
/// </summary>
public static unsafe class RaylibNativeResources
{
    private const int EstimatedBytesPerMeshVertex = 40;

    public static Texture2D LoadTexture(string fileName)
    {
        Texture2D texture = Rl.LoadTexture(fileName);
        TrackIfResident(RaylibNativeResourceKind.Texture, texture.id, EstimateTextureBytes(texture));
        return texture;
    }

    public static Texture2D LoadTextureFromImage(Image image)
    {
        Texture2D texture = Rl.LoadTextureFromImage(image);
        TrackIfResident(RaylibNativeResourceKind.Texture, texture.id, EstimateTextureBytes(texture));
        return texture;
    }

    public static void UnloadTexture(Texture2D texture)
    {
        UntrackIfResident(RaylibNativeResourceKind.Texture, texture.id);
        Rl.UnloadTexture(texture);
    }

    public static RenderTexture2D LoadRenderTexture(int width, int height)
    {
        RenderTexture2D target = Rl.LoadRenderTexture(width, height);
        TrackIfResident(
            RaylibNativeResourceKind.RenderTexture,
            target.id,
            EstimateTextureBytes(target.texture) + EstimateTextureBytes(target.depth));
        return target;
    }

    public static void UnloadRenderTexture(RenderTexture2D target)
    {
        UntrackIfResident(RaylibNativeResourceKind.RenderTexture, target.id);
        Rl.UnloadRenderTexture(target);
    }

    public static Model LoadModel(string fileName)
    {
        Model model = Rl.LoadModel(fileName);
        TrackIfResident(RaylibNativeResourceKind.Model, ModelIdentity(model), EstimateModelBytes(model));
        return model;
    }

    public static void UnloadModel(Model model)
    {
        UntrackIfResident(RaylibNativeResourceKind.Model, ModelIdentity(model));
        Rl.UnloadModel(model);
    }

    public static void UploadMesh(ref Mesh mesh, bool dynamic)
    {
        Rl.UploadMesh(ref mesh, dynamic);
        TrackIfResident(
            RaylibNativeResourceKind.Mesh,
            mesh.vaoId,
            mesh.vertexCount * (long)EstimatedBytesPerMeshVertex);
    }

    public static Mesh GenMeshCube(float width, float height, float length)
    {
        Mesh mesh = Rl.GenMeshCube(width, height, length);
        TrackIfResident(RaylibNativeResourceKind.Mesh, mesh.vaoId, MeshBytes(mesh));
        return mesh;
    }

    public static Mesh GenMeshSphere(float radius, int rings, int slices)
    {
        Mesh mesh = Rl.GenMeshSphere(radius, rings, slices);
        TrackIfResident(RaylibNativeResourceKind.Mesh, mesh.vaoId, MeshBytes(mesh));
        return mesh;
    }

    public static void UnloadMesh(Mesh mesh)
    {
        UntrackIfResident(RaylibNativeResourceKind.Mesh, mesh.vaoId);
        Rl.UnloadMesh(mesh);
    }

    public static Shader LoadShader(string vsFileName, string fsFileName)
    {
        Shader shader = Rl.LoadShader(vsFileName, fsFileName);
        TrackIfResident(RaylibNativeResourceKind.Shader, shader.id, 0);
        return shader;
    }

    public static Shader LoadShaderFromMemory(string vsCode, string fsCode)
    {
        Shader shader = Rl.LoadShaderFromMemory(vsCode, fsCode);
        TrackIfResident(RaylibNativeResourceKind.Shader, shader.id, 0);
        return shader;
    }

    public static void UnloadShader(Shader shader)
    {
        UntrackIfResident(RaylibNativeResourceKind.Shader, shader.id);
        Rl.UnloadShader(shader);
    }

    public static Material LoadMaterialDefault()
    {
        Material material = Rl.LoadMaterialDefault();
        TrackIfResident(RaylibNativeResourceKind.Material, MaterialIdentity(material), 0);
        return material;
    }

    public static void UnloadMaterial(Material material)
    {
        UntrackIfResident(RaylibNativeResourceKind.Material, MaterialIdentity(material));
        Rl.UnloadMaterial(material);
    }

    public static Sound LoadSound(string fileName)
    {
        Sound sound = Rl.LoadSound(fileName);
        TrackIfResident(RaylibNativeResourceKind.Sound, SoundIdentity(sound), EstimateSoundBytes(sound));
        return sound;
    }

    public static Sound LoadSoundAlias(Sound sourceSound)
    {
        Sound alias = Rl.LoadSoundAlias(sourceSound);
        TrackIfResident(RaylibNativeResourceKind.SoundAlias, SoundIdentity(alias), 0);
        return alias;
    }

    public static void UnloadSound(Sound sound)
    {
        UntrackIfResident(RaylibNativeResourceKind.Sound, SoundIdentity(sound));
        Rl.UnloadSound(sound);
    }

    public static void UnloadSoundAlias(Sound alias)
    {
        UntrackIfResident(RaylibNativeResourceKind.SoundAlias, SoundIdentity(alias));
        Rl.UnloadSoundAlias(alias);
    }

    /// <summary>
    /// vendored Raylib-cs 绑定缺 cubemap 装载入口（不可改 vendored 文件）；
    /// 直接声明 native raylib 5.5 导出的 rlLoadTextureCubemap（数据布局：逐 mip 6 face 连续，
    /// face 序 +X/-X/+Y/-Y/+Z/-Z），采样参数由 native 侧设为 LINEAR_MIPMAP_LINEAR + CLAMP_TO_EDGE。
    /// </summary>
    public static Texture2D LoadTextureCubemap(void* data, int size, int format, int mipmapCount)
    {
        uint id = LoadTextureCubemapNative(data, size, format, mipmapCount);
        if (id == 0)
        {
            throw new InvalidOperationException($"rlLoadTextureCubemap failed for size={size} format={format} mipmaps={mipmapCount}.");
        }

        var cubemap = new Texture2D
        {
            id = id,
            width = size,
            height = size,
            mipmaps = mipmapCount,
            format = format,
        };
        RaylibNativeResourceLedger.Track(RaylibNativeResourceKind.Texture, cubemap.id, EstimateTextureBytes(cubemap) * 6);
        return cubemap;
    }

    [DllImport("raylib", EntryPoint = "rlLoadTextureCubemap", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe uint LoadTextureCubemapNative(void* data, int size, int format, int mipmapCount);

    private static void TrackIfResident(RaylibNativeResourceKind kind, ulong identity, long estimatedBytes)
    {
        if (identity != 0)
        {
            RaylibNativeResourceLedger.Track(kind, identity, estimatedBytes);
        }
    }

    private static void UntrackIfResident(RaylibNativeResourceKind kind, ulong identity)
    {
        if (identity != 0)
        {
            RaylibNativeResourceLedger.Untrack(kind, identity);
        }
    }

    private static ulong ModelIdentity(Model model)
    {
        return (ulong)model.meshes;
    }

    private static ulong MaterialIdentity(Material material)
    {
        return (ulong)material.maps;
    }

    private static ulong SoundIdentity(Sound sound)
    {
        return (ulong)sound.stream.buffer;
    }

    private static long MeshBytes(Mesh mesh)
    {
        return mesh.vertexCount * (long)EstimatedBytesPerMeshVertex;
    }

    private static long EstimateModelBytes(Model model)
    {
        long bytes = 0;
        for (int i = 0; i < model.meshCount; i++)
        {
            bytes += model.meshes[i].vertexCount * (long)EstimatedBytesPerMeshVertex;
        }

        return bytes;
    }

    private static long EstimateSoundBytes(Sound sound)
    {
        uint bytesPerSample = sound.stream.sampleSize / 8;
        return (long)sound.frameCount * bytesPerSample * sound.stream.channels;
    }

    internal static long EstimateTextureBytes(Texture2D texture)
    {
        int bitsPerPixel = texture.format switch
        {
            1 => 8,
            2 => 16,
            3 => 16,
            4 => 24,
            5 => 16,
            6 => 16,
            7 => 32,
            8 => 32,
            9 => 96,
            10 => 128,
            11 => 16,
            12 => 48,
            13 => 64,
            14 => 4,
            15 => 4,
            16 => 8,
            17 => 8,
            18 => 4,
            19 => 4,
            20 => 8,
            21 => 4,
            22 => 4,
            23 => 8,
            24 => 2,
            _ => 32,
        };
        long baseBytes = (long)texture.width * texture.height * bitsPerPixel / 8;
        int mipmaps = Math.Clamp(texture.mipmaps, 1, 20);
        long totalBytes = baseBytes;
        long levelBytes = baseBytes;
        for (int level = 1; level < mipmaps; level++)
        {
            levelBytes /= 4;
            totalBytes += levelBytes;
        }

        return totalBytes;
    }
}

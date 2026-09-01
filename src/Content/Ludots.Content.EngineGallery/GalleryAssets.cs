using System.Numerics;
using System.Runtime.InteropServices;
using Ludots.Platform.Abstractions;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using SkiaSharp;
using Ludots.Raylib.Render;
using Ludots.Raylib.SceneKit;

namespace Ludots.Content.EngineGallery
{
    /// <summary>vendored Raylib-cs 缺少的常用颜色常量（绑定内仅有 WHITE/RED/GREEN/BLUE/YELLOW/GRAY 系）。</summary>
    public static class GalleryColors
    {
        public static readonly Color Black = new(0, 0, 0, 255);
        public static readonly Color RayWhite = new(245, 245, 245, 255);
        public static readonly Vector4 ShadowReceiverGray = new(0.62f, 0.64f, 0.66f, 1f);
    }

    /// <summary>窗口标志位的 vendored 命名形态：绑定只暴露 SetConfigFlags(uint)。</summary>
    public static class GalleryWindowFlags
    {
        public const uint FlagWindowHidden = 0x80;
    }

    /// <summary>
    /// 内容工程资产路径解析：相对 URI 先映射到当前引擎工程根（工程是数据真源），再回退输出目录 assets/ 与输出目录本身；
    /// assets/generated/ 是场景运行期程序化生成的贴图目录。
    /// </summary>
    public sealed class GalleryAssetPaths : IRenderAssetPathResolver
    {
        public static GalleryAssetPaths Instance { get; } = new();

        private readonly string _baseDir = AppContext.BaseDirectory;

        public bool TryResolveFullPath(string uri, out string fullPath)
        {
            if (Path.IsPathRooted(uri))
            {
                fullPath = uri;
                return true;
            }

            string? projectRoot = EngineProject.CurrentRoot;
            if (projectRoot != null)
            {
                string inProject = Path.Combine(projectRoot, uri);
                if (File.Exists(inProject))
                {
                    fullPath = inProject;
                    return true;
                }
            }

            string inAssets = Path.Combine(_baseDir, "assets", uri);
            string inBase = Path.Combine(_baseDir, uri);
            fullPath = File.Exists(inAssets) ? inAssets : inBase;
            return true;
        }

        public static string GeneratedDir => Path.Combine(AppContext.BaseDirectory, "assets", "generated");

        public static string GeneratedPath(string fileName) => Path.Combine(GeneratedDir, fileName);
    }

    /// <summary>渲染侧网格资产直读表：场景手工注册 Primitive/Model/Billboard/VFX 描述符。</summary>
    public sealed class GalleryMeshAssets : IRenderMeshAssets
    {
        private readonly Dictionary<int, MeshAssetDescriptor> _byId = new();
        private readonly Dictionary<string, int> _byKey = new(StringComparer.Ordinal);
        private readonly Dictionary<int, string> _names = new();

        public int Register(string key, in MeshAssetDescriptor descriptor)
        {
            _byId[descriptor.Id] = descriptor;
            _byKey[key] = descriptor.Id;
            _names[descriptor.Id] = key;
            return descriptor.Id;
        }

        public bool TryGetDescriptor(int meshAssetId, out MeshAssetDescriptor descriptor)
        {
            return _byId.TryGetValue(meshAssetId, out descriptor);
        }

        public bool TryGetPrimitiveKind(int meshAssetId, out PrimitiveMeshKind kind)
        {
            kind = PrimitiveMeshKind.Cube;
            if (!_byId.TryGetValue(meshAssetId, out MeshAssetDescriptor descriptor) ||
                descriptor.Type != MeshAssetType.Primitive)
            {
                return false;
            }

            kind = descriptor.PrimitiveKind;
            return true;
        }

        public int GetId(string key)
        {
            return _byKey.TryGetValue(key, out int id) ? id : 0;
        }

        public string GetName(int id)
        {
            return _names.TryGetValue(id, out string? name) ? name : string.Empty;
        }
    }

    /// <summary>渲染侧材质资产直读表：命名贴图表 + flags 驱动 blend/cutout/double-sided，支持 parentKey 实例链。</summary>
    public sealed class GalleryMaterialAssets : IRenderMaterialAssets
    {
        private readonly Dictionary<int, MaterialAssetDescriptor> _byId = new();
        private readonly Dictionary<string, int> _byKey = new(StringComparer.Ordinal);
        private readonly Dictionary<int, string> _names = new();
        private readonly Dictionary<int, IReadOnlyDictionary<string, string>> _texturesById = new();

        public int Register(string key, in MaterialAssetDescriptor descriptor, IReadOnlyDictionary<string, string>? textureUris = null)
        {
            _byId[descriptor.Id] = descriptor;
            _byKey[key] = descriptor.Id;
            _names[descriptor.Id] = key;
            if (textureUris != null)
            {
                _texturesById[descriptor.Id] = textureUris;
            }

            return descriptor.Id;
        }

        public bool TryGet(int id, out MaterialAssetDescriptor descriptor)
        {
            return _byId.TryGetValue(id, out descriptor);
        }

        public bool TryResolve(int id, out ResolvedMaterialAsset material)
        {
            if (!_byId.ContainsKey(id))
            {
                material = default;
                return false;
            }

            material = MaterialAssetResolver.Resolve(this, id, ResolveTextureUris);
            return true;
        }

        public int GetId(string key)
        {
            return _byKey.TryGetValue(key, out int id) ? id : 0;
        }

        public string GetName(int id)
        {
            return _names.TryGetValue(id, out string? name) ? name : string.Empty;
        }

        private IReadOnlyDictionary<string, string>? ResolveTextureUris(int id)
        {
            return _texturesById.TryGetValue(id, out IReadOnlyDictionary<string, string>? uris) ? uris : null;
        }
    }

    /// <summary>纯数据图元快照的最小直读实现（Revision 递增标记几何变化）。</summary>
    public sealed class GalleryPrimitiveSnapshot : IPrimitiveDrawSnapshot
    {
        private readonly List<PrimitiveDrawItem> _items = new(1024);
        private readonly List<PrimitiveDrawItem> _emptyDelta = new();
        private readonly List<int> _emptyRemoved = new();
        private int _revision;

        public IReadOnlyList<PrimitiveDrawItem> Items => _items;

        public void BeginFrame()
        {
            _items.Clear();
        }

        public void Add(in PrimitiveDrawItem item)
        {
            _items.Add(item);
            _revision++;
        }

        public int Count => _items.Count;
        public int Revision => _revision;
        public int ProjectionGeneration => 1;
        public int StaticMeshGeometryRevision => 0;
        public int StaticMeshDeltaBaseRevision => _revision;
        public int StaticMeshLaneItemCount => 0;
        public int SkinnedLaneItemCount => 0;
        public int StaticMeshDeltaItemCount => 0;
        public int StaticMeshRemovedStableIdCount => 0;

        public ReadOnlySpan<PrimitiveDrawItem> GetSpan()
        {
            return CollectionsMarshal.AsSpan(_items);
        }

        public ReadOnlySpan<PrimitiveDrawItem> GetStaticMeshDeltaItems()
        {
            return CollectionsMarshal.AsSpan(_emptyDelta);
        }

        public ReadOnlySpan<int> GetStaticMeshRemovedStableIds()
        {
            return CollectionsMarshal.AsSpan(_emptyRemoved);
        }
    }

    /// <summary>蒙皮批次快照的最小直读实现。</summary>
    public sealed class GallerySkinnedBatch : ISkinnedVisualBatchSnapshot
    {
        private readonly List<SkinnedVisualBatchItem> _items = new(128);

        public IReadOnlyList<SkinnedVisualBatchItem> Items => _items;

        public void BeginFrame()
        {
            _items.Clear();
        }

        public void Add(in SkinnedVisualBatchItem item)
        {
            _items.Add(item);
        }

        public int Count => _items.Count;

        public ReadOnlySpan<SkinnedVisualBatchItem> GetSpan()
        {
            return CollectionsMarshal.AsSpan(_items);
        }
    }

    /// <summary>画廊动效通道注册器：locomotion/aim_yaw/recoil 三个知名通道的稳定 id。</summary>
    public static class GalleryAnimationChannels
    {
        public const int Locomotion = 1;
        public const int AimYaw = 2;
        public const int Recoil = 3;

        public static int Register(string name)
        {
            return name switch
            {
                WellKnownAnimationChannelNames.Locomotion => Locomotion,
                WellKnownAnimationChannelNames.AimYaw => AimYaw,
                WellKnownAnimationChannelNames.Recoil => Recoil,
                _ => 0,
            };
        }
    }

    /// <summary>
    /// 程序化 RGBA 贴图工厂：vendored 绑定缺 ExportImage，PNG 编码走 SkiaSharp（Render 传递引用），
    /// 产出落盘后仍由渲染器的 vfs 直读链路加载，保持真实装载路径。
    /// </summary>
    public static class GalleryTextureFactory
    {
        public static void WritePng(string fileName, int width, int height, Func<int, int, Color> pixel)
        {
            Directory.CreateDirectory(GalleryAssetPaths.GeneratedDir);
            string path = GalleryAssetPaths.GeneratedPath(fileName);
            using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            unsafe
            {
                byte* ptr = (byte*)bitmap.GetPixels();
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color c = pixel(x, y);
                        int offset = ((y * width) + x) * 4;
                        ptr[offset] = c.r;
                        ptr[offset + 1] = c.g;
                        ptr[offset + 2] = c.b;
                        ptr[offset + 3] = c.a;
                    }
                }
            }

            using Stream stream = File.Create(path);
            if (!bitmap.Encode(stream, SKEncodedImageFormat.Png, 100))
            {
                throw new InvalidOperationException($"Gallery texture PNG encode failed for '{path}'.");
            }
        }

        public static Texture2D LoadPng(string uri)
        {
            if (!GalleryAssetPaths.Instance.TryResolveFullPath(uri, out string path) || !File.Exists(path))
            {
                throw new FileNotFoundException($"Gallery texture '{uri}' was not generated before load.", uri);
            }

            Texture2D texture = RaylibNativeResources.LoadTexture(path);
            if (texture.id == 0)
            {
                throw new InvalidOperationException($"Gallery texture LoadTexture failed for '{path}'.");
            }

            return texture;
        }

        public static float SmoothNoise(int x, int y, int seed)
        {
            float value = 0f;
            float amplitude = 0.5f;
            int frequency = 1;
            for (int octave = 0; octave < 4; octave++)
            {
                value += HashNoise(x * frequency, y * frequency, seed + octave) * amplitude;
                amplitude *= 0.5f;
                frequency *= 2;
            }

            return value;
        }

        public static float HashNoise(int x, int y, int seed)
        {
            uint hash = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519);
            hash = (hash ^ (hash >> 13)) * 1274126177;
            return ((hash ^ (hash >> 16)) & 0x00FFFFFF) / 16777216f;
        }
    }

    /// <summary>画廊 PrimitiveDrawItem 构造助手：默认可见 Movable 网格项（结构默认值 Visibility=Hidden 必须显式覆盖）。</summary>
    public static class GalleryItems
    {
        public static PrimitiveDrawItem Mesh(
            int meshAssetId,
            int stableId,
            Vector3 position,
            Vector3 scale,
            Vector4 color,
            int materialId = 0,
            Quaternion rotation = default)
        {
            return new PrimitiveDrawItem
            {
                MeshAssetId = meshAssetId,
                StableId = stableId,
                Position = position,
                Rotation = rotation == default ? Quaternion.Identity : rotation,
                Scale = scale,
                Color = color,
                MaterialId = materialId,
                RenderPath = VisualRenderPath.None,
                AssetKind = AssetKind.Mesh,
                Mobility = VisualMobility.Movable,
                Visibility = VisualVisibility.Visible,
            };
        }

        public static PrimitiveDrawItem Vfx(int effectAssetId, int stableId, Vector3 position, Vector4 tint, float scale = 1f)
        {
            return new PrimitiveDrawItem
            {
                MeshAssetId = effectAssetId,
                StableId = stableId,
                Position = position,
                Rotation = Quaternion.Identity,
                Scale = new Vector3(scale, scale, scale),
                Color = tint,
                RenderPath = VisualRenderPath.None,
                AssetKind = AssetKind.VFX,
                Mobility = VisualMobility.Movable,
                Visibility = VisualVisibility.Visible,
            };
        }

        public static PrimitiveDrawItem Decal(
            int stableId,
            int materialId,
            Vector3 position,
            float yawRad,
            float stampWidth,
            float stampDepth,
            Vector4 tint)
        {
            return new PrimitiveDrawItem
            {
                MeshAssetId = 0,
                StableId = stableId,
                Position = position,
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawRad),
                Scale = new Vector3(stampWidth, 3f, stampDepth),
                Color = tint,
                MaterialId = materialId,
                RenderPath = VisualRenderPath.None,
                AssetKind = AssetKind.Decal,
                Mobility = VisualMobility.Movable,
                Visibility = VisualVisibility.Visible,
            };
        }
    }

    /// <summary>场景相机微调：保持轨道角与目标，仅按场景需要拉远（无 Core 相机栈依赖）。</summary>
    public static class GalleryCamera
    {
        public static void EnforceDistance(ref Camera3D camera, float distance)
        {
            Vector3 offset = camera.position - camera.target;
            float length = offset.Length();
            if (length <= 0.001f)
            {
                camera.position = camera.target + new Vector3(distance * 0.6f, distance * 0.5f, distance * 0.6f);
                return;
            }

            camera.position = camera.target + (offset / length) * distance;
        }

        public static CameraRenderState3D StateOf(in Camera3D camera)
        {
            return new CameraRenderState3D(camera.position, camera.target, camera.up, camera.fovy);
        }
    }
}

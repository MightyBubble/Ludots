using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Assimp;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 外部模型格式 → GLB 转换器（AssimpNet）。背景：native raylib 5.5 的 OBJ 装载分支
    /// 对无 texcoord/normal 索引的面片（`f v` 形态，mass_navigation 资产即此形态）原生
    /// AccessViolation（issue #1050，裸 InitWindow+LoadModel 即可复现，与 Cubemap/IBL 无关）。
    /// 因此 OBJ/FBX/COLLADA 一律先转 GLB，再走 raylib 唯一稳定的 glTF 装载路径；
    /// 转换产物按（源路径 + 修改时间 + 大小）哈希缓存到临时目录，源不变不重转。
    /// </summary>
    public static class RaylibModelFileConverter
    {
        /// <summary>声明式可转换集——新增格式必须在这里登记并补转换测试，不做"assimp 能读就都收"的隐式合同。</summary>
        public static readonly string[] ConvertibleExtensions = { ".obj", ".fbx", ".dae" };

        private static readonly object ConvertGate = new();

        public static bool IsConvertible(string path)
        {
            return ConvertibleExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        }

        /// <summary>转换（或命中缓存）并返回 GLB 路径。导入零网格/导出失败 fail-loud，不产出占位文件。</summary>
        public static string ConvertToCachedGlb(string sourcePath)
        {
            string fullSource = Path.GetFullPath(sourcePath);
            DateTime stamp = File.GetLastWriteTimeUtc(fullSource);
            long size = new FileInfo(fullSource).Length;

            using var sha = SHA1.Create();
            byte[] hash = sha.ComputeHash(
                System.Text.Encoding.UTF8.GetBytes($"{fullSource}|{stamp.Ticks}|{size}"));
            string key = Convert.ToHexString(hash);

            string cacheDir = Path.Combine(Path.GetTempPath(), "Ludots.AssetConvert");
            string target = Path.Combine(cacheDir, key, Path.GetFileNameWithoutExtension(fullSource) + ".glb");
            if (File.Exists(target))
            {
                return target;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            lock (ConvertGate)
            {
                if (File.Exists(target))
                {
                    return target;
                }

                var ctx = new AssimpContext();
                try
                {
                    Scene scene = ctx.ImportFile(fullSource, BuildPostProcessSteps());
                    if (scene == null || scene.MeshCount == 0)
                    {
                        throw new InvalidOperationException(
                            $"{nameof(RaylibModelFileConverter)}: '{Path.GetFileName(fullSource)}' 导入成功但无网格，拒绝转换。");
                    }

                    string tempPath = target + ".tmp";
                    if (!ctx.ExportFile(scene, tempPath, "glb2"))
                    {
                        throw new InvalidOperationException(
                            $"{nameof(RaylibModelFileConverter)}: '{Path.GetFileName(fullSource)}' GLB 导出失败（assimp ExportFile 返回 false）。");
                    }

                    File.Move(tempPath, target);
                    return target;
                }
                finally
                {
                    ctx.Dispose();
                }
            }
        }

        private static PostProcessSteps BuildPostProcessSteps()
        {
            return PostProcessSteps.JoinIdenticalVertices
                | PostProcessSteps.Triangulate
                | PostProcessSteps.GenerateSmoothNormals
                | PostProcessSteps.LimitBoneWeights;
        }
    }

    /// <summary>
    /// 模型文件装载入口：glTF 走 native，其余声明式可转换集先经 GLB 转换——
    /// 引擎内禁止直接对用户/资产文件 Rl.LoadModel（#1050 的 OBJ 原生崩溃路径）。
    /// </summary>
    public static class RaylibModelFileLoader
    {
        public static readonly string[] NativeExtensions = { ".glb", ".gltf" };

        /// <summary>返回 native LoadModel 可安全装载的路径（glTF 原样，OBJ/FBX/DAE 转换后的 GLB）。</summary>
        public static string PrepareNativeLoadable(string fullPath)
        {
            string ext = Path.GetExtension(fullPath).ToLowerInvariant();
            if (NativeExtensions.Contains(ext))
            {
                return fullPath;
            }

            if (RaylibModelFileConverter.IsConvertible(fullPath))
            {
                return RaylibModelFileConverter.ConvertToCachedGlb(fullPath);
            }

            throw new InvalidOperationException(
                $"{nameof(RaylibModelFileLoader)}: 不支持的模型格式 '{ext}'（native: {string.Join("/", NativeExtensions)}；可转换: {string.Join("/", RaylibModelFileConverter.ConvertibleExtensions)}）。");
        }

        public static Model LoadModel(string fullPath)
        {
            return RaylibNativeResources.LoadModel(PrepareNativeLoadable(fullPath));
        }
    }
}

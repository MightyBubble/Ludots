using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace Sango.Runtime
{
    /// <summary>
    /// T1.1(#1842/#1847):进程级唯一的官方配置管线。内核配置装载(Data/Common 内容表、
    /// GAS 三表)不再各自私搭 VirtualFileSystem/ModLoader/手工 catalog——统一走平台
    /// ConfigPipeline + ConfigCatalogLoader,catalog 声明在两份 assets/config_catalog.json。
    /// VFS 复用 SangoVfsIO 已安装的那只(引擎宿主即引擎 VFS;headless 即测试自建 VFS),
    /// 按需补挂 SangoSimMod/LudotsCoreMod(GAS 表与 Core:game.json 的来源),挂载幂等。
    /// </summary>
    public static class SangoConfigRuntime
    {
        public const string SimModId = "SangoSimMod";
        public const string CoreModId = "LudotsCoreMod";

        static ConfigPipeline? _pipeline;
        static ConfigCatalog? _catalog;
        static readonly object Gate = new();

        public static ConfigPipeline Pipeline(string? simModRoot = null, string? coreModRoot = null)
        {
            lock (Gate)
            {
                if (_pipeline != null)
                {
                    return _pipeline;
                }

                VirtualFileSystem vfs = RequireKernelVfs();
                string simRoot = simModRoot ?? ResolveSimModRoot(vfs);
                string coreRoot = coreModRoot ?? ResolveCoreModRoot(simModRoot ?? simRoot)
                    ?? throw new InvalidOperationException(
                        "Sango config pipeline requires LudotsCoreMod assets (Core game.json) next to SangoSimMod; the mods layout drifted.");
                EnsureMounted(vfs, SimModId, simRoot);
                EnsureMounted(vfs, CoreModId, coreRoot);

                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                modLoader.LoadedModIds.Add(SangoVfsIO.ContentModId);
                modLoader.LoadedModIds.Add(SimModId);
                modLoader.LoadedModIds.Add(CoreModId);
                _pipeline = new ConfigPipeline(vfs, modLoader);
                return _pipeline;
            }
        }

        public static ConfigCatalog Catalog(string? simModRoot = null, string? coreModRoot = null)
        {
            lock (Gate)
            {
                _catalog ??= ConfigCatalogLoader.Load(Pipeline(simModRoot, coreModRoot));
                return _catalog;
            }
        }

        static VirtualFileSystem RequireKernelVfs()
        {
            if (!SangoVfsIO.Installed)
            {
                throw new InvalidOperationException(
                    "Sango config pipeline requires the kernel VFS (SangoVfsIO.Install) before any config load.");
            }

            return SangoVfsIO.InstalledVfs as VirtualFileSystem
                ?? throw new InvalidOperationException(
                    "Sango config pipeline requires a concrete VirtualFileSystem kernel backend.");
        }

        static void EnsureMounted(VirtualFileSystem vfs, string modId, string modRoot)
        {
            // Mount 幂等(重复挂载覆盖同一路径);TryResolveFullPath 只验证挂载存在,
            // 探任意前缀路径即可判"已挂"。已挂到别处的宿主(引擎)不受影响。
            if (vfs.TryResolveFullPath($"{modId}:config_catalog.json", out _))
            {
                return;
            }

            vfs.Mount(modId, modRoot);
        }

        static string ResolveSimModRoot(VirtualFileSystem vfs)
        {
            // 锚定优先级:已挂的 SangoSimMod > 内容 mod 的兄弟目录(SangoVfsIO 装的 vfs
            // 必挂内容 mod,测试/引擎两态都成立)> 程序集位置(反射装载兜底;ProjectReference
            // 会把 DLL 拷进消费方 bin,该锚只在 mods 下的原生 bin 产物上成立)。
            if (TryMountRoot(vfs, SimModId, out string? mounted))
            {
                return mounted;
            }

            if (TryMountRoot(vfs, SangoVfsIO.ContentModId, out string? contentRoot))
            {
                string sibling = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(contentRoot))!, SimModId);
                if (System.IO.Directory.Exists(System.IO.Path.Combine(sibling, "assets")))
                {
                    return sibling;
                }
            }

            return ResolveSimModRootFromAssembly();
        }

        /// <summary>从挂载表反推 mod 物理根:探 "modId:x" 的解析位剥去尾巴 x 即挂载根
        /// (TryResolveFullPath 不验证资源存在,挂载在即成)。</summary>
        static bool TryMountRoot(VirtualFileSystem vfs, string modId, out string? root)
        {
            if (vfs.TryResolveFullPath($"{modId}:x", out string resolved))
            {
                root = resolved[..^2];
                return true;
            }

            root = null;
            return false;
        }

        static string ResolveSimModRootFromAssembly()
        {
            // SangoSimMod.dll 位于 <modRoot>/bin/net9.0/(LoadFrom 反射装载下同样成立):
            // dll 目录上溯两级 = mod 根。
            string dll = typeof(SangoConfigRuntime).Assembly.Location;
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dll)!, "..", ".."));
        }

        static string? ResolveCoreModRoot(string simModRoot)
        {
            // SangoSimMod 在 <repo>/mods/sango/ 下,LudotsCoreMod 在 <repo>/mods/ 下
            // (兄弟目录差一层)。显式传入的 simModRoot 可能来自引擎 manifest,与
            // 解析出的根同型,统一走同一条兄弟推导。
            string modsRoot = System.IO.Path.GetDirectoryName(
                System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(simModRoot))!)!;
            string candidate = System.IO.Path.Combine(modsRoot, CoreModId);
            return System.IO.File.Exists(System.IO.Path.Combine(candidate, "assets", "game.json")) ? candidate : null;
        }
    }
}

// sango 内核 Path/File/Directory shim 的 IO 网关(M1.b)。
// 语义:shim 不再直读盘;启动线显式 Install(VFS, 内容 mod id) 后,内核所有文件访问
// 都经 Ludots VirtualFileSystem 解析("ModId:assets/..."),未安装后端时读操作 fail-fast。
// 只读后端:写入/删丟操作显式抛错(存档域 M1.d 再引入写后端),存在性判定按"无此资源"处理。

using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Modding;

namespace Sango.Runtime
{
    public static class SangoVfsIO
    {
        private static IVirtualFileSystem? _vfs;
        private static string _contentModId = string.Empty;

        public static bool Installed => _vfs != null;

        public static void Install(IVirtualFileSystem vfs, string contentModId)
        {
            _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
            if (string.IsNullOrWhiteSpace(contentModId))
                throw new ArgumentException("Content mod id is required.", nameof(contentModId));
            _contentModId = contentModId;
        }

        public static void Uninstall()
        {
            _vfs = null;
            _contentModId = string.Empty;
        }

        /// <summary>
        /// 把内核使用的相对内容路径("Data/Common/x.json"、"./Save/y.json"、或 VFS 回流 uri)
        /// 解析为真实存在的 VFS 文件 uri;不存在返回 null。
        /// 注意:TryResolveFullPath 只做挂载根内合法性检查,不验证资源存在,必须补存在性探测。
        /// </summary>
        public static string? Resolve(string? path) => Resolve(path, allowDirectory: false);

        /// <summary>
        /// 同 <see cref="Resolve(string)"/>,但返回挂载根下的物理文件全路径;供二进制流装载
        /// (内核 Map.Load 的 FileStream)把 VFS uri 落到真实文件。不存在返回 null。
        /// </summary>
        public static string? ResolveToFullPath(string? path)
        {
            string? uri = Resolve(path);
            if (uri == null || !_vfs!.TryResolveFullPath(uri, out string full))
                return null;
            return full;
        }

        static string? Resolve(string? path, bool allowDirectory)
        {
            if (_vfs == null || string.IsNullOrWhiteSpace(path))
                return null;

            if (path.Contains(':') && _vfs.TryResolveFullPath(path, out string uriFull)
                && ResourceExists(uriFull, allowDirectory))
                return path;

            string normalized = path.Replace('\\', '/').TrimStart('/');
            while (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized.Substring(2);

            if (normalized.Length == 0)
                return null;

            string mapped = $"{_contentModId}:assets/{normalized}";
            return _vfs.TryResolveFullPath(mapped, out string full) && ResourceExists(full, allowDirectory)
                ? mapped
                : null;
        }

        static bool ResourceExists(string fullPath, bool allowDirectory)
        {
            return System.IO.File.Exists(fullPath)
                || (allowDirectory && System.IO.Directory.Exists(fullPath));
        }

        public static bool Exists(string? path) => Resolve(path) != null;

        public static string? FindFile(string? file) => Resolve(file);

        public static string ReadAllText(string path)
        {
            string uri = RequireUri(path);
            using Stream stream = _vfs!.GetStream(uri);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public static string[] ReadAllLines(string path)
        {
            string uri = RequireUri(path);
            using Stream stream = _vfs!.GetStream(uri);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
                lines.Add(line);
            return lines.ToArray();
        }

        public static StreamReader OpenText(string path)
        {
            string uri = RequireUri(path);
            // StreamReader 接管流所有权;JsonTextReader 场景只做同步读,直接交出。
            return new StreamReader(_vfs!.GetStream(uri));
        }

        public static bool DirectoryExists(string? path)
        {
            if (_vfs == null)
                return false;
            return Resolve(path, allowDirectory: true) != null;
        }

        public static IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            if (_vfs == null)
                throw NotInstalled();
            string? uri = Resolve(path, allowDirectory: true);
            if (uri == null || !_vfs.TryResolveFullPath(uri, out string full) || !System.IO.Directory.Exists(full))
                yield break;

            foreach (string file in System.IO.Directory.EnumerateFiles(full, searchPattern, searchOption))
            {
                string relative = file.Substring(full.Length).Replace('\\', '/').TrimStart('/');
                yield return $"{uri.TrimEnd('/')}/{relative}";
            }
        }

        public static void WriteAllText(string path, string contents) =>
            throw NotSupportedWrite();

        public static void Delete(string path) => throw NotSupportedWrite();

        public static void CreateDirectory(string path) => throw NotSupportedWrite();

        public static void DeleteDirectory(string path, bool recursive) => throw NotSupportedWrite();

        public static string[] GetDirectories(string path, string searchPattern, SearchOption searchOption) =>
            throw NotSupportedWrite();

        private static string RequireUri(string path)
        {
            if (_vfs == null)
                throw NotInstalled();
            return Resolve(path) ?? throw new FileNotFoundException(
                $"Sango asset not found in VFS mount '{_contentModId}': {path}");
        }

        private static InvalidOperationException NotInstalled() => new(
            "SangoVfsIO backend not installed; SangoKernelBoot.Boot must Install(vfs, contentModId) before the kernel touches files.");

        private static NotSupportedException NotSupportedWrite() => new(
            "SangoVfsIO is a read-only VFS backend; kernel write/delete IO is out of scope until the save wave (M1.d).");
    }
}

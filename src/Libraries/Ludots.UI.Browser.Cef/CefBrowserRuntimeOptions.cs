using System;
using System.IO;

namespace Ludots.UI.Browser.Cef;

public sealed class CefBrowserRuntimeOptions
{
    public CefBrowserRuntimeOptions(string runtimeRootPath, string? cacheRootPath = null)
    {
        if (string.IsNullOrWhiteSpace(runtimeRootPath))
        {
            throw new ArgumentException("CEF runtime root path is required.", nameof(runtimeRootPath));
        }

        RuntimeRootPath = Path.GetFullPath(runtimeRootPath);
        CacheRootPath = string.IsNullOrWhiteSpace(cacheRootPath)
            ? Path.Combine(Path.GetTempPath(), "Ludots", "Cef", ComputeCacheSegment(RuntimeRootPath))
            : Path.GetFullPath(cacheRootPath);
    }

    public string RuntimeRootPath { get; }

    public string CacheRootPath { get; }

    /// <summary>
    /// CEF user-data-dir 必须会话独占：并发 Ludots 实例（多 worktree）会 Cef.Initialize false，
    /// 异常死亡实例残留的子进程/损坏档案会让后续会话延迟自亡。按 runtime root 分桶 + 进程号
    /// 隔离——代价仅是冷档案（本地环回秒级回填）。
    /// </summary>
    private static string ComputeCacheSegment(string runtimeRootPath)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in runtimeRootPath)
            {
                hash = (hash ^ c) * 16777619;
            }

            return $"{hash:x8}_{Environment.ProcessId}";
        }
    }
}

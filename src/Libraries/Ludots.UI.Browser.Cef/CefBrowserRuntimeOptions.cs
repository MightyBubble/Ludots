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
    /// 并发的 Ludots 实例（多 worktree）不得共享 CEF user-data-dir——第二个宿主会
    /// Cef.Initialize 失败。按 runtime root 稳定分桶：同树会话复用缓存，异树互不干扰。
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

            return hash.ToString("x8");
        }
    }
}

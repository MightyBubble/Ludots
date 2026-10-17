using Arch.Core;
using Schedulers;

namespace CrowdSimulationTests;

/// <summary>无头测试没有引擎，运动并行用的 JobScheduler 由本线程显式装上。</summary>
internal static class CrowdTestScheduler
{
    public static void Ensure()
    {
        var existing = World.SharedJobScheduler;
        if (existing != null)
        {
            if (!existing.IsMainThread)
            {
                throw new InvalidOperationException("World.SharedJobScheduler 属于别的线程，群体测试无法在本线程并行步进。");
            }

            return;
        }

        World.SharedJobScheduler = new JobScheduler(new JobScheduler.Config
        {
            ThreadPrefixName = "LudotsWorker",
            ThreadCount = 0,
            MaxExpectedConcurrentJobs = 64,
            StrictAllocationMode = false,
        });
    }
}

/// <summary>
/// 独占调度器租约：构造时记下旧实例（可能是别的 fixture 的，也可能是 null）并装上自建实例；
/// <see cref="Dispose"/> 先恢复旧实例，再只释放自建实例——绝不销毁别人的调度器。
/// </summary>
internal sealed class CrowdSchedulerLease : IDisposable
{
    private JobScheduler? _owned;
    private JobScheduler? _previous;

    public CrowdSchedulerLease(string threadPrefix, int threadCount)
    {
        var previous = World.SharedJobScheduler;
        if (previous is { IsMainThread: false })
        {
            throw new InvalidOperationException($"World.SharedJobScheduler 属于别的线程，{threadPrefix} 无法在本线程并行步进。");
        }

        _previous = previous;
        _owned = new JobScheduler(new JobScheduler.Config
        {
            ThreadPrefixName = threadPrefix,
            ThreadCount = threadCount,
            MaxExpectedConcurrentJobs = 16,
            StrictAllocationMode = false,
        });
        World.SharedJobScheduler = _owned;
    }

    /// <summary>租约自建并装上的实例；null = 已释放。</summary>
    public JobScheduler? Owned => _owned;

    public void Dispose()
    {
        World.SharedJobScheduler = _previous;
        _previous = null;
        _owned?.Dispose();
        _owned = null;
    }
}

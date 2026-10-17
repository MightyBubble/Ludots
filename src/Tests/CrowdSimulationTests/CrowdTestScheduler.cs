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
/// <see cref="Dispose"/> 先清字段（幂等），再恢复旧实例，最后才释放自建实例——释放动作抛错也无法撤销恢复，
/// 重复 Dispose 不会把已恢复的全局抹回 null，且绝不销毁别人的调度器。
/// </summary>
internal class CrowdSchedulerLease : IDisposable
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
        var owned = _owned;
        var previous = _previous;
        if (owned is null)
        {
            return;
        }

        _owned = null;
        _previous = null;
        World.SharedJobScheduler = previous;
        DisposeOwned(owned);
    }

    /// <summary>自建实例的释放动作；虚方法供异常注入用例替换，钉死“恢复先于释放”的次序合同。</summary>
    protected virtual void DisposeOwned(JobScheduler owned) => owned.Dispose();
}

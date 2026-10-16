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

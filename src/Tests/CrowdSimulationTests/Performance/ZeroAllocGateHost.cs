using System.IO;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// 零分配门的进程入口。测试宿主进程里的计时器会记进
/// <see cref="System.GC.GetTotalAllocatedBytes(bool)"/>，所以门在独立进程里读数。
/// </summary>
public static class ZeroAllocGateHost
{
    public static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "--zero-alloc")
        {
            Console.Error.WriteLine("测试程序集。进程内零分配门的入口是 --zero-alloc。");
            return 2;
        }

        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        int code = CrowdZeroAllocTests.RunGate();
        Environment.Exit(code);
        return code;
    }
}

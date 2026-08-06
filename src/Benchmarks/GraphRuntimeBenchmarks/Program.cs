using BenchmarkDotNet.Running;

namespace Ludots.Benchmarks.GraphRuntime;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--smoke", StringComparison.Ordinal))
        {
            GraphVmBenchmarkSmoke.Run();
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}

using System.Runtime.InteropServices;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// 当前线程的 CPU 时间。线程被抢占、等调度的时间不算，所以机器忙不忙不影响读数，真变慢（多做了活）照样变大。
/// Linux 用 CLOCK_THREAD_CPUTIME_ID，纳秒精度。Windows 的线程时间按调度节拍（约 15.6 ms）累加，单帧量不准：
/// 单帧读 QueryThreadCycleTime 的周期数，换算比例取整段开始到 <see cref="RawPerMs"/> 时 GetThreadTimes 的总 CPU 时间，
/// 所以 Windows 上整段线程 CPU 时间至少要 2 s。只能在创建它的线程上读。
/// </summary>
internal sealed class ThreadCpuClock
{
    private const int ClockThreadCpuTimeLinux = 3;
    private const double MinWindowsCalibrationMs = 2000d;

    private readonly long _startRaw;
    private readonly long _startTimes100ns;

    private ThreadCpuClock()
    {
        _startRaw = ReadRaw();
        if (OperatingSystem.IsWindows()) _startTimes100ns = WindowsThreadTimes100ns();
    }

    public static ThreadCpuClock StartOnCurrentThread()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("线程 CPU 时钟只实现了 Linux 和 Windows。");
        return new ThreadCpuClock();
    }

    /// <summary>Linux 是纳秒，Windows 是周期数；差值经 <see cref="RawPerMs"/> 换成毫秒。</summary>
    public long ReadRaw()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!QueryThreadCycleTime(GetCurrentThread(), out ulong cycles)) throw new InvalidOperationException("QueryThreadCycleTime 失败。");
            return (long)cycles;
        }

        if (clock_gettime(ClockThreadCpuTimeLinux, out Timespec ts) != 0) throw new InvalidOperationException("clock_gettime(CLOCK_THREAD_CPUTIME_ID) 失败。");
        return ts.Seconds * 1_000_000_000L + ts.Nanoseconds;
    }

    public double RawPerMs()
    {
        if (!OperatingSystem.IsWindows()) return 1_000_000d;
        double cpuMs = (WindowsThreadTimes100ns() - _startTimes100ns) / 10_000d;
        if (cpuMs < MinWindowsCalibrationMs)
            throw new InvalidOperationException($"线程 CPU 时间只有 {cpuMs:F0} ms，不够把周期数换算成毫秒（至少 {MinWindowsCalibrationMs:F0} ms）。");
        return (ReadRaw() - _startRaw) / cpuMs;
    }

    private static long WindowsThreadTimes100ns()
    {
        if (!GetThreadTimes(GetCurrentThread(), out _, out _, out long kernel, out long user)) throw new InvalidOperationException("GetThreadTimes 失败。");
        return kernel + user;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public long Seconds;
        public long Nanoseconds;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int clock_gettime(int clockId, out Timespec ts);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);
}

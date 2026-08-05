using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ludots.Launcher.Backend;

internal sealed class WindowsProcessTreeSnapshot
{
    private const uint SnapshotProcesses = 0x00000002;
    private const int ErrorNoMoreFiles = 18;
    private readonly int _rootProcessId;
    private readonly Dictionary<int, long> _processStartTimesUtcTicks;

    private WindowsProcessTreeSnapshot(
        int rootProcessId,
        Dictionary<int, long> processStartTimesUtcTicks)
    {
        _rootProcessId = rootProcessId;
        _processStartTimesUtcTicks = processStartTimesUtcTicks;
    }

    public static WindowsProcessTreeSnapshot Capture(int rootProcessId, long rootStartTimeUtcTicks)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Process-tree exit confirmation currently requires the Windows process snapshot API.");
        }

        var identities = new Dictionary<int, long>
        {
            [rootProcessId] = rootStartTimeUtcTicks
        };
        AddLiveDescendants(rootProcessId, identities);
        return new WindowsProcessTreeSnapshot(rootProcessId, identities);
    }

    public async Task ConfirmExitedAsync(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Process-tree confirmation timeout must be positive.");
        }

        DateTime deadlineUtc = DateTime.UtcNow + timeout;
        while (true)
        {
            AddLiveDescendants(_rootProcessId, _processStartTimesUtcTicks);
            List<Process> liveProcesses = OpenLiveProcesses();
            if (liveProcesses.Count == 0)
            {
                AddLiveDescendants(_rootProcessId, _processStartTimesUtcTicks);
                liveProcesses = OpenLiveProcesses();
                if (liveProcesses.Count == 0)
                {
                    return;
                }
            }

            TimeSpan remaining = deadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                DisposeProcesses(liveProcesses);
                throw CreateTimeoutException(timeout);
            }

            using var timeoutSource = new CancellationTokenSource(remaining);
            try
            {
                await Task.WhenAll(liveProcesses.Select(process => process.WaitForExitAsync(timeoutSource.Token)));
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
                throw CreateTimeoutException(timeout);
            }
            finally
            {
                DisposeProcesses(liveProcesses);
            }
        }
    }

    private static void AddLiveDescendants(
        int rootProcessId,
        Dictionary<int, long> identities)
    {
        Dictionary<int, int> parentByProcessId = CaptureParentMap();
        var relatedProcessIds = new HashSet<int>(identities.Keys)
        {
            rootProcessId
        };

        bool added;
        do
        {
            added = false;
            foreach ((int processId, int parentProcessId) in parentByProcessId)
            {
                if (!relatedProcessIds.Contains(parentProcessId) || !relatedProcessIds.Add(processId))
                {
                    continue;
                }

                added = true;
            }
        }
        while (added);

        foreach (int processId in relatedProcessIds)
        {
            if (identities.ContainsKey(processId) || !TryGetStartTimeUtcTicks(processId, out long startTimeUtcTicks))
            {
                continue;
            }

            identities.Add(processId, startTimeUtcTicks);
        }
    }

    private List<Process> OpenLiveProcesses()
    {
        var processes = new List<Process>(_processStartTimesUtcTicks.Count);
        try
        {
            foreach ((int processId, long startTimeUtcTicks) in _processStartTimesUtcTicks)
            {
                Process? process = TryOpenMatchingProcess(processId, startTimeUtcTicks);
                if (process != null)
                {
                    processes.Add(process);
                }
            }

            return processes;
        }
        catch
        {
            DisposeProcesses(processes);
            throw;
        }
    }

    private static Process? TryOpenMatchingProcess(int processId, long startTimeUtcTicks)
    {
        try
        {
            Process process = Process.GetProcessById(processId);
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != startTimeUtcTicks)
            {
                process.Dispose();
                return null;
            }

            return process;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool TryGetStartTimeUtcTicks(int processId, out long startTimeUtcTicks)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                startTimeUtcTicks = 0;
                return false;
            }

            startTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
            return true;
        }
        catch (ArgumentException)
        {
            startTimeUtcTicks = 0;
            return false;
        }
        catch (InvalidOperationException)
        {
            startTimeUtcTicks = 0;
            return false;
        }
    }

    private static Dictionary<int, int> CaptureParentMap()
    {
        using SafeFileHandle snapshotHandle = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshotHandle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to capture the Windows process list.");
        }

        var parentByProcessId = new Dictionary<int, int>();
        var entry = new ProcessEntry32
        {
            Size = (uint)Marshal.SizeOf<ProcessEntry32>(),
            ExecutableFile = string.Empty
        };

        if (!Process32First(snapshotHandle, ref entry))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNoMoreFiles)
            {
                return parentByProcessId;
            }

            throw new Win32Exception(error, "Failed to read the first Windows process snapshot entry.");
        }

        do
        {
            parentByProcessId[checked((int)entry.ProcessId)] = checked((int)entry.ParentProcessId);
        }
        while (Process32Next(snapshotHandle, ref entry));

        int finalError = Marshal.GetLastWin32Error();
        if (finalError != ErrorNoMoreFiles)
        {
            throw new Win32Exception(finalError, "Failed while enumerating the Windows process snapshot.");
        }

        return parentByProcessId;
    }

    private TimeoutException CreateTimeoutException(TimeSpan timeout)
    {
        string processIds = string.Join(", ", _processStartTimesUtcTicks.Keys.OrderBy(processId => processId));
        return new TimeoutException(
            $"Process tree rooted at {_rootProcessId} did not exit within {timeout.TotalMilliseconds:F0} ms. Tracked process ids: {processIds}.");
    }

    private static void DisposeProcesses(IEnumerable<Process> processes)
    {
        foreach (Process process in processes)
        {
            process.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint UsageCount;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint ThreadCount;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(SafeFileHandle snapshotHandle, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(SafeFileHandle snapshotHandle, ref ProcessEntry32 entry);
}

using System.Diagnostics;
using System.Globalization;
using Ludots.Launcher.Backend;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
[NonParallelizable]
public sealed class LauncherProcessRunnerContractTests
{
    [Test]
    public async Task RunProcessAsync_PreservesExitCodeAndOutput_ForNormalExit()
    {
        RequireWindows();

        ProcessRunResult result = await LauncherService.RunProcessAsync(
            "cmd.exe",
            "/d /c \"echo stdout-line & echo stderr-line 1>&2\"",
            Path.GetTempPath(),
            timeoutMs: 5_000);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ProcessRunStatus.Success));
            Assert.That(result.WorkflowExitCode, Is.Zero);
            Assert.That(result.ProcessExitCode, Is.Zero);
            Assert.That(result.Output, Does.Contain("stdout-line"));
            Assert.That(result.Output, Does.Contain("stderr-line"));
            Assert.That(result.TimedOut, Is.False);
            Assert.That(result.ProcessExited, Is.True);
            Assert.That(result.ProcessTreeExitConfirmed, Is.False);
            Assert.That(result.OutputComplete, Is.True);
            Assert.That(result.Failures, Is.Empty);
        });
    }

    [Test]
    public async Task RunProcessAsync_PreservesNonZeroExitCode_ForProcessFailure()
    {
        RequireWindows();

        ProcessRunResult result = await LauncherService.RunProcessAsync(
            "cmd.exe",
            "/d /c \"echo failed-output & exit /b 7\"",
            Path.GetTempPath(),
            timeoutMs: 5_000);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ProcessRunStatus.ProcessFailed));
            Assert.That(result.WorkflowExitCode, Is.EqualTo(7));
            Assert.That(result.ProcessExitCode, Is.EqualTo(7));
            Assert.That(result.Output, Does.Contain("failed-output"));
            Assert.That(result.TimedOut, Is.False);
            Assert.That(result.ProcessExited, Is.True);
            Assert.That(result.ProcessTreeExitConfirmed, Is.False);
            Assert.That(result.OutputComplete, Is.True);
            Assert.That(result.Failures, Is.Empty);
        });
    }

    [Test]
    public async Task RunProcessAsync_ReturnsTimedOutCleanly_AfterEntireProcessTreeExits()
    {
        RequireWindows();

        using var fixture = BlockingProcessFixture.Create(startChild: true);
        ProcessRunOperations operations = CreateOperationsWaitingForReady(fixture.ReadyPath);

        ProcessRunResult result = await LauncherService.RunProcessAsync(
            WindowsPowerShellPath,
            fixture.ParentArguments,
            fixture.DirectoryPath,
            timeoutMs: 5_000,
            outputDrainTimeoutMs: 2_000,
            terminationTimeoutMs: 5_000,
            operations);
        fixture.CaptureProcessIds(result);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ProcessRunStatus.TimedOutCleanly));
            Assert.That(result.WorkflowExitCode, Is.EqualTo(-1));
            Assert.That(result.ProcessExitCode, Is.Not.Null);
            Assert.That(result.TimedOut, Is.True);
            Assert.That(result.ProcessExited, Is.True);
            Assert.That(result.ProcessTreeExitConfirmed, Is.True);
            Assert.That(result.OutputComplete, Is.True);
            Assert.That(result.Failures, Is.Empty);
            AssertProcessExited(result.ProcessId);
            AssertProcessExited(fixture.ChildProcessId);
        });
    }

    [Test]
    public async Task RunProcessAsync_ReturnsTerminationFailed_WhenKillThrows()
    {
        RequireWindows();

        using var fixture = BlockingProcessFixture.Create(startChild: false);
        ProcessRunOperations defaults = ProcessRunOperations.Default;
        var operations = new ProcessRunOperations(
            process =>
            {
                WaitForReadyOrThrow(fixture.ReadyPath);
                throw new InvalidOperationException("synthetic kill failure");
            },
            defaults.ConfirmProcessExitAsync,
            defaults.CancelStandardOutputRead,
            defaults.CancelStandardErrorRead);

        ProcessRunResult result = await LauncherService.RunProcessAsync(
            WindowsPowerShellPath,
            fixture.ParentArguments,
            fixture.DirectoryPath,
            timeoutMs: 250,
            outputDrainTimeoutMs: 100,
            terminationTimeoutMs: 100,
            operations);
        fixture.CaptureProcessIds(result);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ProcessRunStatus.TerminationFailed));
            Assert.That(result.WorkflowExitCode, Is.EqualTo(-2));
            Assert.That(result.ProcessExitCode, Is.Null);
            Assert.That(result.ProcessExited, Is.False);
            Assert.That(result.ProcessTreeExitConfirmed, Is.False);
            Assert.That(result.Failures.Any(failure =>
                failure.Stage == ProcessRunFailureStage.TerminateProcessTree &&
                failure.Exception is InvalidOperationException &&
                failure.Exception.Message == "synthetic kill failure"), Is.True);
            Assert.That(result.Failures.Any(failure =>
                failure.Stage == ProcessRunFailureStage.ConfirmProcessTreeExit), Is.True);
            Assert.That(result.Output, Does.Contain(nameof(ProcessRunFailureStage.TerminateProcessTree)));
            Assert.That(result.Output, Does.Contain(typeof(InvalidOperationException).FullName));
            Assert.That(result.Output, Does.Contain("synthetic kill failure"));
            Assert.That(result.Output, Does.Contain(result.ProcessId.ToString(CultureInfo.InvariantCulture)));
            Assert.That(result.Output, Does.Contain(WindowsPowerShellPath));
        });
    }

    [Test]
    public async Task RunProcessAsync_ReturnsTerminationFailed_WhenKillDoesNotExitProcess()
    {
        RequireWindows();

        using var fixture = BlockingProcessFixture.Create(startChild: false);
        ProcessRunOperations defaults = ProcessRunOperations.Default;
        var operations = new ProcessRunOperations(
            _ => WaitForReadyOrThrow(fixture.ReadyPath),
            defaults.ConfirmProcessExitAsync,
            defaults.CancelStandardOutputRead,
            defaults.CancelStandardErrorRead);

        ProcessRunResult result = await LauncherService.RunProcessAsync(
            WindowsPowerShellPath,
            fixture.ParentArguments,
            fixture.DirectoryPath,
            timeoutMs: 250,
            outputDrainTimeoutMs: 100,
            terminationTimeoutMs: 100,
            operations);
        fixture.CaptureProcessIds(result);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ProcessRunStatus.TerminationFailed));
            Assert.That(result.WorkflowExitCode, Is.EqualTo(-2));
            Assert.That(result.ProcessExited, Is.False);
            Assert.That(result.ProcessTreeExitConfirmed, Is.False);
            Assert.That(result.Failures.Any(failure =>
                failure.Stage == ProcessRunFailureStage.TerminateProcessTree), Is.False);
            Assert.That(result.Failures.Any(failure =>
                failure.Stage == ProcessRunFailureStage.ConfirmProcessTreeExit &&
                failure.Exception is TimeoutException), Is.True);
        });
    }

    [Test]
    public async Task RunProcessAsync_MarksOutputIncomplete_WhenDescendantKeepsRedirectedOutputOpen()
    {
        RequireWindows();

        using var fixture = BlockingProcessFixture.Create(startChild: true, parentExitsAfterReady: true);
        ProcessRunResult result = await LauncherService.RunProcessAsync(
            WindowsPowerShellPath,
            fixture.ParentArguments,
            fixture.DirectoryPath,
            timeoutMs: 5_000,
            outputDrainTimeoutMs: 250);
        fixture.CaptureProcessIds(result);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ProcessRunStatus.OutputDrainFailed));
            Assert.That(result.WorkflowExitCode, Is.EqualTo(-3));
            Assert.That(result.ProcessExitCode, Is.Zero);
            Assert.That(result.Output, Does.Contain("parent-done"));
            Assert.That(result.Output, Does.Contain("Redirected output remained open"));
            Assert.That(result.ProcessExited, Is.True);
            Assert.That(result.ProcessTreeExitConfirmed, Is.False);
            Assert.That(result.OutputComplete, Is.False);
            Assert.That(result.Failures.Select(failure => failure.Stage),
                Is.EqualTo(new[] { ProcessRunFailureStage.DrainOutput }));
        });
    }

    [TestCase(nameof(ProcessRunFailureStage.CancelStandardOutputRead))]
    [TestCase(nameof(ProcessRunFailureStage.CancelStandardErrorRead))]
    public async Task RunProcessAsync_ReturnsOutputDrainFailed_WhenOutputCancellationFails(
        string failingStageName)
    {
        RequireWindows();
        var failingStage = Enum.Parse<ProcessRunFailureStage>(failingStageName);

        using var fixture = BlockingProcessFixture.Create(startChild: true, parentExitsAfterReady: true);
        ProcessRunOperations defaults = ProcessRunOperations.Default;
        var operations = new ProcessRunOperations(
            defaults.KillProcessTree,
            defaults.ConfirmProcessExitAsync,
            process =>
            {
                defaults.CancelStandardOutputRead(process);
                if (failingStage == ProcessRunFailureStage.CancelStandardOutputRead)
                {
                    throw new InvalidOperationException("synthetic stdout cancellation failure");
                }
            },
            process =>
            {
                defaults.CancelStandardErrorRead(process);
                if (failingStage == ProcessRunFailureStage.CancelStandardErrorRead)
                {
                    throw new InvalidOperationException("synthetic stderr cancellation failure");
                }
            });

        ProcessRunResult result = await LauncherService.RunProcessAsync(
            WindowsPowerShellPath,
            fixture.ParentArguments,
            fixture.DirectoryPath,
            timeoutMs: 5_000,
            outputDrainTimeoutMs: 250,
            operations: operations);
        fixture.CaptureProcessIds(result);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ProcessRunStatus.OutputDrainFailed));
            Assert.That(result.WorkflowExitCode, Is.EqualTo(-3));
            Assert.That(result.ProcessExitCode, Is.Zero);
            Assert.That(result.ProcessExited, Is.True);
            Assert.That(result.ProcessTreeExitConfirmed, Is.False);
            Assert.That(result.OutputComplete, Is.False);
            Assert.That(result.Failures.Any(failure =>
                failure.Stage == failingStage &&
                failure.Exception is InvalidOperationException), Is.True);
            Assert.That(result.Output, Does.Contain(failingStage.ToString()));
            Assert.That(result.Output, Does.Contain("cancellation failure"));
        });
    }

    private static string WindowsPowerShellPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32",
        "WindowsPowerShell",
        "v1.0",
        "powershell.exe");

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Launcher process-tree cleanup is implemented through the Windows entireProcessTree contract.");
        }
    }

    private static ProcessRunOperations CreateOperationsWaitingForReady(string readyPath)
    {
        ProcessRunOperations defaults = ProcessRunOperations.Default;
        return new ProcessRunOperations(
            process =>
            {
                WaitForReadyOrThrow(readyPath);
                defaults.KillProcessTree(process);
            },
            defaults.ConfirmProcessExitAsync,
            defaults.CancelStandardOutputRead,
            defaults.CancelStandardErrorRead);
    }

    private static void WaitForReadyOrThrow(string readyPath)
    {
        if (!WaitForFile(readyPath, TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException($"Child process did not publish ready signal: {readyPath}");
        }
    }

    private static bool WaitForFile(string path, TimeSpan timeout)
    {
        if (File.Exists(path))
        {
            return true;
        }

        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"File signal path has no directory: {path}");
        string fileName = Path.GetFileName(path);
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
        };
        FileSystemEventHandler handler = (_, _) => signal.TrySetResult(true);
        RenamedEventHandler renamedHandler = (_, eventArgs) =>
        {
            if (string.Equals(eventArgs.Name, fileName, StringComparison.OrdinalIgnoreCase))
            {
                signal.TrySetResult(true);
            }
        };
        watcher.Created += handler;
        watcher.Renamed += renamedHandler;
        watcher.EnableRaisingEvents = true;

        if (File.Exists(path))
        {
            return true;
        }

        return signal.Task.Wait(timeout);
    }

    private static void AssertProcessExited(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            Assert.That(process.WaitForExit(5_000), Is.True, $"Process {processId} remained alive.");
        }
        catch (ArgumentException)
        {
        }
    }

    private sealed class BlockingProcessFixture : IDisposable
    {
        private readonly List<int> _processIds = new();

        private BlockingProcessFixture(
            string directoryPath,
            string readyPath,
            string childPidPath,
            string parentArguments)
        {
            DirectoryPath = directoryPath;
            ReadyPath = readyPath;
            ChildPidPath = childPidPath;
            ParentArguments = parentArguments;
        }

        public string DirectoryPath { get; }

        public string ReadyPath { get; }

        public string ChildPidPath { get; }

        public string ParentArguments { get; }

        public int ChildProcessId => ReadProcessId(ChildPidPath);

        public static BlockingProcessFixture Create(bool startChild, bool parentExitsAfterReady = false)
        {
            string directoryPath = Path.Combine(Path.GetTempPath(), $"ludots-launcher-process-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directoryPath);
            string readyPath = Path.Combine(directoryPath, "ready.txt");
            string childPidPath = Path.Combine(directoryPath, "child.pid");
            string blockingScriptPath = Path.Combine(directoryPath, "block.ps1");
            File.WriteAllText(
                blockingScriptPath,
                "param([string]$ReadyPath, [string]$PidPath)\r\n" +
                "[IO.File]::WriteAllText($PidPath, [string]$PID)\r\n" +
                "[IO.File]::WriteAllText($ReadyPath, 'ready')\r\n" +
                "$gate = [Threading.ManualResetEventSlim]::new($false)\r\n" +
                "$gate.Wait()\r\n");

            string parentScriptPath;
            if (startChild)
            {
                parentScriptPath = Path.Combine(directoryPath, "parent.ps1");
                File.WriteAllText(
                    parentScriptPath,
                    "param([string]$ChildScript, [string]$ReadyPath, [string]$ChildPidPath, [string]$ExitAfterReady)\r\n" +
                    "$powershell = (Get-Process -Id $PID).Path\r\n" +
                    "$childArgs = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', ('\"' + $ChildScript + '\"'), ('\"' + $ReadyPath + '\"'), ('\"' + $ChildPidPath + '\"'))\r\n" +
                    "$child = Start-Process -FilePath $powershell -ArgumentList $childArgs -PassThru -NoNewWindow\r\n" +
                    "$deadline = [Diagnostics.Stopwatch]::StartNew()\r\n" +
                    "while (-not (Test-Path -LiteralPath $ReadyPath)) {\r\n" +
                    "  if ($deadline.ElapsedMilliseconds -ge 5000) { throw 'Child did not become ready.' }\r\n" +
                    "  [Threading.Thread]::Yield() | Out-Null\r\n" +
                    "}\r\n" +
                    "if ($ExitAfterReady -eq 'true') { Write-Output 'parent-done'; exit 0 }\r\n" +
                    "$child.WaitForExit()\r\n");
            }
            else
            {
                parentScriptPath = blockingScriptPath;
                childPidPath = Path.Combine(directoryPath, "parent.pid");
            }

            string parentArguments = startChild
                ? $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File {Quote(parentScriptPath)} {Quote(blockingScriptPath)} {Quote(readyPath)} {Quote(childPidPath)} {parentExitsAfterReady.ToString().ToLowerInvariant()}"
                : $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File {Quote(blockingScriptPath)} {Quote(readyPath)} {Quote(childPidPath)}";
            return new BlockingProcessFixture(directoryPath, readyPath, childPidPath, parentArguments);
        }

        public void CaptureProcessIds(ProcessRunResult result)
        {
            _processIds.Add(result.ProcessId);
            if (File.Exists(ChildPidPath))
            {
                _processIds.Add(ReadProcessId(ChildPidPath));
            }
        }

        public void Dispose()
        {
            foreach (int processId in _processIds.Distinct())
            {
                KillProcessTreeIfRunning(processId);
            }

            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }

        private static int ReadProcessId(string path)
        {
            WaitForReadyOrThrow(path);
            return int.Parse(File.ReadAllText(path), CultureInfo.InvariantCulture);
        }

        private static void KillProcessTreeIfRunning(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5_000);
                }
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
    }
}

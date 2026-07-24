using System.Diagnostics;
using Ludots.Adapter.LiteNetLib;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Networking.Runtime;

string baseDirectory = AppContext.BaseDirectory;
try
{
    string bootstrapPath = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
        ? args[0]
        : "launcher.runtime.json";
    GameBootstrapResult bootstrap = GameBootstrapper.InitializeFromBaseDirectory(
        baseDirectory,
        bootstrapPath);
    if (bootstrap.NetworkHost?.ResolveRole() != NetworkProcessRole.AuthoritativeServer)
    {
        throw new InvalidOperationException(
            "Dedicated server requires an authoritativeServer networkHost bootstrap.");
    }

    LiteNetLibNetworkRuntimeInstaller.Install(in bootstrap, baseDirectory);
    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        shutdown.Cancel();
    };

    using var engine = bootstrap.Engine;
    engine.Start();
    ThrowIfLifecycleFailed(engine, "startup");
    engine.LoadStartupMap();
    ThrowIfLifecycleFailed(engine, "startup map load");
    long previous = Stopwatch.GetTimestamp();
    while (!shutdown.IsCancellationRequested)
    {
        long now = Stopwatch.GetTimestamp();
        float deltaSeconds = (float)((now - previous) / (double)Stopwatch.Frequency);
        previous = now;
        engine.Tick(deltaSeconds);
        Thread.Sleep(1);
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}

static void ThrowIfLifecycleFailed(GameEngine engine, string phase)
{
    var errors = engine.TriggerManager.Errors;
    if (errors.Count == 0)
    {
        return;
    }

    var first = errors[0];
    throw new InvalidOperationException(
        $"Dedicated server {phase} recorded {errors.Count} trigger error(s). " +
        $"First error: event '{first.EventKey}', trigger '{first.TriggerName}': {first.Exception.Message}",
        first.Exception);
}
